using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalConnect.Tests;

[Collection("Postgres API")]
public sealed class ClinicOnboardingApiTests(PostgresApiFixture fixture)
{
    private readonly PostgresApiFixture _fixture = fixture;

    [PostgresFact]
    public async Task Registration_PublishesClinicAndGrantsClinicRoleWithoutEndingSession()
    {
        if (!_fixture.Available) return;
        var subject = "clinic-owner-" + Guid.NewGuid().ToString("N");
        var email = $"{subject}@example.invalid";
        string? clinicId = null;
        try
        {
            using var client = NewClient(subject, email, emailVerified: true);
            using var response = await client.PostAsJsonAsync("/api/clinic-applications", new
            {
                clinicName = "Clínica de integración",
                phone = "7711234567",
                address = "Calle de integración 123",
                municipality = "Tula de Allende",
                state = "Hidalgo",
                postalCode = "42800"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            clinicId = created.RootElement.GetProperty("data").GetProperty("clinicId").GetString();
            Assert.Equal("APPROVED", created.RootElement.GetProperty("data").GetProperty("status").GetString());

            using var profileResponse = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
            using var profile = await JsonDocument.ParseAsync(await profileResponse.Content.ReadAsStreamAsync());
            var user = profile.RootElement.GetProperty("data").GetProperty("user");
            Assert.Equal("CLINIC_ADMIN", user.GetProperty("role").GetString());
            Assert.Equal(clinicId, user.GetProperty("clinicId").GetString());

            using var publicResponse = await client.GetAsync($"/api/clinics/{clinicId}");
            Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        }
        finally
        {
            if (clinicId is not null)
            {
                using var scope = _fixture.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
                var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
                await connection.OpenAsync();
                await using var cleanup = new Npgsql.NpgsqlCommand("""
                    DELETE FROM "ClinicApplication" WHERE "ownerSubject" = @subject;
                    DELETE FROM "ClinicMembership" WHERE "userSubject" = @subject;
                    DELETE FROM "Clinic" WHERE "id" = @clinicId;
                    """, connection);
                cleanup.Parameters.AddWithValue("subject", subject);
                cleanup.Parameters.AddWithValue("clinicId", clinicId);
                await cleanup.ExecuteNonQueryAsync();
            }
        }
    }

    private HttpClient NewClient(string subject, string email, bool emailVerified)
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailVerifiedHeader, emailVerified ? "true" : "false");
        return client;
    }
}
