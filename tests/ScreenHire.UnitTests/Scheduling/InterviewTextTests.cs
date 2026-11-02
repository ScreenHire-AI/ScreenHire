using System.Globalization;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The words and ids interview scheduling writes: the time in an email, an event's title and body, the stable ids behind
/// idempotent sends, the staleness rule for calendar messages, and the three interview emails.
/// </summary>
public sealed class InterviewTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_time_reads_as_a_day_a_range_and_the_zone_it_is_written_in()
    {
        // 15:00 UTC on Tuesday 3 November 2026 is 15:00 in London (GMT again since 25 October) and 10:00 in New York (EST since 1 November).
        var start = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

        InterviewText.Time(start, start.AddMinutes(30), "Europe/London").Should().Be("Tuesday 3 November 2026, 15:00 to 15:30 (Europe/London)");
        InterviewText.Time(start, start.AddMinutes(30), "America/New_York").Should().Be("Tuesday 3 November 2026, 10:00 to 10:30 (America/New_York)");
    }

    [Fact]
    public void The_time_follows_daylight_saving_time_in_the_interviews_zone()
    {
        var summer = new DateTimeOffset(2026, 7, 14, 13, 0, 0, TimeSpan.Zero);

        InterviewText.Time(summer, summer.AddHours(1), "Europe/London").Should().Be("Tuesday 14 July 2026, 14:00 to 15:00 (Europe/London)", "BST is one hour ahead");
    }

    [Fact]
    public void The_day_names_are_english_whatever_the_hosts_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var start = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

            InterviewText.Time(start, start.AddMinutes(30), "UTC").Should().StartWith("Tuesday 3 November 2026");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_zone_the_host_does_not_know_reads_as_utc()
    {
        var start = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

        InterviewText.Time(start, start.AddMinutes(30), "Not/AZone").Should().Be("Tuesday 3 November 2026, 10:00 to 10:30 (UTC)");
    }

    [Theory]
    [InlineData(15, "15 minutes")]
    [InlineData(30, "30 minutes")]
    [InlineData(45, "45 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 hour 30 minutes")]
    [InlineData(120, "2 hours")]
    public void The_length_is_written_in_words(int minutes, string expected) => InterviewText.Length(minutes).Should().Be(expected);

    [Fact]
    public void An_events_title_names_the_candidate_and_the_job()
    {
        InterviewText.EventTitle("Grace Adeyemi", "Support engineer").Should().Be("Interview: Grace Adeyemi - Support engineer");
    }

    [Fact]
    public void A_name_or_title_cannot_put_markup_or_a_new_line_in_an_event()
    {
        const string name = "<a href=\"https://attacker.example\">Reschedule here</a>\nGrace";

        var title = InterviewText.EventTitle(name, "Support <b>engineer</b>");
        var body = InterviewText.EventDescription(name, "Support <b>engineer</b>", applicationLink: null, meetingUrl: null);

        (title + body).Should().NotContain("<").And.NotContain(">");
        body.Split('\n').Should().HaveCount(2, "a name holds no line of its own");
    }

    [Fact]
    public void An_events_body_holds_the_job_the_candidate_and_a_link_to_the_application_only()
    {
        var link = new Uri("https://app.example.com/applications/00000000-0000-0000-0000-000000000001");

        var organizer = InterviewText.EventDescription("Grace Adeyemi", "Support engineer", link, meetingUrl: null);
        var other = InterviewText.EventDescription("Grace Adeyemi", "Support engineer", link, "https://meet.example.org/abc");
        var noLink = InterviewText.EventDescription("Grace Adeyemi", "Support engineer", applicationLink: null, meetingUrl: null);

        organizer.Should().Be($"Job: Support engineer\nCandidate: Grace Adeyemi\nApplication: {link}");
        other.Should().Be($"Job: Support engineer\nCandidate: Grace Adeyemi\nApplication: {link}\nMeeting link: https://meet.example.org/abc");
        noLink.Should().Be("Job: Support engineer\nCandidate: Grace Adeyemi", "a host with no dashboard address leaves the link out");
        (organizer + other + noLink).Should().NotContainEquivalentOf("verification").And.NotContainEquivalentOf("score");
    }

    [Fact]
    public void An_event_id_is_stable_for_the_interview_and_the_member_and_different_for_another_of_either()
    {
        var (interview, user, other) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var id = InterviewText.EventIdempotencyId(interview, user);

        id.Should().Be(InterviewText.EventIdempotencyId(interview, user));
        id.Should().NotBe(InterviewText.EventIdempotencyId(interview, other)).And.NotBe(InterviewText.EventIdempotencyId(Guid.CreateVersion7(), user));
        id.Should().MatchRegex("^[0-9a-f]{32}$", "lowercase hex is a valid Google event id and Microsoft transaction id");
    }

    [Fact]
    public void An_email_send_id_is_stable_for_the_kind_the_interview_and_the_revision()
    {
        var interview = Guid.CreateVersion7();

        var id = InterviewText.SendId("confirmation", interview, 1);

        id.Should().Be(InterviewText.SendId("confirmation", interview, 1));
        id.Should().NotBe(InterviewText.SendId("confirmation", interview, 2), "a move is a new send")
            .And.NotBe(InterviewText.SendId("cancelled", interview, 1))
            .And.NotBe(InterviewText.SendId("confirmation", Guid.CreateVersion7(), 1));
    }

    [Fact]
    public void A_calendar_message_is_current_only_for_the_revision_and_status_it_was_queued_for()
    {
        var request = BookedRequest();
        var revision = request.Revision;

        InterviewText.IsCurrent(request, revision, InterviewStatus.Booked).Should().BeTrue();
        InterviewText.IsCurrent(request, revision - 1, InterviewStatus.Booked).Should().BeFalse("it was queued before the interview changed again");
        InterviewText.IsCurrent(request, revision, InterviewStatus.Cancelled).Should().BeFalse("it is about a cancellation that did not happen");

        request.Reschedule(request.StartsAt!.Value.AddDays(1), Now);
        InterviewText.IsCurrent(request, revision, InterviewStatus.Booked).Should().BeFalse("the move supersedes the booking's message");
        InterviewText.IsCurrent(request, request.Revision, InterviewStatus.Booked).Should().BeTrue();

        request.Cancel(Now, byCandidate: true);
        InterviewText.IsCurrent(request, request.Revision, InterviewStatus.Cancelled).Should().BeTrue();
        InterviewText.IsCurrent(request, request.Revision - 1, InterviewStatus.Booked).Should().BeFalse("the cancellation supersedes the move");
    }

    [Fact]
    public void Every_interview_email_passes_its_own_checks_and_is_invitation_only()
    {
        var templates = new EmailTemplates();
        foreach (var id in new[] { EmailTemplateIds.InterviewInvitation, EmailTemplateIds.InterviewConfirmation, EmailTemplateIds.InterviewCancelled })
        {
            templates.Validate(id, templates.Default(id)).Should().BeEmpty(id);
            EmailTemplateIds.IsInvitationOnly(id).Should().BeTrue(id);
            EmailTemplateIds.IsActionOnly(id).Should().BeTrue(id, "a person never sends it as an ordinary email");
            EmailTemplateIds.IsWellFormed(id).Should().BeTrue(id);
        }
    }

    [Fact]
    public void The_invitation_must_give_the_booking_link_and_the_confirmation_the_time_and_the_change_link()
    {
        var templates = new EmailTemplates();

        templates.Validate(EmailTemplateIds.InterviewInvitation, new("Hi", "<p>Pick a time.</p>")).Should().ContainSingle(p => p.Message.Contains("bookingLink"));
        templates.Validate(EmailTemplateIds.InterviewConfirmation, new("Hi", "<p>See you.</p>")).Select(p => p.Message)
            .Should().Contain(m => m.Contains("interviewTime")).And.Contain(m => m.Contains("manageLink"));
        templates.Validate(EmailTemplateIds.InterviewCancelled, new("Hi", "<p>Cancelled.</p>")).Should().BeEmpty("it has no required variable");
    }

    [Fact]
    public void An_interview_variable_is_unknown_to_every_other_template()
    {
        var templates = new EmailTemplates();

        templates.Validate(EmailTemplateIds.StageChange, new("Hi", "<p>{{ bookingLink }}</p>")).Should().ContainSingle(p => p.Message.Contains("no variable \"bookingLink\""));
        templates.Validate(null, new("Hi", "<p>{{ meetingLink }}</p>")).Should().ContainSingle(p => p.Message.Contains("no variable \"meetingLink\""));
        templates.Validate(EmailTemplateIds.InterviewCancelled, new("Hi", "<p>{{ bookingLink }}</p>")).Should().NotBeEmpty("the cancellation holds no link");
    }

    [Fact]
    public void The_confirmation_gives_the_time_the_meeting_link_when_there_is_one_and_the_change_link()
    {
        var templates = new EmailTemplates();
        var model = new EmailTemplateModel(
            "Grace Adeyemi", "Support engineer", "Brightwater", FirstName: "Grace", LastName: "Adeyemi",
            ManageLink: "https://brightwater.careers.example.com/interviews/token", MeetingLink: "https://meet.example.org/abc",
            InterviewTime: "Tuesday 3 November 2026, 10:00 to 10:30 (Europe/London)", InterviewLength: "30 minutes");

        var with = templates.Render(EmailTemplateIds.InterviewConfirmation, "grace@example.com", model);
        var without = templates.Render(EmailTemplateIds.InterviewConfirmation, "grace@example.com", model with { MeetingLink = null });

        with.TextBody.Should().Contain("Tuesday 3 November 2026, 10:00 to 10:30 (Europe/London)")
            .And.Contain("https://meet.example.org/abc").And.Contain("https://brightwater.careers.example.com/interviews/token");
        with.HtmlBody.Should().Contain("<a href=\"https://brightwater.careers.example.com/interviews/token\"").And.Contain("<a href=\"https://meet.example.org/abc\"");
        without.TextBody.Should().NotContain("Join here").And.Contain("https://brightwater.careers.example.com/interviews/token");
    }

    [Fact]
    public void The_invitation_carries_the_booking_link_and_the_length_and_the_cancellation_the_time_when_it_had_one()
    {
        var templates = new EmailTemplates();
        var model = new EmailTemplateModel(
            "Grace Adeyemi", "Support engineer", "Brightwater", FirstName: "Grace", BookingLink: "https://brightwater.careers.example.com/interviews/token",
            InterviewLength: "45 minutes", InterviewTime: "Tuesday 3 November 2026, 10:00 to 10:45 (UTC)");

        var invitation = templates.Render(EmailTemplateIds.InterviewInvitation, "grace@example.com", model);
        var cancelled = templates.Render(EmailTemplateIds.InterviewCancelled, "grace@example.com", model);
        var offerWithdrawn = templates.Render(EmailTemplateIds.InterviewCancelled, "grace@example.com", model with { InterviewTime = null });

        invitation.TextBody.Should().Contain("45 minutes").And.Contain("https://brightwater.careers.example.com/interviews/token");
        cancelled.TextBody.Should().Contain("Tuesday 3 November 2026, 10:00 to 10:45 (UTC)");
        offerWithdrawn.TextBody.Should().NotContain("Tuesday").And.Contain("cancelled");
    }

    private static InterviewRequest BookedRequest()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Support engineer", Slug = "support-engineer" };
        var candidate = Candidate.Create(organization, "Grace", "Adeyemi", "grace@example.com", null, Now);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, []));
        var request = InterviewRequest.Create(application, 30, Now.AddDays(1), Now.AddDays(8), "UTC", null, Now);
        request.Book(Now.AddDays(2), Now);
        return request;
    }
}
