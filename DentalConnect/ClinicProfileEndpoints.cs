using DentalConnect.Application.Contracts;

namespace DentalConnect;

public static class ClinicProfileEndpoints
{
    public static IEndpointRouteBuilder MapClinicProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/clinics/{id}", UpdateAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        HttpContext context,
        UpdateClinicProfileRequest? request,
        IClinicRepository clinics,
        CancellationToken ct)
    {
        if (!ClinicAccess.CanManage(context, id))
            return ClinicAccess.Forbidden();

        var validId = InputValidator.ValidateClinicId(id);
        var data = InputValidator.ValidateClinicProfileUpdate(request);
        var detail = await clinics.UpdateProfileAsync(validId, data, ct);
        return detail is null
            ? Results.Json(new { error = new { code = "CLINIC_NOT_FOUND", message = "Consultorio no encontrado" } }, statusCode: 404)
            : Results.Json(new { data = detail });
    }
}
