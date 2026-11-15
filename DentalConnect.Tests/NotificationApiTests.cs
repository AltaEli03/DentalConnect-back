using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalConnect.Tests;

[Collection("Postgres API")]
public sealed class NotificationApiTests(PostgresApiFixture fixture)
{
    private readonly PostgresApiFixture _fixture = fixture;

    [PostgresFact]
    public async Task Notifications_AreScopedToRecipient_AndCanBeRead()
    {
        if (!_fixture.Available) return;
        var seed = await SeedAsync();
        try
        {
            using var patient = NewClient(seed.PatientSubject);
            using var patientResponse = await patient.GetAsync("/api/notifications?unreadOnly=true");
            Assert.Equal(HttpStatusCode.OK, patientResponse.StatusCode);
            using var patientJson = await JsonDocument.ParseAsync(await patientResponse.Content.ReadAsStreamAsync());
            var patientData = patientJson.RootElement.GetProperty("data");
            Assert.Single(patientData.EnumerateArray());
            Assert.Equal(1, patientJson.RootElement.GetProperty("unreadCount").GetInt32());
            var patientNotificationId = patientData[0].GetProperty("id").GetString();

            using var clinic = NewClient(seed.ClinicSubject, "CLINIC_ADMIN", seed.ClinicId);
            using var clinicResponse = await clinic.GetAsync("/api/notifications");
            Assert.Equal(HttpStatusCode.OK, clinicResponse.StatusCode);
            using var clinicJson = await JsonDocument.ParseAsync(await clinicResponse.Content.ReadAsStreamAsync());
            Assert.Single(clinicJson.RootElement.GetProperty("data").EnumerateArray());

            using var readResponse = await patient.PostAsync($"/api/notifications/{patientNotificationId}/read", null);
            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            using var unreadResponse = await patient.GetAsync("/api/notifications?unreadOnly=true");
            using var unreadJson = await JsonDocument.ParseAsync(await unreadResponse.Content.ReadAsStreamAsync());
            Assert.Empty(unreadJson.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal(0, unreadJson.RootElement.GetProperty("unreadCount").GetInt32());

            using var clinicUnread = await clinic.GetAsync("/api/notifications?unreadOnly=true");
            using var clinicUnreadJson = await JsonDocument.ParseAsync(await clinicUnread.Content.ReadAsStreamAsync());
            Assert.Single(clinicUnreadJson.RootElement.GetProperty("data").EnumerateArray());
        }
        finally { await CleanupAsync(seed); }
    }

    [PostgresFact]
    public async Task Notification_FromAnotherRecipient_CannotBeMarkedRead()
    {
        if (!_fixture.Available) return;
        var seed = await SeedAsync();
        try
        {
            using var client = NewClient("unrelated-patient");
            using var response = await client.PostAsync($"/api/notifications/{seed.PatientNotificationId}/read", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(seed); }
    }

    [PostgresFact]
    public async Task Notifications_RequireAuthentication()
    {
        if (!_fixture.Available) return;
        using var client = _fixture.Factory!.CreateClient();
        using var response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record SeedIds(string ClinicId, string DentistId, string ServiceId, string AppointmentId,
        string PatientSubject, string ClinicSubject, string PatientNotificationId);

    private async Task<SeedIds> SeedAsync()
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        var clinic = new Clinic { Name = Unique("Notify clinic"), Description = "Test", Phone = "5550000000", Email = "test@example.invalid", Address = "Test", Municipality = "Test", State = "Test", PostalCode = "00000" };
        var service = new DentalService { Name = Unique("Notify service"), Description = "Test", EstimatedDurationMinutes = 30 };
        var dentist = new Dentist { ClinicId = clinic.Id, FirstName = "Test", LastName = "Dentist", ProfessionalLicense = Unique("License"), Specialty = "General", Biography = "Test" };
        var appointment = new Appointment
        {
            Folio = "DC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            PatientSubject = "patient-" + Guid.NewGuid().ToString("N"),
            ClinicId = clinic.Id, DentistId = dentist.Id, DentalServiceId = service.Id,
            StartsAt = DateTimeOffset.UtcNow.AddDays(2), EndsAt = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(30),
            IdempotencyKey = Guid.NewGuid().ToString("N"), RequestFingerprint = new string('a', 64)
        };
        var patientNotification = new UserNotification
        {
            RecipientType = "PATIENT", RecipientKey = appointment.PatientSubject, AppointmentId = appointment.Id,
            EventType = "APPOINTMENT_REQUESTED", Title = "Solicitud enviada", Message = "Tu solicitud quedó pendiente de revisión."
        };
        db.Clinics.Add(clinic);
        db.DentalServices.Add(service);
        db.Dentists.Add(dentist);
        db.Appointments.Add(appointment);
        db.UserNotifications.Add(patientNotification);
        db.UserNotifications.Add(new UserNotification
        {
            RecipientType = "CLINIC", RecipientKey = clinic.Id, AppointmentId = appointment.Id,
            EventType = "APPOINTMENT_REQUESTED", Title = "Nueva solicitud", Message = "Hay una solicitud pendiente."
        });
        await db.SaveChangesAsync();
        return new SeedIds(clinic.Id, dentist.Id, service.Id, appointment.Id, appointment.PatientSubject,
            "clinic-admin-" + Guid.NewGuid().ToString("N"), patientNotification.Id);
    }

    private HttpClient NewClient(string subject, string? role = null, string? clinicId = null)
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        if (role is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (clinicId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.ClinicHeader, clinicId);
        return client;
    }

    private async Task CleanupAsync(SeedIds ids)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        await db.Appointments.Where(a => a.Id == ids.AppointmentId).ExecuteDeleteAsync();
        await db.Dentists.Where(d => d.Id == ids.DentistId).ExecuteDeleteAsync();
        await db.Clinics.Where(c => c.Id == ids.ClinicId).ExecuteDeleteAsync();
        await db.DentalServices.Where(s => s.Id == ids.ServiceId).ExecuteDeleteAsync();
    }

    private static string Unique(string value) => value + " " + Guid.NewGuid().ToString("N");
}
