using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// What happens to an interview around it (docs/architecture/interview-scheduling.md): the application ends, an interviewer
/// leaves the organization, or the candidate is erased. Through the API, the worker and the sandbox calendar.
/// </summary>
public sealed class InterviewLifecycleTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Erasing_a_candidate_with_a_booked_interview_leaves_no_event_on_any_interviewers_calendar()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        (await world.WaitForLiveEventsAsync(world.Owner.UserId, 1)).Single().Summary.Should().Contain("Casey Morgan");
        await world.WaitForLiveEventsAsync(world.Second.UserId, 1);
        var candidateId = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}")).Expect(HttpStatusCode.OK).At("candidate.id").GetGuid();

        (await world.Owner.PostAsync($"/v1/candidates/{candidateId}/erase", new JsonObject { ["confirmation"] = "Casey Morgan" })).Expect(HttpStatusCode.NoContent);

        // Gone for good, not marked deleted: the event row held the candidate's name and address.
        await WolverineStore.WaitUntilAsync(
            async () => (await world.EventsOfAsync(world.Owner.UserId)).Count + (await world.EventsOfAsync(world.Second.UserId)).Count == 0,
            "the erased candidate's events to leave both calendars",
            Ct);
    }

    [Fact]
    public async Task Rejecting_an_application_cancels_its_booked_interview_and_takes_the_events_off_the_calendars()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);
        await world.WaitForLiveEventsAsync(world.Second.UserId, 1);

        (await world.Owner.PostAsync($"/v1/applications/{world.ApplicationId}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);

        await world.WaitForTimelineAsync("InterviewCancelled");
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Cancelled);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 0);
        await world.WaitForLiveEventsAsync(world.Second.UserId, 0);
        (await world.InterviewsAsync()).Single().GetProperty("status").GetString().Should().Be("Cancelled");
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Cancelled");
        var cancelledMail = (await fixture.Mailpit.MessagesAsync(world.CandidateEmail, Ct)).Where(m => m.Subject.Contains("cancel", StringComparison.OrdinalIgnoreCase));
        cancelledMail.Should().BeEmpty("a rejection is not followed by a notice that the interview was cancelled");
    }

    [Fact]
    public async Task Rejecting_an_application_cancels_the_interview_it_was_still_waiting_on()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();

        (await world.Owner.PostAsync($"/v1/applications/{world.ApplicationId}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);

        await world.WaitForTimelineAsync("InterviewCancelled");
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Cancelled);
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Cancelled");
        (await world.ScheduleAsync()).Status.Should().NotBe(HttpStatusCode.Created, "an ended application is not offered another interview");
    }

    [Fact]
    public async Task A_move_that_does_not_end_the_application_leaves_its_interview_alone()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, _) = await world.ScheduledAsync();

        (await world.Owner.PostAsync($"/v1/applications/{world.ApplicationId}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Open);
    }

    [Fact]
    public async Task An_open_interview_whose_organizer_left_goes_on_with_the_next_interviewer_as_organizer()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync(interviewers: [world.Second.UserId, world.Owner.UserId]);

        (await world.Owner.DeleteAsync($"/v1/organization/members/{await world.Owner.MemberIdAsync(world.Second.UserId)}")).Expect(HttpStatusCode.NoContent);

        await WolverineStore.WaitUntilAsync(async () => (await world.AttendeesAsync(interviewId)).Count == 1, "the leaver to come off the interview", Ct);
        var attendees = await world.AttendeesAsync(interviewId);
        attendees.Single().UserId.Should().Be(world.Owner.UserId);
        attendees.Single().IsOrganizer.Should().BeTrue();
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("state").Should().Be("Open");
        page.At("slotsUnavailable").GetBoolean().Should().BeFalse("the candidate no longer waits on a calendar nobody can read");
        var chosen = FarSlot(InterviewWorld.SlotsOf(page));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        (await world.WaitForLiveEventsAsync(world.Owner.UserId, 1)).Single().MeetingUrl.Should().NotBeNullOrEmpty("the organizer's event makes the meeting link");
    }

    [Fact]
    public async Task A_booked_interview_goes_on_without_an_interviewer_who_left_and_can_still_be_moved()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);

        (await world.Owner.DeleteAsync($"/v1/organization/members/{await world.Owner.MemberIdAsync(world.Second.UserId)}")).Expect(HttpStatusCode.NoContent);

        await WolverineStore.WaitUntilAsync(async () => (await world.AttendeesAsync(interviewId)).Count == 1, "the leaver to come off the interview", Ct);
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Booked);
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        var moved = InterviewWorld.SlotsOf(page).First(s => s >= chosen.AddDays(1));
        (await world.RescheduleAsync(token, moved)).Expect(HttpStatusCode.NoContent);
        (await world.WaitForLiveEventsAsync(world.Owner.UserId, 1, e => e.StartsAt == moved)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_booked_interview_is_cancelled_and_the_candidate_told_when_its_organizer_left()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync(interviewers: [world.Second.UserId, world.Owner.UserId]);
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);

        (await world.Owner.DeleteAsync($"/v1/organization/members/{await world.Owner.MemberIdAsync(world.Second.UserId)}")).Expect(HttpStatusCode.NoContent);

        await world.WaitForTimelineAsync("InterviewCancelled");
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Cancelled);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 0);
        (await world.WaitForEmailAsync(world.CandidateEmail, "cancel")).Text.Should().NotBeNullOrEmpty();
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Cancelled");
    }

    private static DateTimeOffset FarSlot(IReadOnlyList<DateTimeOffset> slots) => slots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2));
}
