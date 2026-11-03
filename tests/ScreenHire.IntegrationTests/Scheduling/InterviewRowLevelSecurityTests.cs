using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// Row-level security on <c>interview_requests</c> and <c>interview_attendees</c> alone, with the query filters off
/// (docs/architecture/interview-scheduling.md, ADR-0015): another organization sees nothing, and a member on Only jobs
/// they're on sees an interview, and may write one, only through a job whose hiring team they are on.
/// </summary>
public sealed class InterviewRowLevelSecurityTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_who_cannot_see_the_job_cannot_read_its_interviews_even_without_the_query_filters()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Hidden Role"));
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        await AddInterviewAsync(owner.OrganizationId, application);

        async Task<(int Requests, int Attendees)> CountAsync(Action<TenantContext> scope)
        {
            var tenant = new TenantContext();
            tenant.SetUser(viewer.UserId);
            tenant.SetOrganization(owner.OrganizationId);
            scope(tenant);
            await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
            return (
                await db.InterviewRequests.IgnoreQueryFilters().CountAsync(r => r.OrganizationId == owner.OrganizationId, Ct),
                await db.InterviewAttendees.IgnoreQueryFilters().CountAsync(a => a.OrganizationId == owner.OrganizationId, Ct));
        }

        (await CountAsync(t => t.SetMemberScope(viewerMember, seesAll: false))).Should().Be((0, 0));
        (await CountAsync(_ => { })).Should().Be((0, 0), "a user whose job access was never resolved sees no jobs");
        (await CountAsync(t => t.SetMemberScope(viewerMember, seesAll: true))).Should().Be((1, 1));

        await owner.SetHiringTeamAsync(job.Id(), viewerMember);
        (await CountAsync(t => t.SetMemberScope(viewerMember, seesAll: false))).Should().Be((1, 1));
    }

    [Fact]
    public async Task A_member_who_cannot_see_the_job_cannot_write_an_interview_for_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Hidden Role"));
        var applicationId = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var application = await ApplicationAsync(owner.OrganizationId, applicationId);

        var tenant = new TenantContext();
        tenant.SetUser(viewer.UserId);
        tenant.SetOrganization(owner.OrganizationId);
        tenant.SetMemberScope(await owner.MemberIdAsync(viewer.UserId), seesAll: false);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var now = SchedulingTestSupport.Now();
        db.InterviewRequests.Add(InterviewRequest.Create(application, 30, now, now.AddDays(7), "UTC", viewer.UserId, now));

        var save = () => db.SaveChangesAsync(Ct);

        var failure = await save.Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Another_organization_sees_none_of_an_organizations_interviews()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Role"));
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        await AddInterviewAsync(owner.OrganizationId, application);

        var tenant = new TenantContext();
        tenant.SetOrganization(other.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);

        (await db.InterviewRequests.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.InterviewAttendees.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task An_interview_cannot_name_an_application_of_another_organization()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Role"));
        var application = await ApplicationAsync(owner.OrganizationId, (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id());

        // The composite foreign key (organization_id, application_id) refuses a request that crosses organizations.
        var now = SchedulingTestSupport.Now();
        var crossing = new InterviewRequestBuilder(application, other.OrganizationId, now).Build();
        await using var db = await fixture.ContextAsync(other.OrganizationId, Ct);
        db.InterviewRequests.Add(crossing);

        var save = () => db.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task An_attendee_cannot_name_another_application_than_its_interviews()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Role"));
        var first = await ApplicationAsync(owner.OrganizationId, (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("first"))).Id());
        var second = await ApplicationAsync(owner.OrganizationId, (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("second"))).Id());
        var now = SchedulingTestSupport.Now();
        var interview = InterviewRequest.Create(first, 30, now, now.AddDays(7), "UTC", null, now);
        await using var db = await fixture.ContextAsync(owner.OrganizationId, Ct);
        db.InterviewRequests.Add(interview);
        await db.SaveChangesAsync(Ct);

        // The attendee's own application is what its job_scope policy reads, so it must be its interview's.
        var elsewhere = InterviewAttendee.Create(interview, Guid.CreateVersion7(), isOrganizer: true, CalendarProvider.Sandbox);
        db.Entry(elsewhere).Property(a => a.ApplicationId).CurrentValue = second.Id;
        db.InterviewAttendees.Add(elsewhere);

        var save = () => db.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task AddInterviewAsync(Guid organizationId, Guid applicationId)
    {
        var application = await ApplicationAsync(organizationId, applicationId);
        var now = SchedulingTestSupport.Now();
        var interview = InterviewRequest.Create(application, 30, now, now.AddDays(7), "UTC", null, now);
        await using var db = await fixture.ContextAsync(organizationId, Ct);
        db.InterviewRequests.Add(interview);
        db.InterviewAttendees.Add(InterviewAttendee.Create(interview, Guid.CreateVersion7(), isOrganizer: true, CalendarProvider.Sandbox));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<JobApplication> ApplicationAsync(Guid organizationId, Guid applicationId)
    {
        await using var db = await fixture.ContextAsync(organizationId, Ct);
        return await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId, Ct);
    }

    // A request built for one organization's application but stamped with another organization, which only a bug could do.
    private sealed class InterviewRequestBuilder(JobApplication application, Guid organizationId, DateTimeOffset now)
    {
        public InterviewRequest Build() => new()
        {
            OrganizationId = organizationId,
            JobId = application.JobId,
            ApplicationId = application.Id,
            DurationMinutes = 30,
            WindowStart = now,
            WindowEnd = now.AddDays(7),
            TimeZone = "UTC",
            CreatedAt = now,
        };
    }
}
