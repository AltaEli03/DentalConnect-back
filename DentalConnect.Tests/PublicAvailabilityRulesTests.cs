using DentalConnect;
using DentalConnect.Domain.Entities;

namespace DentalConnect.Tests;

public class PublicAvailabilityRulesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset BeforeSlots = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GeneratesCompleteNonOverlappingIntervalsUsingServiceDuration()
    {
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(10, 0) };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 30, [schedule], [], Utc, BeforeSlots);
        Assert.Equal(2, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), slots[0].StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero), slots[1].StartsAt);
        Assert.All(slots, slot => Assert.Equal(TimeSpan.FromMinutes(30), slot.EndsAt - slot.StartsAt));
        Assert.DoesNotContain(slots.Zip(slots.Skip(1)), pair => pair.First.EndsAt > pair.Second.StartsAt);
    }

    [Fact]
    public void ExcludesIntervalsThatOverlapBreaksAndBlocks()
    {
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(11, 0), BreakStartsAt = new TimeOnly(9, 30), BreakEndsAt = new TimeOnly(10, 0) };
        var block = new DentistScheduleBlock { StartsAt = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 10, 26, 10, 30, 0, TimeSpan.Zero) };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 30, [schedule], [block], Utc, BeforeSlots);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 0);
        Assert.DoesNotContain(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 30);
        Assert.DoesNotContain(slots, slot => slot.StartsAt.Hour == 10 && slot.StartsAt.Minute == 0);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 10 && slot.StartsAt.Minute == 30);
    }

    [Fact]
    public void ConfirmedAppointmentsBlockSlotsButPendingRequestsDoNot()
    {
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(11, 0) };
        var pending = new Appointment { StartsAt = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero), Status = "PENDING" };
        var confirmed = new Appointment { StartsAt = new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), Status = "CONFIRMED" };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 30, [schedule], [], Utc, BeforeSlots, [pending, confirmed]);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 0);
        Assert.DoesNotContain(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 30);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 10 && slot.StartsAt.Minute == 0);
    }

    [Fact]
    public void ProposedStatusDoesNotBlockAvailability()
    {
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(10, 0) };
        var proposed = new Appointment { StartsAt = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), EndsAt = new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero), Status = "PROPOSED" };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 30, [schedule], [], Utc, BeforeSlots, [proposed]);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 0);
        Assert.Contains(slots, slot => slot.StartsAt.Hour == 9 && slot.StartsAt.Minute == 30);
    }

    [Fact]
    public void RejectsInvalidHorizonAndDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicAvailabilityRules.Generate(Monday, 0, 30, [], [], Utc, BeforeSlots));
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicAvailabilityRules.Generate(Monday, 31, 30, [], [], Utc, BeforeSlots));
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicAvailabilityRules.Generate(Monday, 1, 0, [], [], Utc, BeforeSlots));
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicAvailabilityRules.Generate(Monday, 1, -15, [], [], Utc, BeforeSlots));
    }

    [Fact]
    public void EmitsOnlyFullIntervalsAndIgnoresSchedulesForOtherWeekdays()
    {
        var monday = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(10, 0) };
        var tuesday = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Tuesday, StartsAt = new TimeOnly(13, 0), EndsAt = new TimeOnly(14, 0) };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 45, [monday, tuesday], [], Utc, BeforeSlots);
        var slot = Assert.Single(slots);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), slot.StartsAt);
        Assert.Equal(TimeSpan.FromMinutes(45), slot.EndsAt - slot.StartsAt);
    }

    [Fact]
    public void ExcludesStartedSlotsAndReturnsFutureSlotsInChronologicalOrder()
    {
        var monday = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(10, 0) };
        var tuesday = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Tuesday, StartsAt = new TimeOnly(8, 0), EndsAt = new TimeOnly(9, 0) };
        var now = new DateTimeOffset(2026, 10, 26, 9, 15, 0, TimeSpan.Zero);
        var slots = PublicAvailabilityRules.Generate(Monday, 2, 30, [tuesday, monday], [], Utc, now);
        Assert.Equal(3, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero), slots[0].StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero), slots[1].StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 8, 30, 0, TimeSpan.Zero), slots[2].StartsAt);
    }

    [Fact]
    public void ConvertsMexicoCityScheduleToUtcWithoutLosingLocalAppointmentTime()
    {
        var clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(PublicAvailabilityRules.PilotTimeZoneId);
        var schedule = new DentistWeeklySchedule { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(9, 0), EndsAt = new TimeOnly(10, 0) };
        var slots = PublicAvailabilityRules.Generate(Monday, 1, 60, [schedule], [], clinicTimeZone, BeforeSlots);
        var slot = Assert.Single(slots);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero), slot.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 16, 0, 0, TimeSpan.Zero), slot.EndsAt);
    }
}
