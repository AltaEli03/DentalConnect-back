using System.Net;
using System.Net.Mail;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DentalConnect;

public sealed class AppointmentEmailWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AppointmentEmailWorker> logger) : BackgroundService
{
    private readonly string host = configuration["SMTP_HOST"]!;
    private readonly int port = int.TryParse(configuration["SMTP_PORT"], out var parsedPort) ? parsedPort : 587;
    private readonly string username = configuration["SMTP_USERNAME"]!;
    private readonly string password = configuration["SMTP_PASSWORD"]!;
    private readonly string sender = configuration["MAIL_FROM"]!;
    private readonly string frontendOrigin = (configuration["FRONTEND_ORIGIN"] ?? string.Empty).TrimEnd('/');

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessOneAsync(stoppingToken);
                if (!processed) await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "No fue posible entregar un correo de DentalConnect; se reintentará.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string eventId, appointmentId, eventType, folio, clinicName, clinicEmail, patientEmail, patientName, timeZoneId;
        DateTimeOffset startsAt;
        DateTimeOffset? proposedStartsAt;
        string? rejectionReason;
        await using (var command = new NpgsqlCommand("""
            SELECT e."id", a."id", e."eventType", a."folio", c."name", c."email", a."patientEmail",
                   concat_ws(' ', a."patientFirstName", a."patientLastName"), a."startsAt",
                   a."proposedStartsAt", a."rejectionReason", a."timeZoneId"
            FROM "ClinicNotificationEvent" e
            JOIN "Appointment" a ON a."id" = e."appointmentId"
            JOIN "Clinic" c ON c."id" = a."clinicId"
            WHERE e."processedAt" IS NULL
            ORDER BY e."createdAt"
            LIMIT 1
            FOR UPDATE OF e SKIP LOCKED
            """, connection, transaction))
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) { await transaction.CommitAsync(ct); return false; }
            eventId = reader.GetString(0); appointmentId = reader.GetString(1); eventType = reader.GetString(2); folio = reader.GetString(3);
            clinicName = reader.GetString(4); clinicEmail = reader.GetString(5);
            patientEmail = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            patientName = reader.IsDBNull(7) ? "" : reader.GetString(7);
            startsAt = reader.GetFieldValue<DateTimeOffset>(8);
            proposedStartsAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9);
            rejectionReason = reader.IsDBNull(10) ? null : reader.GetString(10);
            timeZoneId = reader.GetString(11);
        }

        var clinicLink = BuildFrontendLink($"/administrar-solicitudes?appointmentId={Uri.EscapeDataString(appointmentId)}");
        var patientLink = eventType == "APPOINTMENT_ALTERNATIVE_PROPOSED"
            ? BuildFrontendLink($"/mi-solicitud?appointmentId={Uri.EscapeDataString(appointmentId)}")
            : BuildFrontendLink("/mis-citas");
        var (recipient, subject, body) = BuildMessage(eventType, folio, clinicName, patientName,
            startsAt, proposedStartsAt, rejectionReason, timeZoneId, clinicEmail, patientEmail, clinicLink, patientLink);
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            await SendAsync(recipient, subject, body, ct);
            if (eventType == "APPOINTMENT_REQUESTED" && !string.IsNullOrWhiteSpace(clinicEmail))
                await SendAsync(clinicEmail, "DentalConnect: nueva solicitud de cita", $"Llegó una solicitud {folio} de {patientName} para {requestedTime(startsAt, timeZoneId)}. Gestiona la solicitud en DentalConnect: {clinicLink}", ct);
        }

        await using var mark = new NpgsqlCommand("""
            UPDATE "ClinicNotificationEvent"
            SET "processedAt" = CURRENT_TIMESTAMP
            WHERE "id" = @id
            """, connection, transaction);
        mark.Parameters.AddWithValue("id", eventId);
        await mark.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Correo de evento {EventType} enviado para folio {Folio}.", eventType, folio);
        return true;
    }

    private async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        using var message = new MailMessage(sender, recipient, subject, body) { IsBodyHtml = false };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password),
            Timeout = 15000
        };
        await client.SendMailAsync(message, ct);
    }

    private static (string Recipient, string Subject, string Body) BuildMessage(
        string eventType, string folio, string clinicName, string patientName,
        DateTimeOffset startsAt, DateTimeOffset? proposedStartsAt, string? reason,
        string timeZoneId, string clinicEmail, string patientEmail, string clinicLink, string patientLink)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        var requested = TimeZoneInfo.ConvertTime(startsAt, zone).ToString("dd/MM/yyyy HH:mm");
        var proposed = proposedStartsAt is null ? "" : TimeZoneInfo.ConvertTime(proposedStartsAt.Value, zone).ToString("dd/MM/yyyy HH:mm");
        var message = eventType switch
        {
            "APPOINTMENT_REQUESTED" => (patientEmail, "DentalConnect: solicitud recibida", $"Hola {patientName}. Recibimos tu solicitud {folio} para {clinicName}, el {requested}. Está pendiente de aceptación por el consultorio. Consulta el estado en DentalConnect: {patientLink}"),
            "APPOINTMENT_CONFIRMED" => (patientEmail, "DentalConnect: cita confirmada", $"Hola {patientName}. Tu cita {folio} con {clinicName} quedó confirmada para el {requested}. Procura llegar a tiempo. Ver mis citas: {patientLink}"),
            "APPOINTMENT_REJECTED" => (patientEmail, "DentalConnect: solicitud rechazada", $"Hola {patientName}. El consultorio {clinicName} rechazó la solicitud {folio}. Motivo: {reason ?? "Consulta el detalle en DentalConnect."} Ver mis citas: {patientLink}"),
            "APPOINTMENT_ALTERNATIVE_PROPOSED" => (patientEmail, "DentalConnect: horario alternativo propuesto", $"Hola {patientName}. El consultorio {clinicName} propone el {proposed} para tu solicitud {folio}. Acepta o rechaza la propuesta en DentalConnect: {patientLink}"),
            "APPOINTMENT_ALTERNATIVE_ACCEPTED" => (clinicEmail, "DentalConnect: horario alternativo aceptado", $"El paciente aceptó el horario alternativo de la solicitud {folio}. La cita quedó confirmada para {proposedOrRequested(proposed, requested)}. Ver solicitudes: {clinicLink}"),
            "APPOINTMENT_ALTERNATIVE_REJECTED" => (clinicEmail, "DentalConnect: horario alternativo rechazado", $"El paciente rechazó el horario alternativo de la solicitud {folio}. Revisa las solicitudes en DentalConnect: {clinicLink}"),
            "APPOINTMENT_ALTERNATIVE_EXPIRED" => (clinicEmail, "DentalConnect: propuesta vencida", $"La propuesta para la solicitud {folio} venció y la solicitud volvió a pendiente. Ver solicitudes: {clinicLink}"),
            _ => (string.Empty, string.Empty, string.Empty)
        };
        return message;
    }

    private string BuildFrontendLink(string path)
    {
        if (!Uri.TryCreate(frontendOrigin, UriKind.Absolute, out var origin) ||
            (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp))
            return string.Empty;
        return new Uri(origin, path.TrimStart('/')).AbsoluteUri;
    }

    private static string proposedOrRequested(string proposed, string requested) => string.IsNullOrWhiteSpace(proposed) ? requested : proposed;
    private static string requestedTime(DateTimeOffset startsAt, string timeZoneId)
    {
        try { return TimeZoneInfo.ConvertTime(startsAt, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).ToString("dd/MM/yyyy HH:mm"); }
        catch (TimeZoneNotFoundException) { return startsAt.ToString("dd/MM/yyyy HH:mm 'UTC'"); }
    }
}
