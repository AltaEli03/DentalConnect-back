namespace DentalConnect;

public sealed class ProposalExpirationOptions
{
    public const string SectionName = "ProposalExpiration";
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
}
