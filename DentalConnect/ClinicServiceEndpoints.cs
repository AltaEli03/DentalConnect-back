using DentalConnect.Application.Contracts;

namespace DentalConnect;

public static class ClinicServiceEndpoints
{
    public static IEndpointRouteBuilder MapClinicServiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/clinics/{clinicId}/services").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{serviceId}", UpdateAsync);
        group.MapPatch("/{serviceId}/status", SetStatusAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(string clinicId, HttpContext context, IClinicRepository clinics, CancellationToken ct)
    {
        if (!ClinicAccess.CanManage(context, clinicId))
            return ClinicAccess.Forbidden();

        var data = await clinics.ListClinicServicesAsync(clinicId, ct);
        return data is null ? ClinicNotFound() : Results.Json(new { data });
    }

    private static async Task<IResult> CreateAsync(string clinicId, HttpContext context, ClinicServiceRequest? request, IClinicRepository clinics, CancellationToken ct)
    {
        if (!ClinicAccess.CanManage(context, clinicId))
            return ClinicAccess.Forbidden();

        var data = InputValidator.ValidateClinicService(request);
        var created = await clinics.CreateClinicServiceAsync(clinicId, data, ct);
        return created is null ? ClinicNotFound() : Results.Json(new { data = created }, statusCode: 201);
    }

    private static async Task<IResult> UpdateAsync(string clinicId, string serviceId, HttpContext context, ClinicServiceRequest? request, IClinicRepository clinics, CancellationToken ct)
    {
        if (!ClinicAccess.CanManage(context, clinicId))
            return ClinicAccess.Forbidden();

        var data = InputValidator.ValidateClinicService(request);
        var updated = await clinics.UpdateClinicServiceAsync(clinicId, serviceId, data, ct);
        return updated is null ? ServiceNotFound() : Results.Json(new { data = updated });
    }

    private static async Task<IResult> SetStatusAsync(string clinicId, string serviceId, HttpContext context, ClinicServiceStatusRequest? request, IClinicRepository clinics, CancellationToken ct)
    {
        if (!ClinicAccess.CanManage(context, clinicId))
            return ClinicAccess.Forbidden();

        var isActive = InputValidator.ValidateClinicServiceStatus(request);
        var updated = await clinics.SetClinicServiceStatusAsync(clinicId, serviceId, isActive, ct);
        return updated is null ? ServiceNotFound() : Results.Json(new { data = updated });
    }

    private static IResult ClinicNotFound() => Results.Json(
        new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } },
        statusCode: 404);

    private static IResult ServiceNotFound() => Results.Json(
        new { error = new { code = "SERVICE_NOT_FOUND", message = "Servicio no encontrado" } },
        statusCode: 404);
}
