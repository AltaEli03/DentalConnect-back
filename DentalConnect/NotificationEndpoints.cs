using System.Security.Claims;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications").RequireAuthorization();
        notifications.MapGet("", ListMineAsync);
        notifications.MapPost("/{notificationId}/read", MarkReadAsync);
        notifications.MapPost("/read-all", MarkAllReadAsync);
        return endpoints;
    }

    private static async Task<IResult> ListMineAsync(HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetRecipient(http.User, out var type, out var key))
            return http.User.IsInRole("CLINIC_ADMIN") ? Results.Forbid() : Results.Unauthorized();

        var unreadOnly = bool.TryParse(http.Request.Query["unreadOnly"], out var parsed) && parsed;
        var take = int.TryParse(http.Request.Query["take"], out var requestedTake) ? Math.Clamp(requestedTake, 1, 100) : 50;
        var query = db.UserNotifications.AsNoTracking()
            .Where(n => n.RecipientType == type && n.RecipientKey == key);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        var unreadCount = await db.UserNotifications.CountAsync(
            n => n.RecipientType == type && n.RecipientKey == key && n.ReadAt == null, ct);
        var data = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new
            {
                id = n.Id,
                eventType = n.EventType,
                title = n.Title,
                message = n.Message,
                appointmentId = n.AppointmentId,
                createdAt = n.CreatedAt,
                readAt = n.ReadAt,
                isRead = n.ReadAt != null
            }).ToListAsync(ct);

        return Results.Json(new { data, unreadCount });
    }

    private static async Task<IResult> MarkReadAsync(string notificationId, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetRecipient(http.User, out var type, out var key))
            return http.User.IsInRole("CLINIC_ADMIN") ? Results.Forbid() : Results.Unauthorized();

        var notification = await db.UserNotifications.SingleOrDefaultAsync(
            n => n.Id == notificationId && n.RecipientType == type && n.RecipientKey == key, ct);
        if (notification is null) return Results.NotFound();
        notification.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Json(new { data = new { id = notification.Id, isRead = true, readAt = notification.ReadAt } });
    }

    private static async Task<IResult> MarkAllReadAsync(HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetRecipient(http.User, out var type, out var key))
            return http.User.IsInRole("CLINIC_ADMIN") ? Results.Forbid() : Results.Unauthorized();

        var now = DateTimeOffset.UtcNow;
        var affected = await db.UserNotifications
            .Where(n => n.RecipientType == type && n.RecipientKey == key && n.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAt, now), ct);
        return Results.Json(new { data = new { markedRead = affected, readAt = now } });
    }

    private static bool TryGetRecipient(ClaimsPrincipal principal, out string type, out string key)
    {
        if (principal.IsInRole("CLINIC_ADMIN"))
        {
            type = "CLINIC";
            key = principal.FindFirst("clinic_id")?.Value ?? principal.FindFirst("custom:clinic_id")?.Value ?? string.Empty;
            return !string.IsNullOrWhiteSpace(key);
        }

        type = "PATIENT";
        key = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(key);
    }
}
