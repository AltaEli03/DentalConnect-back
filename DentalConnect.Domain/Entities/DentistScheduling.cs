namespace DentalConnect.Domain.Entities;

/// <summary>Validaciones deterministas de agenda usadas antes de persistir jornadas y bloqueos.</summary>
public static class DentistScheduleRules
{
    public const string PilotTimeZoneId = "America/Mexico_City";

    public static void ValidateWeekly(DentistWeeklySchedule schedule)
    {
        if (schedule.StartsAt >= schedule.EndsAt)
            throw new ArgumentException("La hora de inicio debe ser anterior a la hora de fin.", nameof(schedule));
        if (schedule.BreakStartsAt.HasValue != schedule.BreakEndsAt.HasValue)
            throw new ArgumentException("El descanso requiere hora de inicio y fin.", nameof(schedule));
        if (schedule.BreakStartsAt is { } breakStart && schedule.BreakEndsAt is { } breakEnd &&
            (breakStart >= breakEnd || breakStart < schedule.StartsAt || breakEnd > schedule.EndsAt))
            throw new ArgumentException("El descanso debe quedar dentro de la jornada y tener duración positiva.", nameof(schedule));
        schedule.TimeZoneId = PilotTimeZoneId;
    }

    public static bool WeeklyOverlaps(DentistWeeklySchedule candidate, IEnumerable<DentistWeeklySchedule> existing) =>
        existing.Any(item => item.DayOfWeek == candidate.DayOfWeek && item.Id != candidate.Id &&
            candidate.StartsAt < item.EndsAt && item.StartsAt < candidate.EndsAt);

    public static void ValidateBlock(DentistScheduleBlock block)
    {
        if (block.StartsAt >= block.EndsAt)
            throw new ArgumentException("El inicio del bloqueo debe ser anterior al fin.", nameof(block));
        if (block.StartsAt.Offset != TimeSpan.Zero || block.EndsAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Los bloqueos deben enviarse en UTC (ISO-8601 con Z).", nameof(block));
    }

    public static bool BlocksOverlap(DentistScheduleBlock candidate, IEnumerable<DentistScheduleBlock> existing) =>
        existing.Any(item => item.Id != candidate.Id && candidate.StartsAt < item.EndsAt && item.StartsAt < candidate.EndsAt);
}
