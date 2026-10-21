using System.Security.Claims;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect;

public static class DentistEndpoints
{
    public static IEndpointRouteBuilder MapDentistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/clinics/{clinicId}/dentists");
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{dentistId}", DetailAsync);
        group.MapPut("/{dentistId}", UpdateAsync);
        group.MapPatch("/{dentistId}/status", SetStatusAsync);
        group.MapGet("/{dentistId}/schedule", ListScheduleAsync);
        group.MapPost("/{dentistId}/schedule", AddScheduleAsync);
        group.MapDelete("/{dentistId}/schedule/{scheduleId}", DeleteScheduleAsync);
        group.MapGet("/{dentistId}/blocks", ListBlocksAsync);
        group.MapPost("/{dentistId}/blocks", AddBlockAsync);
        group.MapDelete("/{dentistId}/blocks/{blockId}", DeleteBlockAsync);
        return endpoints;
    }

    private static bool CanManage(HttpContext context, string clinicId)
    {
        var groups = context.User.FindAll("cognito:groups").SelectMany(c => c.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        var claimedClinic = context.User.FindFirst("custom:clinicId")?.Value ?? context.User.FindFirst("clinicId")?.Value;
        return context.User.Identity?.IsAuthenticated == true &&
            (groups.Contains("CLINIC_ADMIN", StringComparer.Ordinal) || role == "CLINIC_ADMIN") &&
            string.Equals(claimedClinic, clinicId, StringComparison.Ordinal);
    }

    private static IResult Forbidden(HttpContext context, string clinicId) => CanManage(context, clinicId)
        ? Results.StatusCode(204)
        : Results.Json(new { error = new { code = "CLINIC_ACCESS_DENIED", message = "No tienes permiso para administrar este consultorio" } }, statusCode: 403);

    private static async Task<IResult> ListAsync(string clinicId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct)) return Results.NotFound();
        var data = await db.Dentists.AsNoTracking().Where(d => d.ClinicId == clinicId)
            .OrderBy(d => d.LastName).ThenBy(d => d.FirstName)
            .Select(d => new { d.Id, d.ClinicId, d.FirstName, d.LastName, d.ProfessionalLicense, d.Specialty, d.Biography, d.ImageUrl, d.IsActive, d.CreatedAt, d.UpdatedAt })
            .ToListAsync(ct);
        return Results.Ok(new { data });
    }

    private static async Task<IResult> CreateAsync(string clinicId, HttpContext context, DentistRequest? request, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (!Valid(request)) return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Nombre, apellidos, cédula y especialidad son obligatorios" } });
        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId && c.IsActive, ct)) return Results.NotFound(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado o inactivo" } });
        if (await db.Dentists.AnyAsync(d => d.ClinicId == clinicId && d.ProfessionalLicense == request!.ProfessionalLicense.Trim(), ct)) return Results.Conflict(new { error = new { code = "LICENSE_EXISTS", message = "La cédula profesional ya está registrada en este consultorio" } });
        var dentist = new Dentist { ClinicId = clinicId }; Apply(dentist, request!);
        var servicesResult = await SetServicesAsync(db, dentist, request!.DentalServiceIds, ct);
        if (servicesResult is not null) return servicesResult;
        db.Dentists.Add(dentist); await db.SaveChangesAsync(ct);
        return Results.Created($"/api/clinics/{clinicId}/dentists/{dentist.Id}", new { data = await DetailShape(db, dentist.Id, ct) });
    }

    private static async Task<IResult> DetailAsync(string clinicId, string dentistId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        var data = await DetailShape(db, dentistId, ct, clinicId);
        return data is null ? Results.NotFound() : Results.Ok(new { data });
    }

    private static async Task<IResult> UpdateAsync(string clinicId, string dentistId, HttpContext context, DentistRequest? request, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (!Valid(request)) return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Completa los campos obligatorios" } });
        var dentist = await db.Dentists.Include(d => d.DentistServices).SingleOrDefaultAsync(d => d.Id == dentistId && d.ClinicId == clinicId, ct);
        if (dentist is null) return Results.NotFound();
        if (await db.Dentists.AnyAsync(d => d.Id != dentistId && d.ClinicId == clinicId && d.ProfessionalLicense == request!.ProfessionalLicense.Trim(), ct)) return Results.Conflict(new { error = new { code = "LICENSE_EXISTS", message = "La cédula profesional ya está registrada" } });
        Apply(dentist, request!);
        var servicesResult = await SetServicesAsync(db, dentist, request!.DentalServiceIds, ct);
        if (servicesResult is not null) return servicesResult;
        dentist.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        return Results.Ok(new { data = await DetailShape(db, dentistId, ct) });
    }

    private static async Task<IResult> SetStatusAsync(string clinicId, string dentistId, HttpContext context, DentistStatusRequest? request, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (request is null) return Results.BadRequest();
        var dentist = await db.Dentists.SingleOrDefaultAsync(d => d.Id == dentistId && d.ClinicId == clinicId, ct);
        if (dentist is null) return Results.NotFound();
        dentist.IsActive = request.IsActive; dentist.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        return Results.Ok(new { data = new { dentist.Id, dentist.IsActive, dentist.UpdatedAt } });
    }

    private static async Task<IResult> ListScheduleAsync(string clinicId, string dentistId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (!await DentistInClinic(db, clinicId, dentistId, ct)) return Results.NotFound();
        var data = await db.DentistWeeklySchedules.AsNoTracking().Where(s => s.DentistId == dentistId).OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartsAt).ToListAsync(ct);
        return Results.Ok(new { timeZone = DentistScheduleRules.PilotTimeZoneId, data });
    }

    private static async Task<IResult> AddScheduleAsync(string clinicId, string dentistId, HttpContext context, WeeklyScheduleRequest? request, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (!Enum.IsDefined(request?.DayOfWeek ?? (DayOfWeek)(-1)) || request is null) return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Día de la semana inválido" } });
        var dentist = await db.Dentists.SingleOrDefaultAsync(d => d.Id == dentistId && d.ClinicId == clinicId, ct);
        if (dentist is null) return Results.NotFound();
        if (!dentist.IsActive) return Results.Conflict(new { error = new { code = "DENTIST_INACTIVE", message = "No se puede asignar horario a un dentista inactivo" } });
        var schedule = new DentistWeeklySchedule { DentistId = dentistId, DayOfWeek = request.DayOfWeek, StartsAt = request.StartsAt, EndsAt = request.EndsAt, BreakStartsAt = request.BreakStartsAt, BreakEndsAt = request.BreakEndsAt };
        try { DentistScheduleRules.ValidateWeekly(schedule); } catch (ArgumentException ex) { return Results.BadRequest(new { error = new { code = "INVALID_INTERVAL", message = ex.Message } }); }
        var existing = await db.DentistWeeklySchedules.Where(s => s.DentistId == dentistId).ToListAsync(ct);
        if (DentistScheduleRules.WeeklyOverlaps(schedule, existing)) return Results.Conflict(new { error = new { code = "SCHEDULE_OVERLAP", message = "La jornada se superpone con otro intervalo del mismo día" } });
        db.DentistWeeklySchedules.Add(schedule); dentist.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        return Results.Created($"/api/clinics/{clinicId}/dentists/{dentistId}/schedule/{schedule.Id}", new { data = schedule });
    }

    private static async Task<IResult> DeleteScheduleAsync(string clinicId, string dentistId, string scheduleId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        var item = await db.DentistWeeklySchedules.SingleOrDefaultAsync(s => s.Id == scheduleId && s.DentistId == dentistId && s.Dentist.ClinicId == clinicId, ct);
        if (item is null) return Results.NotFound(); db.DentistWeeklySchedules.Remove(item); await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static async Task<IResult> ListBlocksAsync(string clinicId, string dentistId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (!await DentistInClinic(db, clinicId, dentistId, ct)) return Results.NotFound();
        var data = await db.DentistScheduleBlocks.AsNoTracking().Where(b => b.DentistId == dentistId).OrderBy(b => b.StartsAt).ToListAsync(ct); return Results.Ok(new { data });
    }

    private static async Task<IResult> AddBlockAsync(string clinicId, string dentistId, HttpContext context, BlockRequest? request, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        if (request is null) return Results.BadRequest();
        var dentist = await db.Dentists.SingleOrDefaultAsync(d => d.Id == dentistId && d.ClinicId == clinicId, ct);
        if (dentist is null) return Results.NotFound();
        if (!dentist.IsActive) return Results.Conflict(new { error = new { code = "DENTIST_INACTIVE", message = "No se puede bloquear agenda de un dentista inactivo" } });
        var block = new DentistScheduleBlock { DentistId = dentistId, StartsAt = request.StartsAt, EndsAt = request.EndsAt, Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim() };
        try { DentistScheduleRules.ValidateBlock(block); } catch (ArgumentException ex) { return Results.BadRequest(new { error = new { code = "INVALID_INTERVAL", message = ex.Message } }); }
        var existing = await db.DentistScheduleBlocks.Where(b => b.DentistId == dentistId).ToListAsync(ct);
        if (DentistScheduleRules.BlocksOverlap(block, existing)) return Results.Conflict(new { error = new { code = "BLOCK_OVERLAP", message = "El bloqueo se superpone con otro bloqueo" } });
        // El módulo de citas aún no existe: no hay citas confirmadas persistidas que puedan ser afectadas. Al integrarlo, conectar su comprobador antes de aceptar bloqueos.
        db.DentistScheduleBlocks.Add(block); await db.SaveChangesAsync(ct); return Results.Created($"/api/clinics/{clinicId}/dentists/{dentistId}/blocks/{block.Id}", new { data = block });
    }

    private static async Task<IResult> DeleteBlockAsync(string clinicId, string dentistId, string blockId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Forbidden(context, clinicId);
        var item = await db.DentistScheduleBlocks.SingleOrDefaultAsync(b => b.Id == blockId && b.DentistId == dentistId && b.Dentist.ClinicId == clinicId, ct);
        if (item is null) return Results.NotFound(); db.DentistScheduleBlocks.Remove(item); await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static bool Valid(DentistRequest? r) => r is not null && !string.IsNullOrWhiteSpace(r.FirstName) && !string.IsNullOrWhiteSpace(r.LastName) && !string.IsNullOrWhiteSpace(r.ProfessionalLicense) && !string.IsNullOrWhiteSpace(r.Specialty);
    private static void Apply(Dentist d, DentistRequest r) { d.FirstName = r.FirstName.Trim(); d.LastName = r.LastName.Trim(); d.ProfessionalLicense = r.ProfessionalLicense.Trim(); d.Specialty = r.Specialty.Trim(); d.Biography = r.Biography?.Trim() ?? string.Empty; d.ImageUrl = string.IsNullOrWhiteSpace(r.ImageUrl) ? null : r.ImageUrl.Trim(); }
    private static async Task<IResult?> SetServicesAsync(DentalConnectDbContext db, Dentist dentist, string[]? ids, CancellationToken ct)
    {
        var requested = (ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var clinicServiceIds = await db.ClinicServices.Where(cs => cs.ClinicId == dentist.ClinicId && cs.IsActive && requested.Contains(cs.DentalServiceId) && cs.DentalService.IsActive).Select(cs => cs.DentalServiceId).ToListAsync(ct);
        if (requested.Except(clinicServiceIds, StringComparer.Ordinal).Any()) return Results.BadRequest(new { error = new { code = "INVALID_SERVICE", message = "Cada servicio debe estar activo y pertenecer al consultorio" } });
        var current = await db.DentistServices.Where(ds => ds.DentistId == dentist.Id).ToListAsync(ct); db.DentistServices.RemoveRange(current);
        foreach (var id in requested) db.DentistServices.Add(new DentistService { DentistId = dentist.Id, DentalServiceId = id });
        return null;
    }
    private static Task<bool> DentistInClinic(DentalConnectDbContext db, string clinicId, string dentistId, CancellationToken ct) => db.Dentists.AnyAsync(d => d.Id == dentistId && d.ClinicId == clinicId, ct);
    private static async Task<object?> DetailShape(DentalConnectDbContext db, string id, CancellationToken ct, string? clinicId = null) =>
        await db.Dentists.AsNoTracking().Where(d => d.Id == id && (clinicId == null || d.ClinicId == clinicId))
            .Select(d => new { d.Id, d.ClinicId, d.FirstName, d.LastName, d.ProfessionalLicense, d.Specialty, d.Biography, d.ImageUrl, d.IsActive, d.CreatedAt, d.UpdatedAt, Services = d.DentistServices.Select(ds => new { ds.DentalService.Id, ds.DentalService.Name, ds.DentalService.Description, ds.DentalService.EstimatedDurationMinutes }).ToList() })
            .SingleOrDefaultAsync(ct);

    public sealed record DentistRequest(string FirstName, string LastName, string ProfessionalLicense, string Specialty, string? Biography, string? ImageUrl, string[]? DentalServiceIds);
    public sealed record DentistStatusRequest(bool IsActive);
    public sealed record WeeklyScheduleRequest(DayOfWeek DayOfWeek, TimeOnly StartsAt, TimeOnly EndsAt, TimeOnly? BreakStartsAt, TimeOnly? BreakEndsAt);
    public sealed record BlockRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);
}
