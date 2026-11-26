using DentalConnect;

namespace DentalConnect.Tests;

public class SessionPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void AcceptsSessionYoungerThanFifteenMinutes()
    {
        Assert.False(SessionPolicy.IsExpired(Now.AddMinutes(-14).ToUnixTimeSeconds(), Now));
    }

    [Fact]
    public void RejectsSessionAtFifteenMinutes()
    {
        Assert.True(SessionPolicy.IsExpired(Now.AddMinutes(-15).ToUnixTimeSeconds(), Now));
    }

    [Fact]
    public void RejectsOlderSession()
    {
        Assert.True(SessionPolicy.IsExpired(Now.AddMinutes(-16).ToUnixTimeSeconds(), Now));
    }
}
