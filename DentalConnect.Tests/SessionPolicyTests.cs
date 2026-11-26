using DentalConnect;

namespace DentalConnect.Tests;

public class SessionPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void AcceptsSessionYoungerThan24Hours()
    {
        Assert.False(SessionPolicy.IsExpired(Now.AddHours(-23).ToUnixTimeSeconds(), Now));
    }

    [Fact]
    public void RejectsSessionAt24Hours()
    {
        Assert.True(SessionPolicy.IsExpired(Now.AddHours(-24).ToUnixTimeSeconds(), Now));
    }

    [Fact]
    public void RejectsOlderSession()
    {
        Assert.True(SessionPolicy.IsExpired(Now.AddHours(-25).ToUnixTimeSeconds(), Now));
    }
}
