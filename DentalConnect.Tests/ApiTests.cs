using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalConnect.Application.Contracts;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

public class FakeClinicRepository : IClinicRepository
{
    public ClinicSearchFilter? LastFilter { get; private set; }
    public List<ClinicSummaryDto> Summaries { get; set; } = [];
    public ClinicDetailDto Detail { get; set; } = new(
        Id: "ckvclinic00000000000000001",
        Name: "Sonrisa del Centro",
        Description: "Consultorio ficticio.",
        Phone: "222-555-0101",
        Email: "centro@ejemplo.dental",
        Address: "Av. Ejemplo 101",
        Municipality: "Puebla",
        State: "Puebla",
        PostalCode: "72000",
        Latitude: 19.0433,
        Longitude: -98.2019,
        ImageUrl: null,
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
        var dentist = (await ReadJson(okResponse)).RootElement.GetProperty("data").GetProperty("dentists")[0];
        Assert.Equal("Valeria", dentist.GetProperty("firstName").GetString());
        Assert.Equal("Odontología general", dentist.GetProperty("specialty").GetString());

        var notFoundResponse = await client.GetAsync("/api/clinics/ckvclinic00000000000000002");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        var nfDoc = await ReadJson(notFoundResponse);
        Assert.Equal("CLINIC_NOT_FOUND", nfDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
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
