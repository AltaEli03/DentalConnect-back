namespace DentalConnect;

public static class SessionPolicy
{
    // Hard ceiling for the API. The frontend enforces 15 minutes for normal
    // sessions and 5 days when "remember session" is enabled; the token does
    // not carry that flag, so the API can only enforce the absolute maximum.
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(5);

    public static bool IsExpired(long authTimeSeconds, DateTimeOffset now) =>
        now - DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds) >= MaxLifetime;
}
