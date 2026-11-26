using System.Net;
using System.Text.Json;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace DentalConnect.Infrastructure.Services;

public sealed class PushNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PushNotificationWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsConfigured())
            {
                try { await ProcessBatchAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogError(exception, "No se pudo procesar la cola de notificaciones push."); }
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(configuration["VAPID_PUBLIC_KEY"]) &&
        !string.IsNullOrWhiteSpace(configuration["VAPID_PRIVATE_KEY"]) &&
        !string.IsNullOrWhiteSpace(configuration["VAPID_SUBJECT"]);

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        var now = DateTimeOffset.UtcNow;
        // Process every queued notification, including those created while the worker was offline.
        var pending = await db.UserNotifications
            .Where(n => n.PushPreparedAt == null)
            .OrderBy(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        foreach (var notification in pending)
        {
            var subscriptions = await db.WebPushSubscriptions
                .Where(s => s.RecipientType == notification.RecipientType && s.RecipientKey == notification.RecipientKey)
                .ToListAsync(ct);
            foreach (var subscription in subscriptions)
            {
                db.PushNotificationDeliveries.Add(new PushNotificationDelivery
                {
                    NotificationId = notification.Id,
                    SubscriptionId = subscription.Id
                });
            }
            notification.PushPreparedAt = DateTimeOffset.UtcNow;
        }
        if (pending.Count > 0) await db.SaveChangesAsync(ct);

        var deliveries = await db.PushNotificationDeliveries
            .Include(d => d.Notification)
            .Include(d => d.Subscription)
            .Where(d => d.DeliveredAt == null && d.AttemptCount < MaxAttempts &&
                (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.Notification.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var vapid = new VapidDetails(
            configuration["VAPID_SUBJECT"]!,
            configuration["VAPID_PUBLIC_KEY"]!,
            configuration["VAPID_PRIVATE_KEY"]!);
        var client = new WebPushClient();
        foreach (var delivery in deliveries)
        {
            delivery.AttemptCount++;
            delivery.LastAttemptAt = now;
            try
            {
                var path = delivery.Notification.RecipientType == "CLINIC"
                    ? $"/administrar-solicitudes?appointmentId={Uri.EscapeDataString(delivery.Notification.AppointmentId)}"
                    : $"/mi-solicitud?appointmentId={Uri.EscapeDataString(delivery.Notification.AppointmentId)}";
                var payload = JsonSerializer.Serialize(new
                {
                    title = delivery.Notification.Title,
                    body = delivery.Notification.Message,
                    url = path
                });
                await client.SendNotificationAsync(
                    new PushSubscription(delivery.Subscription.Endpoint, delivery.Subscription.P256dh, delivery.Subscription.Auth),
                    payload,
                    vapid,
                    cancellationToken: ct);
                delivery.DeliveredAt = DateTimeOffset.UtcNow;
                delivery.LastError = null;
            }
            catch (WebPushException exception) when (exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                logger.LogInformation("El proveedor expiró una suscripción push; se eliminará.");
                db.WebPushSubscriptions.Remove(delivery.Subscription);
            }
            catch (Exception exception) when (exception is WebPushException or HttpRequestException or TaskCanceledException)
            {
                delivery.LastError = exception is WebPushException pushError
                    ? $"HTTP_{(int)pushError.StatusCode}"
                    : "TEMPORARY_FAILURE";
                var delaySeconds = Math.Min(300, 5 * Math.Pow(2, delivery.AttemptCount - 1));
                delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                logger.LogWarning("Falló la entrega push; intento {Attempt} de {MaxAttempts}.", delivery.AttemptCount, MaxAttempts);
            }
        }
        if (deliveries.Count > 0) await db.SaveChangesAsync(ct);
    }
}
