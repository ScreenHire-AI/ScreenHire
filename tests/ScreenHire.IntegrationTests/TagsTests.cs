using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Realtime;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The organization's shared tags (docs/architecture/tags.md): the list with how many applications use each, under
/// row-level security and job scope, a tag added in another case reusing the existing one, and Settings, Tags' rename,
/// merge and delete, which reach every application in batches (the test stack's batch is two) and the jobs' pipelines,
/// the template drafts and the saved views, never a published version.
/// </summary>
public sealed class TagsTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_counts_each_tag_most_used_first_reuses_its_spelling_and_shows_no_other_organization_or_job()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await JobAsync(owner);
        var first = await ApplicationAsync(owner, job);
        var second = await ApplicationAsync(owner, job);
        await TagAsync(owner, first, "Senior");
        await TagAsync(owner, first, "Remote");
        var reused = await TagAsync(owner, second, " senior ");
        using var other = await fixture.SignedInWithOrganizationAsync();
        await TagAsync(other, await ApplicationAsync(other, await JobAsync(other)), "Confidential");
        using var narrow = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");

        reused.At("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal(["Senior"], "a tag that differs only by case is the existing one");
        (await TagsAsync(owner)).Should().Equal(("Senior", 2), ("Remote", 1));
        (await TagsAsync(other)).Should().Equal(("Confidential", 1));
        (await TagsAsync(narrow)).Should().BeEmpty("a member on only their own jobs counts the applications they can see");
    }

    [Fact]
    public async Task A_rename_reaches_every_application_in_batches_and_the_jobs_drafts_and_views_but_not_a_published_version()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (templateId, versionId) = await owner.CreatePublishedTemplateAsync("Engineering", Samples.DefinitionWith(TagSeniors("Sr")));
        (await owner.PostAsync($"/v1/templates/{templateId}/versions", new JsonObject())).Expect(HttpStatusCode.Created);
        var job = await JobAsync(owner, versionId);
        Guid[] applications = [await ApplicationAsync(owner, job), await ApplicationAsync(owner, job), await ApplicationAsync(owner, job)];
        foreach (var application in applications)
        {
            await TagAsync(owner, application, "Sr");
        }

        var view = (await owner.PostAsync("/v1/views", View("sr"))).Expect(HttpStatusCode.Created).Id();
        await using var events = await EventsConnection.ConnectAsync(owner.Session, Ct);

        var renamed = (await owner.PostAsync("/v1/tags/rename", new JsonObject { ["tag"] = "sr", ["newName"] = " Senior " })).Expect(HttpStatusCode.OK);

        (renamed.At("jobs").GetInt32(), renamed.At("drafts").GetInt32(), renamed.At("views").GetInt32()).Should().Be((1, 1, 1));
        renamed.Id("stillNamedBy.0.templateId").Should().Be(templateId);
        renamed.At("stillNamedBy.0.version").GetInt32().Should().Be(1);
        foreach (var application in applications)
        {
            var changed = await events.WaitForAsync("ApplicationTagsChanged", "applicationId", application, Ct);
            changed.GetProperty("actorUserId").GetGuid().Should().Be(owner.UserId);
        }

        await Wait.UntilAsync(async () => (await TagsAsync(owner)).Contains(("Senior", 3)), "every batch of applications to be renamed", Ct);
        (await TagsAsync(owner)).Should().Equal(
            [("Senior", 3), ("Lead", 0), ("Sr", 0)], "the published version still names the old tag, with no application left");
        foreach (var application in applications)
        {
            (await owner.GetAsync($"/v1/applications/{application}")).At("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("Senior");
            (await TagEventsAsync(owner, application)).Should().Equal(("TagAdded", "Sr"), ("TagRemoved", "Sr"), ("TagAdded", "Senior"));
        }

        var workflow = (await owner.GetAsync($"/v1/jobs/{job}")).At("workflow.automations.0");
        workflow.At("actions.0.tag").GetString().Should().Be("Senior");
        workflow.At("conditions.rules.0.values").EnumerateArray().Select(v => v.GetString()).Should().Equal("Senior", "Lead");
        (await Samples.DefinitionOfAsync(owner, templateId, 2))["workflow"]!["automations"]![0]!["actions"]![0]!["tag"]!.GetValue<string>().Should().Be("Senior");
        (await Samples.DefinitionOfAsync(owner, templateId, 1))["workflow"]!["automations"]![0]!["actions"]![0]!["tag"]!.GetValue<string>().Should().Be("Sr");
        (await owner.GetAsync("/v1/views")).At("views").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == view)
            .At("filters.tag").GetString().Should().Be("Senior");
    }

    [Fact]
    public async Task A_merge_combines_two_tags_a_delete_takes_one_off_and_a_rename_onto_another_tag_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await JobAsync(owner);
        var both = await ApplicationAsync(owner, job);
        var one = await ApplicationAsync(owner, job);
        await TagAsync(owner, both, "Sr");
        await TagAsync(owner, both, "Senior");
        await TagAsync(owner, one, "Sr");
        await TagAsync(owner, one, "Remote");

        (await owner.PostAsync("/v1/tags/rename", new JsonObject { ["tag"] = "Sr", ["newName"] = "senior" })).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync("/v1/tags/merge", new JsonObject { ["tag"] = "Sr", ["into"] = "Principal" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("into");
        (await owner.PostAsync("/v1/tags/merge", new JsonObject { ["tag"] = "Lead", ["into"] = "Senior" })).Expect(HttpStatusCode.NotFound);

        (await owner.PostAsync("/v1/tags/merge", new JsonObject { ["tag"] = "sr", ["into"] = "SENIOR" })).Expect(HttpStatusCode.OK);
        await Wait.UntilAsync(async () => (await TagsAsync(owner)).SequenceEqual([("Senior", 2), ("Remote", 1)]), "the merge to reach both applications", Ct);
        (await TagEventsAsync(owner, both)).Should().EndWith([("TagRemoved", "Sr")], "an application that had both keeps the one");

        (await owner.DeleteAsync("/v1/tags?tag=senior")).Expect(HttpStatusCode.OK);
        await Wait.UntilAsync(async () => (await TagsAsync(owner)).SequenceEqual([("Remote", 1)]), "the delete to reach both applications", Ct);
        (await owner.GetAsync($"/v1/applications/{one}")).At("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("Remote");
    }

    [Fact]
    public async Task Only_someone_with_edit_settings_changes_a_tag_and_never_another_organizations()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner, await JobAsync(owner));
        await TagAsync(owner, application, "Referral");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var other = await fixture.SignedInWithOrganizationAsync();

        (await recruiter.GetAsync("/v1/tags")).Expect(HttpStatusCode.OK).Count("tags").Should().Be(1, "every member reads the tags to pick one");
        (await recruiter.PostAsync("/v1/tags/rename", new JsonObject { ["tag"] = "Referral", ["newName"] = "Referred" })).Expect(HttpStatusCode.Forbidden);
        (await recruiter.DeleteAsync("/v1/tags?tag=Referral")).Expect(HttpStatusCode.Forbidden);
        (await other.PostAsync("/v1/tags/rename", new JsonObject { ["tag"] = "Referral", ["newName"] = "Referred" })).Expect(HttpStatusCode.NotFound);
        (await other.DeleteAsync("/v1/tags?tag=Referral")).Expect(HttpStatusCode.NotFound);

        (await TagsAsync(owner)).Should().Equal(("Referral", 1));
    }

    private static async Task<List<(string Name, int Applications)>> TagsAsync(Tenant tenant) =>
        [.. (await tenant.GetAsync("/v1/tags")).Expect(HttpStatusCode.OK).At("tags").EnumerateArray()
            .Select(t => (t.GetProperty("name").GetString()!, t.GetProperty("applications").GetInt32()))];

    private static async Task<List<(string Type, string Tag)>> TagEventsAsync(Tenant tenant, Guid application) =>
        [.. (await tenant.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() is "TagAdded" or "TagRemoved")
            .Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("tag").GetString()!))];

    private static async Task<ApiResult> TagAsync(Tenant tenant, Guid application, string tag) =>
        (await tenant.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = tag })).Expect(HttpStatusCode.OK);

    private static async Task<Guid> JobAsync(Tenant tenant, Guid? templateVersionId = null)
    {
        var body = Samples.RemoteJob("Platform Engineer");
        if (templateVersionId is { } version)
        {
            body["templateVersionId"] = version;
        }

        var job = (await tenant.CreateJobAsync(body)).Id();
        await tenant.PublishJobAsync(job);
        return job;
    }

    private static async Task<Guid> ApplicationAsync(Tenant tenant, Guid job) =>
        (await tenant.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();

    // A manual automation that names the tag twice: in a condition and in its Add a tag step.
    private static JsonObject TagSeniors(string tag) => new()
    {
        ["id"] = "tag-seniors",
        ["name"] = "Tag seniors",
        ["trigger"] = new JsonObject { ["type"] = "Manual" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = "application.tags", ["operator"] = "In", ["values"] = new JsonArray(tag, "Lead") }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = tag }),
    };

    private static JsonObject View(string tag) => new()
    {
        ["name"] = "Seniors",
        ["scope"] = "Candidates",
        ["jobId"] = null,
        ["shared"] = false,
        ["filters"] = new JsonObject
        {
            ["baseView"] = null,
            ["jobIds"] = new JsonArray(),
            ["stages"] = new JsonArray(),
            ["authenticity"] = null,
            ["aiAssistance"] = null,
            ["tag"] = tag,
            ["sortColumn"] = null,
            ["sortDescending"] = false,
        },
    };
}
