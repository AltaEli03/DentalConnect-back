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
        string eventId, eventType, folio, clinicName, clinicEmail, patientEmail, patientName, timeZoneId;
        DateTimeOffset startsAt;
        DateTimeOffset? proposedStartsAt;
        string? rejectionReason;
        await using (var command = new NpgsqlCommand("""
            SELECT e."id", e."eventType", a."folio", c."name", c."email", a."patientEmail",
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
            eventId = reader.GetString(0); eventType = reader.GetString(1); folio = reader.GetString(2);
            clinicName = reader.GetString(3); clinicEmail = reader.GetString(4);
            patientEmail = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            patientName = reader.IsDBNull(6) ? "" : reader.GetString(6);
            startsAt = reader.GetFieldValue<DateTimeOffset>(7);
            proposedStartsAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8);
            rejectionReason = reader.IsDBNull(9) ? null : reader.GetString(9);
            timeZoneId = reader.GetString(10);
        }

        var (recipient, subject, body) = BuildMessage(eventType, folio, clinicName, patientName,
            startsAt, proposedStartsAt, rejectionReason, timeZoneId, clinicEmail, patientEmail);
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            await SendAsync(recipient, subject, body, ct);
            if (eventType == "APPOINTMENT_REQUESTED" && !string.IsNullOrWhiteSpace(clinicEmail))
                await SendAsync(clinicEmail, "DentalConnect: nueva solicitud de cita", $"Llegó una solicitud {folio} de {patientName} para {requestedTime(startsAt, timeZoneId)}. Entra a DentalConnect para revisarla.", ct);
        }

        await using var mark = new NpgsqlCommand("UPDATE \"ClinicNotificationEvent\" SET \"processedAt\" = CURRENT_TIMESTAMP WHERE \"id\" = @id", connection, transaction);
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
        string timeZoneId, string clinicEmail, string patientEmail)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        var requested = TimeZoneInfo.ConvertTime(startsAt, zone).ToString("dd/MM/yyyy HH:mm");
        var proposed = proposedStartsAt is null ? "" : TimeZoneInfo.ConvertTime(proposedStartsAt.Value, zone).ToString("dd/MM/yyyy HH:mm");
        return eventType switch
        {
            "APPOINTMENT_REQUESTED" => (patientEmail, "DentalConnect: solicitud recibida", $"Hola {patientName},\n\nRecibimos tu solicitud {folio} para {clinicName}, el {requested}. Está pendiente de aceptación por el consultorio.\n\nDentalConnect"),
            "APPOINTMENT_CONFIRMED" => (patientEmail, "DentalConnect: cita confirmada", $"Hola {patientName},\n\nTu cita {folio} con {clinicName} quedó confirmada para el {requested}. Procura llegar a tiempo.\n\nDentalConnect"),
            "APPOINTMENT_REJECTED" => (patientEmail, "DentalConnect: solicitud rechazada", $"Hola {patientName},\n\nEl consultorio {clinicName} rechazó la solicitud {folio}. Motivo: {reason ?? "Consulta el detalle en DentalConnect."}\n\nDentalConnect"),
            "APPOINTMENT_ALTERNATIVE_PROPOSED" => (patientEmail, "DentalConnect: horario alternativo propuesto", $"Hola {patientName},\n\nEl consultorio {clinicName} propone el {proposed} para tu solicitud {folio}. Entra a DentalConnect para aceptar o rechazar la propuesta.\n\nDentalConnect"),
            "APPOINTMENT_ALTERNATIVE_ACCEPTED" => (clinicEmail, "DentalConnect: horario alternativo aceptado", $"El paciente aceptó el horario alternativo de la solicitud {folio}. La cita quedó confirmada para {proposedOrRequested(proposed, requested)}."),
            "APPOINTMENT_ALTERNATIVE_REJECTED" => (clinicEmail, "DentalConnect: horario alternativo rechazado", $"El paciente rechazó el horario alternativo de la solicitud {folio}. Revisa las solicitudes en DentalConnect."),
            "APPOINTMENT_ALTERNATIVE_EXPIRED" => (clinicEmail, "DentalConnect: propuesta vencida", $"La propuesta para la solicitud {folio} venció y la solicitud volvió a pendiente."),
            _ => (string.Empty, string.Empty, string.Empty)
        };
    }

    private static string proposedOrRequested(string proposed, string requested) => string.IsNullOrWhiteSpace(proposed) ? requested : proposed;
    private static string requestedTime(DateTimeOffset startsAt, string timeZoneId)
    {
        try { return TimeZoneInfo.ConvertTime(startsAt, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).ToString("dd/MM/yyyy HH:mm"); }
        catch (TimeZoneNotFoundException) { return startsAt.ToString("dd/MM/yyyy HH:mm 'UTC'"); }
    }
}
