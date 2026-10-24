using System.Net.Mail;
using System.Text.RegularExpressions;
using DentalConnect.Domain.Entities;

namespace DentalConnect.Application.Contracts;

public record ClinicServiceSummaryDto(string Id, string Name);

public record ClinicSummaryDto(
    string Id,
    string Name,
    string Description,
    string Municipality,
    string State,
    string Address,
    string? ImageUrl,
    IReadOnlyList<ClinicServiceSummaryDto> Services
);

public record ClinicServiceDetailDto(
    string Id,
    string Name,
    string Description,
    int EstimatedDurationMinutes,
    decimal? PriceFrom
);

public record DentistDto(
    string Id,
    string FirstName,
    string LastName,
    string ProfessionalLicense,
    string Specialty,
    string Biography,
    string? ImageUrl,
    IReadOnlyList<DentistServiceDto>? Services = null
);

public record DentistServiceDto(string Id, string Name, string Description, int EstimatedDurationMinutes);

public record ClinicDetailDto(
    string Id,
    string Name,
    string Description,
    string Phone,
    string Email,
    string Address,
    string City,
    string Neighborhood,
    string Municipality,
    string State,
    string PostalCode,
    double? Latitude,
    double? Longitude,
    string? ImageUrl,
    string? WebsiteUrl,
    string? FacebookUrl,
    string? InstagramUrl,
    IReadOnlyList<ClinicServiceDetailDto> Services,
    IReadOnlyList<DentistDto> Dentists
);

public record UpdateClinicProfileRequest(
    string? Name,
    string? Description,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? Municipality,
    string? Neighborhood,
    string? State,
    string? PostalCode,
    double? Latitude,
    double? Longitude,
    string? WebsiteUrl,
    string? FacebookUrl,
    string? InstagramUrl
);

public record ClinicProfileUpdate(
    string Name,
    string Description,
    string Phone,
    string Email,
    string Address,
    string City,
    string Municipality,
    string Neighborhood,
    string State,
    string PostalCode,
    double? Latitude,
    double? Longitude,
    string? WebsiteUrl,
    string? FacebookUrl,
    string? InstagramUrl
);

public record ClinicServiceRequest(string? Name, string? Description, int? EstimatedDurationMinutes);
public record ClinicServiceStatusRequest(bool? IsActive);
public record ClinicServiceData(string Name, string Description, int EstimatedDurationMinutes);
public record ClinicServiceManagementDto(string Id, string Name, string Description, int EstimatedDurationMinutes, bool IsActive, decimal? PriceFrom);

public record ClinicSearchFilter(string? Search = null, string? ServiceId = null);

public record RegisterRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Password,
    string? ConfirmPassword
);

public record VerifyEmailRequest(string? Email, string? Code);
public record ResendCodeRequest(string? Email);

public record ValidationErrorDetail(IReadOnlyList<string> FormErrors, IDictionary<string, string[]> FieldErrors);

public sealed class RequestValidationException(IDictionary<string, string[]> fieldErrors) : Exception("Datos inválidos")
{
    public ValidationErrorDetail Details { get; } = new(Array.Empty<string>(), fieldErrors);
}

public static partial class InputValidator
{
    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex SixDigitCodeRegex();

    public static string ValidateClinicId(string? id)
    {
        if (!CuidGenerator.IsValid(id))
            throw new RequestValidationException(new Dictionary<string, string[]> { ["id"] = ["Invalid cuid"] });
        return id!;
    }

    public static ClinicSearchFilter ValidateClinicsQuery(string? search, string? serviceId)
    {
        var errors = new Dictionary<string, string[]>();
        string? normalizedSearch = null;
        if (search is not null)
        {
            normalizedSearch = search.Trim();
            if (normalizedSearch.Length < 1 || normalizedSearch.Length > 120)
                errors["search"] = ["Must be between 1 and 120 characters"];
        }
        if (serviceId is not null && !CuidGenerator.IsValid(serviceId))
        {
            errors["serviceId"] = ["Invalid cuid"];
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
        return new ClinicSearchFilter(normalizedSearch, serviceId);
    }

    public static (string FirstName, string LastName, string Email, string Phone, string Password) ValidateRegistration(RegisterRequest? req)
    {
        var errors = new Dictionary<string, string[]>();
        var firstName = req?.FirstName?.Trim() ?? string.Empty;
        var lastName = req?.LastName?.Trim() ?? string.Empty;
        var email = req?.Email?.Trim() ?? string.Empty;
        var phone = req?.Phone?.Trim() ?? string.Empty;
        var password = req?.Password ?? string.Empty;

        if (firstName.Length < 2 || firstName.Length > 80)
            errors["firstName"] = ["Nombre inválido"];
        if (lastName.Length < 2 || lastName.Length > 80)
            errors["lastName"] = ["Apellido inválido"];
        if (!IsValidEmail(email))
            errors["email"] = ["Correo electrónico inválido"];
        if (phone.Length < 8 || phone.Length > 20)
            errors["phone"] = ["Teléfono inválido"];
        if (password.Length < 12 || password.Length > 128)
            errors["password"] = ["La contraseña debe tener entre 12 y 128 caracteres"];
        if (req?.ConfirmPassword is not null && password != req.ConfirmPassword)
            errors["confirmPassword"] = ["Las contraseñas no coinciden"];

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return (firstName, lastName, email, phone, password);
    }

    public static (string Email, string Code) ValidateVerify(VerifyEmailRequest? req)
    {
        var errors = new Dictionary<string, string[]>();
        var email = req?.Email?.Trim() ?? string.Empty;
        var code = req?.Code ?? string.Empty;

        if (!IsValidEmail(email))
            errors["email"] = ["Correo electrónico inválido"];
        if (!SixDigitCodeRegex().IsMatch(code))
            errors["code"] = ["Código inválido"];

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return (email, code);
    }

    public static string ValidateEmailOnly(ResendCodeRequest? req)
    {
        var email = req?.Email?.Trim() ?? string.Empty;
        if (!IsValidEmail(email))
            throw new RequestValidationException(new Dictionary<string, string[]> { ["email"] = ["Correo electrónico inválido"] });
        return email;
    }

    public static ClinicProfileUpdate ValidateClinicProfileUpdate(UpdateClinicProfileRequest? req)
    {
        var errors = new Dictionary<string, string[]>();
        var name = req?.Name?.Trim() ?? string.Empty;
        var description = req?.Description?.Trim() ?? string.Empty;
        var phone = req?.Phone?.Trim() ?? string.Empty;
        var email = req?.Email?.Trim() ?? string.Empty;
        var address = req?.Address?.Trim() ?? string.Empty;
        var city = req?.City?.Trim() ?? string.Empty;
        var municipality = req?.Municipality?.Trim() ?? string.Empty;
        var neighborhood = req?.Neighborhood?.Trim() ?? string.Empty;
        var state = req?.State?.Trim() ?? string.Empty;
        var postalCode = req?.PostalCode?.Trim() ?? string.Empty;
        var latitude = req?.Latitude;
        var longitude = req?.Longitude;
        var websiteUrl = NormalizeOptional(req?.WebsiteUrl);
        var facebookUrl = NormalizeOptional(req?.FacebookUrl);
        var instagramUrl = NormalizeOptional(req?.InstagramUrl);

        if (name.Length < 2 || name.Length > 120)
            errors["name"] = ["Nombre inválido"];
        if (address.Length < 5 || address.Length > 300)
            errors["address"] = ["Dirección inválida"];
        if (phone.Length < 8 || phone.Length > 20)
            errors["phone"] = ["Teléfono inválido"];
        if (!IsValidEmail(email))
            errors["email"] = ["Correo electrónico inválido"];
        if (description.Length > 1000)
            errors["description"] = ["Descripción demasiado larga"];
        if (city.Length > 120)
            errors["city"] = ["Ciudad inválida"];
        if (municipality.Length > 120)
            errors["municipality"] = ["Municipio inválido"];
        if (neighborhood.Length > 120)
            errors["neighborhood"] = ["Colonia inválida"];
        if (state.Length > 120)
            errors["state"] = ["Estado inválido"];
        if (postalCode.Length > 0 && (postalCode.Length != 5 || !postalCode.All(char.IsDigit)))
            errors["postalCode"] = ["El código postal debe tener 5 dígitos"];

        if (latitude.HasValue != longitude.HasValue)
        {
            errors["latitude"] = ["Latitud y longitud deben enviarse juntas"];
            errors["longitude"] = ["Latitud y longitud deben enviarse juntas"];
        }
        else if (latitude is { } lat && longitude is { } lng)
        {
            if (!double.IsFinite(lat) || lat < -90 || lat > 90)
                errors["latitude"] = ["Latitud fuera de rango"];
            if (!double.IsFinite(lng) || lng < -180 || lng > 180)
                errors["longitude"] = ["Longitud fuera de rango"];
        }

        if (websiteUrl is not null && !IsHttpsUrl(websiteUrl))
            errors["websiteUrl"] = ["La URL debe ser absoluta y usar https"];
        if (facebookUrl is not null && !IsHttpsUrl(facebookUrl))
            errors["facebookUrl"] = ["La URL debe ser absoluta y usar https"];
        if (instagramUrl is not null && !IsHttpsUrl(instagramUrl))
            errors["instagramUrl"] = ["La URL debe ser absoluta y usar https"];

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return new ClinicProfileUpdate(
            name, description, phone, email, address, city, municipality, neighborhood, state,
            postalCode, latitude, longitude, websiteUrl, facebookUrl, instagramUrl);
    }

    public static ClinicServiceData ValidateClinicService(ClinicServiceRequest? req)
    {
        var errors = new Dictionary<string, string[]>();
        var name = req?.Name?.Trim() ?? string.Empty;
        var description = req?.Description?.Trim() ?? string.Empty;
        var minutes = req?.EstimatedDurationMinutes ?? 0;

        if (name.Length < 2 || name.Length > 120)
            errors["name"] = ["Nombre inválido"];
        if (description.Length > 1000)
            errors["description"] = ["Descripción demasiado larga"];
        if (minutes <= 0)
            errors["estimatedDurationMinutes"] = ["La duración debe ser un entero positivo en minutos"];

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return new ClinicServiceData(name, description, minutes);
    }

    public static bool ValidateClinicServiceStatus(ClinicServiceStatusRequest? req)
    {
        if (req?.IsActive is not bool isActive)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["isActive"] = ["Indica si el servicio está activo"] });
        return isActive;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsHttpsUrl(string value) =>
        value.Length <= 500 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return false;
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;
    }
}

public interface IClinicRepository
{
    Task<IReadOnlyList<ClinicSummaryDto>> ListActiveAsync(ClinicSearchFilter filters, CancellationToken ct = default);
    Task<ClinicDetailDto?> FindActiveByIdAsync(string id, CancellationToken ct = default);
    Task<ClinicDetailDto?> UpdateProfileAsync(string id, ClinicProfileUpdate data, CancellationToken ct = default);
    Task<IReadOnlyList<ClinicServiceManagementDto>?> ListClinicServicesAsync(string clinicId, CancellationToken ct = default);
    Task<ClinicServiceManagementDto?> CreateClinicServiceAsync(string clinicId, ClinicServiceData data, CancellationToken ct = default);
    Task<ClinicServiceManagementDto?> UpdateClinicServiceAsync(string clinicId, string serviceId, ClinicServiceData data, CancellationToken ct = default);
    Task<ClinicServiceManagementDto?> SetClinicServiceStatusAsync(string clinicId, string serviceId, bool isActive, CancellationToken ct = default);
}

public enum IdentityOutcome
{
    Success,
    EmailExists,
    InvalidPassword,
    InvalidCode,
    NotPending,
    RateLimited
}

public interface IIdentityProvider
{
    Task<IdentityOutcome> SignUpAsync(string email, string password, string firstName, string lastName, string phone, CancellationToken ct = default);
    Task<IdentityOutcome> ConfirmSignUpAsync(string email, string code, CancellationToken ct = default);
    Task<IdentityOutcome> ResendCodeAsync(string email, CancellationToken ct = default);
}

public class AuthService(IIdentityProvider identity)
{
    public Task<IdentityOutcome> RegisterAsync(string firstName, string lastName, string email, string phone, string password, CancellationToken ct = default) =>
        identity.SignUpAsync(email.ToLowerInvariant(), password, firstName, lastName, NormalizePhone(phone), ct);

    public Task<IdentityOutcome> VerifyAsync(string email, string code, CancellationToken ct = default) =>
        identity.ConfirmSignUpAsync(email.ToLowerInvariant(), code, ct);

    public Task<IdentityOutcome> ResendAsync(string email, CancellationToken ct = default) =>
        identity.ResendCodeAsync(email.ToLowerInvariant(), ct);

    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (phone.StartsWith('+'))
            return $"+{digits}";
        return digits.StartsWith("52", StringComparison.Ordinal) ? $"+{digits}" : $"+52{digits}";
    }
}
