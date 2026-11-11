namespace DentalConnect.Domain.Entities;

public static class AppointmentProposalRules
{
    public static DateTimeOffset ExpiresAt(DateTimeOffset now, DateTimeOffset proposedStart) =>
        proposedStart < now.AddHours(24) ? proposedStart : now.AddHours(24);

    public static bool IsPast(DateTimeOffset now, DateTimeOffset start) => start <= now;
}
