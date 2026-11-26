using DentalConnect;

namespace DentalConnect.Tests;

public class SessionPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void AcceptsSessionYoungerThanFiveDays()
    {
        Assert.False(SessionPolicy.IsExpired(Now.AddDays(-4).ToUnixTimeSeconds(), Now));
    }

    [Fact]
    public void RejectsSessionAtFiveDays()
    {
        Assert.True(SessionPolicy.IsExpired(Now.AddDays(-5).ToUnixTimeSeconds(), Now));
    }

    [Fact]
    public void RejectsOlderSession()
    {
        Assert.True(SessionPolicy.IsExpired(Now.AddDays(-6).ToUnixTimeSeconds(), Now));
    }
}
