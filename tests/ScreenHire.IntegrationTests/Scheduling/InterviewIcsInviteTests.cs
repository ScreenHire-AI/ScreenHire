using System.Text.Json.Nodes;
using static ScreenHire.IntegrationTests.Scheduling.SchedulingOptionSupport;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// The calendar invitations a booking sends (docs/architecture/interview-scheduling.md, "Calendar invitations"), read from
/// Mailpit: the candidate's confirmation and cancellation carry an <c>invite.ics</c> with one UID, and an interviewer whose own
/// calendar did not get the event is emailed one too. The file's format is the unit tests' (<c>IcsWriterTests</c>).
/// </summary>
public sealed class InterviewIcsInviteTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_candidates_confirmation_carries_a_request_invitation_and_the_cancellation_withdraws_it_with_the_same_uid()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);

        var request = await fixture.WaitForInviteAsync(world.CandidateEmail, "REQUEST", "is set");

        var confirmation = (await fixture.Mailpit.MessagesAsync(world.CandidateEmail, Ct)).Single(m => m.Subject.Contains("is set", StringComparison.Ordinal));
        var file = confirmation.Files.Should().ContainSingle().Which;
        file.FileName.Should().Be("invite.ics");
        file.ContentType.Should().StartWith("text/calendar");
        var uid = $"interview-{interviewId:N}";
        request.Should().Contain("BEGIN:VCALENDAR").And.Contain("METHOD:REQUEST").And.Contain(uid)
            .And.Contain($"DTSTART:{chosen.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");

        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.OK);

        var cancel = await fixture.WaitForInviteAsync(world.CandidateEmail, "CANCEL");
        cancel.Should().Contain("METHOD:CANCEL").And.Contain(uid, "a cancellation removes the event the confirmation made");
        cancel.Should().Contain("STATUS:CANCELLED");
    }

    [Fact]
    public async Task An_interviewer_with_only_interview_hours_is_emailed_the_invitation_and_one_with_a_synced_calendar_is_not()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);
        await InterviewWorld.ConnectCalendarAsync(world.Owner);
        (await world.Second.PutAsync("/v1/account/interview-hours", new JsonObject { ["timeZone"] = "UTC", ["weekly"] = WeekdayHours("09:00", "17:00") }))
            .Expect(HttpStatusCode.OK);
        var secondEmail = await world.Second.EmailOfAsync();
        var ownerEmail = await world.Owner.EmailOfAsync();
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);

        var invite = await fixture.WaitForInviteAsync(secondEmail, "REQUEST", "booked");

        invite.Should().Contain($"interview-{interviewId:N}");
        await world.WaitForSyncAsync(interviewId, "Synced");
        var attendees = await world.AttendeesAsync(interviewId);
        attendees.Single(a => a.UserId == world.Second.UserId).Provider.ToString().Should().Be("ScreenHire");
        attendees.Single(a => a.UserId == world.Owner.UserId).Provider.ToString().Should().Be("Sandbox");
        // The organizer's own event is in their calendar; they are handled before the second interviewer, whose email is here.
        (await fixture.Mailpit.MessagesAsync(ownerEmail, Ct)).Should().NotContain(m => m.Files.Count > 0, "their calendar has the event");

        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.OK);
        var cancel = await fixture.WaitForInviteAsync(secondEmail, "CANCEL", "cancelled");
        cancel.Should().Contain($"interview-{interviewId:N}");
    }
}
