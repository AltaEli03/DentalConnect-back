namespace DentalConnect;

public static class SessionPolicy
{
    // The API enforces the same absolute maximum as the remembered frontend session.
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    public static bool IsExpired(long authTimeSeconds, DateTimeOffset now) =>
        now - DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds) >= MaxLifetime;
}
