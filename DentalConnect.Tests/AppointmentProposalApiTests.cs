using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalConnect.Tests;

[Collection("Postgres API")]
public sealed class AppointmentProposalApiTests(PostgresApiFixture fixture)
{
    // DENT-13 traceability: R1 success; R2 occupied/block/outside/inactive service, dentist or duration;
    // R3 second/concurrent; R4 past; R5 rules tests; R6 event; R7 auth/isolation/invalid status;
    // R8 availability rule; R9 same start.
    private const string ProposalEvent = "APPOINTMENT_ALTERNATIVE_PROPOSED";
    private const string AcceptedEvent = "APPOINTMENT_ALTERNATIVE_ACCEPTED";
    private const string RejectedEvent = "APPOINTMENT_ALTERNATIVE_REJECTED";
    private const string ExpiredEvent = "APPOINTMENT_ALTERNATIVE_EXPIRED";
    private const string PatientSubject = "test-patient";
    private readonly PostgresApiFixture _fixture = fixture;

    private sealed record SeedIds(
        string ClinicId,
        string DentistId,
        string ServiceId,
        string AppointmentId,
        string PatientSubject,
        DateTimeOffset OriginalStartsAt,
        DateTimeOffset ProposedStartsAt,
        DateOnly ProposedLocalDate,
        string? ConfirmedAppointmentId = null,
        string? OtherClinicId = null,
        string? OtherDentistId = null,
        string? OtherAppointmentId = null);

    [PostgresFact]
    public async Task Propose_WithoutToken_Returns401()
    {
        if (!_fixture.Available) return;
        using var client = _fixture.Factory!.CreateClient();
        using var response = await PostProposalAsync(client, CuidGenerator.NewId(), DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task Propose_WithoutClinicAdminRole_Returns403()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("PATIENT", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_WithoutClinicClaim_Returns403()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN");
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_AppointmentFromAnotherClinic_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync(withOtherClinic: true);
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.OtherAppointmentId!, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_AppointmentFromAnotherClinicWithPastDate_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync(withOtherClinic: true);
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.OtherAppointmentId!, DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_InvalidStatus_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            await UpdateAsync(db =>
            {
                var item = db.Appointments.Single(a => a.Id == ids.AppointmentId);
                item.Status = "CONFIRMED";
            });
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("INVALID_STATUS", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_PastAlternative_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("PAST_DATE", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_SameStartAsOriginal_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.OriginalStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("PROPOSAL_SAME_AS_ORIGINAL", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_AvailableAlternative_ReturnsProposedWithBothSchedulesAndEvent()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            var before = DateTimeOffset.UtcNow;
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var data = (await ReadJsonAsync(response)).RootElement.GetProperty("data");
            Assert.Equal("PROPOSED", data.GetProperty("status").GetString());
            Assert.Equal(ids.OriginalStartsAt, data.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(ids.ProposedStartsAt, data.GetProperty("proposedStartsAt").GetDateTimeOffset());
            var expires = data.GetProperty("proposalExpiresAt").GetDateTimeOffset();
            Assert.InRange(expires, before.AddHours(24).AddSeconds(-30), DateTimeOffset.UtcNow.AddHours(24).AddSeconds(30));

            var appointment = await GetAppointmentAsync(ids.AppointmentId);
            Assert.Equal("PROPOSED", appointment.Status);
            Assert.Equal(ids.ProposedStartsAt, appointment.ProposedStartsAt);

            var events = await GetEventsAsync(ids.AppointmentId);
            var notice = Assert.Single(events);
            Assert.Equal(ProposalEvent, notice.EventType);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_SecondActiveProposal_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var first = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            using var second = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt.AddMinutes(30));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("PROPOSAL_ALREADY_ACTIVE", await ErrorCodeAsync(second));
            Assert.Single(await GetEventsAsync(ids.AppointmentId));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_OccupiedInterval_Returns409AndDoesNotChangeAppointment()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync(withConfirmedOverlap: true);
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SLOT_UNAVAILABLE", await ErrorCodeAsync(response));
            Assert.Equal("PENDING", (await GetAppointmentAsync(ids.AppointmentId)).Status);
            Assert.Empty(await GetEventsAsync(ids.AppointmentId));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_BreakOrBlockOverlap_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync(withBlockOverlap: true);
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SLOT_UNAVAILABLE", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_OutsideSchedule_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
            var outside = ToUtc(ids.ProposedLocalDate, new TimeOnly(20, 0), zone);
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, outside);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SLOT_UNAVAILABLE", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_InactiveDentist_Returns409DentistUnavailable()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            await UpdateAsync(db =>
            {
                var dentist = db.Dentists.Single(d => d.Id == ids.DentistId);
                dentist.IsActive = false;
            });
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("DENTIST_UNAVAILABLE", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_ServiceDurationChanged_Returns409ServiceChanged()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            await UpdateAsync(db =>
            {
                var service = db.DentalServices.Single(s => s.Id == ids.ServiceId);
                service.EstimatedDurationMinutes = 45;
            });
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SERVICE_CHANGED", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_InactiveService_Returns409ServiceChanged()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            await UpdateAsync(db =>
            {
                var service = db.DentalServices.Single(s => s.Id == ids.ServiceId);
                service.IsActive = false;
            });
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SERVICE_CHANGED", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_OriginalInPastButAlternativeFuture_Returns200()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            await UpdateAsync(db =>
            {
                var item = db.Appointments.Single(a => a.Id == ids.AppointmentId);
                item.StartsAt = DateTimeOffset.UtcNow.AddDays(-2);
                item.EndsAt = item.StartsAt.AddMinutes(30);
            });
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var response = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("PROPOSED", (await GetAppointmentAsync(ids.AppointmentId)).Status);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task List_WithProposedStatus_ReturnsProposalFields()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var created = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            using var response = await client.GetAsync("/api/appointments?status=PROPOSED");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await ReadJsonAsync(response)).RootElement.GetProperty("data");
            var item = data.EnumerateArray().Single(row => row.GetProperty("id").GetString() == ids.AppointmentId);
            Assert.Equal(ids.ProposedStartsAt, item.GetProperty("proposedStartsAt").GetDateTimeOffset());
            Assert.True(item.TryGetProperty("proposalExpiresAt", out _));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Detail_ReturnsOriginalAndProposedSchedules()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var created = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            using var response = await client.GetAsync($"/api/appointments/{ids.AppointmentId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await ReadJsonAsync(response)).RootElement.GetProperty("data");
            Assert.Equal(ids.OriginalStartsAt, data.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(ids.ProposedStartsAt, data.GetProperty("proposedStartsAt").GetDateTimeOffset());
            Assert.True(data.TryGetProperty("proposalExpiresAt", out _));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_ConcurrentRequests_OnlyOneProposed()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var firstClient = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var secondClient = NewClient("CLINIC_ADMIN", ids.ClinicId, "test-user-2");
            var responses = await Task.WhenAll(
                PostProposalAsync(firstClient, ids.AppointmentId, ids.ProposedStartsAt),
                PostProposalAsync(secondClient, ids.AppointmentId, ids.ProposedStartsAt));
            try
            {
                var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
                var diagnostic = string.Join(" | ", responses.Select((r, i) => $"{(int)r.StatusCode} {r.StatusCode}: {bodies[i]}"));
                Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1, diagnostic);
                Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1, diagnostic);
                var conflictIndex = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.Conflict);
                using var conflictDoc = JsonDocument.Parse(bodies[conflictIndex]);
                var conflictCode = conflictDoc.RootElement.GetProperty("error").GetProperty("code").GetString();
                Assert.True(conflictCode is "APPOINTMENT_CONFLICT" or "PROPOSAL_ALREADY_ACTIVE", diagnostic);
            }
            finally
            {
                foreach (var response in responses) response.Dispose();
            }
            Assert.Equal("PROPOSED", (await GetAppointmentAsync(ids.AppointmentId)).Status);
            Assert.Single(await GetEventsAsync(ids.AppointmentId));
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task Propose_ExpiredEvent_IsReused()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var client = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var first = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            string eventId;
            using (var scope = _fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
                var appointment = await db.Appointments.SingleAsync(a => a.Id == ids.AppointmentId);
                appointment.Status = "PENDING";
                var notice = await db.ClinicNotificationEvents.SingleAsync(e => e.AppointmentId == ids.AppointmentId && e.EventType == ProposalEvent);
                notice.ProcessedAt = DateTimeOffset.UtcNow;
                eventId = notice.Id;
                await db.SaveChangesAsync();
            }

            using var second = await PostProposalAsync(client, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var events = await GetEventsAsync(ids.AppointmentId);
            var reused = Assert.Single(events);
            Assert.Equal(eventId, reused.Id);
            Assert.Null(reused.ProcessedAt);
        }
        finally { await CleanupAsync(ids); }
    }

    // DENT-14 R5/R6: autorización por propiedad y lectura de la propuesta.
    [PostgresFact]
    public async Task AcceptProposal_WithoutToken_Returns401()
    {
        if (!_fixture.Available) return;
        using var client = _fixture.Factory!.CreateClient();
        using var response = await PostAcceptAsync(client, CuidGenerator.NewId());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task AcceptProposal_OtherPatient_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var other = NewClient(null, subject: "other-patient");
            using var response = await PostAcceptAsync(other, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task AcceptProposal_ClinicAdminToken_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var response = await PostAcceptAsync(clinic, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task RejectProposal_WithoutToken_Returns401()
    {
        if (!_fixture.Available) return;
        using var client = _fixture.Factory!.CreateClient();
        using var response = await PostRejectAsync(client, CuidGenerator.NewId());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task RejectProposal_OtherPatient_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var other = NewClient(null, subject: "other-patient");
            using var response = await PostRejectAsync(other, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task GetProposal_WithoutToken_Returns401()
    {
        if (!_fixture.Available) return;
        using var client = _fixture.Factory!.CreateClient();
        using var response = await GetProposalAsync(client, CuidGenerator.NewId());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task GetProposal_OtherPatient_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var other = NewClient(null, subject: "other-patient");
            using var response = await GetProposalAsync(other, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task GetProposal_ClinicAdminToken_Returns404()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var response = await GetProposalAsync(clinic, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task AcceptProposal_NoActiveProposal_Returns409()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var patient = NewClient(null, subject: ids.PatientSubject);
            using var response = await PostAcceptAsync(patient, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("PROPOSAL_NOT_ACTIVE", await ErrorCodeAsync(response));
        }
        finally { await CleanupAsync(ids); }
    }

    // DENT-14 R1/R2/R4: respuesta del paciente, eventos y vencimiento.
    [PostgresFact]
    public async Task AcceptProposal_ActiveAndFree_ReturnsConfirmedAndEvent()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var patient = NewClient(null, subject: ids.PatientSubject);
            using var response = await PostAcceptAsync(patient, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var data = (await ReadJsonAsync(response)).RootElement.GetProperty("data");
            Assert.Equal("CONFIRMED", data.GetProperty("status").GetString());
            Assert.Equal(ids.ProposedStartsAt, data.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(ids.ProposedStartsAt.AddMinutes(30), data.GetProperty("endsAt").GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("proposedStartsAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, data.GetProperty("proposedEndsAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, data.GetProperty("proposalExpiresAt").ValueKind);

            var appointment = await GetAppointmentAsync(ids.AppointmentId);
            Assert.Equal("CONFIRMED", appointment.Status);
            Assert.Equal(ids.ProposedStartsAt, appointment.StartsAt);
            Assert.Null(appointment.ProposedStartsAt);
            Assert.Null(appointment.ProposalExpiresAt);

            var events = await GetEventsAsync(ids.AppointmentId);
            Assert.Contains(events, e => e.EventType == AcceptedEvent);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task RejectProposal_Active_ReturnsRejectedAndRecordsPatient()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            using var patient = NewClient(null, subject: ids.PatientSubject);
            using var response = await PostRejectAsync(patient, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var data = (await ReadJsonAsync(response)).RootElement.GetProperty("data");
            Assert.Equal("REJECTED", data.GetProperty("status").GetString());
            Assert.Equal(ids.OriginalStartsAt, data.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(ids.ProposedStartsAt, data.GetProperty("proposedStartsAt").GetDateTimeOffset());

            var appointment = await GetAppointmentAsync(ids.AppointmentId);
            Assert.Equal("REJECTED", appointment.Status);
            Assert.Equal(ids.OriginalStartsAt, appointment.StartsAt);
            Assert.Equal(ids.ProposedStartsAt, appointment.ProposedStartsAt);
            Assert.NotNull(appointment.RejectedAt);
            Assert.Equal(ids.PatientSubject, appointment.RejectedBy);

            var events = await GetEventsAsync(ids.AppointmentId);
            Assert.Contains(events, e => e.EventType == RejectedEvent);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task AcceptProposal_Expired_Returns409AndRevertsToPending()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            await UpdateAsync(db =>
            {
                var item = db.Appointments.Single(a => a.Id == ids.AppointmentId);
                item.ProposalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            });

            using var patient = NewClient(null, subject: ids.PatientSubject);
            using var response = await PostAcceptAsync(patient, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("PROPOSAL_EXPIRED", await ErrorCodeAsync(response));

            var appointment = await GetAppointmentAsync(ids.AppointmentId);
            Assert.Equal("PENDING", appointment.Status);
            Assert.Null(appointment.ProposedStartsAt);
            Assert.Null(appointment.ProposedEndsAt);
            Assert.Null(appointment.ProposalExpiresAt);

            var events = await GetEventsAsync(ids.AppointmentId);
            Assert.Contains(events, e => e.EventType == ExpiredEvent);
        }
        finally { await CleanupAsync(ids); }
    }

    [PostgresFact]
    public async Task RejectProposal_Expired_Returns409AndRevertsToPending()
    {
        if (!_fixture.Available) return;
        var ids = await SeedAsync();
        try
        {
            using var clinic = NewClient("CLINIC_ADMIN", ids.ClinicId);
            using var proposed = await PostProposalAsync(clinic, ids.AppointmentId, ids.ProposedStartsAt);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);

            await UpdateAsync(db =>
            {
                var item = db.Appointments.Single(a => a.Id == ids.AppointmentId);
                item.ProposalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            });

            using var patient = NewClient(null, subject: ids.PatientSubject);
            using var response = await PostRejectAsync(patient, ids.AppointmentId);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("PROPOSAL_EXPIRED", await ErrorCodeAsync(response));

            var appointment = await GetAppointmentAsync(ids.AppointmentId);
            Assert.Equal("PENDING", appointment.Status);
            Assert.Null(appointment.ProposedStartsAt);
            Assert.Null(appointment.ProposedEndsAt);
            Assert.Null(appointment.ProposalExpiresAt);

            var events = await GetEventsAsync(ids.AppointmentId);
            Assert.Contains(events, e => e.EventType == ExpiredEvent);
        }
        finally { await CleanupAsync(ids); }
    }

    private HttpClient NewClient(string? role = "CLINIC_ADMIN", string? clinicId = null, string subject = "test-user")
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        if (!string.IsNullOrWhiteSpace(role)) client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (!string.IsNullOrWhiteSpace(clinicId)) client.DefaultRequestHeaders.Add(TestAuthHandler.ClinicHeader, clinicId);
        return client;
    }

    private static Task<HttpResponseMessage> PostProposalAsync(HttpClient client, string appointmentId, DateTimeOffset startsAt) =>
        client.PostAsJsonAsync($"/api/appointments/{appointmentId}/propose", new { proposedStartsAt = startsAt });

    private static Task<HttpResponseMessage> PostAcceptAsync(HttpClient client, string appointmentId) =>
        client.PostAsync($"/api/appointments/{appointmentId}/proposal/accept", null);

    private static Task<HttpResponseMessage> PostRejectAsync(HttpClient client, string appointmentId) =>
        client.PostAsync($"/api/appointments/{appointmentId}/proposal/reject", null);

    private static Task<HttpResponseMessage> GetProposalAsync(HttpClient client, string appointmentId) =>
        client.GetAsync($"/api/appointments/{appointmentId}/proposal");

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).RootElement.GetProperty("error").GetProperty("code").GetString();

    private async Task<SeedIds> SeedAsync(
        bool dentistActive = true,
        int durationMinutes = 30,
        bool withConfirmedOverlap = false,
        bool withBlockOverlap = false,
        bool withOtherClinic = false)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime).AddDays(7);
        var proposedStart = ToUtc(localDate, new TimeOnly(10, 0), zone);
        var originalStart = ToUtc(localDate.AddDays(2), new TimeOnly(10, 0), zone);

        var clinic = new Clinic { Name = Unique("Clinic"), Description = "Test clinic", Phone = "5550000000", Email = "test@example.com", Address = "Test", Municipality = "Test", State = "Test", PostalCode = "00000" };
        var service = new DentalService { Name = Unique("Service"), Description = "Test service", EstimatedDurationMinutes = durationMinutes };
        var clinicService = new ClinicService { ClinicId = clinic.Id, DentalServiceId = service.Id, PriceFrom = 100m };
        var dentist = new Dentist { ClinicId = clinic.Id, FirstName = "Test", LastName = "Dentist", ProfessionalLicense = Unique("CED"), Specialty = "General", Biography = "Test", IsActive = dentistActive };
        var dentistService = new DentistService { DentistId = dentist.Id, DentalServiceId = service.Id };
        var schedule = new DentistWeeklySchedule { DentistId = dentist.Id, DayOfWeek = localDate.DayOfWeek, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(12, 0) };
        var appointment = NewAppointment(clinic.Id, dentist.Id, service.Id, originalStart, durationMinutes, "PENDING", PatientSubject);

        db.Clinics.Add(clinic);
        db.DentalServices.Add(service);
        db.ClinicServices.Add(clinicService);
        db.Dentists.Add(dentist);
        db.DentistServices.Add(dentistService);
        db.DentistWeeklySchedules.Add(schedule);
        db.Appointments.Add(appointment);

        string? confirmedId = null;
        if (withConfirmedOverlap)
        {
            var confirmed = NewAppointment(clinic.Id, dentist.Id, service.Id, proposedStart, durationMinutes, "CONFIRMED");
            db.Appointments.Add(confirmed);
            confirmedId = confirmed.Id;
        }
        if (withBlockOverlap)
        {
            db.DentistScheduleBlocks.Add(new DentistScheduleBlock
            {
                DentistId = dentist.Id,
                StartsAt = proposedStart,
                EndsAt = proposedStart.AddMinutes(durationMinutes),
                Reason = "Test block"
            });
        }

        string? otherClinicId = null;
        string? otherDentistId = null;
        string? otherAppointmentId = null;
        if (withOtherClinic)
        {
            var otherClinic = new Clinic { Name = Unique("Other clinic"), Description = "Other", Phone = "5550000000", Email = "other@example.com", Address = "Other", Municipality = "Other", State = "Other", PostalCode = "00000" };
            var otherDentist = new Dentist { ClinicId = otherClinic.Id, FirstName = "Other", LastName = "Dentist", ProfessionalLicense = Unique("CED"), Specialty = "General", Biography = "Other" };
            var otherAppointment = NewAppointment(otherClinic.Id, otherDentist.Id, service.Id, originalStart, durationMinutes, "PENDING", "other-patient");
            db.Clinics.Add(otherClinic);
            db.Dentists.Add(otherDentist);
            db.Appointments.Add(otherAppointment);
            otherClinicId = otherClinic.Id;
            otherDentistId = otherDentist.Id;
            otherAppointmentId = otherAppointment.Id;
        }

        await db.SaveChangesAsync();
        return new SeedIds(clinic.Id, dentist.Id, service.Id, appointment.Id, PatientSubject, originalStart, proposedStart, localDate, confirmedId, otherClinicId, otherDentistId, otherAppointmentId);
    }

    private async Task CleanupAsync(SeedIds ids)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        var clinicIds = new[] { ids.ClinicId, ids.OtherClinicId }.Where(id => id is not null).Cast<string>().ToArray();
        var dentistIds = new[] { ids.DentistId, ids.OtherDentistId }.Where(id => id is not null).Cast<string>().ToArray();
        var appointmentIds = new[] { ids.AppointmentId, ids.ConfirmedAppointmentId, ids.OtherAppointmentId }.Where(id => id is not null).Cast<string>().ToArray();
        await db.ClinicNotificationEvents.Where(e => appointmentIds.Contains(e.AppointmentId)).ExecuteDeleteAsync();
        await db.Appointments.Where(a => clinicIds.Contains(a.ClinicId)).ExecuteDeleteAsync();
        await db.DentistScheduleBlocks.Where(b => dentistIds.Contains(b.DentistId)).ExecuteDeleteAsync();
        await db.DentistWeeklySchedules.Where(s => dentistIds.Contains(s.DentistId)).ExecuteDeleteAsync();
        await db.DentistServices.Where(ds => dentistIds.Contains(ds.DentistId)).ExecuteDeleteAsync();
        await db.Dentists.Where(d => clinicIds.Contains(d.ClinicId)).ExecuteDeleteAsync();
        await db.ClinicServices.Where(cs => clinicIds.Contains(cs.ClinicId)).ExecuteDeleteAsync();
        await db.Clinics.Where(c => clinicIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.DentalServices.Where(s => s.Id == ids.ServiceId).ExecuteDeleteAsync();
    }

    private async Task UpdateAsync(Action<DentalConnectDbContext> update)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        update(db);
        await db.SaveChangesAsync();
    }

    private async Task<Appointment> GetAppointmentAsync(string id)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        return await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<List<ClinicNotificationEvent>> GetEventsAsync(string appointmentId)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        return await db.ClinicNotificationEvents.AsNoTracking().Where(e => e.AppointmentId == appointmentId).ToListAsync();
    }

    private static Appointment NewAppointment(string clinicId, string dentistId, string serviceId, DateTimeOffset start, int durationMinutes, string status, string? patientSubject = null) =>
        new()
        {
            Folio = "DC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            PatientSubject = patientSubject ?? Unique("Patient"),
            PatientFirstName = "Test",
            PatientLastName = "Patient",
            PatientEmail = "patient@example.com",
            PatientPhone = "5550000000",
            ClinicId = clinicId,
            DentistId = dentistId,
            DentalServiceId = serviceId,
            StartsAt = start,
            EndsAt = start.AddMinutes(durationMinutes),
            Status = status,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestFingerprint = new string('a', 64),
            TimeZoneId = PublicAvailabilityRules.PilotTimeZoneId
        };

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];
}
