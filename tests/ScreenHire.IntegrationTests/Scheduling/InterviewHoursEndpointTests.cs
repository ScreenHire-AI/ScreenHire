using System.Text.Json;
using System.Text.Json.Nodes;
using static ScreenHire.IntegrationTests.Scheduling.SchedulingOptionSupport;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// A member's own interview hours (docs/architecture/interview-scheduling.md, "Personal hours and ScreenHire's calendar"):
/// saving and reading them back, and what they do to the times a candidate is offered. The arithmetic of hours, buffers and
/// daily limits is the unit tests' (<c>InterviewHoursTests</c>, <c>WeeklyHoursTests</c>); these show it reaches the candidate's page.
/// </summary>
public sealed class InterviewHoursEndpointTests(AppHostFixture fixture)
{
    private const string HoursPath = "/v1/account/interview-hours";

    [Fact]
    public async Task Interview_hours_save_and_read_back_and_a_member_with_none_reads_the_defaults()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);

        var before = (await world.Owner.GetAsync(HoursPath)).Expect(HttpStatusCode.OK);
        before.Text("timeZone").Should().Be("UTC");
        before.At("weekly").ValueKind.Should().Be(JsonValueKind.Null);
        before.At("bufferMinutes").GetInt32().Should().Be(0);
        before.At("maxPerDay").ValueKind.Should().Be(JsonValueKind.Null);
        before.At("feedUrl").ValueKind.Should().Be(JsonValueKind.Null);

        var saved = (await world.Owner.PutAsync(HoursPath, Body("Europe/London", WeekdayHours("10:00", "16:00"), 15, 3))).Expect(HttpStatusCode.OK);
        saved.Text("timeZone").Should().Be("Europe/London");

        var after = (await world.Owner.GetAsync(HoursPath)).Expect(HttpStatusCode.OK);
        after.Text("timeZone").Should().Be("Europe/London");
        after.At("bufferMinutes").GetInt32().Should().Be(15);
        after.At("maxPerDay").GetInt32().Should().Be(3);
        after.At("weekly.days").GetArrayLength().Should().Be(5);
        after.Text("weekly.days.0.from").Should().Be("10:00");
        after.Text("weekly.days.0.to").Should().Be("16:00");

        // Another member's hours are theirs alone.
        (await world.Second.GetAsync(HoursPath)).Expect(HttpStatusCode.OK).At("weekly").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Mars/Olympus", "weekly", 0, null, "timeZone")]
    [InlineData("UTC", "weekly", 500, null, "bufferMinutes")]
    [InlineData("UTC", "weekly", 0, 99, "maxPerDay")]
    [InlineData("UTC", "backwards", 0, null, "weekly")]
    [InlineData("UTC", "none", 0, null, "weekly")]
    public async Task Interview_hours_that_cannot_work_are_refused_naming_the_field(string zone, string hours, int buffer, int? maxPerDay, string field)
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        JsonObject? weekly = hours switch
        {
            "weekly" => WeekdayHours("09:00", "17:00"),
            "backwards" => WeekdayHours("17:00", "09:00"),
            _ => new JsonObject { ["days"] = new JsonArray() },
        };

        var refused = await world.Owner.PutAsync(HoursPath, Body(zone, weekly, buffer, maxPerDay));

        refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain(field);
        (await world.Owner.GetAsync(HoursPath)).Expect(HttpStatusCode.OK).At("weekly").ValueKind.Should().Be(JsonValueKind.Null, "nothing was saved");
    }

    [Fact]
    public async Task Personal_hours_narrow_the_times_the_candidate_is_offered_to_inside_them()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        (await world.Owner.PutAsync(HoursPath, Body("UTC", WeekdayHours("12:00", "14:00"), 0, null))).Expect(HttpStatusCode.OK);

        var (_, token) = await world.ScheduledAsync(interviewers: [world.Owner.UserId]);

        var slots = InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK));
        slots.Should().NotBeEmpty();
        slots.Should().OnlyContain(
            s => s.UtcDateTime.TimeOfDay >= TimeSpan.FromHours(12) && s.UtcDateTime.TimeOfDay <= TimeSpan.FromHours(13.5),
            "the request allows 09:00 to 17:00, and the interviewer's own hours are 12:00 to 14:00");
    }

    [Fact]
    public async Task A_buffer_and_a_daily_limit_take_times_away_around_an_interview_already_booked()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var alone = new[] { world.Owner.UserId };
        var (firstInterview, firstToken) = await world.ScheduledAsync(interviewers: alone);
        var firstSlots = InterviewWorld.SlotsOf((await world.PublicGetAsync(firstToken)).Expect(HttpStatusCode.OK));
        var booked = firstSlots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2) && firstSlots.Contains(s.AddMinutes(-60)) && firstSlots.Contains(s.AddMinutes(60)));
        (await world.BookAsync(firstToken, booked)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(firstInterview, "Synced");

        var (second, secondEmail) = await world.AddApplicationAsync();
        var (_, token) = await world.ScheduledAsync(second, secondEmail, alone);

        // The booked half hour is taken, and the half hours either side of it are free.
        var plain = await SlotsAsync(world, token);
        plain.Should().NotContain(booked).And.Contain([booked.AddMinutes(-30), booked.AddMinutes(30)]);

        // A 30 minute buffer takes those two as well, and leaves the ones beyond.
        (await world.Owner.PutAsync(HoursPath, Body("UTC", WeekdayHours("09:00", "17:00"), 30, null))).Expect(HttpStatusCode.OK);
        var buffered = await SlotsAsync(world, token);
        buffered.Should().NotContain([booked.AddMinutes(-30), booked, booked.AddMinutes(30)]).And.Contain([booked.AddMinutes(-60), booked.AddMinutes(60)]);

        // At most one interview a day takes the rest of that day away, and no other day.
        (await world.Owner.PutAsync(HoursPath, Body("UTC", WeekdayHours("09:00", "17:00"), 0, 1))).Expect(HttpStatusCode.OK);
        var limited = await SlotsAsync(world, token);
        limited.Should().NotContain(s => s.UtcDateTime.Date == booked.UtcDateTime.Date);
        limited.Should().NotBeEmpty().And.Contain(s => s.UtcDateTime.Date != booked.UtcDateTime.Date);
    }

    [Fact]
    public async Task An_interviewer_with_hours_and_no_calendar_can_be_offered_times_where_one_with_neither_cannot()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        var alone = new[] { world.Owner.UserId };

        var refused = (await world.ScheduleAsync(interviewers: alone)).Expect(HttpStatusCode.UnprocessableEntity);
        refused.Text("detail").Should().Contain("no working calendar and no interview hours").And.Contain("Your account, Calendar");
        refused.At("interviewerUserIds").EnumerateArray().Select(u => u.GetGuid()).Should().Equal(world.Owner.UserId);
        var before = (await world.Owner.GetAsync($"/v1/interviewers?jobId={world.JobId}")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(i => i.GetProperty("userId").GetGuid() == world.Owner.UserId);
        before.GetProperty("calendarConnected").GetBoolean().Should().BeFalse();
        before.GetProperty("hasHours").GetBoolean().Should().BeFalse();

        (await world.Owner.PutAsync(HoursPath, Body("UTC", WeekdayHours("09:00", "17:00"), 0, null))).Expect(HttpStatusCode.OK);

        var after = (await world.Owner.GetAsync($"/v1/interviewers?jobId={world.JobId}")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(i => i.GetProperty("userId").GetGuid() == world.Owner.UserId);
        after.GetProperty("calendarConnected").GetBoolean().Should().BeFalse("hours are not a calendar");
        after.GetProperty("hasHours").GetBoolean().Should().BeTrue();
        (await world.ScheduleAsync(interviewers: alone)).Expect(HttpStatusCode.Created);
    }

    private static JsonObject Body(string zone, JsonObject? weekly, int buffer, int? maxPerDay) => new()
    {
        ["timeZone"] = zone,
        ["weekly"] = weekly,
        ["bufferMinutes"] = buffer,
        ["maxPerDay"] = maxPerDay,
    };

    private static async Task<IReadOnlyList<DateTimeOffset>> SlotsAsync(InterviewWorld world, string token) =>
        InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK));
}
