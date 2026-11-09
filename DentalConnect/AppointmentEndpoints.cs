using System.Data;
using System.Security.Cryptography;
using System.Text;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DentalConnect;

public sealed record CreateAppointmentRequest(string ClinicId, string DentistId, string DentalServiceId, DateTimeOffset StartsAt);

public static class AppointmentEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/appointments", CreateAsync).RequireAuthorization();
        endpoints.MapPost("/api/appointments/{appointmentId}/confirm", ConfirmAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(HttpContext http, CreateAppointmentRequest? body, DentalConnectDbContext db, CancellationToken ct)
    {
        var patient = http.User.FindFirst("sub")?.Value ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(patient) || http.User.FindFirst("email_verified")?.Value?.ToLowerInvariant() != "true")
            return Results.Unauthorized();
        if (body is null || !CuidGenerator.IsValid(body.ClinicId) || !CuidGenerator.IsValid(body.DentistId) || !CuidGenerator.IsValid(body.DentalServiceId) || body.StartsAt == default)
            return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Consultorio, dentista, servicio y horario son obligatorios" } });
        var key = http.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length is < 8 or > 128)
            return Results.BadRequest(new { error = new { code = "IDEMPOTENCY_KEY_REQUIRED", message = "Envía una clave Idempotency-Key de 8 a 128 caracteres" } });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{body.ClinicId}|{body.DentistId}|{body.DentalServiceId}|{body.StartsAt.ToUniversalTime():O}")));
        var prior = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.PatientSubject == patient && a.IdempotencyKey == key, ct);
        if (prior is not null) return prior.RequestFingerprint == fingerprint ? AppointmentResponse(prior, StatusCodes.Status200OK) : Conflict("IDEMPOTENCY_KEY_REUSED", "La clave ya fue usada para otra solicitud");

        var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
        var now = DateTimeOffset.UtcNow;
        var start = body.StartsAt.ToUniversalTime();
        if (start <= now) return Conflict("PAST_DATE", "La fecha y hora deben ser futuras");
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var day = DateOnly.FromDateTime(localStart.DateTime);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var lockKey = $"{patient}:{key}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
            prior = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.PatientSubject == patient && a.IdempotencyKey == key, ct);
            if (prior is not null)
            {
                await tx.CommitAsync(ct);
                return prior.RequestFingerprint == fingerprint ? AppointmentResponse(prior, StatusCodes.Status200OK) : Conflict("IDEMPOTENCY_KEY_REUSED", "La clave ya fue usada para otra solicitud");
            }
            var dentist = await db.Dentists.Include(d => d.WeeklySchedules).Include(d => d.ScheduleBlocks)
                .Include(d => d.DentistServices).ThenInclude(ds => ds.DentalService)
                .SingleOrDefaultAsync(d => d.Id == body.DentistId && d.ClinicId == body.ClinicId && d.IsActive, ct);
            var clinicActive = await db.Clinics.AnyAsync(c => c.Id == body.ClinicId && c.IsActive, ct);
            var clinicOffers = await db.ClinicServices.AnyAsync(cs => cs.ClinicId == body.ClinicId && cs.DentalServiceId == body.DentalServiceId && cs.IsActive && cs.DentalService.IsActive, ct);
            var service = dentist?.DentistServices.Select(ds => ds.DentalService).SingleOrDefault(s => s.Id == body.DentalServiceId && s.IsActive);
            if (!clinicActive || dentist is null || service is null || service.EstimatedDurationMinutes <= 0 || !clinicOffers)
            {
                await tx.RollbackAsync(ct);
                return Results.BadRequest(new { error = new { code = "SELECTION_NOT_AVAILABLE", message = "El consultorio, dentista o servicio ya no está disponible" } });
            }
            var end = start.AddMinutes(service.EstimatedDurationMinutes);
            var confirmed = await db.Appointments.AsNoTracking().Where(a => a.DentistId == dentist.Id && a.Status == "CONFIRMED" && a.StartsAt < end && a.EndsAt > start).ToListAsync(ct);
            var slots = PublicAvailabilityRules.Generate(day, 1, service.EstimatedDurationMinutes, dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, now, confirmed);
            if (!slots.Any(s => s.StartsAt == start && s.EndsAt == end))
            {
                await tx.RollbackAsync(ct);
                return Conflict("SLOT_UNAVAILABLE", "El horario ya no está disponible; consulta nuevamente los horarios");
            }
            var appointment = new Appointment { Folio = $"DC-{Guid.NewGuid():N}"[..15].ToUpperInvariant(), PatientSubject = patient, ClinicId = body.ClinicId, DentistId = dentist.Id, DentalServiceId = service.Id, StartsAt = start, EndsAt = end, IdempotencyKey = key, RequestFingerprint = fingerprint, TimeZoneId = PublicAvailabilityRules.PilotTimeZoneId };
            db.Appointments.Add(appointment);
            db.ClinicNotificationEvents.Add(new ClinicNotificationEvent { AppointmentId = appointment.Id });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppointmentResponse(appointment, StatusCodes.Status201Created);
        }
        catch (Exception ex) when (SqlState(ex) is "23505" or "40001")
        {
            await tx.RollbackAsync(ct);
            return Conflict("APPOINTMENT_CONFLICT", "La solicitud entró en conflicto; vuelve a consultar el horario");
        }
    }

    private static async Task<IResult> ConfirmAsync(string appointmentId, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!http.User.IsInRole("CLINIC_ADMIN")) return Results.Forbid();
        var clinicId = http.User.FindFirst("clinic_id")?.Value;
        if (string.IsNullOrWhiteSpace(clinicId)) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClinicId == clinicId, ct);
            if (appointment is null) return Results.NotFound();
            if (appointment.Status == "CONFIRMED") { await tx.CommitAsync(ct); return AppointmentResponse(appointment, StatusCodes.Status200OK); }
            if (appointment.Status != "PENDING") { await tx.RollbackAsync(ct); return Conflict("INVALID_STATUS", "Solo se pueden confirmar solicitudes pendientes"); }
            var dentist = await db.Dentists.Include(d => d.WeeklySchedules).Include(d => d.ScheduleBlocks).SingleOrDefaultAsync(d => d.Id == appointment.DentistId && d.IsActive && d.ClinicId == clinicId, ct);
            if (dentist is null) { await tx.RollbackAsync(ct); return Conflict("DENTIST_UNAVAILABLE", "El dentista ya no está disponible"); }
            var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt, zone).DateTime);
            var others = await db.Appointments.AsNoTracking().Where(a => a.DentistId == dentist.Id && a.Id != appointment.Id && a.Status == "CONFIRMED" && a.StartsAt < appointment.EndsAt && a.EndsAt > appointment.StartsAt).ToListAsync(ct);
            var slots = PublicAvailabilityRules.Generate(day, 1, (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes, dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, DateTimeOffset.UtcNow, others);
            if (!slots.Any(s => s.StartsAt == appointment.StartsAt && s.EndsAt == appointment.EndsAt)) { await tx.RollbackAsync(ct); return Conflict("SLOT_UNAVAILABLE", "El horario ya no está disponible"); }
            appointment.Status = "CONFIRMED";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppointmentResponse(appointment, StatusCodes.Status200OK);
        }
        catch (Exception ex) when (SqlState(ex) is "23P01" or "40001")
        {
            await tx.RollbackAsync(ct);
            return Conflict("APPOINTMENT_CONFLICT", "Otra solicitud confirmó un intervalo que se cruza con este");
        }
    }

    private static string? SqlState(Exception error) => error switch
    {
        PostgresException postgres => postgres.SqlState,
        DbUpdateException { InnerException: PostgresException postgres } => postgres.SqlState,
        _ => null
    };

    private static IResult AppointmentResponse(Appointment item, int statusCode) => Results.Json(new { data = new { item.Id, item.Folio, item.Status, item.ClinicId, item.DentistId, item.DentalServiceId, item.StartsAt, item.EndsAt, item.TimeZoneId } }, statusCode: statusCode);
    private static IResult Conflict(string code, string message) => Results.Conflict(new { error = new { code, message } });
}
