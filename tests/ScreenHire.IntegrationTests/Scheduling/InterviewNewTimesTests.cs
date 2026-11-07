using System.Text.Json.Nodes;
using ScreenHire.Application.Scheduling;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// A recruiter offers new times for a booked interview (docs/architecture/interview-scheduling.md): the booked time stays held on
/// every calendar until the candidate picks one of the new times, and the events in every calendar then move.
/// </summary>
public sealed class InterviewNewTimesTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_times_hold_the_booked_slot_until_the_candidate_picks_and_then_every_calendar_moves()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var first = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        var before = await world.WaitForLiveEventsAsync(world.Owner.UserId, 1);
        await world.WaitForSyncAsync(interviewId, "Synced");
        await world.WaitForEmailAsync(world.CandidateEmail, "is set");

        var (windowStart, windowEnd) = Window(first.AddDays(10), days: 7);
        var offered = (await OfferAsync(world, interviewId, windowStart, windowEnd)).Expect(HttpStatusCode.OK);

        // Still booked at the old time, with the offer pending and the new window.
        offered.Text("status").Should().Be("Booked");
        offered.At("startsAt").GetDateTimeOffset().Should().Be(first);
        offered.At("newTimesOfferedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        offered.At("windowStart").GetDateTimeOffset().Should().Be(windowStart);
        offered.At("windowEnd").GetDateTimeOffset().Should().Be(windowEnd);

        // The candidate is emailed a link to pick, naming the time that stays until they do; the team's copy hides the link.
        var email = await world.WaitForEmailAsync(world.CandidateEmail, "New times to talk");
        email.Text.Should().Contain("stays in place until you pick a new one").And.Contain("(UTC)");
        var newToken = InterviewWorld.TokenIn(email.Text);
        await world.WaitForTimelineAsync("InterviewNewTimesOffered");
        var timeline = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        var entry = timeline.Single(e => e.GetProperty("type").GetString() == "InterviewNewTimesOffered");
        entry.GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);
        entry.GetProperty("startsAt").GetDateTimeOffset().Should().Be(first, "the entry names the time still held");
        var sent = timeline.Last(e => e.GetProperty("type").GetString() == "EmailSent");
        var copy = (await world.Owner.GetAsync($"/v1/emails/{sent.GetProperty("emailId").GetGuid()}")).Expect(HttpStatusCode.OK);
        copy.Text("text").Should().Contain("[the candidate's link, not kept in this copy]").And.NotContain(newToken);

        // Nothing moved yet: every calendar still has the event at the old time, and the slot stays held for other candidates.
        foreach (var user in new[] { world.Owner.UserId, world.Second.UserId })
        {
            (await world.EventsOfAsync(user)).Where(e => !e.Deleted).Should().ContainSingle().Which.StartsAt.Should().Be(first);
        }

        var (otherApplication, otherEmail) = await world.AddApplicationAsync();
        var (_, otherToken) = await world.ScheduledAsync(otherApplication, otherEmail);
        InterviewWorld.SlotsOf((await world.PublicGetAsync(otherToken)).Expect(HttpStatusCode.OK)).Should().NotContain(first, "the old time is held until the candidate picks");

        // The candidate's page offers the new window, says so, and keeps the booked time.
        var page = (await world.PublicGetAsync(newToken)).Expect(HttpStatusCode.OK);
        page.Text("state").Should().Be("Booked");
        page.At("newTimesOffered").GetBoolean().Should().BeTrue();
        page.At("startsAt").GetDateTimeOffset().Should().Be(first);
        page.At("canChange").GetBoolean().Should().BeTrue();
        var slots = InterviewWorld.SlotsOf(page);
        slots.Should().NotBeEmpty().And.OnlyContain(s => s >= windowStart && s < windowEnd, "the times come from the new window");

        // The offer does not touch the booking, so its reminders still go.
        await world.DeliverReminderAsync(interviewId, first, InterviewReminderKind.DayBefore);
        await world.WaitForEmailAsync(world.CandidateEmail, "Reminder: your conversation");

        // The candidate picks one of the new times: the same events move, and the offer is over.
        var picked = slots.First(s => s >= windowStart.AddDays(2));
        (await world.RescheduleAsync(newToken, picked)).Expect(HttpStatusCode.NoContent);
        foreach (var user in new[] { world.Owner.UserId, world.Second.UserId })
        {
            var moved = await world.WaitForLiveEventsAsync(user, 1, e => e.StartsAt == picked);
            moved.Single().EndsAt.Should().Be(picked.AddMinutes(30));
            (await world.EventsOfAsync(user)).Should().ContainSingle("the event moved, it was not made again");
        }

        (await world.EventsOfAsync(world.Owner.UserId)).Single().ProviderEventId.Should().Be(before.Single().ProviderEventId);
        await world.WaitForEmailAsync(world.CandidateEmail, "is set", count: 2);
        await world.WaitForTimelineAsync("InterviewRescheduled");
        var stored = await world.StoredAsync(interviewId);
        stored.StartsAt.Should().Be(picked);
        stored.NewTimesOfferedAt.Should().BeNull();
        var after = (await world.PublicGetAsync(newToken)).Expect(HttpStatusCode.OK);
        after.At("newTimesOffered").GetBoolean().Should().BeFalse();
        after.At("startsAt").GetDateTimeOffset().Should().Be(picked);

        // The new time has its own reminders, and the old time's no longer send.
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        (await store.ScheduledTimesAsync(interviewId.ToString(), Ct)).Should().Contain([picked.AddHours(-24), picked.AddHours(-1)]);
        await world.DeliverReminderAsync(interviewId, first, InterviewReminderKind.HourBefore);
        (await fixture.Mailpit.MessagesAsync(world.CandidateEmail, Ct)).Count(m => m.Subject.Contains("Reminder: your conversation", StringComparison.Ordinal))
            .Should().Be(1, "the interview is no longer at the time the old reminder was about");
    }

    [Fact]
    public async Task While_new_times_are_offered_the_candidate_can_move_or_cancel_inside_the_minimum_notice()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var first = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        var soon = DateTimeOffset.UtcNow.AddHours(2);
        await world.SetTimeAsync(interviewId, soon, soon.AddMinutes(30));
        (await world.RescheduleAsync(token, first.AddDays(1))).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("too_late");

        var (windowStart, windowEnd) = Window(first.AddDays(3), days: 7);
        (await OfferAsync(world, interviewId, windowStart, windowEnd)).Expect(HttpStatusCode.OK);

        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.At("canChange").GetBoolean().Should().BeTrue("the team asked for the move");
        var picked = InterviewWorld.SlotsOf(page)[0];
        (await world.RescheduleAsync(token, picked)).Expect(HttpStatusCode.NoContent);
        (await world.StoredAsync(interviewId)).StartsAt.Should().Be(picked);

        // The offer ended with the move: inside the notice again, nothing can be changed.
        await world.SetTimeAsync(interviewId, soon, soon.AddMinutes(30));
        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("too_late");
    }

    [Fact]
    public async Task Offering_again_replaces_the_window_and_emails_the_candidate_again()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var first = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");

        var (firstStart, firstEnd) = Window(first.AddDays(5), days: 7);
        (await OfferAsync(world, interviewId, firstStart, firstEnd)).Expect(HttpStatusCode.OK);
        await world.WaitForEmailAsync(world.CandidateEmail, "New times to talk");
        var (secondStart, secondEnd) = Window(first.AddDays(20), days: 5);
        var again = (await OfferAsync(world, interviewId, secondStart, secondEnd)).Expect(HttpStatusCode.OK);

        again.At("windowStart").GetDateTimeOffset().Should().Be(secondStart);
        await world.WaitForEmailAsync(world.CandidateEmail, "New times to talk", count: 2);
        InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK))
            .Should().NotBeEmpty().And.OnlyContain(s => s >= secondStart && s < secondEnd);
        (await world.StoredAsync(interviewId)).Revision.Should().Be(1, "an offer is not a change to the booking");
    }

    [Fact]
    public async Task New_times_can_be_offered_only_for_a_booked_interview_with_a_sound_window_by_the_organization_that_owns_it()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var (interviewId, token) = await world.ScheduledAsync();
        var (start, end) = Window(DateTimeOffset.UtcNow.AddDays(5), days: 7);

        // Open: the candidate has not picked, so there is nothing to move.
        (await OfferAsync(world, interviewId, start, end)).Expect(HttpStatusCode.Conflict);

        var first = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");

        (await OfferAsync(world, interviewId, end, start)).Expect(HttpStatusCode.BadRequest).Body.GetProperty("errors").TryGetProperty("windowEnd", out _).Should().BeTrue();
        var past = DateTimeOffset.UtcNow.AddDays(-3);
        (await OfferAsync(world, interviewId, past.AddDays(-2), past)).Expect(HttpStatusCode.BadRequest);
        (await OfferAsync(world, interviewId, start, start.AddDays(120))).Expect(HttpStatusCode.BadRequest);
        (await world.StoredAsync(interviewId)).NewTimesOfferedAt.Should().BeNull("a refused offer changes nothing");

        (await OfferAsync(theirs, interviewId, start, end)).Expect(HttpStatusCode.NotFound);
        (await OfferAsync(world.Owner, Guid.CreateVersion7(), start, end)).Expect(HttpStatusCode.NotFound);
        (await world.StoredAsync(interviewId)).NewTimesOfferedAt.Should().BeNull();

        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.NoContent);
        (await OfferAsync(world, interviewId, start, end)).Expect(HttpStatusCode.Conflict);
    }

    private static Task<ApiResult> OfferAsync(InterviewWorld world, Guid interviewId, DateTimeOffset start, DateTimeOffset end) => OfferAsync(world.Owner, interviewId, start, end);

    private static Task<ApiResult> OfferAsync(Tenant by, Guid interviewId, DateTimeOffset start, DateTimeOffset end) =>
        by.PostAsync($"/v1/interviews/{interviewId}/new-times", new JsonObject { ["windowStart"] = start, ["windowEnd"] = end });

    // Whole days from the day of `from`, in UTC (the interview's zone in these tests).
    private static (DateTimeOffset Start, DateTimeOffset End) Window(DateTimeOffset from, int days)
    {
        var start = new DateTimeOffset(from.UtcDateTime.Date, TimeSpan.Zero);
        return (start, start.AddDays(days));
    }

    private static DateTimeOffset FarSlot(IReadOnlyList<DateTimeOffset> slots) => slots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2));
}
