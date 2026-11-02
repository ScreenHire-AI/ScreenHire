using System.Text.Json;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// <c>GET /v1/interviewers?jobId=</c> through the running stack (docs/architecture/interview-scheduling.md): the members who
/// can interview for a job, with whether their calendar works. Scheduling emails the candidate, so it needs the permission
/// that emails them.
/// </summary>
public sealed class InterviewersEndpointTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_who_can_email_candidates_lists_the_people_who_see_the_job_with_their_calendars()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await fixture.ConnectCalendarAsync(owner.OrganizationId, owner.UserId, problem: false, Ct);

        var result = (await owner.GetAsync($"/v1/interviewers?jobId={job}")).Expect(HttpStatusCode.OK);

        result.Count().Should().Be(2);
        var ownerEntry = Entry(result, owner.UserId);
        ownerEntry.GetProperty("calendarConnected").GetBoolean().Should().BeTrue();
        ownerEntry.GetProperty("calendarProvider").GetString().Should().Be("Sandbox");
        ownerEntry.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        var recruiterEntry = Entry(result, recruiter.UserId);
        recruiterEntry.GetProperty("calendarConnected").GetBoolean().Should().BeFalse();
        recruiterEntry.GetProperty("calendarProvider").ValueKind.Should().Be(JsonValueKind.Null);
        result.Body.GetRawText().Should().NotContainEquivalentOf("token", "the list never carries calendar tokens");
    }

    [Fact]
    public async Task A_calendar_that_needs_reconnecting_or_belongs_to_a_provider_not_set_up_here_is_not_connected()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var broken = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var google = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await fixture.ConnectCalendarAsync(owner.OrganizationId, broken.UserId, problem: true, Ct);
        await fixture.ConnectCalendarAsync(owner.OrganizationId, google.UserId, problem: false, Ct, CalendarProvider.Google);

        var result = (await owner.GetAsync($"/v1/interviewers?jobId={job}")).Expect(HttpStatusCode.OK);

        Entry(result, broken.UserId).GetProperty("calendarConnected").GetBoolean().Should().BeFalse();
        Entry(result, google.UserId).GetProperty("calendarConnected").GetBoolean().Should().BeFalse("Google is not configured on this host");
        Entry(result, google.UserId).GetProperty("calendarProvider").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Members_on_only_jobs_they_are_on_are_listed_only_for_the_jobs_they_are_on()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var onTeam = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var notOnTeam = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.SetHiringTeamAsync(job, await owner.MemberIdAsync(onTeam.UserId));

        var result = (await owner.GetAsync($"/v1/interviewers?jobId={job}")).Expect(HttpStatusCode.OK);

        result.Body.EnumerateArray().Select(e => e.GetProperty("userId").GetGuid()).Should()
            .BeEquivalentTo([owner.UserId, onTeam.UserId], "the owner sees every job, the first recruiter is on the team, the second is not");
    }

    [Fact]
    public async Task A_job_the_caller_does_not_see_is_not_found()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var restricted = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();

        (await restricted.GetAsync($"/v1/interviewers?jobId={job}")).Expect(HttpStatusCode.NotFound);

        await owner.SetHiringTeamAsync(job, await owner.MemberIdAsync(restricted.UserId));
        var result = (await restricted.GetAsync($"/v1/interviewers?jobId={job}")).Expect(HttpStatusCode.OK);
        result.Body.EnumerateArray().Select(e => e.GetProperty("userId").GetGuid()).Should().BeEquivalentTo([owner.UserId, restricted.UserId]);
    }

    [Fact]
    public async Task Without_the_permission_to_email_candidates_the_list_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();

        (await viewer.GetAsync($"/v1/interviewers?jobId={job}")).Expect(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_organizations_job_is_not_found_and_its_members_are_never_listed()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var theirJob = (await other.CreateJobAsync(Samples.RemoteJob("Their Role"))).Id();
        var ourJob = (await owner.CreateJobAsync(Samples.RemoteJob("Our Role"))).Id();
        await fixture.ConnectCalendarAsync(other.OrganizationId, other.UserId, problem: false, Ct);

        (await owner.GetAsync($"/v1/interviewers?jobId={theirJob}")).Expect(HttpStatusCode.NotFound);
        var ours = (await owner.GetAsync($"/v1/interviewers?jobId={ourJob}")).Expect(HttpStatusCode.OK);
        ours.Body.EnumerateArray().Select(e => e.GetProperty("userId").GetGuid()).Should().Equal(owner.UserId);
        Entry(ours, owner.UserId).GetProperty("calendarConnected").GetBoolean().Should().BeFalse("another organization's connection is not ours");
    }

    [Fact]
    public async Task A_missing_or_unknown_job_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        (await owner.GetAsync("/v1/interviewers")).Expect(HttpStatusCode.BadRequest);
        (await owner.GetAsync($"/v1/interviewers?jobId={Guid.CreateVersion7()}")).Expect(HttpStatusCode.NotFound);
    }

    private static JsonElement Entry(ApiResult result, Guid userId) =>
        result.Body.EnumerateArray().Single(e => e.GetProperty("userId").GetGuid() == userId);
}
