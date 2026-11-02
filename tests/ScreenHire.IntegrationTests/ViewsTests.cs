using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Views people save on the candidate tables (<c>/v1/views</c>, docs/architecture/saved-views.md): private until shared,
/// changed only by their owner or a role that edits anyone's views, shown for a job only to members who see that job
/// (ADR-0015), deleted softly so Undo restores the same row, and kept per organization. The built-in views a member hides
/// are their own. Session only: an API key has none.
/// </summary>
public sealed class ViewsTests(AppHostFixture fixture)
{
    private const string Path = "/v1/views";

    private static JsonObject Candidates(string name, bool shared = false, JsonObject? filters = null) => new()
    {
        ["name"] = name,
        ["scope"] = "Candidates",
        ["jobId"] = null,
        ["shared"] = shared,
        ["filters"] = filters ?? Filters(),
    };

    private static JsonObject ForJob(Guid jobId, string name, bool shared = false) => new()
    {
        ["name"] = name,
        ["scope"] = "Job",
        ["jobId"] = jobId.ToString(),
        ["shared"] = shared,
        ["filters"] = Filters(stages: ["screening"]),
    };

    private static JsonObject Filters(string? baseView = null, string[]? stages = null, string? authenticity = null) => new()
    {
        ["baseView"] = baseView,
        ["jobIds"] = new JsonArray(),
        ["stages"] = new JsonArray([.. (stages ?? []).Select(s => JsonValue.Create(s))]),
        ["authenticity"] = authenticity,
        ["aiAssistance"] = null,
        ["tag"] = null,
        ["sortColumn"] = "applied",
        ["sortDescending"] = true,
    };

    private static string[] Names(ApiResult list) => [.. list.Body.GetProperty("views").EnumerateArray().Select(v => v.GetProperty("name").GetString()!)];

    [Fact]
    public async Task A_saved_view_is_private_to_its_owner_until_they_share_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");

        var created = (await owner.PostAsync(Path, Candidates("Screening, verify", filters: Filters("needs-review", ["Screening"], "ReviewSuggested"))))
            .Expect(HttpStatusCode.Created);
        created.At("mine").GetBoolean().Should().BeTrue();
        created.At("canEdit").GetBoolean().Should().BeTrue();
        created.At("filters.baseView").GetString().Should().Be("needs-review");
        created.At("filters.authenticity").GetString().Should().Be("ReviewSuggested");

        Names((await owner.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().Equal("Screening, verify");
        Names((await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().BeEmpty("a private view is its owner's only");

        (await owner.PatchAsync($"{Path}/{created.Id()}", new JsonObject { ["name"] = null, ["shared"] = true })).Expect(HttpStatusCode.OK);

        var seen = (await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK);
        Names(seen).Should().Equal("Screening, verify");
        var view = seen.Body.GetProperty("views")[0];
        view.GetProperty("mine").GetBoolean().Should().BeFalse();
        view.GetProperty("canEdit").GetBoolean().Should().BeFalse("only the owner or a role that edits anyone's views may change it");
        view.GetProperty("filters").GetProperty("stages")[0].GetString().Should().Be("Screening");

        (await owner.PatchAsync($"{Path}/{created.Id()}", new JsonObject { ["name"] = null, ["shared"] = false })).Expect(HttpStatusCode.OK);
        Names((await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().BeEmpty("making it private takes it back");
    }

    [Fact]
    public async Task Only_the_owner_or_a_role_that_edits_anyones_views_changes_a_shared_view()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var shared = (await recruiter.PostAsync(Path, Candidates("Team queue", shared: true))).Expect(HttpStatusCode.Created);
        var privateView = (await recruiter.PostAsync(Path, Candidates("Mine", shared: false))).Expect(HttpStatusCode.Created);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        (await viewer.PatchAsync($"{Path}/{shared.Id()}", new JsonObject { ["name"] = "Hijacked", ["shared"] = null })).Expect(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"{Path}/{shared.Id()}")).Expect(HttpStatusCode.Forbidden);
        (await viewer.PatchAsync($"{Path}/{privateView.Id()}", new JsonObject { ["name"] = "Hijacked", ["shared"] = null }))
            .Expect(HttpStatusCode.NotFound);
        (await admin.PatchAsync($"{Path}/{privateView.Id()}", new JsonObject { ["name"] = "Hijacked", ["shared"] = null }))
            .Expect(HttpStatusCode.NotFound);

        var renamed = (await admin.PatchAsync($"{Path}/{shared.Id()}", new JsonObject { ["name"] = "Team queue (shared)", ["shared"] = null }))
            .Expect(HttpStatusCode.OK);
        renamed.At("name").GetString().Should().Be("Team queue (shared)");
        renamed.At("mine").GetBoolean().Should().BeFalse("the Admin edited it, the recruiter still owns it");
        (await owner.DeleteAsync($"{Path}/{shared.Id()}")).Expect(HttpStatusCode.NoContent);
        Names((await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().Equal("Mine");
    }

    [Fact]
    public async Task A_deleted_view_comes_back_with_Undo_as_the_same_row_and_a_name_taken_meanwhile_refuses_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var view = (await owner.PostAsync(Path, Candidates("Overdue in Screening", shared: true))).Expect(HttpStatusCode.Created);

        (await admin.DeleteAsync($"{Path}/{view.Id()}")).Expect(HttpStatusCode.NoContent);
        Names((await owner.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().BeEmpty();
        (await owner.PatchAsync($"{Path}/{view.Id()}", new JsonObject { ["name"] = "Nope", ["shared"] = null })).Expect(HttpStatusCode.NotFound);

        var restored = (await admin.PostAsync($"{Path}/{view.Id()}/restore")).Expect(HttpStatusCode.OK);
        restored.Id().Should().Be(view.Id());
        var list = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        Names(list).Should().Equal("Overdue in Screening");
        list.Body.GetProperty("views")[0].GetProperty("mine").GetBoolean().Should().BeTrue("the owner is still the owner after an Admin's Undo");

        (await owner.DeleteAsync($"{Path}/{view.Id()}")).Expect(HttpStatusCode.NoContent);
        (await owner.PostAsync(Path, Candidates("overdue in screening"))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"{Path}/{view.Id()}/restore")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync($"{Path}/{Guid.NewGuid()}/restore")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_view_of_a_job_shows_only_to_members_who_see_that_job()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var colleague = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var shared = (await owner.PostAsync(Path, ForJob(job.Id(), "Screening", shared: true))).Expect(HttpStatusCode.Created);
        shared.At("jobId").GetGuid().Should().Be(job.Id());

        Names((await colleague.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().Equal("Screening"); // a member who sees every job sees its views
        Names((await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().BeEmpty("the job is not on their team, so neither is its view");
        (await recruiter.PatchAsync($"{Path}/{shared.Id()}", new JsonObject { ["name"] = "Mine now", ["shared"] = null })).Expect(HttpStatusCode.NotFound);
        (await recruiter.DeleteAsync($"{Path}/{shared.Id()}")).Expect(HttpStatusCode.NotFound);
        (await recruiter.PostAsync($"{Path}/{shared.Id()}/restore")).Expect(HttpStatusCode.NotFound);
        (await recruiter.PostAsync(Path, ForJob(job.Id(), "Mine"))).Expect(HttpStatusCode.BadRequest);

        await owner.SetHiringTeamAsync(job.Id(), await owner.MemberIdAsync(recruiter.UserId));

        Names((await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().Equal("Screening");
        (await recruiter.PostAsync(Path, ForJob(job.Id(), "Mine"))).Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Another_organization_sees_and_changes_nothing_of_ours()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var view = (await ours.PostAsync(Path, Candidates("Ours", shared: true))).Expect(HttpStatusCode.Created);

        Names((await theirs.GetAsync(Path)).Expect(HttpStatusCode.OK)).Should().BeEmpty();
        (await theirs.PatchAsync($"{Path}/{view.Id()}", new JsonObject { ["name"] = "Theirs", ["shared"] = null })).Expect(HttpStatusCode.NotFound);
        (await theirs.DeleteAsync($"{Path}/{view.Id()}")).Expect(HttpStatusCode.NotFound);
        (await theirs.PostAsync($"{Path}/{view.Id()}/restore")).Expect(HttpStatusCode.NotFound);
        (await theirs.PostAsync(Path, ForJob(Guid.NewGuid(), "Not our job"))).Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_view_needs_a_name_a_table_and_known_filters()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Support Engineer"));
        (await owner.PostAsync(Path, Candidates("Weekly"))).Expect(HttpStatusCode.Created);

        (await owner.PostAsync(Path, Candidates("   "))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("name");
        (await owner.PostAsync(Path, Candidates(new string('x', 81)))).Expect(HttpStatusCode.BadRequest);
        (await owner.PostAsync(Path, Candidates("WEEKLY"))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("name");

        var withJob = Candidates("Candidates with a job");
        withJob["jobId"] = job.Id().ToString();
        (await owner.PostAsync(Path, withJob)).Expect(HttpStatusCode.BadRequest);
        var jobWithoutJob = ForJob(job.Id(), "Job without a job");
        jobWithoutJob["jobId"] = null;
        (await owner.PostAsync(Path, jobWithoutJob)).Expect(HttpStatusCode.BadRequest);

        (await owner.PostAsync(Path, Candidates("Unknown base", filters: Filters("no-such-view")))).Expect(HttpStatusCode.BadRequest);
        var unknownJob = Candidates("Unknown job");
        unknownJob["filters"]!["jobIds"] = new JsonArray(JsonValue.Create(Guid.NewGuid().ToString()));
        (await owner.PostAsync(Path, unknownJob)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("filters.jobIds");
        (await owner.PostAsync(Path, Candidates("Unknown level", filters: Filters(authenticity: "Fraud")))).Expect(HttpStatusCode.BadRequest);
        (await owner.PostAsync(Path, Candidates("Numeric level", filters: Filters(authenticity: "1")))).Expect(HttpStatusCode.BadRequest);
        (await owner.PostAsync(Path, Candidates("Pending checks", filters: Filters(authenticity: "Pending")))).Expect(HttpStatusCode.Created);

        var rename = (await owner.PatchAsync($"{Path}/{Guid.NewGuid()}", new JsonObject { ["name"] = null, ["shared"] = null }));
        rename.Status.Should().Be(HttpStatusCode.BadRequest, "a change that changes nothing is bad input");
    }

    [Fact]
    public async Task A_removed_members_private_views_go_with_them_and_their_shared_ones_stay()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        (await recruiter.PostAsync(Path, Candidates("Private notes"))).Expect(HttpStatusCode.Created);
        (await recruiter.PostAsync(Path, Candidates("Team queue", shared: true))).Expect(HttpStatusCode.Created);

        var memberId = await owner.MemberIdAsync(recruiter.UserId);
        (await owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NoContent);

        var views = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        Names(views).Should().Equal("Team queue");
        views.Body.GetProperty("views")[0].GetProperty("canEdit").GetBoolean().Should().BeTrue("an Owner edits anyone's views");
    }

    [Fact]
    public async Task A_member_hides_built_in_views_for_themselves_only()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK).Body.GetProperty("hiddenBuiltIns").GetArrayLength().Should().Be(0);

        var hidden = (await owner.PutAsync($"{Path}/hidden-built-ins", HiddenViews("overdue", "new-today"))).Expect(HttpStatusCode.OK);
        hidden.Body.GetProperty("hiddenBuiltIns").EnumerateArray().Select(v => v.GetString()).Should().Equal("overdue", "new-today");

        (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK).Body.GetProperty("hiddenBuiltIns").GetArrayLength().Should().Be(2);
        (await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK).Body.GetProperty("hiddenBuiltIns").GetArrayLength().Should().Be(0);

        (await owner.PutAsync($"{Path}/hidden-built-ins", HiddenViews("nothing-like-it"))).Expect(HttpStatusCode.BadRequest);
        (await owner.PutAsync($"{Path}/hidden-built-ins", HiddenViews("overdue", "overdue"))).Expect(HttpStatusCode.BadRequest);
        (await owner.PutAsync($"{Path}/hidden-built-ins", HiddenViews())).Expect(HttpStatusCode.OK)
            .Body.GetProperty("hiddenBuiltIns").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task An_API_key_has_no_views()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await owner.CreateKeyAsync("Assistant", ["Read"]);
        using var client = new KeyClient(fixture, key.Text("token"));

        var response = await client.GetAsync(Path);

        response.Status.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static JsonObject HiddenViews(params string[] ids) => new() { ["hiddenBuiltIns"] = new JsonArray([.. ids.Select(id => JsonValue.Create(id))]) };
}
