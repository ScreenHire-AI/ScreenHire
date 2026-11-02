using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// Which start times an interview is offered at (docs/architecture/interview-scheduling.md): weekdays in working hours, on
/// the step, with every interviewer free for the whole interview, and daylight saving time right in the interview's zone.
/// 2026-11-02 is a Monday.
/// </summary>
public sealed class SlotFinderTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static IReadOnlyList<DateTimeOffset> Find(
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        int minutes = 60,
        int step = 30,
        TimeZoneInfo? zone = null,
        TimeOnly? dayStart = null,
        TimeOnly? dayEnd = null,
        DateTimeOffset? earliest = null,
        IReadOnlyList<IReadOnlyList<BusyInterval>>? busy = null,
        int max = 1000)
    {
        var start = windowStart ?? Monday;
        return SlotFinder.Find(
            start, windowEnd ?? start.AddDays(1), TimeSpan.FromMinutes(minutes), step, zone ?? Utc,
            dayStart ?? new TimeOnly(9, 0), dayEnd ?? new TimeOnly(17, 0), earliest ?? start, busy ?? [[]], max);
    }

    private static BusyInterval Busy(int fromHour, int toHour, int day = 2) => new(At(day, fromHour), At(day, toHour));

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    [Fact]
    public void A_free_day_offers_every_step_from_the_start_of_working_hours_to_the_last_one_that_fits()
    {
        var slots = Find();

        slots.Should().HaveCount(15);
        slots[0].Should().Be(At(2, 9));
        slots[^1].Should().Be(At(2, 16), "a 60 minute interview starting at 16:00 ends exactly at the end of the day");
        slots.Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_meeting_that_ends_when_a_slot_starts_leaves_it_free_and_one_that_starts_when_it_ends_too()
    {
        var slots = Find(busy: [[Busy(10, 11)]]);

        slots.Should().NotContain([At(2, 9, 30), At(2, 10), At(2, 10, 30)]);
        slots.Should().Contain([At(2, 9), At(2, 11)]);
    }

    [Fact]
    public void An_interview_longer_than_a_gap_does_not_fit_in_it()
    {
        // Free 10:00 to 11:00 between two meetings.
        var busy = new[] { new[] { Busy(9, 10), Busy(11, 12) } };

        Find(minutes: 60, busy: busy).Should().Contain(At(2, 10));
        Find(minutes: 90, busy: busy).Should().NotContain(At(2, 10)).And.NotContain(At(2, 10, 30));
    }

    [Fact]
    public void Busy_time_outside_the_day_changes_nothing_and_a_meeting_across_the_whole_day_leaves_no_slot()
    {
        Find(busy: [[new BusyInterval(At(1, 0), At(2, 8))]]).Should().HaveCount(15);
        Find(busy: [[new BusyInterval(At(1, 0), At(3, 0))]]).Should().BeEmpty();
    }

    [Fact]
    public void Weekends_are_never_offered()
    {
        // Friday 6 November to Monday 9 November.
        var slots = Find(windowStart: At(6, 0), windowEnd: At(10, 0));

        slots.Select(s => s.DayOfWeek).Distinct().Should().BeEquivalentTo([DayOfWeek.Friday, DayOfWeek.Monday]);
        slots.Should().HaveCount(30);
    }

    [Fact]
    public void Working_hours_bound_the_slots_in_local_time()
    {
        var slots = Find(minutes: 30, dayStart: new TimeOnly(10, 0), dayEnd: new TimeOnly(12, 0));

        slots.Should().Equal(At(2, 10), At(2, 10, 30), At(2, 11), At(2, 11, 30));
    }

    [Fact]
    public void Start_times_follow_the_step_from_the_start_of_working_hours()
    {
        Find(minutes: 30, step: 45, dayEnd: new TimeOnly(11, 0)).Should().Equal(At(2, 9), At(2, 9, 45), At(2, 10, 30));
        Find(minutes: 30, step: 30, dayStart: new TimeOnly(9, 10), dayEnd: new TimeOnly(10, 40)).Should().Equal(At(2, 9, 10), At(2, 9, 40), At(2, 10, 10));
    }

    [Fact]
    public void Nothing_is_offered_before_the_earliest_start_which_is_now_plus_the_notice()
    {
        var slots = Find(earliest: At(2, 12, 15));

        slots[0].Should().Be(At(2, 12, 30));
        Find(earliest: At(2, 12, 30))[0].Should().Be(At(2, 12, 30), "a slot exactly at the earliest start is allowed");
    }

    [Fact]
    public void A_window_that_ends_or_starts_mid_day_cuts_the_slots_there()
    {
        Find(windowEnd: At(2, 12))[^1].Should().Be(At(2, 11), "the interview must end by the end of the window");
        Find(windowStart: At(2, 14, 10), windowEnd: At(3, 0))[0].Should().Be(At(2, 14, 30));
    }

    [Fact]
    public void At_most_max_slots_are_returned_from_the_start()
    {
        Find(max: 3).Should().Equal(At(2, 9), At(2, 9, 30), At(2, 10));
        Find(max: 0).Should().BeEmpty();
    }

    [Fact]
    public void Two_interviewers_are_offered_only_the_times_both_are_free()
    {
        var slots = Find(minutes: 30, busy: [[Busy(9, 10)], [Busy(11, 12)]], dayEnd: new TimeOnly(13, 0));

        slots.Should().Equal(At(2, 10), At(2, 10, 30), At(2, 12), At(2, 12, 30));
    }

    [Fact]
    public void Three_interviewers_intersect_and_one_with_no_busy_time_changes_nothing()
    {
        var slots = Find(minutes: 60, dayEnd: new TimeOnly(14, 0), busy: [[Busy(9, 10)], [Busy(10, 11)], [Busy(12, 13)], []]);

        slots.Should().Equal(At(2, 11), At(2, 13));
    }

    [Fact]
    public void Nobody_to_interview_means_nobody_is_busy()
    {
        Find(busy: []).Should().HaveCount(15);
    }

    [Fact]
    public void Busy_intervals_merge_when_they_overlap_nest_or_touch_and_empty_ones_are_dropped()
    {
        var merged = SlotFinder.Merge(
        [
            Busy(13, 14),
            Busy(9, 11),
            Busy(10, 12),
            Busy(9, 10),
            Busy(12, 12),
            new BusyInterval(At(2, 15), At(2, 14)),
            Busy(14, 15),
        ]);

        merged.Should().Equal(Busy(9, 12), Busy(13, 15));
    }

    [Fact]
    public void An_unsorted_calendar_gives_the_same_answer_as_a_sorted_one()
    {
        var sorted = new[] { Busy(9, 10), Busy(11, 12), Busy(14, 15) };

        Find(busy: [[.. sorted.Reverse()]]).Should().Equal(Find(busy: [sorted]));
    }

    [Fact]
    public void Invalid_inputs_are_refused()
    {
        FluentActions.Invoking(() => Find(minutes: 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Find(step: 0)).Should().Throw<ArgumentOutOfRangeException>();
        Find(dayStart: new TimeOnly(17, 0), dayEnd: new TimeOnly(9, 0)).Should().BeEmpty();
    }

    // The daylight saving transitions of the real zones fall on Sundays, which are never offered. The weekdays around them
    // show the local 09:00 start moving by an hour in UTC.
    [Fact]
    public void In_New_York_the_morning_slot_moves_an_hour_in_utc_when_the_clocks_go_forward()
    {
        var newYork = Zone("America/New_York");

        // Friday 6 March 2026 is on standard time, Monday 9 March on daylight saving time (the change was Sunday 8 March).
        var slots = SlotFinder.Find(
            new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(60), 30, newYork, new TimeOnly(9, 0), new TimeOnly(17, 0), DateTimeOffset.MinValue, [[]], 1000);

        slots.Should().Contain(new DateTimeOffset(2026, 3, 6, 14, 0, 0, TimeSpan.Zero), "09:00 EST");
        slots.Should().Contain(new DateTimeOffset(2026, 3, 9, 13, 0, 0, TimeSpan.Zero), "09:00 EDT");
        slots.Should().NotContain(new DateTimeOffset(2026, 3, 9, 14, 0, 0, TimeSpan.Zero).AddHours(-2), "08:00 EDT is before working hours");
        slots.Select(s => TimeZoneInfo.ConvertTime(s, newYork).DayOfWeek).Distinct().Should().BeEquivalentTo([DayOfWeek.Friday, DayOfWeek.Monday]);
        slots.Should().HaveCount(30);
    }

    [Fact]
    public void In_New_York_the_morning_slot_moves_an_hour_in_utc_when_the_clocks_go_back()
    {
        var newYork = Zone("America/New_York");

        // Friday 30 October is on daylight saving time, Monday 2 November on standard time (the change was Sunday 1 November).
        var slots = SlotFinder.Find(
            new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(60), 30, newYork, new TimeOnly(9, 0), new TimeOnly(17, 0), DateTimeOffset.MinValue, [[]], 1000);

        slots.Should().Contain(new DateTimeOffset(2026, 10, 30, 13, 0, 0, TimeSpan.Zero), "09:00 EDT");
        slots.Should().Contain(new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero), "09:00 EST");
        slots.Should().HaveCount(30);
    }

    [Fact]
    public void In_Sydney_the_southern_summer_time_change_moves_the_slot_the_other_way()
    {
        var sydney = Zone("Australia/Sydney");

        // Daylight saving time began Sunday 4 October 2026: Friday 2 October is AEST (+10), Monday 5 October AEDT (+11).
        var slots = SlotFinder.Find(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(60), 30, sydney, new TimeOnly(9, 0), new TimeOnly(17, 0), DateTimeOffset.MinValue, [[]], 1000);

        slots.Should().Contain(new DateTimeOffset(2026, 10, 1, 23, 0, 0, TimeSpan.Zero), "09:00 AEST on Friday");
        slots.Should().Contain(new DateTimeOffset(2026, 10, 4, 22, 0, 0, TimeSpan.Zero), "09:00 AEDT on Monday");
        slots.Select(s => TimeZoneInfo.ConvertTime(s, sydney).DayOfWeek).Should().OnlyContain(d => d != DayOfWeek.Saturday && d != DayOfWeek.Sunday);
    }

    [Fact]
    public void A_local_time_that_does_not_exist_is_skipped_and_the_slots_after_it_keep_real_time()
    {
        // A zone whose clocks go forward on a Wednesday (11 March 2026, 02:00 to 03:00), unlike the real ones.
        var zone = WeekdayChangeZone();

        var slots = SlotFinder.Find(
            new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30), 30, zone, new TimeOnly(1, 0), new TimeOnly(5, 0), DateTimeOffset.MinValue, [[]], 1000);

        // 01:00 and 01:30 EST, no 02:00 or 02:30, then 03:00 to 04:30 EDT. Base offset is -05:00.
        slots.Should().Equal(
            new DateTimeOffset(2026, 3, 11, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 6, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 7, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 8, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_local_time_that_happens_twice_means_its_first_occurrence()
    {
        // The clocks go back on Wednesday 4 November 2026, 02:00 to 01:00, so 01:00 to 02:00 happens twice.
        var zone = WeekdayChangeZone();

        var slots = SlotFinder.Find(
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(30), 30, zone, new TimeOnly(0, 0), new TimeOnly(3, 0), DateTimeOffset.MinValue, [[]], 1000);

        // 00:00 and 00:30 EDT, 01:00 and 01:30 EDT (the first hour), then 02:00 and 02:30 EST. The second 01:00 hour is not offered.
        slots.Should().Equal(
            new DateTimeOffset(2026, 11, 4, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 4, 4, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 4, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 4, 5, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 4, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 4, 7, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_local_time_is_read_as_an_instant_by_the_rules_the_finder_uses()
    {
        var newYork = Zone("America/New_York");

        SlotFinder.InstantOf(new DateTime(2026, 11, 1, 1, 30, 0), newYork).Should().Be(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), "01:30 happens twice; the first is EDT");
        SlotFinder.InstantOf(new DateTime(2026, 3, 8, 2, 30, 0), newYork).Should().Be(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), "02:30 does not exist; it reads with the offset before the change");
        SlotFinder.InstantOf(new DateTime(2026, 7, 1, 12, 0, 0), newYork).Should().Be(new DateTimeOffset(2026, 7, 1, 16, 0, 0, TimeSpan.Zero));
    }

    private static TimeZoneInfo WeekdayChangeZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 11),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 4));
        return TimeZoneInfo.CreateCustomTimeZone("Test/Wednesday", TimeSpan.FromHours(-5), "Test", "Test standard", "Test daylight", [rule]);
    }
}
