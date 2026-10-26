using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using ScreenHire.Domain.Applications;
using ScreenHire.IntegrationTests.Automations;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// Live updates follow job access (ADR-0015): an event about a job reaches a member on Only jobs they're on only while
/// they are on its hiring team, joining a team starts its events without a reconnect, and leaving one, or a change of
/// job access, closes their connections so they reconnect into the right groups.
/// </summary>
public sealed class JobScopeRealtimeTests(AppHostFixture fixture)
{
    private static readonly TimeSpan CloseDeadline = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_on_only_their_jobs_gets_events_about_their_jobs_and_none_about_others()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var theirs = (await owner.CreateJobAsync(Samples.RemoteJob("Their job"))).Id();
        var hidden = await AutomationSamples.JobWithAutomationsAsync(owner, "Hidden job", new JsonObject
        {
            ["id"] = "notify",
            ["name"] = "Tell the team",
            ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "NotifyTeam", ["note"] = "A new application arrived." }),
        });
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(theirs, await owner.MemberIdAsync(viewer.UserId));
        await using var ownerEvents = await EventsConnection.ConnectAsync(owner.Session, Ct);
        await using var viewerEvents = await EventsConnection.ConnectAsync(viewer.Session, Ct);

        // Everything about the hidden job: a publish, an application (with its NotifyTeam), a note mentioning the owner.
        await owner.PublishJobAsync(hidden);
        await ownerEvents.WaitForAsync("JobPublished", "jobId", hidden, Ct);
        var application = (await owner.CreateApplicationAsync(hidden, ApiSession.NewEmail("candidate"))).Id();
        await ownerEvents.WaitForAsync("TeamNotified", "applicationId", application, Ct);
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject
        {
            ["note"] = $"Have a look {NoteMentions.Token(owner.UserId)}",
        })).Expect(HttpStatusCode.Created);
        await ownerEvents.WaitForAsync("ApplicationNoteAdded", "applicationId", application, Ct);

        // Then one about theirs, which arrives after the others would have.
        await owner.PublishJobAsync(theirs);
        await viewerEvents.WaitForAsync("JobPublished", "jobId", theirs, Ct);

        viewerEvents.ReceivedAnythingAbout(hidden).Should().BeFalse("the member is not on the hidden job's hiring team");
        viewerEvents.ReceivedAnythingAbout(application).Should().BeFalse();

        var subscribe = () => viewerEvents.Hub.InvokeAsync("Subscribe", "job", hidden, Ct);
        await subscribe.Should().ThrowAsync<HubException>("a job outside their teams is like one that does not exist");
        await viewerEvents.Hub.InvokeAsync("Subscribe", "job", theirs, Ct);
    }

    [Fact]
    public async Task Joining_a_team_starts_its_events_at_once_and_leaving_it_closes_the_connection()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Joined later"))).Id();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        await using var viewerEvents = await EventsConnection.ConnectAsync(viewer.Session, Ct);

        await owner.SetHiringTeamAsync(job, viewerMember);
        await viewerEvents.WaitForAsync("HiringTeamChanged", "jobId", job, Ct);
        await owner.PublishJobAsync(job);
        await viewerEvents.WaitForAsync("JobPublished", "jobId", job, Ct);
        viewerEvents.Closed.IsCompleted.Should().BeFalse("joining a team adds the job's group to the open connection");

        await owner.SetHiringTeamAsync(job);
        await viewerEvents.Closed.WaitAsync(CloseDeadline, Ct);
    }

    [Fact]
    public async Task A_change_of_job_access_closes_the_members_connections()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var recruiterMember = await owner.MemberIdAsync(recruiter.UserId);
        await using var recruiterEvents = await EventsConnection.ConnectAsync(recruiter.Session, Ct);
        await using var ownerEvents = await EventsConnection.ConnectAsync(owner.Session, Ct);

        (await owner.PutAsync($"/v1/organization/members/{recruiterMember}/job-access", new JsonObject { ["jobAccess"] = "HiringTeamOnly" }))
            .Expect(HttpStatusCode.OK);

        await recruiterEvents.Closed.WaitAsync(CloseDeadline, Ct);
        ownerEvents.Closed.IsCompleted.Should().BeFalse("only the member whose access changed reconnects");

        // Reconnected, they hear about none of the jobs they are not on.
        await using var again = await EventsConnection.ConnectAsync(recruiter.Session, Ct);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Not theirs"))).Id();
        await owner.PublishJobAsync(job);
        await ownerEvents.WaitForAsync("JobPublished", "jobId", job, Ct);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        again.ReceivedAnythingAbout(job).Should().BeFalse();
    }

    [Fact]
    public async Task Becoming_an_admin_closes_the_connection_and_the_reconnect_hears_about_every_job()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var member = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var memberId = await owner.MemberIdAsync(member.UserId);
        await using var memberEvents = await EventsConnection.ConnectAsync(member.Session, Ct);

        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Admin" })).Expect(HttpStatusCode.OK);

        await memberEvents.Closed.WaitAsync(CloseDeadline, Ct);
        await using var again = await EventsConnection.ConnectAsync(member.Session, Ct);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Not on the team"))).Id();
        await owner.PublishJobAsync(job);
        await again.WaitForAsync("JobPublished", "jobId", job, Ct);
    }

    [Fact]
    public async Task Leaving_the_admin_role_closes_the_connection_and_the_reconnect_hears_only_about_their_jobs()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var member = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var memberId = await owner.MemberIdAsync(member.UserId);
        var theirs = (await owner.CreateJobAsync(Samples.RemoteJob("Their job"))).Id();
        var hidden = (await owner.CreateJobAsync(Samples.RemoteJob("Hidden job"))).Id();
        await owner.SetHiringTeamAsync(theirs, memberId);
        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Admin" })).Expect(HttpStatusCode.OK);
        await using var adminEvents = await EventsConnection.ConnectAsync(member.Session, Ct);

        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Recruiter" })).Expect(HttpStatusCode.OK);

        await adminEvents.Closed.WaitAsync(CloseDeadline, Ct);
        await using var again = await EventsConnection.ConnectAsync(member.Session, Ct);
        await owner.PublishJobAsync(hidden);
        await owner.PublishJobAsync(theirs);
        await again.WaitForAsync("JobPublished", "jobId", theirs, Ct);
        again.ReceivedAnythingAbout(hidden).Should().BeFalse("back on Only jobs they're on, the job they are not on is gone");
    }
}
