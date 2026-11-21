using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Named teams (sign-in-and-tenancy.md, "Job access"): Settings, Team, Teams keeps them, a new job's Who's hiring and a job's
/// Hiring team copy their people, and the copy stays as it was when the team changes. Managing them needs Manage the team;
/// every member reads them. They are one organization's, like every tenant row.
/// </summary>
public sealed class NamedTeamsTests(AppHostFixture fixture)
{
    private static JsonObject Team(string name, params Guid[] memberIds) => new()
    {
        ["name"] = name,
        ["memberIds"] = new JsonArray([.. memberIds.Select(id => JsonValue.Create(id))]),
    };

    private static IEnumerable<Guid> MemberIds(ApiResult result, string path = "") =>
        (path.Length == 0 ? result.Body : result.At(path)).EnumerateArray().Select(m => m.GetProperty("memberId").GetGuid());

    private static async Task<IReadOnlyList<Guid>> JobTeamAsync(Tenant reader, Guid jobId) =>
        [.. MemberIds((await reader.GetAsync($"/v1/jobs/{jobId}/hiring-team")).Expect(HttpStatusCode.OK))];

    [Fact]
    public async Task An_owner_makes_renames_and_deletes_a_team_and_a_name_is_one_teams_in_any_case()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var recruiterMember = await owner.MemberIdAsync(recruiter.UserId);
        var ownerMember = await owner.MemberIdAsync(owner.UserId);

        var created = (await owner.PostAsync("/v1/organization/teams", Team("  Kitchen ", recruiterMember, recruiterMember))).Expect(HttpStatusCode.Created);
        created.Text("name").Should().Be("Kitchen");
        MemberIds(created, "members").Should().Equal(recruiterMember);
        var teamId = created.Id();

        (await owner.PostAsync("/v1/organization/teams", Team("KITCHEN"))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("name");
        (await owner.PostAsync("/v1/organization/teams", Team("Bar", Guid.NewGuid()))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
        (await owner.PostAsync("/v1/organization/teams", Team(" "))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("name");

        var renamed = (await owner.PutAsync($"/v1/organization/teams/{teamId}", Team("Front of house", ownerMember, recruiterMember))).Expect(HttpStatusCode.OK);
        renamed.Text("name").Should().Be("Front of house");
        MemberIds(renamed, "members").Should().BeEquivalentTo([ownerMember, recruiterMember]);
        (await owner.PutAsync($"/v1/organization/teams/{teamId}", Team("front of HOUSE", ownerMember))).Expect(HttpStatusCode.OK)
            .Text("name").Should().Be("front of HOUSE", "its own name in another case is still its own");

        // Every member reads the teams, to pick one for a job.
        var listed = (await recruiter.GetAsync("/v1/organization/teams")).Expect(HttpStatusCode.OK);
        listed.Body.EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().Equal("front of HOUSE");

        // The Team page's history says it.
        var history = (await owner.GetAsync("/v1/organization/team/history")).Expect(HttpStatusCode.OK).Body.GetRawText();
        history.Should().Contain("added the team ").And.Contain("renamed the team Kitchen to ");

        (await owner.DeleteAsync($"/v1/organization/teams/{teamId}")).Expect(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/v1/organization/teams/{teamId}")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync("/v1/organization/teams")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
    }

    [Fact]
    public async Task Only_a_role_that_manages_the_team_changes_teams()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var teamId = (await owner.PostAsync("/v1/organization/teams", Team("Kitchen"))).Expect(HttpStatusCode.Created).Id();

        (await recruiter.PostAsync("/v1/organization/teams", Team("Bar"))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PutAsync($"/v1/organization/teams/{teamId}", Team("Bar"))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.DeleteAsync($"/v1/organization/teams/{teamId}")).Expect(HttpStatusCode.Forbidden);
        (await owner.GetAsync("/v1/organization/teams")).Expect(HttpStatusCode.OK)
            .Body.EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().Equal("Kitchen");
    }

    [Fact]
    public async Task Another_organizations_team_is_invisible_and_cannot_fill_a_job()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var teamId = (await owner.PostAsync("/v1/organization/teams", Team("Kitchen"))).Expect(HttpStatusCode.Created).Id();

        (await other.GetAsync("/v1/organization/teams")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        (await other.PutAsync($"/v1/organization/teams/{teamId}", Team("Taken"))).Expect(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/v1/organization/teams/{teamId}")).Expect(HttpStatusCode.NotFound);
        var job = Samples.RemoteJob("Line cook");
        job["teamId"] = teamId;
        (await other.PostAsync("/v1/jobs", job)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("teamId");
        // Names are unique within one organization only.
        var theirs = (await other.PostAsync("/v1/organization/teams", Team("Kitchen"))).Expect(HttpStatusCode.Created).Id();

        // Nobody from another organization goes on a team or a job, and another organization's jobs suggest nothing.
        var ownerMember = await owner.MemberIdAsync(owner.UserId);
        (await other.PostAsync("/v1/organization/teams", Team("Bar", ownerMember))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
        (await other.PutAsync($"/v1/organization/teams/{theirs}", Team("Kitchen", ownerMember))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
        var withStranger = Samples.RemoteJob("Sous chef");
        withStranger["memberIds"] = new JsonArray(JsonValue.Create(ownerMember));
        (await other.PostAsync("/v1/jobs", withStranger)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
        var (_, versionId) = await owner.CreatePublishedTemplateAsync("Line cook");
        var used = new JsonObject { ["title"] = "Line cook", ["templateVersionId"] = versionId, ["teamId"] = teamId };
        await owner.CreateJobAsync(used);
        (await other.GetAsync($"/v1/organization/teams/suggested?templateVersionId={versionId}")).Expect(HttpStatusCode.OK)
            .At("teamId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // The job keeps the people it was given: a later change to the team, deleting it, or someone leaving the organization
    // after the copy changes nothing on the job, and a person who left before it is not copied at all.
    [Fact]
    public async Task A_new_job_copies_the_teams_people_and_anyone_chosen_and_keeps_them_when_the_team_changes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var leaver = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var ownerMember = await owner.MemberIdAsync(owner.UserId);
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        var recruiterMember = await owner.MemberIdAsync(recruiter.UserId);
        var leaverMember = await owner.MemberIdAsync(leaver.UserId);
        var teamId = (await owner.PostAsync("/v1/organization/teams", Team("Kitchen", viewerMember, leaverMember))).Expect(HttpStatusCode.Created).Id();
        (await owner.DeleteAsync($"/v1/organization/members/{leaverMember}")).Expect(HttpStatusCode.NoContent);

        var body = Samples.RemoteJob("Line cook");
        body["teamId"] = teamId;
        body["memberIds"] = new JsonArray(JsonValue.Create(recruiterMember));
        var job = await owner.CreateJobAsync(body);

        (await JobTeamAsync(owner, job.Id())).Should().BeEquivalentTo([ownerMember, viewerMember, recruiterMember], "the creator, the team's members and the one chosen");
        (await viewer.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);

        (await owner.PutAsync($"/v1/organization/teams/{teamId}", Team("Kitchen", ownerMember))).Expect(HttpStatusCode.OK);
        (await JobTeamAsync(owner, job.Id())).Should().BeEquivalentTo([ownerMember, viewerMember, recruiterMember], "a later change to the team changes no job");
        (await owner.DeleteAsync($"/v1/organization/teams/{teamId}")).Expect(HttpStatusCode.NoContent);
        (await JobTeamAsync(owner, job.Id())).Should().BeEquivalentTo([ownerMember, viewerMember, recruiterMember]);
        (await viewer.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);

        var stranger = Samples.RemoteJob("Sous chef");
        stranger["memberIds"] = new JsonArray(JsonValue.Create(Guid.NewGuid()));
        (await owner.PostAsync("/v1/jobs", stranger)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
    }

    // A recruiter on Only jobs they're on may create jobs and so choose who works on them, as the hiring team sheet lets them.
    [Fact]
    public async Task A_member_on_only_their_jobs_starts_a_job_with_a_team_and_stays_on_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var recruiterMember = await owner.MemberIdAsync(recruiter.UserId);
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        var teamId = (await owner.PostAsync("/v1/organization/teams", Team("Kitchen", viewerMember))).Expect(HttpStatusCode.Created).Id();

        var body = Samples.RemoteJob("Line cook");
        body["teamId"] = teamId;
        var job = await recruiter.CreateJobAsync(body);

        (await JobTeamAsync(recruiter, job.Id())).Should().BeEquivalentTo([recruiterMember, viewerMember]);
        (await viewer.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
    }

    // The team the last job from the same template, or with the same department, started with, among the jobs the reader
    // sees. A member on Only jobs they're on learns nothing from a job they cannot see.
    [Fact]
    public async Task A_new_job_is_offered_the_team_the_last_job_like_it_used()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var kitchen = (await owner.PostAsync("/v1/organization/teams", Team("Kitchen"))).Expect(HttpStatusCode.Created).Id();
        var engineering = (await owner.PostAsync("/v1/organization/teams", Team("Engineering"))).Expect(HttpStatusCode.Created).Id();
        var (_, cookVersion) = await owner.CreatePublishedTemplateAsync("Line cook");
        var (_, otherVersion) = await owner.CreatePublishedTemplateAsync("Bartender");

        async Task<Guid?> SuggestedAsync(Tenant reader, Guid versionId)
        {
            var teamId = (await reader.GetAsync($"/v1/organization/teams/suggested?templateVersionId={versionId}")).Expect(HttpStatusCode.OK).At("teamId");
            return teamId.ValueKind == JsonValueKind.Null ? null : teamId.GetGuid();
        }

        (await SuggestedAsync(owner, cookVersion)).Should().BeNull("no job has used a team yet");

        await owner.CreateJobAsync(new JsonObject { ["title"] = "Line cook", ["templateVersionId"] = cookVersion, ["teamId"] = kitchen });
        // The ScreenHire software engineer template's department is Engineering; a job of that department, in any case, used a team.
        var platform = Samples.RemoteJob("Platform engineer");
        platform["department"] = "engineering";
        platform["teamId"] = engineering;
        await owner.CreateJobAsync(platform);

        (await SuggestedAsync(owner, cookVersion)).Should().Be(kitchen);
        (await SuggestedAsync(owner, SystemTemplates.SoftwareEngineerVersion1)).Should().Be(engineering);
        (await SuggestedAsync(owner, otherVersion)).Should().BeNull();
        (await SuggestedAsync(recruiter, cookVersion)).Should().BeNull("the job that used Kitchen is not one of theirs");

        // A deleted team is not offered.
        (await owner.DeleteAsync($"/v1/organization/teams/{kitchen}")).Expect(HttpStatusCode.NoContent);
        (await SuggestedAsync(owner, cookVersion)).Should().BeNull();
    }
}
