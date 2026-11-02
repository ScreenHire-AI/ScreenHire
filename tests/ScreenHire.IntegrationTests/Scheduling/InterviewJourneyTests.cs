using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Scheduling;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// An interview from the recruiter's offer to the booked calendar events, through the API, the worker, the sandbox
/// calendar and Mailpit (docs/architecture/interview-scheduling.md): the invitation, the candidate's page, booking,
/// moving, cancelling, and the rules around them.
/// </summary>
public sealed class InterviewJourneyTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recruiter_offers_an_interview_the_candidate_books_and_both_interviewers_get_the_event_with_a_meeting_link()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ours = await EventsConnection.ConnectAsync(world.Owner.Session, Ct);
        await using var theirEvents = await EventsConnection.ConnectAsync(theirs.Session, Ct);

        var created = (await world.ScheduleAsync()).Expect(HttpStatusCode.Created);
        var interviewId = created.Id();

        created.Text("status").Should().Be("Open");
        created.Count("interviewers").Should().Be(2);
        created.At("interviewers.0.userId").GetGuid().Should().Be(world.Owner.UserId);
        created.At("interviewers.0.isOrganizer").GetBoolean().Should().BeTrue("the first interviewer is the organizer");
        created.At("interviewers.1.isOrganizer").GetBoolean().Should().BeFalse();
        created.At("interviewers.0.syncStatus").GetString().Should().Be("Pending");
        created.At("durationMinutes").GetInt32().Should().Be(30);
        created.At("startsAt").ValueKind.Should().Be(JsonValueKind.Null);
        var changed = await ours.WaitForAsync("InterviewChanged", "interviewId", interviewId, Ct);
        changed.GetProperty("status").GetString().Should().Be("Open");
        changed.GetProperty("applicationId").GetGuid().Should().Be(world.ApplicationId);

        // The invitation: a link whose token resolves, and a team copy that does not hold it.
        var invitation = await world.WaitForEmailAsync(world.CandidateEmail, "Pick a time");
        var token = InterviewWorld.TokenIn(invitation.Text);
        invitation.Text.Should().Contain(world.Slug).And.Contain("30 minutes");
        await world.WaitForTimelineAsync("InterviewInvited");
        await world.WaitForTimelineAsync("EmailSent");
        var timeline = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        timeline.Single(e => e.GetProperty("type").GetString() == "InterviewInvited").GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);
        var sent = timeline.Single(e => e.GetProperty("type").GetString() == "EmailSent");
        var copy = (await world.Owner.GetAsync($"/v1/emails/{sent.GetProperty("emailId").GetGuid()}")).Expect(HttpStatusCode.OK);
        copy.Text("text").Should().Contain("[the candidate's link, not kept in this copy]").And.NotContain(token);
        copy.Text("html").Should().NotContain(token);

        // The candidate's page: the offered times, nothing about the interviewers, and a busy block takes a time away.
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("state").Should().Be("Open");
        page.Text("jobTitle").Should().Be("Platform Engineer");
        page.At("durationMinutes").GetInt32().Should().Be(30);
        page.At("canChange").GetBoolean().Should().BeFalse();
        page.At("slotsUnavailable").GetBoolean().Should().BeFalse();
        var slots = InterviewWorld.SlotsOf(page);
        slots.Should().NotBeEmpty().And.BeInAscendingOrder();
        slots.Should().OnlyContain(s => s > DateTimeOffset.UtcNow.AddHours(23), "a time needs the minimum notice");
        page.Body.GetRawText().Should().NotContain(world.Owner.UserId.ToString()).And.NotContain(world.Second.UserId.ToString());
        var busy = slots[0];
        await fixture.AddBusyAsync(world.OrganizationId, world.Second.UserId, busy, busy.AddMinutes(30), Ct);
        InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)).Should().NotContain(busy, "an interviewer is busy then");

        // Forged, damaged and other organizations' links are not found.
        (await world.PublicGetAsync("not-a-token")).Status.Should().Be(HttpStatusCode.NotFound);
        (await world.PublicGetAsync(token + "x")).Status.Should().Be(HttpStatusCode.NotFound);
        (await world.PublicGetAsync(token, await SlugOfAsync(theirs))).Status.Should().Be(HttpStatusCode.NotFound);
        (await world.PublicGetAsync(token, "no-such-organization")).Status.Should().Be(HttpStatusCode.NotFound);

        // Booking: a time that is not offered is a conflict; an offered one is booked.
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        var notOffered = await world.BookAsync(token, chosen.AddMinutes(7));
        notOffered.Expect(HttpStatusCode.Conflict).Text("type").Should().Be("slot_taken");
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);

        var organizerEvents = await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);
        var secondEvents = await world.WaitForLiveEventsAsync(world.Second.UserId, 1);
        organizerEvents.Single().StartsAt.Should().Be(chosen);
        organizerEvents.Single().EndsAt.Should().Be(chosen.AddMinutes(30));
        organizerEvents.Single().Summary.Should().Be("Interview: Casey Morgan - Platform Engineer");
        organizerEvents.Single().MeetingUrl.Should().NotBeNullOrEmpty("the organizer's event makes the meeting link");
        secondEvents.Single().StartsAt.Should().Be(chosen);
        secondEvents.Single().Summary.Should().Be(organizerEvents.Single().Summary);
        secondEvents.Single().MeetingUrl.Should().BeNull();
        secondEvents.Single().Description.Should().Contain(organizerEvents.Single().MeetingUrl!, "the other copies carry the organizer's link in their text");
        (organizerEvents.Single().Description + secondEvents.Single().Description).Should().Contain("Platform Engineer").And.Contain("Casey Morgan")
            .And.NotContainEquivalentOf("verification");

        await world.WaitForSyncAsync(interviewId, "Synced");
        var booked = (await world.InterviewsAsync()).Single();
        booked.GetProperty("status").GetString().Should().Be("Booked");
        booked.GetProperty("startsAt").GetDateTimeOffset().Should().Be(chosen);
        booked.GetProperty("meetingUrl").GetString().Should().Be(organizerEvents.Single().MeetingUrl);

        var confirmation = await world.WaitForEmailAsync(world.CandidateEmail, "is set");
        confirmation.Text.Should().Contain(organizerEvents.Single().MeetingUrl!).And.Contain("(UTC)").And.Contain(InterviewWorld.TokenIn(confirmation.Text));
        await world.WaitForTimelineAsync("InterviewBooked");
        var entry = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "InterviewBooked");
        entry.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "the candidate booked");
        entry.GetProperty("startsAt").GetDateTimeOffset().Should().Be(chosen);
        var confirmed = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        confirmed.Text("state").Should().Be("Booked");
        confirmed.At("startsAt").GetDateTimeOffset().Should().Be(chosen);
        confirmed.At("canChange").GetBoolean().Should().BeTrue();

        // A second booking of the same interview is refused, and none of this reached another organization.
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("already_booked");
        theirEvents.ReceivedAnythingAbout(interviewId).Should().BeFalse("another organization never hears about our interviews");
        theirEvents.ReceivedAnythingAbout(world.ApplicationId).Should().BeFalse();
    }

    [Fact]
    public async Task Two_candidates_racing_for_one_interviewers_time_book_it_once()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (second, secondEmail) = await world.AddApplicationAsync();
        var (firstInterview, firstToken) = await world.ScheduledAsync();
        var (secondInterview, secondToken) = await world.ScheduledAsync(second, secondEmail);
        var shared = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(firstToken)).Expect(HttpStatusCode.OK)));
        InterviewWorld.SlotsOf((await world.PublicGetAsync(secondToken)).Expect(HttpStatusCode.OK)).Should().Contain(shared, "both candidates are offered it");

        var results = await Task.WhenAll(world.BookAsync(firstToken, shared), world.BookAsync(secondToken, shared));

        results.Count(r => r.Status == HttpStatusCode.NoContent).Should().Be(1, "one booking wins");
        var loser = results.Single(r => r.Status == HttpStatusCode.Conflict);
        loser.Text("type").Should().Be("slot_taken");
        var winner = results[0].Status == HttpStatusCode.NoContent ? firstInterview : secondInterview;
        (await world.StoredAsync(winner)).Status.Should().Be(InterviewStatus.Booked);
        (await world.StoredAsync(winner == firstInterview ? secondInterview : firstInterview)).Status.Should().Be(InterviewStatus.Open);
        (await world.WaitForLiveEventsAsync(world.Owner.UserId, 1)).Single().StartsAt.Should().Be(shared);
        (await world.EventsOfAsync(world.Owner.UserId)).Should().ContainSingle("the loser put nothing on the calendar");
    }

    [Fact]
    public async Task The_candidate_moves_a_booked_interview_and_the_same_events_move_with_no_duplicates()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var slots = InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK));
        var first = FarSlot(slots);
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        var before = await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);
        await world.WaitForSyncAsync(interviewId, "Synced");
        await world.WaitForEmailAsync(world.CandidateEmail, "is set");

        var offeredToMove = InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK));
        offeredToMove.Should().NotBeEmpty("a booked interview that can still change is offered other times");
        var next = InterviewWorld.NextDayAfter(offeredToMove, first);
        (await world.RescheduleAsync(token, next)).Expect(HttpStatusCode.NoContent);

        foreach (var user in new[] { world.Owner.UserId, world.Second.UserId })
        {
            var moved = await world.WaitForLiveEventsAsync(user, 1, e => e.StartsAt == next);
            moved.Single().EndsAt.Should().Be(next.AddMinutes(30));
            (await world.EventsOfAsync(user)).Should().ContainSingle("the event moved, it was not made again");
        }

        (await world.EventsOfAsync(world.Owner.UserId)).Single().ProviderEventId.Should().Be(before.Single().ProviderEventId);
        await world.WaitForEmailAsync(world.CandidateEmail, "is set", count: 2);
        await world.WaitForTimelineAsync("InterviewRescheduled");
        (await world.StoredAsync(interviewId)).StartsAt.Should().Be(next);
        (await world.InterviewsAsync()).Single().GetProperty("startsAt").GetDateTimeOffset().Should().Be(next);

        // Moving an interview that is not booked is a conflict.
        var (other, otherEmail) = await world.AddApplicationAsync();
        var (_, openToken) = await world.ScheduledAsync(other, otherEmail);
        (await world.RescheduleAsync(openToken, next)).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("not_booked");
    }

    // A create can roll back after the provider answered, which leaves an event on the calendar and no id for it in the row.
    [Fact]
    public async Task A_move_finds_an_event_whose_id_was_never_saved_and_moves_it()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var first = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        await world.ForgetEventIdsAsync(interviewId);

        var next = InterviewWorld.NextDayAfter(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)), first);
        (await world.RescheduleAsync(token, next)).Expect(HttpStatusCode.NoContent);

        await world.WaitForSyncAsync(interviewId, "Synced");
        foreach (var user in new[] { world.Owner.UserId, world.Second.UserId })
        {
            (await world.EventsOfAsync(user)).Should().ContainSingle("the event moved, it was not made again").Which.StartsAt.Should().Be(next);
        }
    }

    [Fact]
    public async Task The_candidate_cancels_and_the_events_go_the_candidate_is_told_and_the_link_closes()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);
        await world.WaitForLiveEventsAsync(world.Second.UserId, 1);
        await world.WaitForSyncAsync(interviewId, "Synced");

        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.NoContent);

        await world.WaitForLiveEventsAsync(world.Owner.UserId, 0);
        await world.WaitForLiveEventsAsync(world.Second.UserId, 0);
        (await world.EventsOfAsync(world.Owner.UserId)).Should().ContainSingle().Which.Deleted.Should().BeTrue();
        var cancelled = await world.WaitForEmailAsync(world.CandidateEmail, "is cancelled");
        cancelled.Text.Should().Contain("(UTC)", "the cancellation names the time that was cancelled");
        await world.WaitForTimelineAsync("InterviewCancelled");
        var listed = (await world.InterviewsAsync()).Single();
        listed.GetProperty("status").GetString().Should().Be("Cancelled");
        listed.GetProperty("cancelledByCandidate").GetBoolean().Should().BeTrue();
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Cancelled");
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.Gone).Text("type").Should().Be("cancelled");
        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.Gone);

        // The application can be offered a new interview now.
        (await world.ScheduleAsync()).Expect(HttpStatusCode.Created);
        (await world.InterviewsAsync()).Select(i => i.GetProperty("status").GetString()).Should().Equal("Open", "Cancelled");
    }

    [Fact]
    public async Task An_offer_closes_for_the_candidate_once_their_application_has_ended()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));

        (await world.Owner.PostAsync($"/v1/applications/{world.ApplicationId}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);

        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("state").Should().Be("Cancelled");
        InterviewWorld.SlotsOf(page).Should().BeEmpty();
        page.At("canChange").GetBoolean().Should().BeFalse();
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.Gone).Text("type").Should().Be("cancelled");
        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.Gone);
        (await world.EventsOfAsync(world.Owner.UserId)).Should().BeEmpty("nothing reached a calendar");
    }

    [Fact]
    public async Task Within_the_minimum_notice_the_candidate_can_neither_move_nor_cancel_a_booked_interview()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        var soon = DateTimeOffset.UtcNow.AddHours(2);
        await world.SetTimeAsync(interviewId, soon, soon.AddMinutes(30));

        (await world.RescheduleAsync(token, chosen.AddDays(1))).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("too_late");
        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("too_late");
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("state").Should().Be("Booked");
        page.At("canChange").GetBoolean().Should().BeFalse();
        InterviewWorld.SlotsOf(page).Should().BeEmpty("nothing else is offered when it cannot change");
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Booked);

        // The team can still cancel it.
        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Cancelled");
    }

    [Fact]
    public async Task The_team_cancels_an_offer_and_a_booked_interview()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var viewer = await fixture.InviteAndJoinAsync(world.Owner, "Viewer");
        var (open, openToken) = await world.ScheduledAsync();

        (await viewer.PostAsync($"/v1/interviews/{open}/cancel")).Expect(HttpStatusCode.Forbidden);
        var withdrawn = (await world.Owner.PostAsync($"/v1/interviews/{open}/cancel")).Expect(HttpStatusCode.OK);
        withdrawn.Text("status").Should().Be("Cancelled");
        (await world.PublicGetAsync(openToken)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Cancelled");
        (await world.Owner.PostAsync($"/v1/interviews/{open}/cancel")).Expect(HttpStatusCode.Conflict);

        var (booked, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);
        await world.WaitForSyncAsync(booked, "Synced");

        (await world.Second.PostAsync($"/v1/interviews/{booked}/cancel")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Cancelled");

        await world.WaitForLiveEventsAsync(world.Owner.UserId, 0);
        await world.WaitForLiveEventsAsync(world.Second.UserId, 0);
        await world.WaitForEmailAsync(world.CandidateEmail, "is cancelled");
        await world.WaitForSyncAsync(booked, "Synced");
        var entry = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "InterviewCancelled").ToList();
        entry.Should().HaveCount(2);
        entry.Select(e => e.GetProperty("actorUserId").GetGuid()).Should().BeEquivalentTo([world.Owner.UserId, world.Second.UserId]);
        (await world.InterviewsAsync()).Select(i => i.GetProperty("cancelledByCandidate").GetBoolean()).Should().AllBeEquivalentTo(false);
    }

    [Fact]
    public async Task One_interview_is_live_per_application_and_an_open_one_past_its_window_does_not_count()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (first, token) = await world.ScheduledAsync();

        var again = (await world.ScheduleAsync()).Expect(HttpStatusCode.Conflict);
        again.Text("detail").Should().Contain("already has an interview");

        await world.PassWindowAsync(first);
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Expired");
        (await world.BookAsync(token, DateTimeOffset.UtcNow.AddDays(3))).Expect(HttpStatusCode.Gone).Text("type").Should().Be("expired");
        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.Gone);
        (await world.Owner.PostAsync($"/v1/interviews/{first}/cancel")).Expect(HttpStatusCode.Conflict);

        (await world.ScheduleAsync()).Expect(HttpStatusCode.Created);
        var history = await world.InterviewsAsync();
        history.Select(i => i.GetProperty("status").GetString()).Should().Equal("Open", "Expired");
        (await world.StoredAsync(first)).Status.Should().Be(InterviewStatus.Expired, "the stale request was stored as expired before the new one");
    }

    [Fact]
    public async Task An_interviewer_without_a_calendar_is_a_422_that_names_them_and_nothing_is_created()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        await InterviewWorld.ConnectCalendarAsync(world.Owner);

        var refused = (await world.ScheduleAsync()).Expect(HttpStatusCode.UnprocessableEntity);

        refused.Text("detail").Should().Contain("no working calendar");
        refused.At("interviewerUserIds").EnumerateArray().Select(u => u.GetGuid()).Should().Equal(world.Second.UserId);
        (await world.InterviewsAsync()).Should().BeEmpty();
        (await world.ScheduleAsync(interviewers: [world.Owner.UserId])).Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Scheduling_checks_the_request_the_permission_the_job_scope_and_the_interviewers()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var viewer = await fixture.InviteAndJoinAsync(world.Owner, "Viewer");
        using var narrow = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter", jobAccess: "HiringTeamOnly");

        (await world.ScheduleAsync(by: viewer)).Expect(HttpStatusCode.Forbidden);
        (await world.ScheduleAsync(by: narrow)).Expect(HttpStatusCode.NotFound);
        (await world.InterviewsAsync(by: viewer)).Should().BeEmpty("a viewer may read the application's interviews");
        (await narrow.GetAsync($"/v1/applications/{world.ApplicationId}/interviews")).Expect(HttpStatusCode.NotFound);

        (await world.ScheduleAsync(change: b => b["durationMinutes"] = 20)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("durationMinutes");
        (await world.ScheduleAsync(change: b => b["timeZone"] = "Not/AZone")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("timeZone");
        (await world.ScheduleAsync(change: b => b["windowEnd"] = DateTimeOffset.UtcNow.AddDays(-1))).Expect(HttpStatusCode.BadRequest);
        (await world.ScheduleAsync(change: b => b["dayStart"] = "9am")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("dayStart");
        (await world.ScheduleAsync(change: b => { b["dayStart"] = "17:00"; b["dayEnd"] = "09:00"; })).Expect(HttpStatusCode.BadRequest);
        (await world.ScheduleAsync(interviewers: [])).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("interviewerUserIds");
        (await world.ScheduleAsync(interviewers: [world.Owner.UserId, world.Owner.UserId])).Expect(HttpStatusCode.BadRequest);
        (await world.ScheduleAsync(interviewers: [.. Enumerable.Range(0, 6).Select(_ => Guid.NewGuid())])).Expect(HttpStatusCode.BadRequest);
        (await world.ScheduleAsync(interviewers: [Guid.NewGuid()])).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("interviewerUserIds");
        (await world.ScheduleAsync(applicationId: Guid.NewGuid())).Expect(HttpStatusCode.NotFound);
        (await world.InterviewsAsync()).Should().BeEmpty("none of the refused requests left a row");

        // Working hours are set when the recruiter says so: nothing at 09:00 UTC when the day starts at 12:00.
        (await world.ScheduleAsync(change: b => { b["dayStart"] = "12:00"; b["dayEnd"] = "14:00"; })).Expect(HttpStatusCode.Created)
            .Text("dayStart").Should().Be("12:00");
    }

    [Fact]
    public async Task Removing_an_interviewer_takes_them_off_a_booked_interview_which_stays_with_the_others()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        var memberId = await world.Owner.MemberIdAsync(world.Second.UserId);

        (await world.Owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NoContent);

        await WolverineStore.WaitUntilAsync(
            async () => (await world.InterviewsAsync()).Single().GetProperty("interviewers").GetArrayLength() == 1,
            "the worker to take the leaver off the interview",
            Ct);
        var interviewers = (await world.InterviewsAsync()).Single().GetProperty("interviewers").EnumerateArray().ToList();
        interviewers.Single().GetProperty("userId").GetGuid().Should().Be(world.Owner.UserId);
        interviewers.Single().GetProperty("syncStatus").GetString().Should().Be("Synced");
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Booked, "the interview stays");
    }

    [Fact]
    public async Task Another_organization_sees_and_changes_nothing_of_ours()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var (interviewId, token) = await world.ScheduledAsync();

        (await theirs.GetAsync($"/v1/applications/{world.ApplicationId}/interviews")).Expect(HttpStatusCode.NotFound);
        (await theirs.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.NotFound);
        (await world.ScheduleAsync(by: theirs)).Expect(HttpStatusCode.NotFound);
        (await world.PublicGetAsync(token, await SlugOfAsync(theirs))).Status.Should().Be(HttpStatusCode.NotFound, "a token names its own organization");
        (await world.BookAsync(token, DateTimeOffset.UtcNow.AddDays(3))).Status.Should().NotBe(HttpStatusCode.NoContent);
        (await world.PublicPostAsync(token, "/cancel", slug: await SlugOfAsync(theirs))).Status.Should().Be(HttpStatusCode.NotFound);
        (await world.StoredAsync(interviewId)).Status.Should().Be(InterviewStatus.Open);
    }

    [Theory]
    [InlineData("interview-invitation")]
    [InlineData("interview-confirmation")]
    [InlineData("interview-cancelled")]
    public async Task The_interview_emails_cannot_be_sent_by_hand_as_ordinary_email(string templateId)
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);

        var refused = await world.Owner.PostAsync("/v1/emails", new JsonObject
        {
            ["applicationIds"] = new JsonArray(world.ApplicationId),
            ["templateId"] = templateId,
        });

        refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("templateId");
        (await fixture.Mailpit.MessagesAsync(world.CandidateEmail, Ct)).Where(m => m.Subject.Contains("interview", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
    }

    // A time at least two days away, so the minimum notice never decides a test.
    private static DateTimeOffset FarSlot(IReadOnlyList<DateTimeOffset> slots) => slots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2));

    private static async Task<string> SlugOfAsync(Tenant tenant) => (await tenant.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
}
