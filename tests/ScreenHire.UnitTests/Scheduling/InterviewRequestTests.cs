using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>The interview request's state machine and validation (docs/architecture/interview-scheduling.md).</summary>
public sealed class InterviewRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowStart = Now.AddDays(1);
    private static readonly DateTimeOffset WindowEnd = Now.AddDays(8);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    [Fact]
    public void A_new_request_is_open_with_office_hours_and_no_time_yet()
    {
        var (application, request) = Requested();

        request.Status.Should().Be(InterviewStatus.Open);
        request.OrganizationId.Should().Be(application.OrganizationId);
        request.JobId.Should().Be(application.JobId);
        request.ApplicationId.Should().Be(application.Id);
        request.DayStart.Should().Be(new TimeOnly(9, 0));
        request.DayEnd.Should().Be(new TimeOnly(17, 0));
        request.StartsAt.Should().BeNull();
        request.Revision.Should().Be(0);
        request.CreatedByUserId.Should().Be(Recruiter);
        request.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    public void The_allowed_lengths_are_accepted(int minutes) =>
        InterviewRequest.Problem(minutes, WindowStart, WindowEnd, "UTC", new TimeOnly(9, 0), new TimeOnly(17, 0), Now).Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(61)]
    [InlineData(180)]
    public void Another_length_is_refused(int minutes) =>
        InterviewRequest.Problem(minutes, WindowStart, WindowEnd, "UTC", new TimeOnly(9, 0), new TimeOnly(17, 0), Now).Should().Contain("minutes");

    [Fact]
    public void The_window_must_end_after_it_starts_in_the_future_and_within_ninety_days()
    {
        var (nine, five) = (new TimeOnly(9, 0), new TimeOnly(17, 0));

        InterviewRequest.Problem(30, WindowEnd, WindowStart, "UTC", nine, five, Now).Should().Contain("end after");
        InterviewRequest.Problem(30, WindowStart, WindowStart, "UTC", nine, five, Now).Should().Contain("end after");
        InterviewRequest.Problem(30, WindowStart, WindowStart.AddDays(90), "UTC", nine, five, Now).Should().BeNull("exactly 90 days is allowed");
        InterviewRequest.Problem(30, WindowStart, WindowStart.AddDays(90).AddMinutes(1), "UTC", nine, five, Now).Should().Contain("90 days");
        InterviewRequest.Problem(30, Now.AddDays(-3), Now.AddDays(-1), "UTC", nine, five, Now).Should().Contain("future");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    [InlineData("Eastern Time")]
    public void An_unknown_time_zone_is_refused(string? zone) =>
        InterviewRequest.Problem(30, WindowStart, WindowEnd, zone, new TimeOnly(9, 0), new TimeOnly(17, 0), Now).Should().Contain("time zone");

    [Fact]
    public void Working_hours_must_end_after_they_start_and_hold_the_interview()
    {
        InterviewRequest.Problem(30, WindowStart, WindowEnd, "UTC", new TimeOnly(17, 0), new TimeOnly(9, 0), Now).Should().Contain("Working hours");
        InterviewRequest.Problem(60, WindowStart, WindowEnd, "UTC", new TimeOnly(9, 0), new TimeOnly(9, 30), Now).Should().Contain("shorter");
    }

    [Fact]
    public void Create_throws_the_problem_it_found()
    {
        var (application, _) = Requested();

        FluentActions.Invoking(() => InterviewRequest.Create(application, 20, WindowStart, WindowEnd, "UTC", Recruiter, Now))
            .Should().Throw<ArgumentException>().WithMessage("*minutes*");
    }

    [Fact]
    public void Booking_sets_the_time_and_counts_the_revision()
    {
        var (_, request) = Requested();

        request.Book(WindowStart.AddHours(10), Now);

        request.Status.Should().Be(InterviewStatus.Booked);
        request.StartsAt.Should().Be(WindowStart.AddHours(10));
        request.EndsAt.Should().Be(WindowStart.AddHours(10).AddMinutes(45));
        request.Revision.Should().Be(1);
    }

    [Fact]
    public void A_time_outside_the_window_cannot_be_booked_and_the_last_one_that_fits_can()
    {
        var (_, request) = Requested();

        FluentActions.Invoking(() => request.Book(WindowStart.AddMinutes(-1), Now)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => request.Book(WindowEnd.AddMinutes(-30), Now)).Should().Throw<ArgumentOutOfRangeException>("it would end after the window");
        request.Status.Should().Be(InterviewStatus.Open, "a refused booking changes nothing");
        request.Revision.Should().Be(0);

        request.Book(WindowEnd.AddMinutes(-45), Now);

        request.Status.Should().Be(InterviewStatus.Booked);
    }

    [Fact]
    public void Only_an_open_request_books()
    {
        var (_, booked) = Requested();
        booked.Book(WindowStart.AddHours(10), Now);
        FluentActions.Invoking(() => booked.Book(WindowStart.AddHours(12), Now)).Should().Throw<InvalidOperationException>();

        var (_, cancelled) = Requested();
        cancelled.Cancel(Now, byCandidate: false);
        FluentActions.Invoking(() => cancelled.Book(WindowStart.AddHours(10), Now)).Should().Throw<InvalidOperationException>();

        var (_, stale) = Requested();
        FluentActions.Invoking(() => stale.Book(WindowStart.AddHours(10), WindowEnd)).Should().Throw<InvalidOperationException>("the window has passed");
    }

    [Fact]
    public void Rescheduling_moves_a_booked_interview_and_counts_the_revision()
    {
        var (_, request) = Requested();
        request.Book(WindowStart.AddHours(10), Now);

        request.Reschedule(WindowStart.AddDays(1).AddHours(14), Now);

        request.Status.Should().Be(InterviewStatus.Booked);
        request.StartsAt.Should().Be(WindowStart.AddDays(1).AddHours(14));
        request.Revision.Should().Be(2);
    }

    [Fact]
    public void Only_a_booked_interview_is_rescheduled_to_a_time_in_the_window_that_is_not_past()
    {
        var (_, open) = Requested();
        FluentActions.Invoking(() => open.Reschedule(WindowStart.AddHours(10), Now)).Should().Throw<InvalidOperationException>();

        var (_, booked) = Requested();
        booked.Book(WindowStart.AddHours(10), Now);
        FluentActions.Invoking(() => booked.Reschedule(WindowEnd, Now)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => booked.Reschedule(WindowStart.AddHours(12), WindowStart.AddHours(13))).Should().Throw<ArgumentOutOfRangeException>("not into the past");
    }

    [Fact]
    public void Cancelling_records_who_and_when_keeps_the_time_as_history_and_counts_the_revision()
    {
        var (_, request) = Requested();
        request.Book(WindowStart.AddHours(10), Now);

        request.Cancel(Now.AddHours(1), byCandidate: true);

        request.Status.Should().Be(InterviewStatus.Cancelled);
        request.CancelledAt.Should().Be(Now.AddHours(1));
        request.CancelledByCandidate.Should().BeTrue();
        request.StartsAt.Should().Be(WindowStart.AddHours(10));
        request.Revision.Should().Be(2);
        FluentActions.Invoking(() => request.Cancel(Now, false)).Should().Throw<InvalidOperationException>("already cancelled");
    }

    [Fact]
    public void An_open_request_can_be_cancelled_before_anyone_books()
    {
        var (_, request) = Requested();

        request.Cancel(Now, byCandidate: false);

        request.Status.Should().Be(InterviewStatus.Cancelled);
        request.CancelledByCandidate.Should().BeFalse();
    }

    [Fact]
    public void An_open_request_past_its_window_reads_as_expired_and_stores_it_on_request()
    {
        var (_, request) = Requested();

        request.EffectiveStatus(WindowEnd.AddTicks(-1)).Should().Be(InterviewStatus.Open);
        request.EffectiveStatus(WindowEnd).Should().Be(InterviewStatus.Expired);
        request.Status.Should().Be(InterviewStatus.Open, "reading changes nothing");

        request.MarkExpired(WindowEnd.AddTicks(-1)).Should().BeFalse();
        request.MarkExpired(WindowEnd).Should().BeTrue();
        request.Status.Should().Be(InterviewStatus.Expired);
        request.MarkExpired(WindowEnd.AddDays(1)).Should().BeFalse("it is stored already");
        FluentActions.Invoking(() => request.Cancel(WindowEnd, false)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_booked_interview_never_expires()
    {
        var (_, request) = Requested();
        request.Book(WindowStart.AddHours(10), Now);

        request.EffectiveStatus(WindowEnd.AddDays(30)).Should().Be(InterviewStatus.Booked);
        request.MarkExpired(WindowEnd.AddDays(30)).Should().BeFalse();
    }

    [Fact]
    public void The_candidate_may_change_a_booked_interview_until_the_minimum_notice_before_it_starts()
    {
        var (_, request) = Requested();
        var notice = TimeSpan.FromHours(24);
        CandidateMayChange(request, Now).Should().BeFalse("nothing is booked");
        request.Book(WindowStart.AddHours(10), Now);
        var starts = WindowStart.AddHours(10);

        request.CanBeChangedByCandidate(starts - notice, notice).Should().BeTrue("exactly the notice before is still allowed");
        request.CanBeChangedByCandidate(starts - notice + TimeSpan.FromMinutes(1), notice).Should().BeFalse();
        request.CanBeChangedByCandidate(starts.AddHours(1), notice).Should().BeFalse();
        request.Cancel(Now, true);
        request.CanBeChangedByCandidate(Now, notice).Should().BeFalse();
    }

    [Fact]
    public void An_attendee_tracks_whether_its_calendar_event_is_written()
    {
        var (_, request) = Requested();
        var attendee = InterviewAttendee.Create(request, Recruiter, isOrganizer: true, CalendarProvider.Sandbox);

        attendee.SyncStatus.Should().Be(InterviewSyncStatus.Pending);
        attendee.OrganizationId.Should().Be(request.OrganizationId);
        attendee.ApplicationId.Should().Be(request.ApplicationId);
        attendee.InterviewId.Should().Be(request.Id);

        attendee.MarkSynced(CalendarProvider.Sandbox, "event-1");
        attendee.SyncStatus.Should().Be(InterviewSyncStatus.Synced);
        attendee.ProviderEventId.Should().Be("event-1");

        attendee.MarkFailed("Calendar refused the event.");
        attendee.SyncStatus.Should().Be(InterviewSyncStatus.Failed);
        attendee.SyncProblem.Should().Be("Calendar refused the event.");

        attendee.MarkPending();
        attendee.SyncStatus.Should().Be(InterviewSyncStatus.Pending);
        attendee.SyncProblem.Should().BeNull();
        attendee.ProviderEventId.Should().Be("event-1", "the event id stays, so a later change moves the same event");
    }

    [Fact]
    public void The_timeline_entries_name_the_interview_and_its_time()
    {
        var (application, request) = Requested();
        var invited = application.InterviewInvited(request, Now, Recruiter);
        request.Book(WindowStart.AddHours(10), Now);
        var booked = application.InterviewBooked(request, Now);
        request.Reschedule(WindowStart.AddHours(12), Now);
        var rescheduled = application.InterviewRescheduled(request, Now);
        request.Cancel(Now, true);
        var cancelled = application.InterviewCancelled(request, Now);

        invited.Type.Should().Be(ApplicationEventType.InterviewInvited);
        invited.Data.InterviewId.Should().Be(request.Id);
        invited.Data.StartsAt.Should().BeNull();
        invited.ActorUserId.Should().Be(Recruiter);
        booked.Type.Should().Be(ApplicationEventType.InterviewBooked);
        booked.Data.StartsAt.Should().Be(WindowStart.AddHours(10));
        booked.Data.EndsAt.Should().Be(WindowStart.AddHours(10).AddMinutes(45));
        booked.ActorUserId.Should().BeNull("the candidate booked");
        rescheduled.Type.Should().Be(ApplicationEventType.InterviewRescheduled);
        rescheduled.Data.StartsAt.Should().Be(WindowStart.AddHours(12));
        cancelled.Type.Should().Be(ApplicationEventType.InterviewCancelled);
        cancelled.Data.InterviewId.Should().Be(request.Id);
        new[] { invited, booked, rescheduled, cancelled }.Should().OnlyContain(e => e.ApplicationId == application.Id && e.OrganizationId == application.OrganizationId);
    }

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "https://meet.google.com/abc-defg-hij")]
    [InlineData("  https://teams.microsoft.com/l/meetup-join/abc  ", "https://teams.microsoft.com/l/meetup-join/abc")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("http://meet.example.org/abc", null)]
    [InlineData("data:text/html,hello", null)]
    [InlineData("/relative/path", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_an_absolute_https_meeting_link_is_kept(string? given, string? kept)
    {
        var (_, request) = Requested();

        request.SetMeetingUrl(given);

        request.MeetingUrl.Should().Be(kept);
    }

    [Fact]
    public void An_overlong_meeting_link_is_dropped()
    {
        var (_, request) = Requested();

        request.SetMeetingUrl("https://meet.example.org/" + new string('a', InterviewRequest.MaxMeetingUrlLength));

        request.MeetingUrl.Should().BeNull();
    }

    private static bool CandidateMayChange(InterviewRequest request, DateTimeOffset now) => request.CanBeChangedByCandidate(now, TimeSpan.FromHours(24));

    private static (JobApplication Application, InterviewRequest Request) Requested()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Support engineer", Slug = "support-engineer" };
        var candidate = Candidate.Create(organization, "Grace", "Adeyemi", "grace@example.com", null, Now);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, []));
        return (application, InterviewRequest.Create(application, 45, WindowStart, WindowEnd, "America/New_York", Recruiter, Now));
    }
}
