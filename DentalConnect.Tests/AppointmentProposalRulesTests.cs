using DentalConnect.Domain.Entities;

namespace DentalConnect.Tests;

// DENT-13 R4/R5: vigencia de la propuesta y rechazo de horarios pasados.
public class AppointmentProposalRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiresAt_Uses24HoursWhenProposedStartIsLater()
    {
        var proposed = Now.AddHours(30);
        Assert.Equal(Now.AddHours(24), AppointmentProposalRules.ExpiresAt(Now, proposed));
    }

    [Fact]
    public void ExpiresAt_UsesProposedStartWhenSooner()
    {
        var proposed = Now.AddHours(2);
        Assert.Equal(proposed, AppointmentProposalRules.ExpiresAt(Now, proposed));
    }

    [Fact]
    public void IsPast_RejectsNowAndEarlier()
    {
        Assert.True(AppointmentProposalRules.IsPast(Now, Now));
        Assert.True(AppointmentProposalRules.IsPast(Now, Now.AddSeconds(-1)));
        Assert.False(AppointmentProposalRules.IsPast(Now, Now.AddSeconds(1)));
    }
}
