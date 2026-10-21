using DentalConnect.Domain.Entities;

namespace DentalConnect.Tests;

public class DentistScheduleRulesTests
{
    [Fact]
    public void ValidateWeekly_RejectsStartAfterEnd()
    {
        var schedule = new DentistWeeklySchedule { StartsAt = new TimeOnly(17, 0), EndsAt = new TimeOnly(9, 0) };
        Assert.Throws<ArgumentException>(() => DentistScheduleRules.ValidateWeekly(schedule));
    }

    [Fact]
    public void ValidateWeekly_RequiresBreakToFitInsideShift()
    {
        var schedule = new DentistWeeklySchedule
        { StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(17, 0), BreakStartsAt = new TimeOnly(16, 30), BreakEndsAt = new TimeOnly(17, 30) };
        Assert.Throws<ArgumentException>(() => DentistScheduleRules.ValidateWeekly(schedule));
    }

    [Fact]
    public void WeeklyOverlaps_RejectsIntersectingIntervalsButAllowsAdjacentOnes()
    {
        var existing = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(12, 0) };
        var overlap = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(11, 30), EndsAt = new TimeOnly(13, 0) };
        var adjacent = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(12, 0), EndsAt = new TimeOnly(13, 0) };
        Assert.True(DentistScheduleRules.WeeklyOverlaps(overlap, [existing]));
        Assert.False(DentistScheduleRules.WeeklyOverlaps(adjacent, [existing]));
    }

    [Fact]
    public void ValidateWeekly_UsesMexicoCityPilotTimeZone()
    {
        var schedule = new DentistWeeklySchedule { StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(17, 0) };
        DentistScheduleRules.ValidateWeekly(schedule);
        Assert.Equal("America/Mexico_City", schedule.TimeZoneId);
    }

    [Fact]
    public void ValidateBlock_RejectsNonUtcAndInvalidIntervals()
    {
        var invalid = new DentistScheduleBlock
        { StartsAt = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.FromHours(-6)), EndsAt = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(-6)) };
        Assert.Throws<ArgumentException>(() => DentistScheduleRules.ValidateBlock(invalid));
        var reversed = new DentistScheduleBlock
        { StartsAt = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero) };
        Assert.Throws<ArgumentException>(() => DentistScheduleRules.ValidateBlock(reversed));
    }
}
