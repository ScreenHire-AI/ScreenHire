using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// Keeping a candidate in mind (docs/architecture/talent-pool.md): the permission the candidate gives on the apply form,
/// who can be kept, what the pool lists, taking someone out, and bringing a kept application back to its job. Each test
/// makes its own organization and people.
/// </summary>
public sealed class TalentPoolKeepTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_tick_on_the_apply_form_stores_twelve_months_of_permission_and_no_tick_stores_none()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Platform Engineer");

        var ticked = await world.DetailAsync(await world.ApplyAsync(job, ApiSession.NewEmail("ticked"), keep: true));
        var unticked = await world.DetailAsync(await world.ApplyAsync(job, ApiSession.NewEmail("unticked"), keep: false));

        var given = ticked.At("futureRolesGivenAt").GetDateTimeOffset();
        given.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), "the permission starts when the candidate submits");
        ticked.At("futureRolesUntil").GetDateTimeOffset().Should().Be(given.AddMonths(12), "the default period is 12 months");
        ticked.At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
        unticked.At("futureRolesGivenAt").ValueKind.Should().Be(JsonValueKind.Null);
        unticked.At("futureRolesUntil").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_later_application_without_the_tick_leaves_the_permission_given_on_the_first_one_intact()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (first, _) = await world.OpenJobAsync("Data Engineer");
        var (second, _) = await world.OpenJobAsync("Office Manager");
        var email = ApiSession.NewEmail("again");
        var ticked = await world.ApplyAsync(first, email, keep: true);
        var before = (await world.DetailAsync(ticked)).At("futureRolesUntil").GetDateTimeOffset();

        var unticked = await world.ApplyAsync(second, email, keep: false);

        var firstAfter = await world.DetailAsync(ticked);
        firstAfter.At("futureRolesUntil").GetDateTimeOffset().Should().Be(before);
        firstAfter.At("futureRolesGivenAt").ValueKind.Should().Be(JsonValueKind.String);
        var secondDetail = await world.DetailAsync(unticked);
        secondDetail.At("futureRolesGivenAt").ValueKind.Should().Be(JsonValueKind.Null, "a tick on one job's form does not cover another job");
        secondDetail.Id("candidate.id").Should().Be((await world.DetailAsync(ticked)).Id("candidate.id"), "it is the same person");
    }

    [Fact]
    public async Task Keeping_an_application_leaves_its_stage_records_the_note_and_lists_the_person_in_the_pool()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Support Lead");
        var email = ApiSession.NewEmail("kept");
        var application = await world.ApplyAsync(job, email, keep: true, firstName: "Noor", lastName: "Haddad");
        var stageBefore = (await world.DetailAsync(application)).Text("stage.id");

        var kept = (await world.KeepAsync([application], "Strong on support tooling, ask again in spring")).Expect(HttpStatusCode.OK);

        kept.At("kept").GetInt32().Should().Be(1);
        kept.At("notAllowed").GetInt32().Should().Be(0);
        kept.At("alreadyEnded").GetInt32().Should().Be(0);
        var detail = await world.DetailAsync(application);
        detail.At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.String);
        detail.Text("stage.id").Should().Be(stageBefore, "keeping someone in mind is not a move and not a rejection");
        detail.At("inTalentPool").GetBoolean().Should().BeTrue();
        var entry = await world.EntryAsync(application, "KeptInMind");
        entry.GetProperty("note").GetString().Should().Be("Strong on support tooling, ask again in spring");
        entry.GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);

        var pool = (await world.Owner.GetAsync("/v1/talent-pool")).Expect(HttpStatusCode.OK);
        pool.At("permissionMonths").GetInt32().Should().Be(12);
        pool.Count("people").Should().Be(1);
        pool.Text("people.0.candidateName").Should().Be("Noor Haddad");
        pool.Text("people.0.email").Should().Be(email);
        pool.Text("people.0.note").Should().Be("Strong on support tooling, ask again in spring");
        pool.Text("people.0.jobTitle").Should().Be("Support Lead");
        pool.Id("people.0.applicationId").Should().Be(application);
        pool.At("people.0.permissionEndsAt").GetDateTimeOffset().Should().Be(detail.At("futureRolesUntil").GetDateTimeOffset());
        pool.At("people.0.lastInvitedJobId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task One_selection_counts_who_was_kept_who_gave_no_permission_and_who_had_already_ended()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Site Reliability Engineer");
        var eligible = await world.ApplyAsync(job, ApiSession.NewEmail("eligible"), keep: true);
        var noPermission = (await world.Owner.CreateApplicationAsync(job, ApiSession.NewEmail("noconsent"))).Id();
        var rejected = await world.ApplyAsync(job, ApiSession.NewEmail("rejected"), keep: true);
        (await world.Owner.PostAsync($"/v1/applications/{rejected}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);

        var result = (await world.KeepAsync([eligible, noPermission, rejected])).Expect(HttpStatusCode.OK);

        result.At("kept").GetInt32().Should().Be(1);
        result.At("notAllowed").GetInt32().Should().Be(1, "a recruiter-added application has no permission from the candidate");
        result.At("alreadyEnded").GetInt32().Should().Be(1, "a rejected application has ended, whatever the candidate agreed");
        (await world.DetailAsync(noPermission)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await world.DetailAsync(rejected)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await world.DetailAsync(rejected)).Text("stage.id").Should().Be("rejected");
        (await world.PoolAsync()).Select(p => p.GetProperty("applicationId").GetGuid()).Should().Equal(eligible);
    }

    [Fact]
    public async Task A_viewer_cannot_keep_anyone_in_mind()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Viewer job");
        var application = await world.ApplyAsync(job, ApiSession.NewEmail("viewed"), keep: true);
        using var viewer = await fixture.InviteAndJoinAsync(world.Owner, "Viewer");

        (await world.KeepAsync([application], by: viewer)).Status.Should().Be(HttpStatusCode.Forbidden);

        (await world.DetailAsync(application)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await world.PoolAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_application_id_from_another_organization_is_a_validation_problem_and_changes_nothing()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        using var other = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Mine");
        var (otherJob, _) = await other.OpenJobAsync("Theirs");
        var mine = await world.ApplyAsync(job, ApiSession.NewEmail("mine"), keep: true);
        var theirs = await other.ApplyAsync(otherJob, ApiSession.NewEmail("theirs"), keep: true);

        var result = (await world.KeepAsync([mine, theirs])).Expect(HttpStatusCode.BadRequest);

        result.ProblemKeys().Should().Equal("applicationIds");
        (await world.DetailAsync(mine)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null, "the selection is refused as a whole");
        (await other.DetailAsync(theirs)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_sees_and_removes_only_people_kept_from_jobs_they_are_on()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (onTeam, _) = await world.OpenJobAsync("On my team");
        var (offTeam, _) = await world.OpenJobAsync("Not my team");
        var visible = await world.KeptPersonAsync(onTeam, "visible");
        var hidden = await world.KeptPersonAsync(offTeam, "hidden");
        using var recruiter = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter", jobAccess: "HiringTeamOnly");
        await world.Owner.SetHiringTeamAsync(onTeam, await world.Owner.MemberIdAsync(recruiter.UserId));

        var seenByRecruiter = await world.PoolCandidateIdsAsync(recruiter);

        seenByRecruiter.Should().Equal([visible.CandidateId], "the person kept from the job they are on is listed, the other is not");
        (await world.PoolCandidateIdsAsync()).Should().BeEquivalentTo([visible.CandidateId, hidden.CandidateId], "the owner sees both");
        (await recruiter.PostAsync($"/v1/talent-pool/{hidden.CandidateId}/remove", new JsonObject()))
            .Status.Should().Be(HttpStatusCode.NotFound, "an entry from a job they are not on does not exist for them");
        (await world.PoolCandidateIdsAsync()).Should().Contain(hidden.CandidateId);
    }

    [Fact]
    public async Task Removing_someone_takes_them_out_of_the_pool_and_keeps_their_permission()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Remove job");
        var person = await world.KeptPersonAsync(job, "removed");

        (await world.Owner.PostAsync($"/v1/talent-pool/{person.CandidateId}/remove", new JsonObject())).Expect(HttpStatusCode.NoContent);

        (await world.PoolAsync()).Should().BeEmpty();
        var detail = await world.DetailAsync(person.ApplicationId);
        detail.At("inTalentPool").GetBoolean().Should().BeFalse();
        detail.At("futureRolesUntil").ValueKind.Should().Be(JsonValueKind.String, "a person removing them is not the candidate withdrawing");
        var left = await world.EntryAsync(person.ApplicationId, "LeftTalentPool");
        left.GetProperty("poolExit").GetString().Should().Be("Removed");
        left.GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);
        (await world.Owner.PostAsync($"/v1/talent-pool/{person.CandidateId}/remove", new JsonObject()))
            .Status.Should().Be(HttpStatusCode.NotFound, "they are no longer in the pool");
    }

    [Fact]
    public async Task Removing_someone_at_their_request_also_withdraws_their_permission()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Asked job");
        var person = await world.KeptPersonAsync(job, "asked");

        (await world.Owner.PostAsync($"/v1/talent-pool/{person.CandidateId}/remove", new JsonObject { ["candidateAsked"] = true }))
            .Expect(HttpStatusCode.NoContent);

        (await world.PoolAsync()).Should().BeEmpty();
        var detail = await world.DetailAsync(person.ApplicationId);
        detail.At("futureRolesUntil").ValueKind.Should().Be(JsonValueKind.Null, "the permission is withdrawn");
        (await world.EntryAsync(person.ApplicationId, "LeftTalentPool")).GetProperty("poolExit").GetString().Should().Be("CandidateAsked");
        (await world.KeepAsync([person.ApplicationId])).Expect(HttpStatusCode.OK).At("alreadyEnded").GetInt32()
            .Should().Be(1, "the application is still kept in mind, so it cannot be kept again");
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("screening")]
    public async Task Moving_a_kept_application_to_a_stage_brings_it_back_to_the_job_and_out_of_the_pool(string stage)
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Back in job");
        var person = await world.KeptPersonAsync(job, "back");
        (await world.DetailAsync(person.ApplicationId)).Text("stage.id").Should().Be("applied");

        (await world.Owner.PostAsync($"/v1/applications/{person.ApplicationId}/move", new JsonObject { ["toStageId"] = stage })).Expect(HttpStatusCode.OK);

        var detail = await world.DetailAsync(person.ApplicationId);
        detail.At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
        detail.At("inTalentPool").GetBoolean().Should().BeFalse();
        detail.Text("stage.id").Should().Be(stage);
        (await world.PoolAsync()).Should().BeEmpty();
        (await world.EntryAsync(person.ApplicationId, "LeftTalentPool")).GetProperty("poolExit").GetString().Should().Be("BackInJob");
    }

    [Fact]
    public async Task The_stage_change_email_is_skipped_for_a_kept_application_and_still_queued_for_an_open_one()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Email job");
        var kept = await world.KeptPersonAsync(job, "keptmail");
        var open = await world.ApplyAsync(job, ApiSession.NewEmail("openmail"), keep: true);

        var queued = (await world.Owner.PostAsync("/v1/emails", new JsonObject
        {
            ["templateId"] = "stage-change",
            ["applicationIds"] = PoolWorld.Ids([kept.ApplicationId, open]),
        })).Expect(HttpStatusCode.Accepted);

        queued.At("queued").GetInt32().Should().Be(1);
        queued.At("skipped").GetInt32().Should().Be(1, "a kept application left the job, so it is not told it moved on");
    }
}
