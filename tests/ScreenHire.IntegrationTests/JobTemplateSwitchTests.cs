using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.TalentPool;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Switching a job to a different template (workflow-engine.md section 7, Tailoring a job):
/// <c>GET /v1/jobs/{jobId}/workflow/switch-template</c> previews the stages and the suggested mapping, and
/// <c>POST /v1/jobs/{jobId}/workflow/switch-template</c> replaces the pipeline and moves the candidates as mapped, with no
/// email and no automation. Each test has its own organization.
/// </summary>
public sealed class JobTemplateSwitchTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Switching_moves_each_candidate_as_mapped_with_a_pipeline_entry_and_keeps_saved_candidates_saved()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var owner = world.Owner;
        var (job, _) = await world.OpenJobAsync("Line cook");
        var waiting = await world.ApplyAsync(job, ApiSession.NewEmail("waiting"), keep: false);
        var screened = await world.ApplyAsync(job, ApiSession.NewEmail("screened"), keep: false);
        var saved = await world.ApplyAsync(job, ApiSession.NewEmail("saved"), keep: true);
        await MoveAsync(owner, screened, "screening");
        await MoveAsync(owner, saved, "interview");
        (await owner.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = new JsonArray(JsonValue.Create(saved)) })).Expect(HttpStatusCode.OK);
        var (kitchen, version) = await KitchenTemplateAsync(owner);

        var preview = (await owner.GetAsync($"/v1/jobs/{job}/workflow/switch-template?templateVersionId={version}")).Expect(HttpStatusCode.OK);

        preview.Text("templateName").Should().Be("Kitchen");
        preview.At("expectedWorkflowVersion").GetInt32().Should().Be(1);
        Ids(preview.At("stages"), "id").Should().Equal("new", "call", "trial", "hired", "no", "withdrawn");
        Ids(preview.At("occupied"), "stageId").Should().Equal(["applied", "screening", "interview"], "the stages that hold candidates, a saved one included, in pipeline order");
        Ids(preview.At("occupied"), "suggestedStageId").Should().Equal(["new", "call", "trial"], "each is preselected by its kind");
        preview.At("occupied.0.candidates").GetInt32().Should().Be(1);

        // The person changes one choice: the saved candidate goes to the call rather than the trial shift.
        var switched = (await owner.PostAsync($"/v1/jobs/{job}/workflow/switch-template",
            SwitchRequest(version, 1, ("applied", "new"), ("screening", "call"), ("interview", "call")))).Expect(HttpStatusCode.OK);

        switched.At("workflowVersion").GetInt32().Should().Be(2);
        switched.Text("template.templateId").Should().Be(kitchen.ToString());
        switched.Text("template.versionId").Should().Be(version.ToString());
        Ids(switched.At("workflow.stages"), "id").Should().Equal("new", "call", "trial", "hired", "no", "withdrawn");
        Ids(switched.At("former.stages"), "id").Should().Contain(["applied", "screening", "interview"], "the old stages keep their names for the timelines");
        (await StageOfAsync(owner, waiting)).Should().Be("new");
        (await StageOfAsync(owner, screened)).Should().Be("call");
        var savedDetail = (await owner.GetAsync($"/v1/applications/{saved}")).Expect(HttpStatusCode.OK);
        savedDetail.Text("stage.id").Should().Be("call");
        savedDetail.At("keptInMindAt").ValueKind.Should().NotBe(JsonValueKind.Null, "a pipeline change is not a person bringing the candidate back");

        var entry = await LastEntryAsync(owner, screened);
        entry.GetProperty("type").GetString().Should().Be("StageChanged");
        entry.GetProperty("fromStageId").GetString().Should().Be("screening");
        entry.GetProperty("toStageId").GetString().Should().Be("call");
        entry.GetProperty("templateVersionId").GetString().Should().Be(version.ToString());
        entry.GetProperty("actorUserId").GetString().Should().Be(owner.UserId.ToString());
        (await LastEntryAsync(owner, waiting)).GetProperty("templateVersionId").GetString().Should().Be(version.ToString(), "every candidate's timeline says the pipeline changed");

        var history = (await owner.GetAsync($"/v1/jobs/{job}/history")).Expect(HttpStatusCode.OK);
        history.Text("entries.0.lines.0.text").Should().Be("switched to the template Kitchen");
    }

    [Fact]
    public async Task The_moves_send_no_email_and_run_no_automation_while_a_move_by_hand_still_does()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Prep cook"))).Id();
        var switchedEmail = ApiSession.NewEmail("switched");
        var movedEmail = ApiSession.NewEmail("moved");
        var switched = (await owner.CreateApplicationAsync(job, switchedEmail, "Sam Okafor")).Id();
        var moved = (await owner.CreateApplicationAsync(job, movedEmail, "Lee Moreno")).Id();
        await MoveAsync(owner, switched, "screening");
        var (_, version) = await KitchenTemplateAsync(owner, AutomationSamples.EmailOnStage("call", "stage-change"));

        (await owner.PostAsync($"/v1/jobs/{job}/workflow/switch-template",
            SwitchRequest(version, 1, ("applied", "new"), ("screening", "call")))).Expect(HttpStatusCode.OK);
        (await StageOfAsync(owner, switched)).Should().Be("call");

        // The same stage entered by hand afterwards runs the automation and sends its email, so the worker is up and the
        // automation works: the switched candidate got neither because the switch raised no move.
        await MoveAsync(owner, moved, "call");
        await AutomationSamples.WaitForRunAsync(owner, moved, "email-on-stage", "Completed");
        (await fixture.Mailpit.WaitForMessagesAsync(movedEmail, 1, Ct)).Should().ContainSingle();

        (await owner.GetAsync($"/v1/applications/{switched}/automation-runs")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        (await fixture.Mailpit.MessagesAsync(switchedEmail, Ct)).Should().BeEmpty("a pipeline change emails nobody");
    }

    [Fact]
    public async Task A_stale_version_a_closed_job_and_a_candidate_left_without_a_stage_are_refused_and_change_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Dishwasher"))).Id();
        var screened = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("screened"))).Id();
        await MoveAsync(owner, screened, "screening");
        var closed = (await owner.CreateJobAsync(Samples.RemoteJob("Closed kitchen"))).Id();
        await owner.PublishJobAsync(closed);
        await owner.CloseJobAsync(closed);
        var (_, version) = await KitchenTemplateAsync(owner);

        var stale = await owner.PostAsync($"/v1/jobs/{job}/workflow/switch-template", SwitchRequest(version, 2, ("screening", "call")));
        var onClosed = await owner.PostAsync($"/v1/jobs/{closed}/workflow/switch-template", SwitchRequest(version, 1));
        var unmapped = await owner.PostAsync($"/v1/jobs/{job}/workflow/switch-template", SwitchRequest(version, 1));
        var nowhere = await owner.PostAsync($"/v1/jobs/{job}/workflow/switch-template", SwitchRequest(version, 1, ("screening", "screening")));

        stale.Expect(HttpStatusCode.Conflict);
        onClosed.Expect(HttpStatusCode.Conflict);
        unmapped.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("stageMappings");
        unmapped.Text("errors.stageMappings.0").Should().StartWith("Screening has 1 candidate.");
        nowhere.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("stageMappings");
        var stored = (await owner.GetAsync($"/v1/jobs/{job}")).Expect(HttpStatusCode.OK);
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.At("template").ValueKind.Should().Be(JsonValueKind.Null);
        (await StageOfAsync(owner, screened)).Should().Be("screening");
        (await LastEntryAsync(owner, screened)).TryGetProperty("templateVersionId", out var marker).Should().BeTrue();
        marker.ValueKind.Should().Be(JsonValueKind.Null, "no pipeline entry was written");
    }

    [Fact]
    public async Task Another_organizations_job_is_not_found_and_its_template_cannot_be_switched_to()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Pastry chef"))).Id();
        var (_, ownVersion) = await KitchenTemplateAsync(owner);
        var (_, otherVersion) = await KitchenTemplateAsync(other);

        var preview = await other.GetAsync($"/v1/jobs/{job}/workflow/switch-template?templateVersionId={otherVersion}");
        var post = await other.PostAsync($"/v1/jobs/{job}/workflow/switch-template", SwitchRequest(otherVersion, 1));
        var theirTemplate = await owner.PostAsync($"/v1/jobs/{job}/workflow/switch-template", SwitchRequest(otherVersion, 1));
        var theirPreview = await owner.GetAsync($"/v1/jobs/{job}/workflow/switch-template?templateVersionId={otherVersion}");

        preview.Status.Should().Be(HttpStatusCode.NotFound);
        post.Status.Should().Be(HttpStatusCode.NotFound);
        theirTemplate.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateVersionId");
        theirPreview.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateVersionId");
        (await owner.GetAsync($"/v1/jobs/{job}")).At("workflowVersion").GetInt32().Should().Be(1);
        ownVersion.Should().NotBe(otherVersion);
    }

    /// <summary>A published template "Kitchen" whose stages differ from the default pipeline's, with these automations.</summary>
    private static async Task<(Guid TemplateId, Guid VersionId)> KitchenTemplateAsync(Tenant tenant, params JsonObject[] automations)
    {
        var definition = Samples.DefinitionWith(automations);
        definition["workflow"]!["stages"] = new JsonArray(
            Stage("new", "New", "Applied"),
            Stage("call", "Phone call", "Screening"),
            Stage("trial", "Trial shift", "Interview"),
            Stage("hired", "Hired", "Hired"),
            Stage("no", "Not this time", "Rejected"),
            Stage("withdrawn", "Withdrawn", "Withdrawn"));
        return await tenant.CreatePublishedTemplateAsync("Kitchen", definition);
    }

    private static JsonObject Stage(string id, string name, string kind) => new() { ["id"] = id, ["name"] = name, ["kind"] = kind };

    private static JsonObject SwitchRequest(Guid versionId, int expectedWorkflowVersion, params (string From, string To)[] mappings) => new()
    {
        ["templateVersionId"] = versionId,
        ["expectedWorkflowVersion"] = expectedWorkflowVersion,
        ["stageMappings"] = new JsonArray([.. mappings.Select(m => (JsonNode)new JsonObject { ["fromStageId"] = m.From, ["toStageId"] = m.To })]),
    };

    private static async Task MoveAsync(Tenant tenant, Guid application, string stageId) =>
        (await tenant.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = stageId })).Expect(HttpStatusCode.OK);

    private static async Task<string> StageOfAsync(Tenant tenant, Guid application) =>
        (await tenant.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK).Text("stage.id");

    private static async Task<JsonElement> LastEntryAsync(Tenant tenant, Guid application)
    {
        var timeline = (await tenant.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body;
        return timeline.EnumerateArray().Last(e => e.GetProperty("type").GetString() is "StageChanged" or "Submitted");
    }

    private static List<string> Ids(JsonElement array, string member) =>
        [.. array.EnumerateArray().Select(e => e.GetProperty(member).GetString()!)];
}
