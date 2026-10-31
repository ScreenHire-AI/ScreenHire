using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol;
using ScreenHire.Application.Integrity;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The MCP server at <c>/v1/mcp</c> (docs/architecture/mcp-server.md), driven by a real MCP client as an AI assistant
/// would drive it: an organization API key signs it in, a Read key sees and calls only the read tools, every change goes
/// through the hiring API's own rules and lands in Activity under the key, another organization's data is out of reach,
/// and no tool returns integrity output (ADR-0011).
/// </summary>
public sealed class McpServerTests(AppHostFixture fixture)
{
    private static readonly string[] ReadTools = ["find_candidates", "find_jobs", "get_application"];

    private static readonly string[] WriteTools = ["add_note", "add_tag", "create_task", "move_to_stage", "remove_tag"];

    [Fact]
    public async Task A_write_key_sees_every_tool_and_a_read_key_only_the_reads()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var writer = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));
        await using var reader = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Reader", ["Read"])).Text("token"));

        (await writer.ToolNamesAsync()).Should().Equal([.. ReadTools.Concat(WriteTools).Order(StringComparer.Ordinal)]);
        (await reader.ToolNamesAsync()).Should().Equal(ReadTools);
        writer.ServerInfo?.Name.Should().Be("ScreenHire");
        writer.ServerInstructions.Should().Contain("candidateWritten").And.Contain("Rejected");
    }

    [Fact]
    public async Task Each_tool_follows_the_hiring_rules_and_its_changes_land_in_activity_under_the_key()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        await owner.CreateJobAsync(Samples.RemoteJob("Data Analyst"));
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"), "Casey Morgan")).Id();
        var key = await owner.CreateKeyAsync("Assistant", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, key.Text("token"));

        var jobs = await mcp.CallAsync("find_jobs", new() { ["text"] = "platform", ["status"] = "Open" });
        jobs.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(job.Id());

        (await mcp.CallFailingAsync("find_candidates", new() { ["text"] = new string('a', 101) })).Should().Contain("100 characters");
        var found = await mcp.CallAsync("find_candidates", new() { ["text"] = "casey" });
        var match = found.GetProperty("applications").EnumerateArray().Should().ContainSingle().Subject;
        match.GetProperty("applicationId").GetGuid().Should().Be(application);
        match.GetProperty("candidateName").GetProperty("candidateWritten").GetString().Should().Be("Casey Morgan");

        var read = await mcp.CallAsync("get_application", new() { ["applicationId"] = application });
        read.GetProperty("stage").GetProperty("id").GetString().Should().Be("applied");
        read.GetProperty("candidate").GetProperty("name").GetProperty("candidateWritten").GetString().Should().Be("Casey Morgan");
        var stages = read.GetProperty("stages").EnumerateArray().ToList();
        stages.Select(s => s.GetProperty("id").GetString()).Should().Contain("screening");
        var rejected = stages.First(s => s.GetProperty("kind").GetString() == "Rejected").GetProperty("id").GetString();

        var note = await mcp.CallAsync("add_note", new() { ["applicationId"] = application, ["note"] = "Strong portfolio." });
        note.GetProperty("type").GetString().Should().Be("NoteAdded");
        note.GetProperty("actorApiKeyName").GetString().Should().Be("Assistant");
        (await mcp.CallFailingAsync("add_note", new() { ["applicationId"] = application, ["note"] = "  " })).Should().Contain("note", "the route's validation answers");
        (await mcp.CallAsync("add_tag", new() { ["applicationId"] = application, ["tag"] = "referral" }))
            .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("referral");
        (await mcp.CallAsync("remove_tag", new() { ["applicationId"] = application, ["tag"] = "REFERRAL" }))
            .GetProperty("tags").GetArrayLength().Should().Be(0);
        var task = await mcp.CallAsync("create_task", new() { ["applicationId"] = application, ["note"] = "Call the references." });
        (await mcp.CallAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = "screening", ["note"] = "Asked to by Avery." }))
            .GetProperty("stage").GetProperty("id").GetString().Should().Be("screening");
        (await mcp.CallFailingAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = "nowhere" })).Should().Contain("toStageId");
        (await mcp.CallAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = rejected }))
            .GetProperty("stage").GetProperty("kind").GetString().Should().Be("Rejected", "a rejection the person asked for follows the API's rules");

        (await owner.GetAsync("/v1/tasks?scope=Team")).Body.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Contain(task.GetProperty("id").GetGuid());
        var keyId = key.Id("key.id");
        var changes = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() is not ("Submitted" or "IntegrityAssessed"))
            .Select(e => (e.GetProperty("type").GetString(), e.GetProperty("actorApiKeyId").GetGuid(), e.GetProperty("actorUserId").ValueKind))
            .ToList();
        changes.Should().Equal(
            ("NoteAdded", keyId, JsonValueKind.Null), ("TagAdded", keyId, JsonValueKind.Null), ("TagRemoved", keyId, JsonValueKind.Null),
            ("TaskCreated", keyId, JsonValueKind.Null), ("StageChanged", keyId, JsonValueKind.Null), ("StageChanged", keyId, JsonValueKind.Null));
        var activity = (await mcp.CallAsync("get_application", new() { ["applicationId"] = application })).GetProperty("activity");
        activity[0].GetProperty("toStageId").GetString().Should().Be(rejected, "activity is newest first");
        activity.EnumerateArray().Should().Contain(e => e.GetProperty("type").GetString() == "NoteAdded" && e.GetProperty("note").GetString() == "Strong portfolio.");
    }

    [Fact]
    public async Task A_read_key_is_refused_every_write_and_nothing_changes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        await using var reader = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Reader", ["Read"])).Text("token"));

        (await reader.CallAsync("get_application", new() { ["applicationId"] = application })).GetProperty("id").GetGuid().Should().Be(application);
        foreach (var (tool, arguments) in new (string, Dictionary<string, object?>)[]
        {
            ("add_note", new() { ["applicationId"] = application, ["note"] = "Hello" }),
            ("add_tag", new() { ["applicationId"] = application, ["tag"] = "mine" }),
            ("create_task", new() { ["applicationId"] = application, ["note"] = "Mine" }),
            ("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = "screening" }),
            ("remove_tag", new() { ["applicationId"] = application, ["tag"] = "mine" }),
        })
        {
            var call = () => reader.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken).AsTask();
            await call.Should().ThrowAsync<McpProtocolException>(tool);
        }

        var timeline = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK);
        Changes(timeline).Should().Equal("Submitted");
        (await owner.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task Another_organizations_jobs_and_candidates_are_out_of_reach()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var theirJob = await theirs.CreateJobAsync(Samples.RemoteJob("Their Engineer"));
        await theirs.PublishJobAsync(theirJob.Id());
        var theirApplication = (await theirs.CreateApplicationAsync(theirJob.Id(), ApiSession.NewEmail("theirs"), "Robin Theirs")).Id();
        await using var mcp = await McpSupport.ConnectAsync(fixture, (await ours.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));

        (await mcp.CallAsync("find_jobs", new())).GetProperty("jobs").GetArrayLength().Should().Be(0);
        (await mcp.CallAsync("find_candidates", new() { ["text"] = "Robin" })).GetProperty("applications").GetArrayLength().Should().Be(0);
        (await mcp.CallAsync("find_candidates", new() { ["jobId"] = theirJob.Id() })).GetProperty("applications").GetArrayLength().Should().Be(0);
        (await mcp.CallFailingAsync("get_application", new() { ["applicationId"] = theirApplication })).Should().Contain("No application");
        (await mcp.CallFailingAsync("add_note", new() { ["applicationId"] = theirApplication, ["note"] = "Hello" })).Should().Contain("No application");
        (await mcp.CallFailingAsync("add_tag", new() { ["applicationId"] = theirApplication, ["tag"] = "mine" })).Should().Contain("No application");
        (await mcp.CallFailingAsync("create_task", new() { ["applicationId"] = theirApplication, ["note"] = "Mine" })).Should().Contain("No application");
        var ourJob = await ours.CreateJobAsync(Samples.RemoteJob("Our Engineer"));
        await ours.PublishJobAsync(ourJob.Id());
        var ourApplication = (await ours.CreateApplicationAsync(ourJob.Id(), ApiSession.NewEmail("ours"))).Id();
        (await mcp.CallFailingAsync("create_task", new() { ["applicationId"] = ourApplication, ["note"] = "Call", ["assigneeUserId"] = theirs.UserId }))
            .Should().Contain("assigneeUserId", "another organization's member is nobody here");
        (await mcp.CallFailingAsync("move_to_stage", new() { ["applicationId"] = theirApplication, ["stageId"] = "screening" }))
            .Should().Contain("No application");

        var timeline = (await theirs.GetAsync($"/v1/applications/{theirApplication}/timeline")).Expect(HttpStatusCode.OK);
        Changes(timeline).Should().Equal("Submitted");
    }

    [Fact]
    public async Task An_application_comes_back_without_integrity_output_and_with_the_candidates_words_marked()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, owner);
        var applicationId = await IntegritySupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await IntegritySupport.AssessedAsync(owner, applicationId);
        await using var mcp = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Reader", ["Read"])).Text("token"));

        var application = await mcp.CallAsync("get_application", new() { ["applicationId"] = applicationId });
        var found = await mcp.CallAsync("find_candidates", new() { ["jobId"] = jobId });

        foreach (var result in new[] { application, found })
        {
            McpSupport.PropertyNames(result).Should().NotContain(
                name => name.Contains("integrity", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("level", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("reason", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("signal", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("score", StringComparison.OrdinalIgnoreCase),
                "integrity output is for the person reviewing, never for a key (ADR-0011)");
        }

        application.GetProperty("activity").EnumerateArray().Select(e => e.GetProperty("type").GetString())
            .Should().NotContain("IntegrityAssessed", "a key does not learn that an assessment ran");
        var resume = application.GetProperty("answers").EnumerateArray().Single(a => a.GetProperty("questionId").GetString() == "resume");
        resume.GetProperty("text").GetProperty("candidateWritten").GetString().Should().NotBeNullOrEmpty("a file's name is the candidate's words");
        application.GetProperty("files").EnumerateArray().Should().OnlyContain(f => f.GetProperty("fileName").GetProperty("candidateWritten").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task Only_a_valid_api_key_connects()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var revoked = await owner.CreateKeyAsync("Old", ["Read", "Write"]);
        (await owner.PostAsync($"/v1/organization/api-keys/{revoked.Id("key.id")}/revoke")).Expect(HttpStatusCode.OK);
        var endpoint = McpSupport.Endpoint(fixture);

        using var anonymous = new HttpClient(new SocketsHttpHandler { UseCookies = false });
        (await McpSupport.InitializeStatusAsync(anonymous, endpoint)).Should().Be(HttpStatusCode.Unauthorized);
        (await McpSupport.InitializeStatusAsync(owner.Session.Client, endpoint)).Should().Be(HttpStatusCode.Unauthorized, "a session cookie is not a key");
        using var withRevoked = new HttpClient(new SocketsHttpHandler { UseCookies = false });
        withRevoked.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", revoked.Text("token"));
        (await McpSupport.InitializeStatusAsync(withRevoked, endpoint)).Should().Be(HttpStatusCode.Unauthorized);
        using var withGarbage = new HttpClient(new SocketsHttpHandler { UseCookies = false });
        withGarbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "shk_not_a_key");
        (await McpSupport.InitializeStatusAsync(withGarbage, endpoint)).Should().Be(HttpStatusCode.Unauthorized);
    }

    // The timeline's entries but the integrity engine's: it assesses each new application in the background, so whether
    // its IntegrityAssessed entry is in yet is a race, and these tests are about the key's changes.
    private static IEnumerable<string?> Changes(ApiResult timeline) =>
        timeline.Body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).Where(type => type != "IntegrityAssessed");
}
