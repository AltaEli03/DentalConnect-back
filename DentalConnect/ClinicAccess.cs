using System.Security.Claims;

namespace DentalConnect;

public static class ClinicAccess
{
    public static bool CanManage(HttpContext context, string clinicId)
    {
        var groups = context.User.FindAll("cognito:groups")
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        var claimedClinic = context.User.FindFirst("custom:clinicId")?.Value ?? context.User.FindFirst("clinicId")?.Value;
        return context.User.Identity?.IsAuthenticated == true &&
            (groups.Contains("CLINIC_ADMIN", StringComparer.Ordinal) || role == "CLINIC_ADMIN") &&
            string.Equals(claimedClinic, clinicId, StringComparison.Ordinal);
    }

    public static IResult Forbidden() =>
        Results.Json(new { error = new { code = "CLINIC_ACCESS_DENIED", message = "No tienes permiso para administrar este consultorio" } }, statusCode: 403);
}
