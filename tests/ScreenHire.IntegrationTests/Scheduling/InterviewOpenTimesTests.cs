using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// What the Schedule interview sheet checks before anything is sent (docs/architecture/interview-scheduling.md, "Changing
/// course"): <c>POST /v1/scheduling/open-times</c> says how many times pick from our times would offer in the next 7 days, so
/// the sheet can suggest another way when there are none, and which interviewers' time cannot be known.
/// </summary>
public sealed class InterviewOpenTimesTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Open_times_count_the_next_seven_days_and_say_none_when_an_interviewer_is_busy_for_all_of_them()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);

        var open = (await OpenTimesAsync(world, world.JobId, [world.Owner.UserId, world.Second.UserId])).Expect(HttpStatusCode.OK);
        open.At("nextSevenDays").GetInt32().Should().BeGreaterThan(0, "weekdays 09:00 to 17:00 are free in the coming week");
        open.At("slots").GetArrayLength().Should().BeInRange(1, 50);
        open.At("unavailableInterviewerUserIds").GetArrayLength().Should().Be(0);

        // Busy through the whole first week: nothing is open in it, but the second week still is.
        var now = DateTimeOffset.UtcNow;
        await fixture.AddBusyAsync(world.OrganizationId, world.Second.UserId, now.AddHours(-1), now.AddDays(8), Ct);
        var busy = (await OpenTimesAsync(world, world.JobId, [world.Owner.UserId, world.Second.UserId])).Expect(HttpStatusCode.OK);
        busy.At("nextSevenDays").GetInt32().Should().Be(0);
        var later = busy.At("slots").EnumerateArray().Select(s => s.GetDateTimeOffset()).ToList();
        later.Should().NotBeEmpty().And.OnlyContain(s => s >= now.AddDays(8).AddMinutes(-1), "only times after the busy block are open");

        // The owner alone is not busy.
        (await OpenTimesAsync(world, world.JobId, [world.Owner.UserId])).Expect(HttpStatusCode.OK).At("nextSevenDays").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Open_times_name_an_interviewer_with_no_calendar_and_no_hours_and_offer_nothing_while_one_is_named()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        await InterviewWorld.ConnectCalendarAsync(world.Owner);

        var open = (await OpenTimesAsync(world, world.JobId, [world.Owner.UserId, world.Second.UserId])).Expect(HttpStatusCode.OK);

        open.At("unavailableInterviewerUserIds").EnumerateArray().Select(u => u.GetGuid()).Should().Equal(world.Second.UserId);
        open.At("slots").GetArrayLength().Should().Be(0);
        open.At("nextSevenDays").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Open_times_are_a_404_for_a_job_the_caller_cannot_see_and_a_400_for_an_interviewer_who_is_not_on_the_job()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var theirJob = (await theirs.CreateJobAsync(Samples.RemoteJob("Their job"))).Id();

        (await OpenTimesAsync(world, theirJob, [world.Owner.UserId])).Status.Should().Be(HttpStatusCode.NotFound, "another organization's job is not there");
        (await OpenTimesAsync(world, Guid.NewGuid(), [world.Owner.UserId])).Status.Should().Be(HttpStatusCode.NotFound);

        var stranger = await OpenTimesAsync(world, world.JobId, [world.Owner.UserId, theirs.UserId]);
        stranger.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("interviewerUserIds");
        (await OpenTimesAsync(world, world.JobId, [])).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("interviewerUserIds");
    }

    private static Task<ApiResult> OpenTimesAsync(InterviewWorld world, Guid jobId, Guid[] interviewers)
    {
        var now = DateTimeOffset.UtcNow;
        return world.Owner.PostAsync("/v1/scheduling/open-times", new JsonObject
        {
            ["jobId"] = jobId,
            ["interviewerUserIds"] = new JsonArray([.. interviewers.Select(id => (JsonNode)JsonValue.Create(id)!)]),
            ["durationMinutes"] = 30,
            ["windowStart"] = now,
            ["windowEnd"] = now.AddDays(21),
            ["timeZone"] = "UTC",
        });
    }
}
