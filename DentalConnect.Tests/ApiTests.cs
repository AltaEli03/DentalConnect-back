using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using DentalConnect.Application.Contracts;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DentalConnect.Tests;

public class DatabaseConnectionTests
{
    [Fact]
    public void BuildConnectionString_UsesRdsComponentsAndEnforcesTls()
    {
        var connectionString = DentalConnectDbContext.BuildConnectionString(
            rawConnectionString: null,
            host: "db.example.us-east-1.rds.amazonaws.com",
            database: "dentalconnect",
            username: "dentalconnect",
            password: "test-only-password",
            port: "5432");

        Assert.Contains("Host=db.example.us-east-1.rds.amazonaws.com", connectionString);
        Assert.Contains("Database=dentalconnect", connectionString);
        Assert.Contains("Username=dentalconnect", connectionString);
        Assert.Contains("SSL Mode=VerifyFull", connectionString);
    }

    [Fact]
    public void BuildConnectionString_RejectsIncompleteRdsConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => DentalConnectDbContext.BuildConnectionString(
            rawConnectionString: null,
            host: "db.example.us-east-1.rds.amazonaws.com",
            database: "dentalconnect",
            username: "dentalconnect",
            password: null,
            port: null));
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.Role, role.ToString()) };
        if (Request.Headers.TryGetValue("X-Test-Clinic", out var clinic))
            claims.Add(new Claim("custom:clinicId", clinic.ToString()));
        if (Request.Headers.TryGetValue("X-Test-Groups", out var groups))
            claims.Add(new Claim("cognito:groups", groups.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

public class FakeClinicRepository : IClinicRepository
{
    public ClinicSearchFilter? LastFilter { get; private set; }
    public int UpdateCalls { get; private set; }
    public List<ClinicSummaryDto> Summaries { get; set; } = [];
    public ClinicDetailDto Detail { get; set; } = new(
        Id: "ckvclinic00000000000000001",
        Name: "Sonrisa del Centro",
        Description: "Consultorio ficticio.",
        Phone: "222-555-0101",
        Email: "centro@ejemplo.dental",
        Address: "Av. Ejemplo 101",
        City: "Puebla",
        Neighborhood: "Centro",
        Municipality: "Puebla",
        State: "Puebla",
        PostalCode: "72000",
        Latitude: 19.0433,
        Longitude: -98.2019,
        ImageUrl: null,
        WebsiteUrl: "https://sonrisa.example",
        FacebookUrl: null,
        InstagramUrl: null,
        Services: [new ClinicServiceDetailDto("ckvservice0000000000000001", "Limpieza dental", "Profilaxis ficticia.", 45, 700m)],
        Dentists: [new DentistDto("ckvdentist0000000000000001", "Valeria", "Mendoza", "CED-FICT-1001", "Odontología general", "Profesional ficticia.", null)]
    );

    public FakeClinicRepository()
    {
        Summaries.Add(new ClinicSummaryDto(
            Detail.Id,
            Detail.Name,
            Detail.Description,
            Detail.Municipality,
            Detail.State,
            Detail.Address,
            Detail.ImageUrl,
            Detail.Services.Select(s => new ClinicServiceSummaryDto(s.Id, s.Name)).ToList()
        ));
    }

    public Task<IReadOnlyList<ClinicSummaryDto>> ListActiveAsync(ClinicSearchFilter filters, CancellationToken ct = default)
    {
        LastFilter = filters;
        return Task.FromResult<IReadOnlyList<ClinicSummaryDto>>(Summaries);
    }

    public Task<ClinicDetailDto?> FindActiveByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(id == Detail.Id ? Detail : null);

    public Task<ClinicDetailDto?> UpdateProfileAsync(string id, ClinicProfileUpdate data, CancellationToken ct = default)
    {
        UpdateCalls++;
        if (id != Detail.Id)
            return Task.FromResult<ClinicDetailDto?>(null);

        Detail = Detail with
        {
            Name = data.Name,
            Description = data.Description,
            Phone = data.Phone,
            Email = data.Email,
            Address = data.Address,
            City = data.City,
            Municipality = data.Municipality,
            Neighborhood = data.Neighborhood,
            State = data.State,
            PostalCode = data.PostalCode,
            Latitude = data.Latitude,
            Longitude = data.Longitude,
            WebsiteUrl = data.WebsiteUrl,
            FacebookUrl = data.FacebookUrl,
            InstagramUrl = data.InstagramUrl
        };
        Summaries = Summaries.Select(s => s.Id == id ? s with
        {
            Name = data.Name,
            Description = data.Description,
            Municipality = data.Municipality,
            State = data.State,
            Address = data.Address
        } : s).ToList();
        return Task.FromResult<ClinicDetailDto?>(Detail);
    }
}

public class FakeIdentityProvider : IIdentityProvider
{
    public IdentityOutcome SignUpOutcome { get; set; } = IdentityOutcome.Success;
    public IdentityOutcome ConfirmOutcome { get; set; } = IdentityOutcome.Success;
    public IdentityOutcome ResendOutcome { get; set; } = IdentityOutcome.Success;
    public string? LastSignUpEmail { get; private set; }
    public string? LastSignUpPhone { get; private set; }
    public int ConfirmCalls { get; private set; }

    public Task<IdentityOutcome> SignUpAsync(string email, string password, string firstName, string lastName, string phone, CancellationToken ct = default)
    {
        LastSignUpEmail = email;
        LastSignUpPhone = phone;
        return Task.FromResult(SignUpOutcome);
    }

    public Task<IdentityOutcome> ConfirmSignUpAsync(string email, string code, CancellationToken ct = default)
    {
        ConfirmCalls++;
        return Task.FromResult(ConfirmOutcome);
    }

    public Task<IdentityOutcome> ResendCodeAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(ResendOutcome);
}

public class ApiTests
{
    private static (WebApplicationFactory<Program> Factory, FakeClinicRepository Clinics, FakeIdentityProvider Identity) CreateTestApp()
    {
        var clinics = new FakeClinicRepository();
        var identity = new FakeIdentityProvider();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("COGNITO_USER_POOL_ID", "us-east-1_test");
            builder.UseSetting("COGNITO_CLIENT_ID", "test-client");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClinicRepository>();
                services.RemoveAll<IIdentityProvider>();
                services.AddSingleton<IClinicRepository>(clinics);
                services.AddSingleton<IIdentityProvider>(identity);
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultScheme = "Test";
                });
            });
        });

        return (factory, clinics, identity);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static object ValidRegistration(string? confirmPassword = "SecurePassword123!") => new
    {
        firstName = "Ana",
        lastName = "García",
        email = "Ana@Ejemplo.com",
        phone = "5512345678",
        password = "SecurePassword123!",
        confirmPassword
    };

    private static Dictionary<string, object?> ValidProfile() => new()
    {
        ["name"] = "Clínica Actualizada",
        ["description"] = "Nueva descripción",
        ["phone"] = "2225550199",
        ["email"] = "nuevo@ejemplo.dental",
        ["address"] = "Av. Nueva 202",
        ["city"] = "Puebla",
        ["municipality"] = "Puebla",
        ["neighborhood"] = "La Paz",
        ["state"] = "Puebla",
        ["postalCode"] = "72160",
        ["latitude"] = 19.05,
        ["longitude"] = -98.22,
        ["websiteUrl"] = "https://nueva.example",
        ["facebookUrl"] = "https://facebook.com/nueva",
        ["instagramUrl"] = "https://instagram.com/nueva"
    };

    private static HttpRequestMessage PutProfile(
        string id,
        object body,
        string? clinicId,
        string? role = "CLINIC_ADMIN")
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/clinics/{id}")
        {
            Content = JsonContent.Create(body)
        };
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        if (clinicId is not null) request.Headers.Add("X-Test-Clinic", clinicId);
        return request;
    }

    [Fact]
    public async Task HealthCheck_ReturnsOkAndServiceName()
    {
        var (factory, _, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await ReadJson(response);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("DentalConnect API", doc.RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task ListActiveClinics_AcceptsSearchAndServiceFilter()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/clinics?search=Puebla&serviceId=ckvservice0000000000000001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Puebla", clinics.LastFilter?.Search);
        Assert.Equal("ckvservice0000000000000001", clinics.LastFilter?.ServiceId);

        var doc = await ReadJson(response);
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
        Assert.Equal("Consultorio ficticio.", doc.RootElement.GetProperty("data")[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task ListActiveClinics_ReturnsEmptyListWhenNoClinics()
    {
        var (factory, clinics, _) = CreateTestApp();
        clinics.Summaries.Clear();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/clinics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await ReadJson(response);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task GetClinicDetail_ReturnsDetailAnd404WhenMissing()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var okResponse = await client.GetAsync($"/api/clinics/{clinics.Detail.Id}");
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        var detail = (await ReadJson(okResponse)).RootElement.GetProperty("data");
        var dentist = detail.GetProperty("dentists")[0];
        Assert.Equal("Valeria", dentist.GetProperty("firstName").GetString());
        Assert.Equal("Odontología general", dentist.GetProperty("specialty").GetString());
        Assert.Equal("Puebla", detail.GetProperty("city").GetString());
        Assert.Equal("Centro", detail.GetProperty("neighborhood").GetString());
        Assert.Equal("https://sonrisa.example", detail.GetProperty("websiteUrl").GetString());

        var notFoundResponse = await client.GetAsync("/api/clinics/ckvclinic00000000000000002");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        var nfDoc = await ReadJson(notFoundResponse);
        Assert.Equal("CLINIC_NOT_FOUND", nfDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateClinicProfile_OwnClinic_ReturnsDetailAndPublicViewsReflectIt()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(PutProfile(clinics.Detail.Id, ValidProfile(), clinics.Detail.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await ReadJson(response)).RootElement.GetProperty("data");
        Assert.Equal("Clínica Actualizada", data.GetProperty("name").GetString());
        Assert.Equal("La Paz", data.GetProperty("neighborhood").GetString());
        Assert.Equal("https://nueva.example", data.GetProperty("websiteUrl").GetString());

        var detailResponse = await client.GetAsync($"/api/clinics/{clinics.Detail.Id}");
        var detail = (await ReadJson(detailResponse)).RootElement.GetProperty("data");
        Assert.Equal("Av. Nueva 202", detail.GetProperty("address").GetString());
        Assert.Equal("72160", detail.GetProperty("postalCode").GetString());
        Assert.Equal("https://instagram.com/nueva", detail.GetProperty("instagramUrl").GetString());

        var listResponse = await client.GetAsync("/api/clinics");
        var list = (await ReadJson(listResponse)).RootElement.GetProperty("data");
        Assert.Equal("Clínica Actualizada", list[0].GetProperty("name").GetString());
        Assert.Equal("Nueva descripción", list[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task UpdateClinicProfile_MissingAddress_Returns400AndKeepsPreviousData()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();
        var previousAddress = clinics.Detail.Address;
        var profile = ValidProfile();
        profile.Remove("address");

        var response = await client.SendAsync(PutProfile(clinics.Detail.Id, profile, clinics.Detail.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await ReadJson(response)).RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("details").GetProperty("fieldErrors").TryGetProperty("address", out _));
        Assert.Equal(0, clinics.UpdateCalls);

        var detailResponse = await client.GetAsync($"/api/clinics/{clinics.Detail.Id}");
        var detail = (await ReadJson(detailResponse)).RootElement.GetProperty("data");
        Assert.Equal(previousAddress, detail.GetProperty("address").GetString());
    }

    [Fact]
    public async Task UpdateClinicProfile_InvalidUrl_Returns400WithoutUpdating()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();
        var profile = ValidProfile();
        profile["websiteUrl"] = "http://nueva.example";

        var response = await client.SendAsync(PutProfile(clinics.Detail.Id, profile, clinics.Detail.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await ReadJson(response)).RootElement.GetProperty("error");
        Assert.True(error.GetProperty("details").GetProperty("fieldErrors").TryGetProperty("websiteUrl", out _));
        Assert.Equal(0, clinics.UpdateCalls);
    }

    [Fact]
    public async Task UpdateClinicProfile_OtherClinic_Returns403()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(PutProfile(
            clinics.Detail.Id,
            ValidProfile(),
            "ckvclinic00000000000000002"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = (await ReadJson(response)).RootElement.GetProperty("error");
        Assert.Equal("CLINIC_ACCESS_DENIED", error.GetProperty("code").GetString());
        Assert.Equal(0, clinics.UpdateCalls);
    }

    [Fact]
    public async Task UpdateClinicProfile_WithoutToken_Returns401()
    {
        var (factory, clinics, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(PutProfile(clinics.Detail.Id, ValidProfile(), null, role: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, clinics.UpdateCalls);
    }

    [Fact]
    public async Task UpdateClinicProfile_UnknownClinic_Returns404()
    {
        var (factory, _, _) = CreateTestApp();
        using var client = factory.CreateClient();
        const string unknownId = "ckvclinic00000000000000099";

        var response = await client.SendAsync(PutProfile(unknownId, ValidProfile(), unknownId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = (await ReadJson(response)).RootElement.GetProperty("error");
        Assert.Equal("CLINIC_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownRoute_ReturnsConsistent404()
    {
        var (factory, _, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("NOT_FOUND", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_WithoutConfirmPassword_Returns201AndNormalizes()
    {
        var (factory, _, identity) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRegistration(confirmPassword: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("ana@ejemplo.com", identity.LastSignUpEmail);
        Assert.Equal("+525512345678", identity.LastSignUpPhone);
    }

    [Fact]
    public async Task Register_MismatchedConfirmPassword_Returns400WithoutCallingCognito()
    {
        var (factory, _, identity) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRegistration(confirmPassword: "OtherPassword123!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(identity.LastSignUpEmail);
    }

    [Fact]
    public async Task Register_ExistingEmail_Returns409()
    {
        var (factory, _, identity) = CreateTestApp();
        identity.SignUpOutcome = IdentityOutcome.EmailExists;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRegistration());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("EMAIL_EXISTS", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_PoolRejectsPassword_Returns400WithPasswordFieldError()
    {
        var (factory, _, identity) = CreateTestApp();
        identity.SignUpOutcome = IdentityOutcome.InvalidPassword;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRegistration());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(doc.RootElement.GetProperty("error").GetProperty("details").GetProperty("fieldErrors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task VerifyEmail_InvalidCode_Returns400()
    {
        var (factory, _, identity) = CreateTestApp();
        identity.ConfirmOutcome = IdentityOutcome.InvalidCode;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new { email = "ana@ejemplo.com", code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("INVALID_VERIFICATION_CODE", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task VerifyEmail_MalformedCode_Returns400WithoutCallingCognito()
    {
        var (factory, _, identity) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new { email = "ana@ejemplo.com", code = "12ab" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, identity.ConfirmCalls);
    }

    [Fact]
    public async Task ResendCode_UnknownEmail_Returns202()
    {
        var (factory, _, identity) = CreateTestApp();
        identity.ResendOutcome = IdentityOutcome.NotPending;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/resend-verification-code", new { email = "nadie@ejemplo.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Login_IsNotExposed_Returns404()
    {
        var (factory, _, _) = CreateTestApp();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "ana@ejemplo.com", password = "SecurePassword123!" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var doc = await ReadJson(response);
        Assert.Equal("NOT_FOUND", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AuthEndpoints_Return429AfterTwentyRequests()
    {
        var (factory, _, identity) = CreateTestApp();
        using var client = factory.CreateClient();

        for (var i = 0; i < 20; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/auth/resend-verification-code", new { email = "ana@ejemplo.com" });
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/resend-verification-code", new { email = "ana@ejemplo.com" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var doc = await ReadJson(limited);
        Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("5512345678", "+525512345678")]
    [InlineData("+14155550100", "+14155550100")]
    public void NormalizePhone_MirrorsFrontendRule(string input, string expected)
    {
        Assert.Equal(expected, AuthService.NormalizePhone(input));
    }

    [Fact]
    public void NormalizeConnectionString_ParsesPostgresUri()
    {
        var parsed = DentalConnectDbContext.NormalizeConnectionString(
            "postgresql://dentalconnect:secret@db.internal:5432/dentalconnect?schema=public");
        Assert.Contains("Host=db.internal", parsed);
        Assert.Contains("Database=dentalconnect", parsed);
        Assert.Contains("Username=dentalconnect", parsed);
        Assert.Contains("Search Path=public", parsed);
    }
}
