using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// Arranging interview scheduling tests against the real database, as the application role: organizations, jobs,
/// applications, calendar connections and interview requests written through the domain methods, and the scheduling
/// services built over a context with the sandbox provider (see CalendarSandboxTests, which this follows).
/// </summary>
internal static class SchedulingTestSupport
{
    /// <summary>Protects the connection rows' tokens, and reads them back inside <see cref="CalendarAccess"/>.</summary>
    private static readonly CalendarTokens Tokens = new(new EphemeralDataProtectionProvider());

    /// <summary>Now, cut to whole seconds, so windows built from it survive the database's microsecond precision.</summary>
    public static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    /// <summary>A tenant that acts for the whole organization, as the public careers endpoints and the worker do.</summary>
    public static TenantContext TenantOf(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return tenant;
    }

    public static async Task<ScreenHireDbContext> ContextAsync(this AppHostFixture fixture, Guid organizationId, CancellationToken cancellationToken) =>
        await fixture.CreateDbContextAsync(TenantOf(organizationId), cancellationToken);

    /// <summary>Adds a draft job and an application from a new candidate to <paramref name="organizationId"/>. Returns the application.</summary>
    public static async Task<JobApplication> AddApplicationAsync(this AppHostFixture fixture, Guid organizationId, CancellationToken cancellationToken)
    {
        var slug = $"job-{Guid.NewGuid():N}";
        var job = TestData.NewJob(organizationId, slug);
        var candidate = Candidate.Create(organizationId, "Casey", "Morgan", $"{slug}@example.com", null, Now());
        var (application, submitted) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now(), null);
        await using var db = await fixture.ContextAsync(organizationId, cancellationToken);
        db.Jobs.Add(job);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.ApplicationEvents.Add(submitted);
        await db.SaveChangesAsync(cancellationToken);
        return application;
    }

    /// <summary>
    /// Connects the sandbox calendar for <paramref name="userId"/> by writing the row the OAuth round trip would, with
    /// tokens the sandbox provider accepts. <paramref name="problem"/> marks it as needing a reconnect.
    /// </summary>
    public static async Task ConnectCalendarAsync(
        this AppHostFixture fixture, Guid organizationId, Guid userId, bool problem, CancellationToken cancellationToken,
        CalendarProvider provider = CalendarProvider.Sandbox)
    {
        var now = Now();
        var connection = CalendarConnection.Connect(
            organizationId,
            userId,
            provider,
            "interviewer@example.com",
            Tokens.Protect(organizationId, userId, SandboxCalendarProvider.AccessTokenFor(organizationId, userId)),
            Tokens.Protect(organizationId, userId, $"sandbox-refresh.{organizationId:N}.{userId:N}"),
            now.AddHours(1),
            now);
        if (problem)
        {
            connection.RecordProblem(now);
        }

        await using var db = await fixture.ContextAsync(organizationId, cancellationToken);
        db.CalendarConnections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Marks an interviewer busy in the sandbox calendar.</summary>
    public static async Task AddBusyAsync(
        this AppHostFixture fixture, Guid organizationId, Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        await using var db = await fixture.ContextAsync(organizationId, cancellationToken);
        db.CalendarSandboxBusy.Add(new CalendarSandboxBusy { OrganizationId = organizationId, UserId = userId, StartsAt = start, EndsAt = end });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// An Open 30 minute interview for <paramref name="application"/> in UTC, 09:00 to 17:00 on weekdays, with a window
    /// from now to two weeks on, and <paramref name="interviewers"/> as attendees (the first is the organizer).
    /// </summary>
    public static async Task<Guid> AddInterviewAsync(
        this AppHostFixture fixture, JobApplication application, Guid[] interviewers, CancellationToken cancellationToken,
        int minutes = 30, DateTimeOffset? windowStart = null, DateTimeOffset? windowEnd = null, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? Now();
        var interview = InterviewRequest.Create(
            application, minutes, windowStart ?? now, windowEnd ?? now.AddDays(14), "UTC", null, now);
        await using var db = await fixture.ContextAsync(application.OrganizationId, cancellationToken);
        db.InterviewRequests.Add(interview);
        for (var i = 0; i < interviewers.Length; i++)
        {
            db.InterviewAttendees.Add(InterviewAttendee.Create(interview, interviewers[i], isOrganizer: i == 0, CalendarProvider.Sandbox));
        }

        await db.SaveChangesAsync(cancellationToken);
        return interview.Id;
    }

    /// <summary>The scheduling services over <paramref name="db"/>, which must belong to <paramref name="tenant"/>.</summary>
    public static (InterviewAvailability Availability, InterviewBooking Booking) Services(ScreenHireDbContext db, TenantContext tenant)
    {
        var options = Options.Create(new CalendarOptions { Sandbox = new CalendarSandboxOptions { Enabled = true } });
        var access = new CalendarAccess(
            options, Tokens, db, TimeProvider.System, new GoogleCalendarProvider(), new MicrosoftCalendarProvider(),
            new SandboxCalendarProvider(db, tenant, TimeProvider.System));
        var availability = new InterviewAvailability(db, access, TimeProvider.System);
        return (availability, new InterviewBooking(db, availability, access, TimeProvider.System));
    }

    /// <summary>The slots offered for the interview, from a fresh context.</summary>
    public static async Task<InterviewAvailabilityResult> OfferedAsync(this AppHostFixture fixture, Guid organizationId, Guid interviewId, CancellationToken cancellationToken)
    {
        var tenant = TenantOf(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        var (availability, _) = Services(db, tenant);
        var interview = await db.InterviewRequests.SingleAsync(r => r.Id == interviewId, cancellationToken);
        var attendees = await db.InterviewAttendees.Where(a => a.InterviewId == interviewId).OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        return await availability.FindSlotsAsync(interview, attendees, cancellationToken);
    }

    public static async Task<InterviewBookingResult> BookAsync(
        this AppHostFixture fixture, Guid organizationId, Guid interviewId, DateTimeOffset start, CancellationToken cancellationToken)
    {
        var tenant = TenantOf(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        return await Services(db, tenant).Booking.BookAsync(interviewId, start, cancellationToken);
    }

    public static async Task<InterviewBookingResult> RescheduleAsync(
        this AppHostFixture fixture, Guid organizationId, Guid interviewId, DateTimeOffset start, bool byCandidate, CancellationToken cancellationToken)
    {
        var tenant = TenantOf(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
        return await Services(db, tenant).Booking.RescheduleAsync(interviewId, start, byCandidate, cancellationToken);
    }
}
