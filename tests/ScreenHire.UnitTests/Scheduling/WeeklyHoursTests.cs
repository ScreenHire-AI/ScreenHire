using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// Hours in a week (docs/architecture/interview-scheduling.md, "Hours"): their rules, the busy time outside them in a zone,
/// across a change of the clocks, and what they do to the times offered. 2026-11-02 is a Monday; in London the clocks go
/// forward an hour on Sunday 2027-03-28.
/// </summary>
public sealed class WeeklyHoursTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static WeeklyHours TuesdayAndThursday(string from = "14:00", string to = "17:00") =>
        new([new DayHours(DayOfWeek.Tuesday, from, to), new DayHours(DayOfWeek.Thursday, from, to)]);

    [Theory]
    [InlineData("9:00", "17:00")]
    [InlineData("09:00", "25:00")]
    [InlineData("09:60", "17:00")]
    [InlineData("24:00", "24:00")]
    [InlineData("nine", "17:00")]
    public void Times_are_hours_and_minutes(string from, string to) =>
        new WeeklyHours([new DayHours(DayOfWeek.Monday, from, to)]).Problem().Should().Be("Write each time as hours and minutes, such as 09:00.");

    [Fact]
    public void Hours_end_after_they_start_do_not_overlap_and_hold_the_interview()
    {
        new WeeklyHours([]).Problem().Should().Be("Choose at least one day with hours.");
        new WeeklyHours([new DayHours(DayOfWeek.Monday, "17:00", "09:00")]).Problem().Should().Be("Hours must end after they start.");
        new WeeklyHours([new DayHours(DayOfWeek.Monday, "09:00", "12:00"), new DayHours(DayOfWeek.Monday, "11:00", "13:00")])
            .Problem().Should().Be("The hours on Monday overlap.");
        TuesdayAndThursday("14:00", "14:30").Problem(45).Should().Be("The hours are shorter than the interview.");
        TuesdayAndThursday().Problem(60).Should().BeNull();
        new WeeklyHours([new DayHours(DayOfWeek.Saturday, "18:00", "24:00")]).Problem().Should().BeNull("24:00 is the end of the day");
    }

    [Fact]
    public void Outside_the_hours_is_busy_and_the_hours_are_free()
    {
        var busy = TuesdayAndThursday().Outside(Utc, At(2, 0), At(6, 0));

        busy.Should().Equal(
            new BusyInterval(At(2, 0), At(3, 14)),
            new BusyInterval(At(3, 17), At(5, 14)),
            new BusyInterval(At(5, 17), At(6, 0)));
        TuesdayAndThursday().Contains(Utc, At(3, 14), At(3, 15)).Should().BeTrue();
        TuesdayAndThursday().Contains(Utc, At(3, 16, 30), At(3, 17, 30)).Should().BeFalse("it runs past the block");
        TuesdayAndThursday().Contains(Utc, At(4, 14), At(4, 15)).Should().BeFalse("Wednesday has no hours");
    }

    [Fact]
    public void Hours_are_wall_clock_hours_across_the_change_of_the_clocks()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var mornings = new WeeklyHours([new DayHours(DayOfWeek.Friday, "09:00", "10:00"), new DayHours(DayOfWeek.Monday, "09:00", "10:00")]);

        var busy = mornings.Outside(london, new DateTimeOffset(2027, 3, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 3, 30, 0, 0, 0, TimeSpan.Zero));

        // Friday 09:00 is GMT (09:00 UTC); Monday 09:00 is BST (08:00 UTC).
        busy.Should().Contain(new BusyInterval(new DateTimeOffset(2027, 3, 26, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 3, 29, 8, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Interview_blocks_offer_times_on_their_days_only_weekends_too()
    {
        var hours = new WeeklyHours([new DayHours(DayOfWeek.Saturday, "10:00", "12:00")]);

        var slots = SlotFinder.Find(At(2, 0), At(9, 0), TimeSpan.FromMinutes(60), 30, Utc, hours, At(2, 0), [[]], 100);

        slots.Should().Equal(At(7, 10), At(7, 10, 30), At(7, 11));
    }

    [Fact]
    public void Two_stretches_on_a_day_each_start_on_their_own_step()
    {
        var hours = new WeeklyHours([new DayHours(DayOfWeek.Monday, "09:00", "10:00"), new DayHours(DayOfWeek.Monday, "13:15", "14:15")]);

        var slots = SlotFinder.Find(At(2, 0), At(3, 0), TimeSpan.FromMinutes(60), 30, Utc, hours, At(2, 0), [[]], 100);

        slots.Should().Equal(At(2, 9), At(2, 13, 15));
    }

    [Fact]
    public void The_weekday_hours_offer_what_every_request_offered_before()
    {
        var before = SlotFinder.Find(At(2, 0), At(9, 0), TimeSpan.FromMinutes(30), 30, Utc, new TimeOnly(9, 0), new TimeOnly(17, 0), At(2, 0), [[]], 1000);
        var weekdays = SlotFinder.Find(At(2, 0), At(9, 0), TimeSpan.FromMinutes(30), 30, Utc, WeeklyHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0)), At(2, 0), [[]], 1000);

        weekdays.Should().Equal(before);
        weekdays.Should().HaveCount(5 * 16);
    }
}

/// <summary>A member's own interview hours as busy time: outside their hours, the buffer around each booking, and a full day.</summary>
public sealed class InterviewHoursTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static InterviewHours Hours(WeeklyHours? weekly = null, int buffer = 0, int? max = null)
    {
        var hours = InterviewHours.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        hours.Set("UTC", weekly, buffer, max, Now);
        return hours;
    }

    [Fact]
    public void The_buffer_keeps_time_free_either_side_of_each_booking()
    {
        var busy = Hours(buffer: 15).BusyTime([new BusyInterval(At(2, 10), At(2, 11))], At(2, 0), At(3, 0));

        busy.Should().Equal(new BusyInterval(At(2, 9, 45), At(2, 11, 15)));
    }

    [Fact]
    public void A_day_at_the_most_interviews_is_busy_from_midnight_to_midnight()
    {
        var busy = Hours(max: 2).BusyTime([new BusyInterval(At(2, 9), At(2, 10)), new BusyInterval(At(2, 14), At(2, 15))], At(2, 0), At(4, 0));

        busy.Should().Equal(new BusyInterval(At(2, 0), At(3, 0)));
        Hours(max: 3).BusyTime([new BusyInterval(At(2, 9), At(2, 10)), new BusyInterval(At(2, 14), At(2, 15))], At(2, 0), At(4, 0))
            .Should().BeEmpty("the day has room for one more");
    }

    [Fact]
    public void Outside_the_member_hours_is_busy_and_without_hours_nothing_is()
    {
        var weekly = new WeeklyHours([new DayHours(DayOfWeek.Monday, "09:00", "12:00")]);

        Hours(weekly).BusyTime([], At(2, 0), At(3, 0)).Should().Equal(new BusyInterval(At(2, 0), At(2, 9)), new BusyInterval(At(2, 12), At(3, 0)));
        Hours(weekly).HasHours.Should().BeTrue();
        Hours().BusyTime([], At(2, 0), At(3, 0)).Should().BeEmpty();
        Hours().HasHours.Should().BeFalse("a buffer alone is not a calendar");
    }

    [Fact]
    public void Settings_out_of_range_are_refused_in_words()
    {
        InterviewHours.Problem("Nowhere/Else", null, 0, null).Should().Be("Choose a time zone.");
        InterviewHours.Problem("UTC", null, 121, null).Should().Be("The time between interviews can be at most 120 minutes.");
        InterviewHours.Problem("UTC", null, 0, 0).Should().Be("The most interviews in a day must be between 1 and 12.");
        InterviewHours.Problem("UTC", new WeeklyHours([]), 0, null).Should().Be("Choose at least one day with hours.");
        InterviewHours.Problem("Europe/London", null, 10, 4).Should().BeNull();
    }
}
