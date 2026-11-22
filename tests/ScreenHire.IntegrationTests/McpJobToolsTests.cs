using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The job tools at <c>/v1/mcp</c> (docs/architecture/mcp-server.md): an assistant reads a job and its pipeline, drafts
/// and edits one, sets its questions and stages, and publishes it. Each tool runs the hiring route's own rules, so the
/// tests show the wiring and the points an assistant relies on: only what is passed changes, a stale workflow version is
/// refused, another organization's job does not exist, and publishing asks the person.
/// </summary>
public sealed class McpJobToolsTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Create_job_starts_a_draft_from_a_template_and_get_job_reads_its_stages_and_questions()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var mcp = await ConnectAsync(owner);
        var templates = (await mcp.CallAsync("list_hiring_templates", new())).GetProperty("templates").EnumerateArray().ToList();
        var template = templates.First(t => t.GetProperty("latestPublishedVersionId").ValueKind == JsonValueKind.String);

        var created = await mcp.CallAsync("create_job", new()
        {
            ["title"] = "Support Engineer",
            ["templateVersionId"] = template.GetProperty("latestPublishedVersionId").GetGuid(),
            ["description"] = "<p>Help customers succeed.</p>",
            ["workplaceType"] = "Remote",
            ["employmentType"] = "FullTime",
        });
        var read = await mcp.CallAsync("get_job", new() { ["jobId"] = created.GetProperty("id").GetGuid() });

        read.GetProperty("title").GetString().Should().Be("Support Engineer");
        read.GetProperty("status").GetString().Should().Be("Draft", "nothing is public until publish_job");
        read.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("kind").GetString()).Should().Contain(["Applied", "Hired", "Rejected"]);
        read.GetProperty("questions").GetProperty("questions").ValueKind.Should().Be(JsonValueKind.Array);
        read.GetProperty("workflowVersion").GetInt32().Should().BeGreaterThan(0);
        read.GetProperty("template").GetProperty("templateId").GetGuid().Should().Be(template.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Edit_job_changes_only_what_it_is_given()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await using var mcp = await ConnectAsync(owner);
        var before = await mcp.CallAsync("get_job", new() { ["jobId"] = job });

        var edited = await mcp.CallAsync("edit_job", new() { ["jobId"] = job, ["department"] = "Platform" });

        edited.GetProperty("department").GetString().Should().Be("Platform");
        foreach (var unchanged in new[] { "title", "description", "employmentType", "workplaceType" })
        {
            edited.GetProperty(unchanged).GetString().Should().Be(before.GetProperty(unchanged).GetString(), unchanged);
        }

        edited.GetProperty("salary").GetRawText().Should().Be(before.GetProperty("salary").GetRawText());
        (await owner.GetAsync($"/v1/jobs/{job}")).Text("department").Should().Be("Platform");
    }

    [Fact]
    public async Task Set_job_questions_adds_a_question_and_a_stale_workflow_version_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await using var mcp = await ConnectAsync(owner);
        var read = await mcp.CallAsync("get_job", new() { ["jobId"] = job });
        var version = read.GetProperty("workflowVersion").GetInt32();
        var questions = JsonNode.Parse(read.GetProperty("questions").GetRawText())!.AsObject();
        var list = questions["questions"] as JsonArray ?? [];
        list.Add(new JsonObject { ["id"] = "portfolio", ["type"] = "Url", ["label"] = "A link to your portfolio", ["required"] = false });
        questions["questions"] = list;

        var saved = await mcp.CallAsync("set_job_questions", new() { ["jobId"] = job, ["questions"] = questions, ["expectedWorkflowVersion"] = version });
        var stale = await mcp.CallFailingAsync("set_job_questions", new() { ["jobId"] = job, ["questions"] = questions, ["expectedWorkflowVersion"] = version });

        saved.GetProperty("questions").GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("id").GetString()).Should().Contain("portfolio");
        saved.GetProperty("workflowVersion").GetInt32().Should().BeGreaterThan(version);
        stale.Should().Contain("reload it", "the version it read is old, so the route's conflict says to read again");
        (await owner.GetAsync($"/v1/jobs/{job}")).At("workflow.questionnaire.questions").EnumerateArray()
            .Count(q => q.GetProperty("id").GetString() == "portfolio").Should().Be(1);
    }

    [Fact]
    public async Task Set_job_pipeline_adds_a_stage_after_screening()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await using var mcp = await ConnectAsync(owner);
        var read = await mcp.CallAsync("get_job", new() { ["jobId"] = job });
        var stages = JsonNode.Parse(read.GetProperty("stages").GetRawText())!.AsArray();
        var screening = stages.ToList().FindIndex(s => s!["kind"]!.GetValue<string>() == "Screening");
        stages.Insert(screening + 1, new JsonObject { ["id"] = "reference-check", ["name"] = "Reference check", ["kind"] = "Custom" });

        var saved = await mcp.CallAsync(
            "set_job_pipeline", new() { ["jobId"] = job, ["stages"] = stages, ["expectedWorkflowVersion"] = read.GetProperty("workflowVersion").GetInt32() });

        var ids = saved.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("id").GetString()).ToList();
        ids.IndexOf("reference-check").Should().Be(screening + 1);
        (await owner.GetAsync($"/v1/jobs/{job}")).At("workflow.stages").EnumerateArray().Select(s => s.GetProperty("id").GetString())
            .Should().Equal(ids);
    }

    [Fact]
    public async Task Publish_job_opens_the_job_only_with_the_persons_yes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await using var mcp = await ConnectAsync(owner);

        var refused = await mcp.CallFailingAsync("publish_job", new() { ["jobId"] = job, ["confirm"] = false });
        (await owner.GetAsync($"/v1/jobs/{job}")).Text("status").Should().Be("Draft");
        var published = await mcp.CallAsync("publish_job", new() { ["jobId"] = job, ["confirm"] = true });

        refused.Should().Contain("Not done yet").And.Contain("Platform Engineer");
        published.GetProperty("status").GetString().Should().Be("Open");
        (await owner.GetAsync($"/v1/jobs/{job}")).Text("status").Should().Be("Open");
    }

    [Fact]
    public async Task Get_pipeline_counts_an_application_in_its_stage_with_the_candidates_name_marked_as_theirs()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Casey Morgan")).Id();
        await using var mcp = await ConnectAsync(owner);

        var pipeline = await mcp.CallAsync("get_pipeline", new() { ["jobId"] = job });

        var stages = pipeline.GetProperty("stages").EnumerateArray().ToList();
        var applied = stages.Single(s => s.GetProperty("id").GetString() == "applied");
        applied.GetProperty("count").GetInt32().Should().Be(1);
        var entry = applied.GetProperty("applications").EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("applicationId").GetGuid().Should().Be(application);
        entry.GetProperty("candidateName").GetProperty("candidateWritten").GetString().Should().Be("Casey Morgan");
        stages.Where(s => s.GetProperty("id").GetString() != "applied").Should().OnlyContain(s => s.GetProperty("count").GetInt32() == 0);
    }

    [Fact]
    public async Task Another_organizations_job_does_not_exist_and_a_read_key_is_not_offered_the_write_tools()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var theirJob = (await theirs.CreateJobAsync(Samples.RemoteJob("Their Engineer"))).Id();
        await using var mcp = await ConnectAsync(ours);
        await using var reader = await McpSupport.ConnectAsync(fixture, (await ours.CreateKeyAsync("Reader", ["Read"])).Text("token"));

        (await mcp.CallFailingAsync("get_job", new() { ["jobId"] = theirJob })).Should().Contain("No job with that id");
        (await mcp.CallFailingAsync("get_pipeline", new() { ["jobId"] = theirJob })).Should().Contain("No job with that id");
        (await mcp.CallFailingAsync("edit_job", new() { ["jobId"] = theirJob, ["title"] = "Mine now" })).Should().Contain("No job with that id");
        (await mcp.CallFailingAsync("publish_job", new() { ["jobId"] = theirJob, ["confirm"] = true })).Should().Contain("No job with that id");

        (await theirs.GetAsync($"/v1/jobs/{theirJob}")).Text("title").Should().Be("Their Engineer", "the edit reached nothing");
        (await reader.ToolNamesAsync()).Should().NotContain(["create_job", "edit_job", "publish_job", "close_job", "set_job_questions", "set_job_pipeline"]);
    }

    private async Task<ModelContextProtocol.Client.McpClient> ConnectAsync(Tenant owner) =>
        await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));
}
