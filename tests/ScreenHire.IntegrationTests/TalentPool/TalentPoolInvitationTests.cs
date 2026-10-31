using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// Inviting people in the talent pool to apply for another job, through the API, the worker and Mailpit: the email the
/// candidate gets, the copy the team keeps, the timeline, and the refusals.
/// </summary>
public sealed class TalentPoolInvitationTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invitation_emails_the_job_link_and_a_link_to_stop_being_kept_while_the_teams_copy_holds_no_token()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (keptFrom, _) = await world.OpenJobAsync("Kept from here");
        var (target, targetSlug) = await world.OpenJobAsync("Invited to here");
        var person = await world.KeptPersonAsync(keptFrom, "invited");

        var queued = (await world.InviteAsync([person.CandidateId], target)).Expect(HttpStatusCode.Accepted);

        queued.At("queued").GetInt32().Should().Be(1);
        queued.At("skipped").GetInt32().Should().Be(0);
        var text = await world.WaitForInvitationAsync(person.Email);
        text.Should().Contain($"/jobs/{targetSlug}", "the email links to the job's careers page");
        var (board, token) = PoolWorld.RemoveLinkIn(text);
        board.Should().Contain(world.Slug);

        await world.WaitForTimelineAsync(person.ApplicationId, "InvitedToApply", "EmailSent");
        var invited = await world.EntryAsync(person.ApplicationId, "InvitedToApply");
        invited.GetProperty("otherJobId").GetGuid().Should().Be(target);
        invited.GetProperty("actorUserId").GetGuid().Should().Be(world.Owner.UserId);
        var sent = await world.EntryAsync(person.ApplicationId, "EmailSent");
        var copy = (await world.Owner.GetAsync($"/v1/emails/{sent.GetProperty("emailId").GetGuid()}")).Expect(HttpStatusCode.OK);
        copy.Text("text").Should().Contain("[the candidate's link, not kept in this copy]").And.NotContain(token).And.NotContain("/talent-pool/");
        copy.Text("html").Should().NotContain(token).And.NotContain("/talent-pool/");
        copy.Text("text").Should().Contain($"/jobs/{targetSlug}", "the job link is public and stays in the copy");

        // The worker commits the pool entry's invitation with the timeline entries.
        var listed = (await world.PoolAsync()).Single();
        listed.GetProperty("lastInvitedJobTitle").GetString().Should().Be("Invited to here");
        listed.GetProperty("lastInvitedJobId").GetGuid().Should().Be(target);
    }

    [Fact]
    public async Task Inviting_to_a_draft_or_a_closed_job_is_a_conflict_and_nothing_is_sent()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (keptFrom, _) = await world.OpenJobAsync("Kept from");
        var draft = await world.DraftJobAsync("Still a draft");
        var closed = await world.ClosedJobAsync("Already closed");
        var person = await world.KeptPersonAsync(keptFrom, "refused");

        (await world.InviteAsync([person.CandidateId], draft)).Status.Should().Be(HttpStatusCode.Conflict);
        (await world.InviteAsync([person.CandidateId], closed)).Status.Should().Be(HttpStatusCode.Conflict);

        (await world.InviteAsync([person.CandidateId], Guid.NewGuid())).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("jobId");
        (await world.TimelineAsync(person.ApplicationId)).Select(e => e.GetProperty("type").GetString()).Should().NotContain("InvitedToApply");
    }

    [Fact]
    public async Task A_candidate_who_already_applied_to_the_job_is_skipped_and_gets_no_invitation()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (keptFrom, _) = await world.OpenJobAsync("Origin");
        var (target, _) = await world.OpenJobAsync("Already applied here");
        var email = ApiSession.NewEmail("applied");
        var origin = await world.ApplyAsync(keptFrom, email, keep: true);
        await world.ApplyAsync(target, email, keep: false);
        (await world.KeepAsync([origin])).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(1);
        var candidate = (await world.DetailAsync(origin)).Id("candidate.id");

        var queued = (await world.InviteAsync([candidate], target)).Expect(HttpStatusCode.Accepted);

        queued.At("queued").GetInt32().Should().Be(0);
        queued.At("skipped").GetInt32().Should().Be(1);
        (await world.PoolAsync()).Single().GetProperty("appliedJobIds").EnumerateArray().Select(j => j.GetGuid())
            .Should().BeEquivalentTo([keptFrom, target]);
    }

    [Fact]
    public async Task A_viewer_cannot_invite_anyone()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (keptFrom, _) = await world.OpenJobAsync("Origin");
        var (target, _) = await world.OpenJobAsync("Target");
        var person = await world.KeptPersonAsync(keptFrom, "viewerinvite");
        using var viewer = await fixture.InviteAndJoinAsync(world.Owner, "Viewer");

        (await world.InviteAsync([person.CandidateId], target, by: viewer)).Status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Adding_a_pooled_candidate_to_another_job_keeps_the_original_permission_clock_and_takes_them_out_of_the_pool()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (keptFrom, _) = await world.OpenJobAsync("Origin");
        var (target, _) = await world.OpenJobAsync("Added to");
        var person = await world.KeptPersonAsync(keptFrom, "added");
        var original = await world.DetailAsync(person.ApplicationId);

        var added = (await world.Owner.PostAsync("/v1/talent-pool/applications", new JsonObject
        {
            ["candidateIds"] = PoolWorld.Ids([person.CandidateId]),
            ["jobId"] = target,
        })).Expect(HttpStatusCode.OK);

        added.At("skipped").GetInt32().Should().Be(0);
        added.Count("applicationIds").Should().Be(1);
        var newApplication = added.Id("applicationIds.0");
        var created = await world.DetailAsync(newApplication);
        created.Text("source").Should().Be("Recruiter");
        created.Id("jobId").Should().Be(target);
        created.Id("candidate.id").Should().Be(person.CandidateId);
        created.At("futureRolesGivenAt").GetDateTimeOffset().Should().Be(
            original.At("futureRolesGivenAt").GetDateTimeOffset(), "the candidate's clock does not restart when they are added to a job");
        created.At("futureRolesUntil").GetDateTimeOffset().Should().Be(original.At("futureRolesUntil").GetDateTimeOffset());
        (await world.PoolAsync()).Should().BeEmpty();
        (await world.DetailAsync(person.ApplicationId)).At("inTalentPool").GetBoolean().Should().BeFalse();
        var left = await world.EntryAsync(person.ApplicationId, "LeftTalentPool");
        left.GetProperty("poolExit").GetString().Should().Be("AddedToJob");
        left.GetProperty("otherApplicationId").GetGuid().Should().Be(newApplication);
        left.GetProperty("otherJobId").GetGuid().Should().Be(target);
    }

    [Fact]
    public async Task Adding_to_a_closed_job_is_a_conflict_and_a_candidate_who_already_applied_is_skipped()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (origin, _) = await world.OpenJobAsync("Origin");
        var (alreadyApplied, _) = await world.OpenJobAsync("Applied already");
        var closed = await world.ClosedJobAsync("Closed job");
        var email = ApiSession.NewEmail("addskip");
        var originApplication = await world.ApplyAsync(origin, email, keep: true);
        var existing = await world.ApplyAsync(alreadyApplied, email, keep: false);
        (await world.KeepAsync([originApplication])).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(1);
        var candidate = (await world.DetailAsync(originApplication)).Id("candidate.id");

        (await world.Owner.PostAsync("/v1/talent-pool/applications", new JsonObject
        {
            ["candidateIds"] = PoolWorld.Ids([candidate]),
            ["jobId"] = closed,
        })).Status.Should().Be(HttpStatusCode.Conflict);
        var skipped = (await world.Owner.PostAsync("/v1/talent-pool/applications", new JsonObject
        {
            ["candidateIds"] = PoolWorld.Ids([candidate]),
            ["jobId"] = alreadyApplied,
        })).Expect(HttpStatusCode.OK);

        skipped.Count("applicationIds").Should().Be(0);
        skipped.At("skipped").GetInt32().Should().Be(1);
        (await world.PoolCandidateIdsAsync()).Should().Equal([candidate], "nothing was added, so they stay in the pool");
        (await world.DetailAsync(existing)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_pooled_candidate_who_applies_again_on_the_careers_site_leaves_the_pool()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (origin, _) = await world.OpenJobAsync("Origin");
        var (other, _) = await world.OpenJobAsync("Applied again here");
        var person = await world.KeptPersonAsync(origin, "reapply");

        var again = await world.ApplyAsync(other, person.Email, keep: false);

        (await world.PoolAsync()).Should().BeEmpty();
        (await world.DetailAsync(person.ApplicationId)).At("inTalentPool").GetBoolean().Should().BeFalse();
        var left = await world.EntryAsync(person.ApplicationId, "LeftTalentPool");
        left.GetProperty("poolExit").GetString().Should().Be("AppliedAgain");
        left.GetProperty("otherApplicationId").GetGuid().Should().Be(again);
        left.GetProperty("otherJobId").GetGuid().Should().Be(other);
    }
}

/// <summary>
/// The careers page behind the link in an invitation (anonymous): a candidate stops the organization keeping their
/// details, and a forged token or another organization's slug finds nothing.
/// </summary>
public sealed class PublicTalentPoolLinkTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_link_in_an_invitation_shows_the_status_and_stopping_removes_the_person_and_their_permission()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (origin, _) = await world.OpenJobAsync("Origin");
        var (target, _) = await world.OpenJobAsync("Target");
        var person = await world.KeptPersonAsync(origin, "stop");
        (await world.InviteAsync([person.CandidateId], target)).Expect(HttpStatusCode.Accepted);
        var (_, token) = PoolWorld.RemoveLinkIn(await world.WaitForInvitationAsync(person.Email));
        var path = world.PublicPath(token);

        var before = await world.Careers.GetFromJsonAsync<JsonElement>(path, Ct);
        before.At("kept").GetBoolean().Should().BeTrue();
        before.At("organization.slug").GetString().Should().Be(world.Slug);

        (await world.Careers.PostAsync(path, null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await world.PoolAsync()).Should().BeEmpty();
        (await world.Careers.GetFromJsonAsync<JsonElement>(path, Ct)).At("kept").GetBoolean().Should().BeFalse();
        (await world.DetailAsync(person.ApplicationId)).At("futureRolesUntil").ValueKind.Should().Be(JsonValueKind.Null);
        (await world.EntryAsync(person.ApplicationId, "LeftTalentPool")).GetProperty("poolExit").GetString().Should().Be("CandidateAsked");
        (await world.Careers.PostAsync(path, null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent, "stopping twice changes nothing");
        (await world.TimelineAsync(person.ApplicationId)).Count(e => e.GetProperty("type").GetString() == "LeftTalentPool")
            .Should().Be(1);
    }

    [Fact]
    public async Task A_forged_token_and_a_token_under_another_organizations_slug_are_not_found_and_change_nothing()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (origin, _) = await world.OpenJobAsync("Origin");
        var (target, _) = await world.OpenJobAsync("Target");
        var person = await world.KeptPersonAsync(origin, "forged");
        (await world.InviteAsync([person.CandidateId], target)).Expect(HttpStatusCode.Accepted);
        var (_, token) = PoolWorld.RemoveLinkIn(await world.WaitForInvitationAsync(person.Email));
        var elsewhere = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var forged = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        foreach (var path in new[] { world.PublicPath(forged), world.PublicPath("not-a-token"), world.PublicPath(token, elsewhere.Slug) })
        {
            (await world.Careers.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            (await world.Careers.PostAsync(path, null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        }

        (await world.PoolCandidateIdsAsync()).Should().Equal(person.CandidateId);
        (await world.DetailAsync(person.ApplicationId)).At("futureRolesUntil").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task The_preview_shows_the_invitation_for_someone_in_the_pool_and_nobody_else()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (keptFrom, _) = await world.OpenJobAsync("Previewed from");
        var (target, targetSlug) = await world.OpenJobAsync("Previewed for");
        var person = await world.KeptPersonAsync(keptFrom, "previewed");
        var applicant = await world.ApplyAsync(keptFrom, ApiSession.NewEmail("notkept"), keep: true);
        var notKept = (await world.DetailAsync(applicant)).Id("candidate.id");

        var preview = (await PreviewAsync(world, person.CandidateId, target)).Expect(HttpStatusCode.OK);
        preview.Text("text").Should().Contain($"/jobs/{targetSlug}").And.Contain("[the candidate's link, not kept in this copy]");

        (await PreviewAsync(world, notKept, target)).Expect(HttpStatusCode.NotFound); // outside the pool, so no invitation to preview
    }

    private static Task<ApiResult> PreviewAsync(PoolWorld world, Guid candidateId, Guid jobId) =>
        world.Owner.PostAsync("/v1/talent-pool/invitations/preview", new JsonObject { ["candidateId"] = candidateId, ["jobId"] = jobId });
}
