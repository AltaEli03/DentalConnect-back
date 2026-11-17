namespace DentalConnect.Application.Contracts;

public interface IProposalExpirationProcessor
{
    Task<int> ProcessExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
}
