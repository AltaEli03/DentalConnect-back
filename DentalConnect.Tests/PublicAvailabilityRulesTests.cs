using DentalConnect;
using DentalConnect.Domain.Entities;

namespace DentalConnect.Tests;

public class PublicAvailabilityRulesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset BeforeSlots = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GeneratesIntervalsAtFifteenMinuteStepsWithinWeeklySchedule()
    {
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(10, 0) };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 30, [schedule], [], Utc, BeforeSlots);
        Assert.Equal(3, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), slots[0].StartsAt);
        Assert.All(slots, slot => Assert.Equal(TimeSpan.FromMinutes(30), slot.EndsAt - slot.StartsAt));
    }

    [Fact]
    public void ExcludesIntervalsThatOverlapBreaksAndBlocks()
    {
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(11, 0), BreakStartsAt = new TimeOnly(9, 30), BreakEndsAt = new TimeOnly(10, 0) };
        var block = new DentistScheduleBlock { StartsAt = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 10, 26, 10, 30, 0, TimeSpan.Zero) };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 30, [schedule], [block], Utc, BeforeSlots);
        Assert.DoesNotContain(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 15);
        Assert.DoesNotContain(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 30);
        Assert.DoesNotContain(slots, slot => slot.StartsAt.Hour == 10 && slot.StartsAt.Minute == 0);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 10 && slot.StartsAt.Minute == 30);
    }

    [Fact]
    public void RejectsInvalidHorizonAndDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicAvailabilityRules.Generate(Monday, 31, 30, [], [], Utc, BeforeSlots));
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicAvailabilityRules.Generate(Monday, 1, 0, [], [], Utc, BeforeSlots));
    }
}
