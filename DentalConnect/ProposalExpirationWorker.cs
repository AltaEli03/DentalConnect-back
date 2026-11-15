using DentalConnect.Application.Contracts;
using Microsoft.Extensions.Options;

namespace DentalConnect;

public sealed class ProposalExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ProposalExpirationOptions> options,
    ILogger<ProposalExpirationWorker> logger) : BackgroundService
{
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
    private readonly int _batchSize = options.Value.BatchSize;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        await ProcessBatchAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessBatchAsync(stoppingToken);
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProposalExpirationProcessor>();
            var expiredCount = await processor.ProcessExpiredAsync(DateTimeOffset.UtcNow, _batchSize, cancellationToken);
            if (expiredCount > 0)
                logger.LogInformation("Expired {ExpiredCount} appointment proposals", expiredCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown is expected; propagate cancellation to stop the service.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Proposal expiration pass failed; the next pass will retry");
        }
    }
}
