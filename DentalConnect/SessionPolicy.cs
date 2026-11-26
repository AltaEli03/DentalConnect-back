namespace DentalConnect;

public static class SessionPolicy
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(5);

    public static bool IsExpired(long authTimeSeconds, DateTimeOffset now) =>
        now - DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds) >= MaxLifetime;
}
