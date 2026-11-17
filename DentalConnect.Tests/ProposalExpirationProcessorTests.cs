using DentalConnect.Application.Contracts;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalConnect.Tests;

[Collection("Postgres API")]
public sealed class ProposalExpirationProcessorTests(PostgresApiFixture fixture)
{
    private readonly PostgresApiFixture _fixture = fixture;

    [PostgresFact]
    public async Task ProcessExpiredAsync_ExpiredProposal_RevertsAndCreatesClinicNotice()
    {
        if (!_fixture.Available) return;
        var now = DateTimeOffset.UtcNow;
        var seeded = await SeedAsync(now);
        try
        {
            using var scope = _fixture.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProposalExpirationProcessor>();

            var processed = await processor.ProcessExpiredAsync(now, 100, CancellationToken.None);
            Assert.Equal(1, processed);

            var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
            var appointment = await db.Appointments.AsNoTracking().SingleAsync(item => item.Id == seeded.AppointmentId);
            Assert.Equal("PENDING", appointment.Status);
            Assert.Null(appointment.ProposedStartsAt);
            Assert.Null(appointment.ProposedEndsAt);
            Assert.Null(appointment.ProposalExpiresAt);

            var eventItem = await db.ClinicNotificationEvents.AsNoTracking().SingleAsync(item =>
                item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent);
            Assert.Null(eventItem.ProcessedAt);
            var notification = await db.UserNotifications.AsNoTracking().SingleAsync(item =>
                item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent);
            Assert.Equal("CLINIC", notification.RecipientType);
            Assert.Equal(seeded.ClinicId, notification.RecipientKey);
            Assert.Null(notification.ReadAt);
        }
        finally { await CleanupAsync(seeded); }
    }

    [PostgresFact]
    public async Task ProcessExpiredAsync_FutureProposal_LeavesItUnchanged()
    {
        if (!_fixture.Available) return;
        var now = DateTimeOffset.UtcNow;
        var seeded = await SeedAsync(now.AddMinutes(1));
        try
        {
            using var scope = _fixture.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProposalExpirationProcessor>();
            var processed = await processor.ProcessExpiredAsync(now, 100, CancellationToken.None);

            Assert.Equal(0, processed);
            var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
            var appointment = await db.Appointments.AsNoTracking().SingleAsync(item => item.Id == seeded.AppointmentId);
            Assert.Equal("PROPOSED", appointment.Status);
            Assert.NotNull(appointment.ProposalExpiresAt);
            Assert.Empty(await db.ClinicNotificationEvents.AsNoTracking().Where(item => item.AppointmentId == seeded.AppointmentId).ToListAsync());
        }
        finally { await CleanupAsync(seeded); }
    }

    [PostgresFact]
    public async Task ProcessExpiredAsync_RepeatedCycle_ReusesAndReopensUniqueNotice()
    {
        if (!_fixture.Available) return;
        var firstNow = DateTimeOffset.UtcNow;
        var seeded = await SeedAsync(firstNow.AddSeconds(-1));
        try
        {
            Assert.Equal(1, await ProcessInNewScopeAsync(firstNow));
            var secondNow = firstNow.AddMinutes(5);
            using (var scope = _fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
                var appointment = await db.Appointments.SingleAsync(item => item.Id == seeded.AppointmentId);
                appointment.Status = "PROPOSED";
                appointment.ProposedStartsAt = secondNow.AddDays(1);
                appointment.ProposedEndsAt = secondNow.AddDays(1).AddMinutes(30);
                appointment.ProposalExpiresAt = secondNow.AddSeconds(-1);
                var eventItem = await db.ClinicNotificationEvents.SingleAsync(item =>
                    item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent);
                eventItem.ProcessedAt = firstNow;
                var notification = await db.UserNotifications.SingleAsync(item =>
                    item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent);
                notification.ReadAt = firstNow;
                await db.SaveChangesAsync();
            }

            Assert.Equal(1, await ProcessInNewScopeAsync(secondNow));
            using var verifyScope = _fixture.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
            Assert.Single(await verifyDb.ClinicNotificationEvents.AsNoTracking().Where(item =>
                item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent).ToListAsync());
            var reopened = await verifyDb.UserNotifications.AsNoTracking().SingleAsync(item =>
                item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent);
            Assert.Null(reopened.ReadAt);
            Assert.InRange(reopened.CreatedAt, secondNow.AddMilliseconds(-1), secondNow.AddMilliseconds(1));
        }
        finally { await CleanupAsync(seeded); }
    }

    [PostgresFact]
    public async Task ProcessExpiredAsync_TwoConcurrentPasses_ExpireOnlyOnce()
    {
        if (!_fixture.Available) return;
        var now = DateTimeOffset.UtcNow;
        var seeded = await SeedAsync(now.AddSeconds(-1));
        try
        {
            var results = await Task.WhenAll(ProcessInNewScopeAsync(now), ProcessInNewScopeAsync(now));
            Assert.Equal(1, results.Sum());

            using var scope = _fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
            Assert.Single(await db.ClinicNotificationEvents.AsNoTracking().Where(item =>
                item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent).ToListAsync());
            Assert.Single(await db.UserNotifications.AsNoTracking().Where(item =>
                item.AppointmentId == seeded.AppointmentId && item.EventType == ExpiredEvent).ToListAsync());
        }
        finally { await CleanupAsync(seeded); }
    }

    private async Task<int> ProcessInNewScopeAsync(DateTimeOffset now)
    {
        using var scope = _fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IProposalExpirationProcessor>();
        return await processor.ProcessExpiredAsync(now, 100, CancellationToken.None);
    }

    private async Task<SeedIds> SeedAsync(DateTimeOffset proposalExpiresAt)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        var clinic = new Clinic { Name = Unique("Expiry clinic"), Description = "Test", Phone = "5550000000", Email = "expiry@example.invalid", Address = "Test", Municipality = "Test", State = "Test", PostalCode = "00000" };
        var service = new DentalService { Name = Unique("Expiry service"), Description = "Test", EstimatedDurationMinutes = 30 };
        var dentist = new Dentist { ClinicId = clinic.Id, FirstName = "Test", LastName = "Dentist", ProfessionalLicense = Unique("Expiry license"), Specialty = "General", Biography = "Test" };
        var proposedStart = proposalExpiresAt.AddDays(1);
        var appointment = new Appointment
        {
            Folio = "DC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            PatientSubject = "patient-" + Guid.NewGuid().ToString("N"),
            ClinicId = clinic.Id,
            DentistId = dentist.Id,
            DentalServiceId = service.Id,
            StartsAt = proposedStart.AddDays(-1),
            EndsAt = proposedStart.AddDays(-1).AddMinutes(30),
            ProposedStartsAt = proposedStart,
            ProposedEndsAt = proposedStart.AddMinutes(30),
            ProposalExpiresAt = proposalExpiresAt,
            Status = "PROPOSED",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestFingerprint = new string('b', 64)
        };
        db.Clinics.Add(clinic);
        db.DentalServices.Add(service);
        db.Dentists.Add(dentist);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return new SeedIds(clinic.Id, dentist.Id, service.Id, appointment.Id);
    }

    private async Task CleanupAsync(SeedIds ids)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalConnectDbContext>();
        await db.Appointments.Where(item => item.Id == ids.AppointmentId).ExecuteDeleteAsync();
        await db.Dentists.Where(item => item.Id == ids.DentistId).ExecuteDeleteAsync();
        await db.Clinics.Where(item => item.Id == ids.ClinicId).ExecuteDeleteAsync();
        await db.DentalServices.Where(item => item.Id == ids.ServiceId).ExecuteDeleteAsync();
    }

    private static string Unique(string value) => value + " " + Guid.NewGuid().ToString("N");
    private const string ExpiredEvent = "APPOINTMENT_ALTERNATIVE_EXPIRED";
    private sealed record SeedIds(string ClinicId, string DentistId, string ServiceId, string AppointmentId);
}
