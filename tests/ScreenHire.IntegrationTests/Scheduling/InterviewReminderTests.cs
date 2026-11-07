using ScreenHire.Application.Scheduling;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// Interview reminders through the API, the worker, Wolverine's schedule and Mailpit (docs/architecture/interview-scheduling.md):
/// a booking schedules one for a day before (the candidate and every interviewer) and one for an hour before (the candidate).
/// A reminder waits a day or more, so the tests read when the schedule would release it and publish the message themselves, as
/// the time-elapsed test does.
/// </summary>
public sealed class InterviewReminderTests(AppHostFixture fixture)
{
    private const string CandidateSubject = "Reminder: your conversation";
    private const string InterviewerSubject = "Interview in a day";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_booking_schedules_a_reminder_a_day_and_an_hour_before_and_each_goes_to_its_people_once()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        await world.WaitForEmailAsync(world.CandidateEmail, "is set");

        // The booking's own transaction scheduled both, for the times they are about.
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var due = await store.ScheduledTimesAsync(interviewId.ToString(), Ct);
        due.Should().Equal(chosen.AddHours(-24), chosen.AddHours(-1));

        var ownerEmail = await world.Owner.EmailOfAsync();
        var secondEmail = await world.Second.EmailOfAsync();
        await world.DeliverReminderAsync(interviewId, chosen, InterviewReminderKind.DayBefore);

        var candidate = await world.WaitForEmailAsync(world.CandidateEmail, CandidateSubject);
        candidate.Text.Should().Contain("(UTC)", "the reminder says the time, in the interview's zone")
            .And.Contain(InterviewWorld.TokenIn(candidate.Text), "the reminder carries the link to change or cancel it");
        var forOwner = await world.WaitForEmailAsync(ownerEmail, InterviewerSubject);
        forOwner.Text.Should().Contain("Casey Morgan").And.Contain("Platform Engineer");
        await world.WaitForEmailAsync(secondEmail, InterviewerSubject);

        // The same reminder delivered again sends nothing, and the one for an hour before goes to the candidate alone.
        await world.DeliverReminderAsync(interviewId, chosen, InterviewReminderKind.DayBefore);
        await world.DeliverReminderAsync(interviewId, chosen, InterviewReminderKind.HourBefore);

        await world.WaitForEmailAsync(world.CandidateEmail, CandidateSubject, count: 2);
        (await Matching(world.CandidateEmail, CandidateSubject)).Should().HaveCount(2, "a day before and an hour before, each once");
        (await Matching(ownerEmail, InterviewerSubject)).Should().ContainSingle();
        (await Matching(secondEmail, InterviewerSubject)).Should().ContainSingle();
        (await Matching(ownerEmail, CandidateSubject)).Should().BeEmpty("the hour-before reminder is the candidate's alone");

        // The candidate's reminders are timeline entries, so the team can see what was sent.
        await world.WaitForTimelineAsync("EmailSent", count: 4);
        (await world.TimelineTypesAsync()).Count(t => t == "EmailSent").Should().Be(1 /* invitation */ + 1 /* confirmation */ + 2 /* reminders */);
    }

    [Fact]
    public async Task A_reminder_for_a_time_the_interview_no_longer_has_sends_nothing()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var first = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, first)).Expect(HttpStatusCode.NoContent);
        await world.WaitForSyncAsync(interviewId, "Synced");
        var next = InterviewWorld.NextDayAfter(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)), first);
        (await world.RescheduleAsync(token, next)).Expect(HttpStatusCode.NoContent);
        await world.WaitForEmailAsync(world.CandidateEmail, "is set", count: 2);
        await world.WaitForSyncAsync(interviewId, "Synced");

        // The move scheduled the reminders for the new time; the old ones still wait, and are harmless when they come.
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        (await store.ScheduledTimesAsync(interviewId.ToString(), Ct)).Should()
            .Equal(new[] { first.AddHours(-24), first.AddHours(-1), next.AddHours(-24), next.AddHours(-1) }.Order());

        var ownerEmail = await world.Owner.EmailOfAsync();
        await world.DeliverReminderAsync(interviewId, first, InterviewReminderKind.DayBefore);
        await world.DeliverReminderAsync(interviewId, first, InterviewReminderKind.HourBefore);
        (await Matching(world.CandidateEmail, CandidateSubject)).Should().BeEmpty("the interview moved away from that time");
        (await Matching(ownerEmail, InterviewerSubject)).Should().BeEmpty();

        await world.DeliverReminderAsync(interviewId, next, InterviewReminderKind.DayBefore);
        await world.WaitForEmailAsync(world.CandidateEmail, CandidateSubject);
        await world.WaitForEmailAsync(ownerEmail, InterviewerSubject);

        // A cancelled interview is not reminded of.
        (await world.CandidateCancelAsync(token)).Expect(HttpStatusCode.NoContent);
        await world.WaitForEmailAsync(world.CandidateEmail, "is cancelled");
        await world.DeliverReminderAsync(interviewId, next, InterviewReminderKind.HourBefore);
        (await Matching(world.CandidateEmail, CandidateSubject)).Should().ContainSingle("nothing was sent after the cancellation");
    }

    private async Task<IReadOnlyList<MailpitMessage>> Matching(string address, string subject) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.Contains(subject, StringComparison.Ordinal))];

    private static DateTimeOffset FarSlot(IReadOnlyList<DateTimeOffset> slots) => slots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2));
}
