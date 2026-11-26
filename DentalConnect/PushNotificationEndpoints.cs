using System.Security.Claims;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect;

public static class PushNotificationEndpoints
{
    public static IEndpointRouteBuilder MapPushNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/push/config", (IConfiguration config) =>
        {
            var publicKey = config["VAPID_PUBLIC_KEY"];
            var configured = !string.IsNullOrWhiteSpace(publicKey) &&
                !string.IsNullOrWhiteSpace(config["VAPID_PRIVATE_KEY"]) &&
                !string.IsNullOrWhiteSpace(config["VAPID_SUBJECT"]);
            return Results.Json(new { data = new { configured, publicKey = configured ? publicKey : null } });
        });

        var group = endpoints.MapGroup("/api/push/subscriptions").RequireAuthorization();
        group.MapGet("/me", GetStatusAsync);
        group.MapPut("", SaveAsync);
        group.MapDelete("", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetRecipient(http.User, out var type, out var key)) return Results.Unauthorized();
        var count = await db.WebPushSubscriptions.CountAsync(s => s.RecipientType == type && s.RecipientKey == key, ct);
        return Results.Json(new { data = new { enabled = count > 0, devices = count } });
    }

    private static async Task<IResult> SaveAsync(PushSubscriptionRequest? request, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetRecipient(http.User, out var type, out var key)) return Results.Unauthorized();
        if (request is null || !Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) ||
            !IsAllowedPushEndpoint(endpoint) || request.Endpoint!.Length > 2048 ||
            string.IsNullOrWhiteSpace(request.Keys?.P256dh) || request.Keys.P256dh.Length > 256 ||
            string.IsNullOrWhiteSpace(request.Keys.Auth) || request.Keys.Auth.Length > 128)
            return Results.BadRequest(new { error = new { code = "INVALID_PUSH_SUBSCRIPTION", message = "La suscripción de notificaciones no es válida." } });

        var subscription = await db.WebPushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (subscription is null)
        {
            subscription = new WebPushSubscription { Endpoint = request.Endpoint };
            db.WebPushSubscriptions.Add(subscription);
        }
        subscription.RecipientType = type;
        subscription.RecipientKey = key;
        subscription.P256dh = request.Keys.P256dh;
        subscription.Auth = request.Keys.Auth;
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Json(new { data = new { enabled = true } });
    }

    private static bool IsAllowedPushEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 ||
            System.Net.IPAddress.TryParse(endpoint.Host, out _))
            return false;
        var host = endpoint.Host.ToLowerInvariant();
        return host == "fcm.googleapis.com" ||
            host.EndsWith(".push.services.mozilla.com", StringComparison.Ordinal) ||
            host.EndsWith(".push.apple.com", StringComparison.Ordinal) ||
            host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
    }

    private static async Task<IResult> DeleteAsync(PushSubscriptionRequest? request, HttpContext http, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!TryGetRecipient(http.User, out var type, out var key)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request?.Endpoint)) return Results.BadRequest();
        var subscription = await db.WebPushSubscriptions.SingleOrDefaultAsync(
            s => s.Endpoint == request.Endpoint && s.RecipientType == type && s.RecipientKey == key, ct);
        if (subscription is not null)
        {
            db.WebPushSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
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

public sealed record PushSubscriptionRequest(string? Endpoint, PushSubscriptionKeys? Keys);
public sealed record PushSubscriptionKeys(string? P256dh, string? Auth);
