using System.Text.Json;
using System.Text.Json.Nodes;
using static ScreenHire.IntegrationTests.Scheduling.SchedulingOptionSupport;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// Changing how the candidate picks after the invitation went (docs/architecture/interview-scheduling.md, "Changing course"):
/// an open interview moves between the three options on its own link, and a booked one is moved with the new option while its
/// time stays held until the candidate accepts a new one.
/// </summary>
public sealed class InterviewChangeOptionTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_open_interview_moves_from_our_times_to_their_times_to_our_suggestion_on_the_same_link_and_the_candidate_is_emailed_each_time()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.OfferedAsync("OurTimes", OurTimesSubject);

        (await ChangeAsync(world, interviewId, "TheirTimes")).Expect(HttpStatusCode.OK).Text("option").Should().Be("TheirTimes");
        await world.WaitForEmailAsync(world.CandidateEmail, OptionsUpdatedSubject);
        var theirs = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        theirs.Text("option").Should().Be("TheirTimes", "the link the candidate already had shows the new options");
        theirs.TimesOf("suggestable").Should().NotBeEmpty();

        var proposed = new[] { DaysOut(3) };
        var suggested = (await ChangeAsync(world, interviewId, "OurSuggestion", body => body["proposedTimes"] = new JsonArray(JsonValue.Create(proposed[0])))).Expect(HttpStatusCode.OK);
        suggested.Text("option").Should().Be("OurSuggestion");
        await world.WaitForEmailAsync(world.CandidateEmail, OptionsUpdatedSubject, count: 2);
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("option").Should().Be("OurSuggestion");
        page.TimesOf("proposedTimes").Should().Equal(proposed);
        page.TimesOf("suggestable").Should().BeEmpty();

        (await ChangeAsync(world, interviewId, "OurTimes")).Expect(HttpStatusCode.OK);
        InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)).Should().NotBeEmpty("it picks from our times again");
        (await world.InterviewsAsync()).Should().ContainSingle("the change is on the same interview, not a new one");
        await world.WaitForTimelineAsync("InterviewOptionChanged", count: 3);
    }

    [Fact]
    public async Task A_change_to_we_will_suggest_a_time_without_a_time_is_refused_and_leaves_the_interview_as_it_was()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.OfferedAsync("OurTimes", OurTimesSubject);

        var refused = await ChangeAsync(world, interviewId, "OurSuggestion");

        refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("proposedTimes");
        (await world.InterviewAsync(interviewId)).GetProperty("option").GetString().Should().Be("OurTimes");
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("option").Should().Be("OurTimes");
    }

    [Fact]
    public async Task A_booked_interview_keeps_its_time_when_moved_with_a_suggestion_until_the_candidate_accepts_and_then_reschedules()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.OfferedAsync("OurTimes", OurTimesSubject);
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        var proposed = chosen.AddDays(1);

        var moved = (await ChangeAsync(world, interviewId, "OurSuggestion", body => body["proposedTimes"] = new JsonArray(JsonValue.Create(proposed)))).Expect(HttpStatusCode.OK);

        moved.Text("status").Should().Be("Booked");
        moved.At("startsAt").GetDateTimeOffset().Should().Be(chosen, "the booked time stays held until a new one is booked");
        moved.At("newTimesOfferedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        await world.WaitForEmailAsync(world.CandidateEmail, OptionsUpdatedSubject);
        var waiting = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        waiting.Text("state").Should().Be("Booked");
        waiting.At("newTimesOffered").GetBoolean().Should().BeTrue();
        waiting.TimesOf("proposedTimes").Should().Equal(proposed);

        (await world.RescheduleAsync(token, proposed.AddMinutes(30))).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("slot_taken");
        (await world.InterviewAsync(interviewId)).GetProperty("startsAt").GetDateTimeOffset().Should().Be(chosen, "a time that was not proposed moved nothing");
        (await world.RescheduleAsync(token, proposed)).Expect(HttpStatusCode.NoContent);

        var after = await world.InterviewAsync(interviewId);
        after.GetProperty("status").GetString().Should().Be("Booked");
        after.GetProperty("startsAt").GetDateTimeOffset().Should().Be(proposed);
        await world.WaitForTimelineAsync("InterviewRescheduled");
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 1, e => e.StartsAt == proposed);
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).At("newTimesOffered").GetBoolean().Should().BeFalse("the offer ended with the move");
    }

    private static Task<ApiResult> ChangeAsync(InterviewWorld world, Guid interviewId, string option, Action<JsonObject>? more = null)
    {
        var now = DateTimeOffset.UtcNow;
        var body = new JsonObject { ["option"] = option, ["windowStart"] = now, ["windowEnd"] = now.AddDays(14) };
        more?.Invoke(body);
        return world.Owner.PostAsync($"/v1/interviews/{interviewId}/option", body);
    }
}
