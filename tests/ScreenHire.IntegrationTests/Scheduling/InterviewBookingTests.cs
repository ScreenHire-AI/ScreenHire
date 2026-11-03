using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// The offered times and the booking transaction against the real database and the sandbox calendar
/// (docs/architecture/interview-scheduling.md): busy time from the interviewer's calendar and from interviews ScreenHire
/// has already booked, the chosen time checked against what was offered, and interviewers never double-booked when
/// bookings race. Each test uses its own organization and fresh interviewers.
/// </summary>
public sealed class InterviewBookingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Half = TimeSpan.FromMinutes(30);

    [Fact]
    public async Task A_sandbox_busy_block_takes_the_slots_it_covers_and_no_others()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct, minutes: 60);
        var free = (await fixture.OfferedAsync(organization.Id, interview, Ct)).Slots;
        free.Should().NotBeEmpty();
        // An interior slot: the ones next to it are offered too, so what disappears is the busy block's doing.
        var target = free.First(s => free.Contains(s - Half) && free.Contains(s + Half + Half));

        await fixture.AddBusyAsync(organization.Id, interviewer, target, target.AddHours(1), Ct);
        var offered = await fixture.OfferedAsync(organization.Id, interview, Ct);

        offered.HasAllCalendars.Should().BeTrue();
        offered.Slots.Should().NotContain([target - Half, target, target + Half], "a 60 minute interview in any of those would overlap the busy hour");
        offered.Slots.Should().Contain(target + Half + Half, "an interview starting when the busy block ends is fine, when it is a working-hours slot");
        offered.Slots.Should().OnlyContain(s => s.UtcDateTime.Hour >= 9 && s.UtcDateTime.Hour < 17 && s.DayOfWeek != DayOfWeek.Saturday && s.DayOfWeek != DayOfWeek.Sunday);
        offered.Slots.Should().OnlyContain(s => s >= DateTimeOffset.UtcNow.AddHours(24), "a slot needs the minimum notice");
        free.Except(offered.Slots).Should().OnlyContain(s => s >= target - Half && s < target + Half + Half);
    }

    [Fact]
    public async Task An_interviewer_with_no_usable_calendar_is_reported_and_nothing_is_offered()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (connected, none, broken) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await fixture.ConnectCalendarAsync(organization.Id, connected, problem: false, Ct);
        await fixture.ConnectCalendarAsync(organization.Id, broken, problem: true, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [connected, none, broken], Ct);

        var offered = await fixture.OfferedAsync(organization.Id, interview, Ct);

        offered.HasAllCalendars.Should().BeFalse();
        offered.UnavailableInterviewers.Should().BeEquivalentTo([none, broken]);
        offered.Slots.Should().BeEmpty("times offered without someone's calendar could double-book them");
    }

    [Fact]
    public async Task A_calendar_of_a_provider_this_host_has_not_configured_is_not_usable()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct, CalendarProvider.Google);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);

        (await fixture.OfferedAsync(organization.Id, interview, Ct)).UnavailableInterviewers.Should().Equal(interviewer);
    }

    [Fact]
    public async Task Booking_succeeds_once_and_stores_the_time_the_revision_and_pending_attendees()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (first, second) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await fixture.ConnectCalendarAsync(organization.Id, first, problem: false, Ct);
        await fixture.ConnectCalendarAsync(organization.Id, second, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [first, second], Ct);
        var start = (await fixture.OfferedAsync(organization.Id, interview, Ct)).Slots[3];

        var booked = await fixture.BookAsync(organization.Id, interview, start, Ct);

        booked.Outcome.Should().Be(InterviewBookingOutcome.Done);
        booked.IsDone.Should().BeTrue();
        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        var stored = await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == interview, Ct);
        stored.Status.Should().Be(InterviewStatus.Booked);
        stored.StartsAt.Should().Be(start);
        stored.EndsAt.Should().Be(start + Half);
        stored.Revision.Should().Be(1);
        var attendees = await db.InterviewAttendees.AsNoTracking().Where(a => a.InterviewId == interview).ToListAsync(Ct);
        attendees.Should().HaveCount(2).And.OnlyContain(a => a.SyncStatus == InterviewSyncStatus.Pending);
        attendees.Single(a => a.IsOrganizer).UserId.Should().Be(first);

        (await fixture.BookAsync(organization.Id, interview, start, Ct)).Outcome.Should().Be(InterviewBookingOutcome.NotOpen, "it is booked already");
    }

    [Fact]
    public async Task A_time_that_was_not_offered_is_not_booked()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var offered = (await fixture.OfferedAsync(organization.Id, interview, Ct)).Slots;

        var overnight = await fixture.BookAsync(organization.Id, interview, new DateTimeOffset(offered[0].UtcDateTime.Date.AddDays(1), TimeSpan.Zero), Ct);
        var offGrid = await fixture.BookAsync(organization.Id, interview, offered[0].AddMinutes(10), Ct);
        var soon = await fixture.BookAsync(organization.Id, interview, DateTimeOffset.UtcNow.AddHours(1), Ct);

        new[] { overnight, offGrid, soon }.Should().OnlyContain(r => r.Outcome == InterviewBookingOutcome.SlotTaken);
        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        (await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == interview, Ct)).Status.Should().Be(InterviewStatus.Open);
    }

    [Fact]
    public async Task A_refreshed_token_outlives_the_transaction_of_a_booking_that_failed()
    {
        // The public route holds a transaction around the booking. A provider that rotates refresh tokens has spent the old
        // one by the time the booking loses, so the new one must survive the rollback.
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var expired = SchedulingTestSupport.Now().AddMinutes(-1);
        await using (var arrange = await fixture.ContextAsync(organization.Id, Ct))
        {
            await arrange.Database.ExecuteSqlAsync($"UPDATE calendar_connections SET access_token_expires_at = {expired} WHERE user_id = {interviewer}", Ct);
        }

        var tenant = SchedulingTestSupport.TenantOf(organization.Id);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            var refused = await SchedulingTestSupport.Services(db, tenant).Booking.BookAsync(interview, DateTimeOffset.UtcNow.AddHours(1), Ct);
            refused.Outcome.Should().Be(InterviewBookingOutcome.SlotTaken);
            await transaction.RollbackAsync(Ct);
        }

        await using var check = await fixture.ContextAsync(organization.Id, Ct);
        var stored = await check.CalendarConnections.AsNoTracking().SingleAsync(c => c.UserId == interviewer, Ct);
        stored.AccessTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(5), "the refresh committed on its own connection");
        stored.Problem.Should().BeNull();
    }

    [Fact]
    public async Task A_slot_the_interviewer_got_busy_in_since_it_was_offered_is_taken()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var start = (await fixture.OfferedAsync(organization.Id, interview, Ct)).Slots[1];
        await fixture.AddBusyAsync(organization.Id, interviewer, start, start + Half, Ct);

        (await fixture.BookAsync(organization.Id, interview, start, Ct)).Outcome.Should().Be(InterviewBookingOutcome.SlotTaken);
    }

    [Fact]
    public async Task Booking_with_an_interviewer_who_has_no_calendar_reports_who()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (connected, none) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await fixture.ConnectCalendarAsync(organization.Id, connected, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [connected, none], Ct);

        var result = await fixture.BookAsync(organization.Id, interview, DateTimeOffset.UtcNow.AddDays(3), Ct);

        result.Outcome.Should().Be(InterviewBookingOutcome.NoCalendar);
        result.UnavailableInterviewers.Should().Equal(none);
    }

    [Fact]
    public async Task An_unknown_interview_is_not_found_and_so_is_another_organizations()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var other = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);

        (await fixture.BookAsync(organization.Id, Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddDays(3), Ct)).Outcome.Should().Be(InterviewBookingOutcome.NotFound);
        (await fixture.BookAsync(other.Id, interview, DateTimeOffset.UtcNow.AddDays(3), Ct)).Outcome.Should().Be(InterviewBookingOutcome.NotFound);
    }

    [Fact]
    public async Task A_booked_interview_takes_its_time_from_other_interviews_of_the_same_interviewer_and_leaves_other_interviewers_alone()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (shared, other) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var user in new[] { shared, other })
        {
            await fixture.ConnectCalendarAsync(organization.Id, user, problem: false, Ct);
        }

        var booked = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [shared], Ct);
        var sameInterviewer = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [shared], Ct);
        var differentInterviewer = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [other], Ct);
        var slots = (await fixture.OfferedAsync(organization.Id, booked, Ct)).Slots;
        var start = slots[2];

        (await fixture.BookAsync(organization.Id, booked, start, Ct)).IsDone.Should().BeTrue();

        (await fixture.OfferedAsync(organization.Id, sameInterviewer, Ct)).Slots.Should().NotContain(start).And.Contain(slots[0]);
        (await fixture.OfferedAsync(organization.Id, differentInterviewer, Ct)).Slots.Should().Contain(start);
        (await fixture.BookAsync(organization.Id, sameInterviewer, start, Ct)).Outcome.Should().Be(InterviewBookingOutcome.SlotTaken);
        (await fixture.BookAsync(organization.Id, differentInterviewer, start, Ct)).IsDone.Should().BeTrue("a different interviewer is free");
    }

    // The core guarantee. Each request is for a different candidate on a different job, all for the same interviewer and the
    // same time; they race on separate connections, as the careers page's requests would.
    [Fact]
    public async Task Racing_bookings_of_one_interviewers_slot_book_exactly_one()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interviews = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            interviews.Add(await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct));
        }

        var start = (await fixture.OfferedAsync(organization.Id, interviews[0], Ct)).Slots[2];

        var results = await Task.WhenAll(interviews.Select(id => fixture.BookAsync(organization.Id, id, start, Ct)));

        results.Count(r => r.Outcome == InterviewBookingOutcome.Done).Should().Be(1);
        results.Count(r => r.Outcome == InterviewBookingOutcome.SlotTaken).Should().Be(3);
        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        var bookedRows = await db.InterviewRequests.AsNoTracking().Where(r => r.Status == InterviewStatus.Booked).ToListAsync(Ct);
        bookedRows.Should().ContainSingle().Which.StartsAt.Should().Be(start);
    }

    [Fact]
    public async Task Racing_bookings_with_overlapping_times_and_shared_interviewers_never_double_book_anyone()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (alice, bob) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var user in new[] { alice, bob })
        {
            await fixture.ConnectCalendarAsync(organization.Id, user, problem: false, Ct);
        }

        // Alice and Bob together, Bob and Alice in the other order (the locks are taken in sorted order), and Bob alone.
        var both = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [alice, bob], Ct, minutes: 60);
        var reversed = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [bob, alice], Ct, minutes: 60);
        var bobAlone = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [bob], Ct, minutes: 30);
        var slots = (await fixture.OfferedAsync(organization.Id, both, Ct)).Slots;
        var start = slots[3];

        var results = await Task.WhenAll(
            fixture.BookAsync(organization.Id, both, start, Ct),
            fixture.BookAsync(organization.Id, reversed, start, Ct),
            fixture.BookAsync(organization.Id, bobAlone, start + Half, Ct));

        results.Count(r => r.IsDone).Should().Be(1, "all three need Bob during the same hour");
        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        var booked = await db.InterviewRequests.AsNoTracking().Where(r => r.Status == InterviewStatus.Booked).ToListAsync(Ct);
        booked.Should().ContainSingle();
    }

    [Fact]
    public async Task A_cancelled_interview_frees_its_time()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var first = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var second = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var start = (await fixture.OfferedAsync(organization.Id, first, Ct)).Slots[2];
        (await fixture.BookAsync(organization.Id, first, start, Ct)).IsDone.Should().BeTrue();
        (await fixture.BookAsync(organization.Id, second, start, Ct)).Outcome.Should().Be(InterviewBookingOutcome.SlotTaken);

        await using (var db = await fixture.ContextAsync(organization.Id, Ct))
        {
            (await db.InterviewRequests.SingleAsync(r => r.Id == first, Ct)).Cancel(DateTimeOffset.UtcNow, byCandidate: false);
            await db.SaveChangesAsync(Ct);
        }

        (await fixture.BookAsync(organization.Id, second, start, Ct)).IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task Rescheduling_moves_a_booked_interview_counts_the_revision_and_may_overlap_its_own_old_time()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var interview = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct, minutes: 60);
        var slots = (await fixture.OfferedAsync(organization.Id, interview, Ct)).Slots;
        var start = slots.Skip(2).First(s => slots.Contains(s + Half));
        (await fixture.BookAsync(organization.Id, interview, start, Ct)).IsDone.Should().BeTrue();

        // Half an hour later overlaps the booking it replaces; the interview does not block itself.
        var overlapping = await fixture.RescheduleAsync(organization.Id, interview, start + Half, byCandidate: true, Ct);
        overlapping.Outcome.Should().Be(InterviewBookingOutcome.Done);
        var later = slots[^1];
        var moved = await fixture.RescheduleAsync(organization.Id, interview, later, byCandidate: true, Ct);

        moved.Outcome.Should().Be(InterviewBookingOutcome.Done);
        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        var stored = await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == interview, Ct);
        stored.Status.Should().Be(InterviewStatus.Booked);
        stored.StartsAt.Should().Be(later);
        stored.Revision.Should().Be(3, "booked, moved, moved");
        (await db.InterviewAttendees.AsNoTracking().SingleAsync(a => a.InterviewId == interview, Ct)).SyncStatus.Should().Be(InterviewSyncStatus.Pending);
    }

    [Fact]
    public async Task Rescheduling_into_a_time_another_interview_holds_is_refused_and_changes_nothing()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var mine = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var theirs = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var slots = (await fixture.OfferedAsync(organization.Id, mine, Ct)).Slots;
        (await fixture.BookAsync(organization.Id, mine, slots[1], Ct)).IsDone.Should().BeTrue();
        (await fixture.BookAsync(organization.Id, theirs, slots[4], Ct)).IsDone.Should().BeTrue();

        (await fixture.RescheduleAsync(organization.Id, mine, slots[4], byCandidate: false, Ct)).Outcome.Should().Be(InterviewBookingOutcome.SlotTaken);

        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        var stored = await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == mine, Ct);
        (stored.StartsAt, stored.Revision).Should().Be((slots[1], 1));
    }

    [Fact]
    public async Task Only_a_booked_interview_is_rescheduled_and_the_candidate_cannot_move_one_that_starts_inside_the_notice()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var open = await fixture.AddInterviewAsync(await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct);
        var slots = (await fixture.OfferedAsync(organization.Id, open, Ct)).Slots;
        (await fixture.RescheduleAsync(organization.Id, open, slots[0], byCandidate: true, Ct)).Outcome.Should().Be(InterviewBookingOutcome.NotOpen);

        // An interview booked for tomorrow morning in a window that opened yesterday: inside the 24 hour notice.
        var now = SchedulingTestSupport.Now();
        var application = await fixture.AddApplicationAsync(organization.Id, Ct);
        var soon = await fixture.AddInterviewAsync(application, [interviewer], Ct, windowStart: now.AddDays(-1), windowEnd: now.AddDays(10), createdAt: now.AddDays(-2));
        await using (var db = await fixture.ContextAsync(organization.Id, Ct))
        {
            (await db.InterviewRequests.SingleAsync(r => r.Id == soon, Ct)).Book(now.AddHours(5), now.AddDays(-1));
            await db.SaveChangesAsync(Ct);
        }

        (await fixture.RescheduleAsync(organization.Id, soon, slots[0], byCandidate: true, Ct)).Outcome.Should().Be(InterviewBookingOutcome.TooLate);
        (await fixture.RescheduleAsync(organization.Id, soon, slots[0], byCandidate: false, Ct)).Outcome
            .Should().Be(InterviewBookingOutcome.Done, "the team may move it whenever it likes");
    }

    [Fact]
    public async Task An_open_request_past_its_window_cannot_be_booked_and_is_stored_as_expired()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var interviewer = Guid.CreateVersion7();
        await fixture.ConnectCalendarAsync(organization.Id, interviewer, problem: false, Ct);
        var now = SchedulingTestSupport.Now();
        var interview = await fixture.AddInterviewAsync(
            await fixture.AddApplicationAsync(organization.Id, Ct), [interviewer], Ct, windowStart: now.AddDays(-8), windowEnd: now.AddDays(-1), createdAt: now.AddDays(-9));

        var result = await fixture.BookAsync(organization.Id, interview, now.AddDays(-2), Ct);

        result.Outcome.Should().Be(InterviewBookingOutcome.NotOpen);
        await using var db = await fixture.ContextAsync(organization.Id, Ct);
        (await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == interview, Ct)).Status.Should().Be(InterviewStatus.Expired);
    }

    [Fact]
    public async Task A_stale_open_request_blocks_a_new_one_until_it_is_stored_as_expired()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var application = await fixture.AddApplicationAsync(organization.Id, Ct);
        var now = SchedulingTestSupport.Now();
        var stale = await fixture.AddInterviewAsync(application, [], Ct, windowStart: now.AddDays(-8), windowEnd: now.AddDays(-1), createdAt: now.AddDays(-9));
        var sourceKey = "run-1:0";

        // The partial unique index sees the stored status: one live interview per application.
        await using (var db = await fixture.ContextAsync(organization.Id, Ct))
        {
            db.InterviewRequests.Add(InterviewRequest.Create(application, 30, now, now.AddDays(7), "UTC", null, now));
            await FluentActions.Awaiting(() => db.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>();
        }

        await using (var db = await fixture.ContextAsync(organization.Id, Ct))
        {
            (await db.InterviewRequests.SingleAsync(r => r.Id == stale, Ct)).MarkExpired(now).Should().BeTrue();
            await db.SaveChangesAsync(Ct);
            db.InterviewRequests.Add(InterviewRequest.Create(application, 30, now, now.AddDays(7), "UTC", null, now, sourceKey: sourceKey));
            await db.SaveChangesAsync(Ct);
        }

        // A workflow action that runs twice finds its request: the source key is unique per application.
        await using var again = await fixture.ContextAsync(organization.Id, Ct);
        var live = await again.InterviewRequests.SingleAsync(r => r.SourceKey == sourceKey, Ct);
        live.Cancel(now, byCandidate: false);
        await again.SaveChangesAsync(Ct);
        again.InterviewRequests.Add(InterviewRequest.Create(application, 30, now, now.AddDays(7), "UTC", null, now, sourceKey: sourceKey));
        await FluentActions.Awaiting(() => again.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>();
    }
}
