using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect;

public sealed record PublicAvailabilitySlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public static class PublicAvailabilityRules
{
    public const string PilotTimeZoneId = "America/Mexico_City";

    public static IReadOnlyList<PublicAvailabilitySlot> Generate(
        DateOnly firstDay, int days, int durationMinutes,
        IEnumerable<DentistWeeklySchedule> weeklySchedules,
        IEnumerable<DentistScheduleBlock> blocks,
        TimeZoneInfo timeZone, DateTimeOffset now)
    {
        if (days is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(days));
        if (durationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        var slots = new List<PublicAvailabilitySlot>();
        var blocked = blocks.ToArray();
        for (var offset = 0; offset < days; offset++)
        {
            var date = firstDay.AddDays(offset);
            foreach (var schedule in weeklySchedules.Where(s => s.DayOfWeek == date.DayOfWeek))
            {
                for (var start = schedule.StartsAt; start.AddMinutes(durationMinutes) <= schedule.EndsAt; start = start.AddMinutes(15))
                {
                    var end = start.AddMinutes(durationMinutes);
                    if (schedule.BreakStartsAt is { } breakStart && schedule.BreakEndsAt is { } breakEnd && start < breakEnd && end > breakStart) continue;
                    var localStart = DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Unspecified);
                    var localEnd = DateTime.SpecifyKind(date.ToDateTime(end), DateTimeKind.Unspecified);
                    if (timeZone.IsInvalidTime(localStart) || timeZone.IsInvalidTime(localEnd)) continue;
                    var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), TimeSpan.Zero);
                    var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone), TimeSpan.Zero);
                    if (startUtc <= now || blocked.Any(b => startUtc < b.EndsAt && endUtc > b.StartsAt)) continue;
                    slots.Add(new PublicAvailabilitySlot(startUtc, endUtc));
                }
            }
        }
        return slots.OrderBy(s => s.StartsAt).ToArray();
    }
}

public static class PublicAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapPublicAvailabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/clinics/{clinicId}/dentists/{dentistId}/availability", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(string clinicId, string dentistId, string? serviceId, int? days,
        DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CuidGenerator.IsValid(clinicId) || !CuidGenerator.IsValid(dentistId) || !CuidGenerator.IsValid(serviceId))
            return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Identificadores inválidos" } });
        var horizon = days ?? 14;
        if (horizon is < 1 or > 30)
            return Results.BadRequest(new { error = new { code = "INVALID_DAYS", message = "El periodo debe ser de 1 a 30 días" } });
        var clinic = await db.Clinics.AsNoTracking().Where(c => c.Id == clinicId && c.IsActive)
            .Select(c => new { c.Id }).SingleOrDefaultAsync(ct);
        if (clinic is null) return Results.NotFound(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no disponible" } });
        var dentist = await db.Dentists.AsNoTracking()
            .Where(d => d.Id == dentistId && d.ClinicId == clinicId && d.IsActive)
            .Include(d => d.WeeklySchedules).Include(d => d.ScheduleBlocks)
            .Include(d => d.DentistServices.Where(ds => ds.DentalServiceId == serviceId && ds.DentalService.IsActive))
                .ThenInclude(ds => ds.DentalService)
            .SingleOrDefaultAsync(ct);
        if (dentist is null) return Results.NotFound(new { error = new { code = "DENTIST_NOT_AVAILABLE", message = "Dentista no disponible" } });
        var service = dentist.DentistServices.Select(ds => ds.DentalService).SingleOrDefault();
        var clinicOffersService = await db.ClinicServices.AsNoTracking().AnyAsync(cs => cs.ClinicId == clinicId && cs.DentalServiceId == serviceId && cs.IsActive && cs.DentalService.IsActive, ct);
        if (service is null || service.EstimatedDurationMinutes <= 0 || !clinicOffersService)
            return Results.BadRequest(new { error = new { code = "INCOMPATIBLE_SERVICE", message = "El servicio no está activo o no lo ofrece este dentista y consultorio" } });
        var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var data = PublicAvailabilityRules.Generate(localToday, horizon, service.EstimatedDurationMinutes,
            dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, DateTimeOffset.UtcNow);
        return Results.Ok(new { data, timeZone = PublicAvailabilityRules.PilotTimeZoneId, service = new { service.Id, service.Name, service.EstimatedDurationMinutes } });
    }
}
