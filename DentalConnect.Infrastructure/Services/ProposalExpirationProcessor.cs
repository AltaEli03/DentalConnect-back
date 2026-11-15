using DentalConnect.Application.Contracts;
using DentalConnect.Domain.Entities;
using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DentalConnect.Infrastructure.Services;

public sealed class ProposalExpirationProcessor(DentalConnectDbContext db, ILogger<ProposalExpirationProcessor> logger) : IProposalExpirationProcessor
{
    private const string ExpiredEvent = "APPOINTMENT_ALTERNATIVE_EXPIRED";

    public async Task<int> ProcessExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be positive.");

        var candidates = await db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Status == "PROPOSED" && appointment.ProposalExpiresAt <= now)
            .OrderBy(appointment => appointment.ProposalExpiresAt)
            .Select(appointment => new ExpiredProposalCandidate(appointment.Id, appointment.ClinicId))
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var affected = await db.Appointments
                    .Where(appointment => appointment.Id == candidate.AppointmentId &&
                                          appointment.Status == "PROPOSED" &&
                                          appointment.ProposalExpiresAt <= now)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(appointment => appointment.Status, "PENDING")
                        .SetProperty(appointment => appointment.ProposedStartsAt, (DateTimeOffset?)null)
                        .SetProperty(appointment => appointment.ProposedEndsAt, (DateTimeOffset?)null)
                        .SetProperty(appointment => appointment.ProposalExpiresAt, (DateTimeOffset?)null),
                        cancellationToken);

                if (affected == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                await QueueClinicEventAsync(candidate.AppointmentId, now, cancellationToken);
                await QueueClinicNotificationAsync(candidate, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                expiredCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception)
            {
                db.ChangeTracker.Clear();
                logger.LogError(exception, "Could not expire appointment proposal {AppointmentId}; continuing with the next candidate",
                    candidate.AppointmentId);
            }
        }

        return expiredCount;
    }

    private async Task QueueClinicEventAsync(string appointmentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var item = await db.ClinicNotificationEvents.SingleOrDefaultAsync(
            notificationEvent => notificationEvent.AppointmentId == appointmentId && notificationEvent.EventType == ExpiredEvent,
            cancellationToken);
        if (item is null)
        {
            db.ClinicNotificationEvents.Add(new ClinicNotificationEvent
            {
                AppointmentId = appointmentId,
                EventType = ExpiredEvent,
                CreatedAt = now
            });
            return;
        }

        item.CreatedAt = now;
        item.ProcessedAt = null;
    }

    private async Task QueueClinicNotificationAsync(ExpiredProposalCandidate candidate, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = await db.UserNotifications.SingleOrDefaultAsync(notification =>
                notification.RecipientType == "CLINIC" &&
                notification.RecipientKey == candidate.ClinicId &&
                notification.AppointmentId == candidate.AppointmentId &&
                notification.EventType == ExpiredEvent,
            cancellationToken);
        if (item is null)
        {
            db.UserNotifications.Add(new UserNotification
            {
                RecipientType = "CLINIC",
                RecipientKey = candidate.ClinicId,
                AppointmentId = candidate.AppointmentId,
                EventType = ExpiredEvent,
                Title = "Propuesta vencida",
                Message = "El paciente no respondió a tiempo. La solicitud volvió a estar pendiente.",
                CreatedAt = now
            });
            return;
        }

        item.Title = "Propuesta vencida";
        item.Message = "El paciente no respondió a tiempo. La solicitud volvió a estar pendiente.";
        item.CreatedAt = now;
        item.ReadAt = null;
    }

    private sealed record ExpiredProposalCandidate(string AppointmentId, string ClinicId);
}
