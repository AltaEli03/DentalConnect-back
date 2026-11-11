using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DentalConnect;

public sealed record CreateAppointmentRequest(string ClinicId, string DentistId, string DentalServiceId, DateTimeOffset StartsAt);
public sealed record RejectAppointmentRequest(string? Reason);
public sealed record ProposeAlternativeRequest(DateTimeOffset ProposedStartsAt);

public static class AppointmentEndpoints
{
    private const string AlternativeProposedEvent = "APPOINTMENT_ALTERNATIVE_PROPOSED";
    private const string AlternativeAcceptedEvent = "APPOINTMENT_ALTERNATIVE_ACCEPTED";
    private const string AlternativeRejectedEvent = "APPOINTMENT_ALTERNATIVE_REJECTED";
    private const string AlternativeExpiredEvent = "APPOINTMENT_ALTERNATIVE_EXPIRED";
    private static readonly string[] AppointmentStatuses = ["PENDING", "CONFIRMED", "REJECTED", "CANCELLED", "PROPOSED"];
    private static readonly string[] ClinicalTerms = ["diagnóstico", "diagnostico", "síntoma", "sintoma", "enfermedad", "medicamento", "tratamiento", "expediente", "historial clínico", "historial clinico"];

    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/appointments", CreateAsync).RequireAuthorization();
        endpoints.MapGet("/api/appointments", ListForClinicAsync).RequireAuthorization();
        endpoints.MapGet("/api/appointments/{appointmentId}", DetailForClinicAsync).RequireAuthorization();
        endpoints.MapPost("/api/appointments/{appointmentId}/confirm", ConfirmAsync).RequireAuthorization();
        endpoints.MapPost("/api/appointments/{appointmentId}/reject", RejectAsync).RequireAuthorization();
        endpoints.MapPost("/api/appointments/{appointmentId}/propose", ProposeAsync).RequireAuthorization();
        endpoints.MapGet("/api/appointments/{appointmentId}/proposal", GetProposalAsync).RequireAuthorization();
        endpoints.MapPost("/api/appointments/{appointmentId}/proposal/accept", AcceptProposalAsync).RequireAuthorization();
        endpoints.MapPost("/api/appointments/{appointmentId}/proposal/reject", RejectProposalAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(HttpContext http, CreateAppointmentRequest? body, DentalConnectDbContext db, CancellationToken ct)
    {
        var patient = http.User.FindFirst("sub")?.Value ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
            var appointment = new Appointment
            {
                Folio = $"DC-{Guid.NewGuid():N}"[..15].ToUpperInvariant(), PatientSubject = patient,
                PatientFirstName = ClaimValue(http.User, "given_name", "first_name"),
                PatientLastName = ClaimValue(http.User, "family_name", "last_name"),
                PatientEmail = ClaimValue(http.User, "email"), PatientPhone = ClaimValue(http.User, "phone_number"),
                ClinicId = body.ClinicId, DentistId = dentist.Id, DentalServiceId = service.Id,
                StartsAt = start, EndsAt = end, IdempotencyKey = key, RequestFingerprint = fingerprint,
                TimeZoneId = PublicAvailabilityRules.PilotTimeZoneId
            };
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

    private static async Task<IResult> ListForClinicAsync(HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetClinicContext(http, out var clinicId, out _)) return Results.Forbid();
        var statusText = http.Request.Query["status"].ToString().Trim();
        var fromText = http.Request.Query["dateFrom"].ToString().Trim();
        var toText = http.Request.Query["dateTo"].ToString().Trim();
        string? status = null;
        if (statusText.Length > 0)
        {
            status = statusText.ToUpperInvariant();
            if (!AppointmentStatuses.Contains(status)) return Validation("El estado no es válido");
        }
        DateOnly? fromDate = null;
        DateOnly? toDate = null;
        if (fromText.Length > 0)
        {
            if (!DateOnly.TryParseExact(fromText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return Validation("dateFrom debe usar formato AAAA-MM-DD");
            fromDate = parsed;
        }
        if (toText.Length > 0)
        {
            if (!DateOnly.TryParseExact(toText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return Validation("dateTo debe usar formato AAAA-MM-DD");
            toDate = parsed;
        }
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate) return Validation("dateFrom no puede ser posterior a dateTo");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
        var query = from a in db.Appointments.AsNoTracking()
                    join d in db.Dentists.AsNoTracking() on a.DentistId equals d.Id
                    join s in db.DentalServices.AsNoTracking() on a.DentalServiceId equals s.Id
                    where a.ClinicId == clinicId
                    select new { a, d, s };
        if (status is not null) query = query.Where(x => x.a.Status == status);
        if (fromDate.HasValue) query = query.Where(x => x.a.StartsAt >= StartOfDay(fromDate.Value, zone));
        if (toDate.HasValue) query = query.Where(x => x.a.StartsAt < StartOfDay(toDate.Value.AddDays(1), zone));
        var data = await query.OrderBy(x => x.a.CreatedAt).ThenBy(x => x.a.Id).Select(x => new
        {
            id = x.a.Id, folio = x.a.Folio, status = x.a.Status, startsAt = x.a.StartsAt, endsAt = x.a.EndsAt,
            proposedStartsAt = x.a.ProposedStartsAt, proposedEndsAt = x.a.ProposedEndsAt, proposalExpiresAt = x.a.ProposalExpiresAt,
            receivedAt = x.a.CreatedAt, clinicId = x.a.ClinicId, dentist = new { id = x.d.Id, firstName = x.d.FirstName, lastName = x.d.LastName },
            service = new { id = x.s.Id, name = x.s.Name, estimatedDurationMinutes = x.s.EstimatedDurationMinutes }
        }).ToListAsync(ct);
        return Results.Json(new { data });
    }

    private static async Task<IResult> DetailForClinicAsync(string appointmentId, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetClinicContext(http, out var clinicId, out _)) return Results.Forbid();
        var item = await (from a in db.Appointments.AsNoTracking()
                          join d in db.Dentists.AsNoTracking() on a.DentistId equals d.Id
                          join s in db.DentalServices.AsNoTracking() on a.DentalServiceId equals s.Id
                          where a.Id == appointmentId && a.ClinicId == clinicId
                          select new { a, d, s }).SingleOrDefaultAsync(ct);
        if (item is null) return Results.NotFound();
        var localStart = TimeZoneInfo.ConvertTime(item.a.StartsAt, TimeZoneInfo.FindSystemTimeZoneById(item.a.TimeZoneId));
        return Results.Json(new { data = new
        {
            id = item.a.Id, folio = item.a.Folio, status = item.a.Status, receivedAt = item.a.CreatedAt,
            patient = new { firstName = item.a.PatientFirstName, lastName = item.a.PatientLastName, email = item.a.PatientEmail, phone = item.a.PatientPhone },
            clinicId = item.a.ClinicId, dentist = new { id = item.d.Id, firstName = item.d.FirstName, lastName = item.d.LastName },
            service = new { id = item.s.Id, name = item.s.Name, estimatedDurationMinutes = item.s.EstimatedDurationMinutes },
            startsAt = item.a.StartsAt, endsAt = item.a.EndsAt, localStartsAt = localStart, timeZoneId = item.a.TimeZoneId,
            proposedStartsAt = item.a.ProposedStartsAt, proposedEndsAt = item.a.ProposedEndsAt, proposalExpiresAt = item.a.ProposalExpiresAt,
            rejection = item.a.Status == "REJECTED" ? new { reason = item.a.RejectionReason, rejectedAt = item.a.RejectedAt, rejectedBy = item.a.RejectedBy } : null
        } });
    }

    private static async Task<IResult> ConfirmAsync(string appointmentId, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetClinicContext(http, out var clinicId, out _)) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClinicId == clinicId, ct);
            if (appointment is null) return Results.NotFound();
            if (appointment.Status != "PENDING") { await tx.RollbackAsync(ct); return Conflict("INVALID_STATUS", "Solo se pueden confirmar solicitudes pendientes"); }
            var dentist = await db.Dentists.Include(d => d.WeeklySchedules).Include(d => d.ScheduleBlocks).SingleOrDefaultAsync(d => d.Id == appointment.DentistId && d.IsActive && d.ClinicId == clinicId, ct);
            if (dentist is null) { await tx.RollbackAsync(ct); return Conflict("DENTIST_UNAVAILABLE", "El dentista ya no está disponible"); }
                            var service = await db.DentalServices.AsNoTracking().SingleOrDefaultAsync(s => s.Id == appointment.DentalServiceId && s.IsActive, ct);
                if (service is null || (appointment.EndsAt - appointment.StartsAt).TotalMinutes != service.EstimatedDurationMinutes)
                {
                    await tx.RollbackAsync(ct);
                    return Conflict("SERVICE_CHANGED", "El servicio ya no está activo o su duración cambió; solicita un horario actualizado");
                }
var patientConflict = await db.Appointments.AsNoTracking().AnyAsync(a => a.Id != appointment.Id && a.PatientSubject == appointment.PatientSubject && a.Status == "CONFIRMED" && a.StartsAt < appointment.EndsAt && a.EndsAt > appointment.StartsAt, ct);
            if (patientConflict) { await tx.RollbackAsync(ct); return Conflict("PATIENT_CONFLICT", "El paciente ya tiene una cita confirmada en ese intervalo"); }
            var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt, zone).DateTime);
            var others = await db.Appointments.AsNoTracking().Where(a => a.DentistId == dentist.Id && a.Id != appointment.Id && a.Status == "CONFIRMED" && a.StartsAt < appointment.EndsAt && a.EndsAt > appointment.StartsAt).ToListAsync(ct);
            var slots = PublicAvailabilityRules.Generate(day, 1, (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes, dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, DateTimeOffset.UtcNow, others);
            if (!slots.Any(s => s.StartsAt == appointment.StartsAt && s.EndsAt == appointment.EndsAt)) { await tx.RollbackAsync(ct); return Conflict("SLOT_UNAVAILABLE", "El horario ya no está disponible"); }
            appointment.Status = "CONFIRMED";
            db.ClinicNotificationEvents.Add(new ClinicNotificationEvent { AppointmentId = appointment.Id, EventType = "APPOINTMENT_CONFIRMED" });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppointmentResponse(appointment, StatusCodes.Status200OK);
        }
        catch (Exception ex) when (SqlState(ex) is "23P01" or "40001")
        {
            await tx.RollbackAsync(ct);
            return Conflict("APPOINTMENT_CONFLICT", "Otra solicitud confirmó un intervalo que se cruza con este horario o con la agenda del paciente");
        }
    }

    private static async Task<IResult> RejectAsync(string appointmentId, RejectAppointmentRequest? body, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetClinicContext(http, out var clinicId, out var actor)) return Results.Forbid();
        var reason = body?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length is < 10 or > 250)
            return Results.BadRequest(new { error = new { code = "INVALID_REJECTION_REASON", message = "Escribe un motivo de 10 a 250 caracteres" } });
        if (ClinicalTerms.Any(term => reason.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return Results.BadRequest(new { error = new { code = "CLINICAL_DATA_NOT_ALLOWED", message = "El motivo no debe incluir información clínica del paciente" } });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClinicId == clinicId, ct);
            if (appointment is null) return Results.NotFound();
            if (appointment.Status != "PENDING") { await tx.RollbackAsync(ct); return Conflict("INVALID_STATUS", "Solo se pueden rechazar solicitudes pendientes"); }
            appointment.Status = "REJECTED";
            appointment.RejectionReason = reason;
            appointment.RejectedAt = DateTimeOffset.UtcNow;
            appointment.RejectedBy = actor;
            db.ClinicNotificationEvents.Add(new ClinicNotificationEvent { AppointmentId = appointment.Id, EventType = "APPOINTMENT_REJECTED" });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppointmentResponse(appointment, StatusCodes.Status200OK);
        }
        catch (Exception ex) when (SqlState(ex) is "40001" or "23505")
        {
            await tx.RollbackAsync(ct);
            return Conflict("APPOINTMENT_CONFLICT", "La solicitud cambió al mismo tiempo; recarga e inténtalo nuevamente");
        }
    }

    private static async Task<IResult> ProposeAsync(string appointmentId, ProposeAlternativeRequest? body, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetClinicContext(http, out var clinicId, out _)) return Results.Forbid();
        if (body is null || body.ProposedStartsAt == default) return Validation("El horario propuesto es obligatorio");

        var now = DateTimeOffset.UtcNow;
        var start = body.ProposedStartsAt.ToUniversalTime();

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId && a.ClinicId == clinicId, ct);
            if (appointment is null) return Results.NotFound();
            if (AppointmentProposalRules.IsPast(now, start)) return Conflict("PAST_DATE", "La fecha y hora deben ser futuras");
            if (appointment.Status == "PROPOSED") return Conflict("PROPOSAL_ALREADY_ACTIVE", "Ya existe una propuesta activa para esta solicitud");
            if (appointment.Status != "PENDING") return Conflict("INVALID_STATUS", "Solo se puede proponer sobre solicitudes pendientes");
            if (start == appointment.StartsAt) return Conflict("PROPOSAL_SAME_AS_ORIGINAL", "El horario propuesto debe ser distinto del original");

            var dentist = await db.Dentists.Include(d => d.WeeklySchedules).Include(d => d.ScheduleBlocks)
                .Include(d => d.DentistServices).ThenInclude(ds => ds.DentalService)
                .SingleOrDefaultAsync(d => d.Id == appointment.DentistId && d.ClinicId == clinicId && d.IsActive, ct);
            if (dentist is null) return Conflict("DENTIST_UNAVAILABLE", "El dentista ya no está disponible");

            var service = await db.DentalServices.AsNoTracking().SingleOrDefaultAsync(s => s.Id == appointment.DentalServiceId && s.IsActive, ct);
            var clinicOffers = await db.ClinicServices.AsNoTracking().AnyAsync(cs => cs.ClinicId == clinicId && cs.DentalServiceId == appointment.DentalServiceId && cs.IsActive && cs.DentalService.IsActive, ct);
            var dentistOffers = dentist.DentistServices.Any(ds => ds.DentalServiceId == appointment.DentalServiceId && ds.DentalService.IsActive);
            if (service is null || service.EstimatedDurationMinutes <= 0 || !clinicOffers || !dentistOffers ||
                (appointment.EndsAt - appointment.StartsAt).TotalMinutes != service.EstimatedDurationMinutes)
                return Conflict("SERVICE_CHANGED", "El servicio ya no está disponible o cambió; solicita un horario actualizado");

            var end = start.AddMinutes(service.EstimatedDurationMinutes);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
            var confirmed = await db.Appointments.AsNoTracking()
                .Where(a => a.DentistId == dentist.Id && a.Status == "CONFIRMED" && a.StartsAt < end && a.EndsAt > start)
                .ToListAsync(ct);
            var slots = PublicAvailabilityRules.Generate(day, 1, service.EstimatedDurationMinutes, dentist.WeeklySchedules, dentist.ScheduleBlocks, zone, now, confirmed);
            if (!slots.Any(s => s.StartsAt == start && s.EndsAt == end))
                return Conflict("SLOT_UNAVAILABLE", "El horario propuesto no está disponible");

            appointment.Status = "PROPOSED";
            appointment.ProposedStartsAt = start;
            appointment.ProposedEndsAt = end;
            appointment.ProposalExpiresAt = AppointmentProposalRules.ExpiresAt(now, start);
            // NOTE (DENT-14): la aceptación/rechazo de la propuesta es de HU-14.
            // NOTE (DENT-15) / NOTE (DENT-31): la entrega al paciente y el vencimiento son de HU-15/TEC-07.
            var notice = await db.ClinicNotificationEvents.SingleOrDefaultAsync(e => e.AppointmentId == appointment.Id && e.EventType == AlternativeProposedEvent, ct);
            if (notice is null)
                db.ClinicNotificationEvents.Add(new ClinicNotificationEvent { AppointmentId = appointment.Id, EventType = AlternativeProposedEvent });
            else
            {
                notice.ProcessedAt = null;
                notice.CreatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppointmentResponse(appointment, StatusCodes.Status200OK);
        }
        catch (Exception ex) when (SqlState(ex) is "40001" or "23505")
        {
            try { await tx.RollbackAsync(ct); }
            catch (InvalidOperationException rollbackError) { Console.Error.WriteLine(rollbackError); }
            return Conflict("APPOINTMENT_CONFLICT", "La solicitud cambió al mismo tiempo; recarga e inténtalo nuevamente");
        }
    }

    private static async Task<IResult> GetProposalAsync(string appointmentId, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetPatientSubject(http, out var patient)) return Results.Unauthorized();
        var appointment = await db.Appointments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId && a.PatientSubject == patient, ct);
        return appointment is null ? Results.NotFound() : AppointmentResponse(appointment, StatusCodes.Status200OK);
    }

    private static Task<IResult> AcceptProposalAsync(string appointmentId, HttpContext http, DentalConnectDbContext db, CancellationToken ct) =>
        RespondToProposalAsync(appointmentId, true, http, db, ct);

    private static Task<IResult> RejectProposalAsync(string appointmentId, HttpContext http, DentalConnectDbContext db, CancellationToken ct) =>
        RespondToProposalAsync(appointmentId, false, http, db, ct);

    private static async Task<IResult> RespondToProposalAsync(string appointmentId, bool accept, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetPatientSubject(http, out var patient)) return Results.Unauthorized();

        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId && a.PatientSubject == patient, ct);
            if (appointment is null) return Results.NotFound();
            if (appointment.Status != "PROPOSED") return Conflict("PROPOSAL_NOT_ACTIVE", "La solicitud no tiene una propuesta activa");
            if (appointment.ProposedStartsAt is null || appointment.ProposedEndsAt is null || appointment.ProposalExpiresAt is null)
                return Conflict("PROPOSAL_NOT_ACTIVE", "La propuesta está incompleta; solicita un nuevo horario");

            if (AppointmentProposalRules.IsExpired(now, appointment.ProposalExpiresAt.Value))
            {
                appointment.Status = "PENDING";
                appointment.ProposedStartsAt = null;
                appointment.ProposedEndsAt = null;
                appointment.ProposalExpiresAt = null;
                // NOTE (DENT-31): barrido proactivo y aviso de vencimiento en TEC-07.
                await QueueClinicEventAsync(db, appointment.Id, AlternativeExpiredEvent, now, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Conflict("PROPOSAL_EXPIRED", "La propuesta venció; la solicitud volvió a pendiente");
            }

            if (accept)
            {
                var proposedStart = appointment.ProposedStartsAt.Value.ToUniversalTime();
                var proposedEnd = appointment.ProposedEndsAt.Value.ToUniversalTime();
                appointment.StartsAt = proposedStart;
                appointment.EndsAt = proposedEnd;
                appointment.Status = "CONFIRMED";
                appointment.ProposedStartsAt = null;
                appointment.ProposedEndsAt = null;
                appointment.ProposalExpiresAt = null;
                await QueueClinicEventAsync(db, appointment.Id, AlternativeAcceptedEvent, now, ct);
            }
            else
            {
                appointment.Status = "REJECTED";
                appointment.RejectedAt = now;
                appointment.RejectedBy = patient;
                await QueueClinicEventAsync(db, appointment.Id, AlternativeRejectedEvent, now, ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppointmentResponse(appointment, StatusCodes.Status200OK);
        }
        catch (Exception ex) when (SqlState(ex) is "40001" or "23505" or "23P01")
        {
            try { await tx.RollbackAsync(ct); }
            catch (InvalidOperationException rollbackError) { Console.Error.WriteLine(rollbackError); }
            return Conflict("APPOINTMENT_CONFLICT", "La solicitud cambió al mismo tiempo; recarga e inténtalo nuevamente");
        }
    }

    private static bool TryGetPatientSubject(HttpContext http, out string subject)
    {
        subject = http.User.FindFirst("sub")?.Value ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(subject);
    }

    // NOTE (DENT-15): visualización in-app del aviso en HU-15.
    private static async Task QueueClinicEventAsync(DentalConnectDbContext db, string appointmentId, string eventType, DateTimeOffset now, CancellationToken ct)
    {
        var notice = await db.ClinicNotificationEvents.SingleOrDefaultAsync(e => e.AppointmentId == appointmentId && e.EventType == eventType, ct);
        if (notice is null)
        {
            db.ClinicNotificationEvents.Add(new ClinicNotificationEvent { AppointmentId = appointmentId, EventType = eventType, CreatedAt = now });
            return;
        }
        notice.ProcessedAt = null;
        notice.CreatedAt = now;
    }

    private static bool TryGetClinicContext(HttpContext http, out string clinicId, out string actor)
    {
        clinicId = http.User.FindFirst("clinic_id")?.Value ?? http.User.FindFirst("custom:clinic_id")?.Value ?? string.Empty;
        actor = http.User.FindFirst("sub")?.Value ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        return http.User.IsInRole("CLINIC_ADMIN") && !string.IsNullOrWhiteSpace(clinicId) && !string.IsNullOrWhiteSpace(actor);
    }

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone) => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone));
    private static string? ClaimValue(ClaimsPrincipal principal, params string[] names) => names.Select(name => principal.FindFirst(name)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static IResult Validation(string message) => Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message } });
    private static string? SqlState(Exception error) => error switch
    {
        PostgresException postgres => postgres.SqlState,
        DbUpdateException { InnerException: PostgresException postgres } => postgres.SqlState,
        _ => error.InnerException is null ? null : SqlState(error.InnerException)
    };
    private static IResult AppointmentResponse(Appointment item, int statusCode) => Results.Json(new { data = new { item.Id, item.Folio, item.Status, item.ClinicId, item.DentistId, item.DentalServiceId, item.StartsAt, item.EndsAt, item.ProposedStartsAt, item.ProposedEndsAt, item.ProposalExpiresAt, item.TimeZoneId } }, statusCode: statusCode);
    private static IResult Conflict(string code, string message) => Results.Conflict(new { error = new { code, message } });
}
