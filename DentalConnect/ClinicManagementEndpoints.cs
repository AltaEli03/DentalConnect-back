using System.Security.Claims;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalConnect;

public sealed record ClinicProfileRequest(
    string? Name, string? Description, string? Phone, string? Email, string? Address,
    string? Municipality, string? State, string? PostalCode, string? ImageUrl,
    double? Latitude, double? Longitude);
public sealed record ClinicServiceRequest(bool IsActive, decimal PriceFrom);

public static class ClinicManagementEndpoints
{
    public static IEndpointRouteBuilder MapClinicManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/clinics/{clinicId}");
        group.MapGet("/management", GetAsync).RequireAuthorization();
        group.MapPut("/management", UpdateAsync).RequireAuthorization();
        group.MapPut("/management/services/{serviceId}", SetServiceAsync).RequireAuthorization();
        return endpoints;
    }

    private static bool CanManage(HttpContext context, string clinicId)
    {
        var groups = context.User.FindAll("cognito:groups")
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var isClinicAdmin = groups.Contains("CLINIC_ADMIN", StringComparer.Ordinal) ||
            context.User.FindFirst(ClaimTypes.Role)?.Value == "CLINIC_ADMIN";
        var claimedClinic = context.User.FindFirst("custom:clinicId")?.Value ?? context.User.FindFirst("clinicId")?.Value;
        return context.User.Identity?.IsAuthenticated == true && isClinicAdmin &&
            string.Equals(claimedClinic, clinicId, StringComparison.Ordinal);
    }

    private static IResult Denied() => Results.Json(new
    {
        error = new { code = "CLINIC_ACCESS_DENIED", message = "No tienes permiso para administrar este consultorio" }
    }, statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult> GetAsync(string clinicId, HttpContext context, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Denied();
        var clinic = await db.Clinics.AsNoTracking().Where(c => c.Id == clinicId)
            .Select(c => new { c.Id, c.Name, c.Description, c.Phone, c.Email, c.Address, c.Municipality,
                c.State, c.PostalCode, c.ImageUrl, c.Latitude, c.Longitude, c.IsActive })
            .SingleOrDefaultAsync(ct);
        if (clinic is null) return Results.NotFound(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } });
        var services = await db.DentalServices.AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Description, s.EstimatedDurationMinutes,
                IsOffered = s.ClinicServices.Any(cs => cs.ClinicId == clinicId && cs.IsActive),
                PriceFrom = s.ClinicServices.Where(cs => cs.ClinicId == clinicId && cs.IsActive)
                    .Select(cs => (decimal?)cs.PriceFrom).FirstOrDefault() })
            .ToListAsync(ct);
        return Results.Ok(new { data = new { clinic, services } });
    }

    private static async Task<IResult> UpdateAsync(string clinicId, HttpContext context, ClinicProfileRequest? request,
        DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Denied();
        if (!Valid(request)) return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Revisa los campos obligatorios, la URL de imagen y las coordenadas" } });
        var clinic = await db.Clinics.SingleOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return Results.NotFound(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } });
        clinic.Name = request!.Name!.Trim();
        clinic.Description = request.Description!.Trim();
        clinic.Phone = request.Phone!.Trim();
        clinic.Email = request.Email!.Trim().ToLowerInvariant();
        clinic.Address = request.Address!.Trim();
        clinic.Municipality = request.Municipality!.Trim();
        clinic.State = request.State!.Trim();
        clinic.PostalCode = request.PostalCode!.Trim();
        clinic.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        clinic.Latitude = request.Latitude;
        clinic.Longitude = request.Longitude;
        clinic.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { data = new { clinic.Id, clinic.Name, clinic.Description, clinic.Phone, clinic.Email,
            clinic.Address, clinic.Municipality, clinic.State, clinic.PostalCode, clinic.ImageUrl, clinic.Latitude,
            clinic.Longitude, clinic.IsActive, clinic.UpdatedAt } });
    }

    private static async Task<IResult> SetServiceAsync(string clinicId, string serviceId, HttpContext context,
        ClinicServiceRequest? request, DentalConnectDbContext db, CancellationToken ct)
    {
        if (!CanManage(context, clinicId)) return Denied();
        if (!CuidGenerator.IsValid(clinicId) || !CuidGenerator.IsValid(serviceId) || request is null ||
            request.PriceFrom < 0 || request.PriceFrom > 1_000_000)
            return Results.BadRequest(new { error = new { code = "VALIDATION_ERROR", message = "Precio o identificador inválido" } });
        var clinicExists = await db.Clinics.AnyAsync(c => c.Id == clinicId, ct);
        if (!clinicExists) return Results.NotFound(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } });
        if (!await db.DentalServices.AnyAsync(s => s.Id == serviceId && s.IsActive, ct))
            return Results.NotFound(new { error = new { code = "SERVICE_NOT_FOUND", message = "Servicio dental no disponible" } });
        var offer = await db.ClinicServices.SingleOrDefaultAsync(cs => cs.ClinicId == clinicId && cs.DentalServiceId == serviceId, ct);
        if (offer is null)
        {
            offer = new ClinicService { ClinicId = clinicId, DentalServiceId = serviceId };
            db.ClinicServices.Add(offer);
        }
        offer.PriceFrom = request.PriceFrom;
        offer.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { data = new { offer.Id, offer.ClinicId, offer.DentalServiceId, offer.PriceFrom, offer.IsActive } });
    }

    private static bool Valid(ClinicProfileRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120 ||
            string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000 ||
            string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Trim().Length > 30 ||
            string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 254 || !request.Email.Contains('@') ||
            string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length > 250 ||
            string.IsNullOrWhiteSpace(request.Municipality) || request.Municipality.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.State) || request.State.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.PostalCode) || request.PostalCode.Trim().Length > 20) return false;
        if (!string.IsNullOrWhiteSpace(request.ImageUrl) &&
            (!Uri.TryCreate(request.ImageUrl.Trim(), UriKind.Absolute, out var image) ||
             (image.Scheme != Uri.UriSchemeHttps && image.Scheme != Uri.UriSchemeHttp) || request.ImageUrl.Length > 2048)) return false;
        if (request.Latitude.HasValue != request.Longitude.HasValue) return false;
        return request.Latitude is null ||
            (request.Latitude is >= -90 and <= 90 && request.Longitude is >= -180 and <= 180);
    }
}
