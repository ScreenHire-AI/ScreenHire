using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The person's yes before an assistant does something that reaches people (docs/architecture/mcp-server.md,
/// "Confirmation"): sending an email, closing a job, rejecting a candidate. A client that can ask the person (form
/// elicitation on the 2026-07-28 protocol) is asked; any other client must pass <c>confirm: true</c>. Nothing happens
/// on a no, and a yes to one request is no yes to another.
/// </summary>
public sealed class McpConfirmationTests(AppHostFixture fixture)
{
    private const string Subject = "About your application";
    private const string Body = "Thanks for applying.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Send_email_without_a_yes_is_refused_and_sends_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, application, address) = await ApplicationAsync(owner);
        await using var mcp = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));

        var refused = await mcp.CallFailingAsync("send_email", Email(application, confirm: false));

        refused.Should().Contain("Not done yet").And.Contain("confirm: true");
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a refused send must not queue an email", Ct);
        (await EmailEntriesAsync(owner, application)).Should().BeEmpty();
        (await fixture.Mailpit.MessagesAsync(address, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Send_email_with_confirm_true_sends_it_under_the_keys_name()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, application, address) = await ApplicationAsync(owner);
        var key = await owner.CreateKeyAsync("Assistant", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, key.Text("token"));

        var sent = await mcp.CallAsync("send_email", Email(application, confirm: true));

        sent.GetProperty("queued").GetBoolean().Should().BeTrue();
        var entry = await WaitForEmailEntryAsync(owner, application);
        entry.GetProperty("actorApiKeyId").GetGuid().Should().Be(key.Id("key.id"));
        entry.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "a key acts for no person");
        var received = await fixture.Mailpit.WaitForMessagesAsync(address, 1, Ct);
        received.Should().ContainSingle().Which.Subject.Should().Be(Subject);
    }

    [Fact]
    public async Task A_client_that_asks_the_person_sends_after_their_yes_and_names_the_job_in_the_question()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, application, address) = await ApplicationAsync(owner);
        var asked = new List<string?>();
        await using var mcp = await McpSupport.ConnectAsync(
            fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"), Eliciting(asked, agree: true));

        var sent = await mcp.CallAsync("send_email", Email(application, confirm: false));

        sent.GetProperty("queued").GetBoolean().Should().BeTrue("the person said yes, so the argument is not what decides");
        asked.Should().ContainSingle().Which.Should().Contain("Platform Engineer").And.Contain(Subject);
        await WaitForEmailEntryAsync(owner, application);
        (await fixture.Mailpit.WaitForMessagesAsync(address, 1, Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_client_whose_person_says_no_gets_a_refusal_and_nothing_is_sent()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, application, address) = await ApplicationAsync(owner);
        var asked = new List<string?>();
        await using var mcp = await McpSupport.ConnectAsync(
            fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"), Eliciting(asked, agree: false));

        // confirm: true does not override the person: the question is asked whenever the client can ask it.
        var refused = await mcp.CallFailingAsync("send_email", Email(application, confirm: true));

        refused.Should().Contain("did not agree").And.Contain("nothing changed");
        asked.Should().ContainSingle();
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a declined send must not queue an email", Ct);
        (await EmailEntriesAsync(owner, application)).Should().BeEmpty();
        (await fixture.Mailpit.MessagesAsync(address, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_confirmation_state_the_server_did_not_issue_is_refused_even_with_a_yes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, application, address) = await ApplicationAsync(owner);
        await using var mcp = await McpSupport.ConnectAsync(
            fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"), Eliciting([], agree: true));
        var yes = new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) } };

        var forged = await mcp.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "send_email",
                Arguments = Email(application, confirm: false).ToDictionary(a => a.Key, a => JsonSerializer.SerializeToElement(a.Value)),
                RequestState = "not-a-state-the-server-protected",
                InputResponses = new Dictionary<string, InputResponse> { ["confirm"] = new() { RawValue = JsonSerializer.SerializeToElement(yes, McpJsonUtilities.DefaultOptions) } },
            },
            Ct);

        forged.IsError.Should().BeTrue();
        string.Join(" ", forged.Content.OfType<TextContentBlock>().Select(c => c.Text)).Should().Contain("That confirmation is for another request");
        await Wait.ForNothingAsync(TimeSpan.FromSeconds(2), "a forged confirmation must not queue an email", Ct);
        (await fixture.Mailpit.MessagesAsync(address, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Moving_to_a_rejected_stage_needs_the_yes_and_any_other_stage_does_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, application, _) = await ApplicationAsync(owner);
        await using var mcp = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));
        var rejected = (await mcp.CallAsync("get_application", new() { ["applicationId"] = application })).GetProperty("stages").EnumerateArray()
            .First(s => s.GetProperty("kind").GetString() == "Rejected").GetProperty("id").GetString();

        (await mcp.CallAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = "screening" }))
            .GetProperty("stage").GetProperty("id").GetString().Should().Be("screening");
        var refused = await mcp.CallFailingAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = rejected });
        var declined = await mcp.CallFailingAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = rejected, ["confirm"] = false });

        refused.Should().Contain("Not done yet");
        declined.Should().Contain("Not done yet");
        (await owner.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("screening");
        (await mcp.CallAsync("move_to_stage", new() { ["applicationId"] = application, ["stageId"] = rejected, ["confirm"] = true }))
            .GetProperty("stage").GetProperty("kind").GetString().Should().Be("Rejected");
    }

    [Fact]
    public async Task Closing_a_job_needs_the_yes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (job, _, _) = await ApplicationAsync(owner);
        await using var mcp = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));

        (await mcp.CallFailingAsync("close_job", new() { ["jobId"] = job, ["confirm"] = false })).Should().Contain("Not done yet");
        (await owner.GetAsync($"/v1/jobs/{job}")).Text("status").Should().Be("Open");
        (await mcp.CallAsync("close_job", new() { ["jobId"] = job, ["confirm"] = true })).GetProperty("status").GetString().Should().Be("Closed");
        (await owner.GetAsync($"/v1/jobs/{job}")).Text("status").Should().Be("Closed");
    }

    // An open job with one application, and the address its candidate applied with.
    private static async Task<(Guid Job, Guid Application, string Email)> ApplicationAsync(Tenant owner)
    {
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var address = ApiSession.NewEmail("candidate");
        return (job, (await owner.CreateApplicationAsync(job, address)).Id(), address);
    }

    private static Dictionary<string, object?> Email(Guid application, bool? confirm)
    {
        var arguments = new Dictionary<string, object?> { ["applicationId"] = application, ["subject"] = Subject, ["body"] = Body };
        if (confirm is { } answer)
        {
            arguments["confirm"] = answer;
        }

        return arguments;
    }

    // A client whose person answers every question the same way, and a record of what they were asked.
    private static McpClientOptions Eliciting(List<string?> asked, bool agree) => new()
    {
        Handlers = new McpClientHandlers
        {
            ElicitationHandler = (request, _) =>
            {
                asked.Add(request?.Message);
                return ValueTask.FromResult(agree
                    ? new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) } }
                    : new ElicitResult { Action = "decline" });
            },
        },
    };

    private static async Task<List<JsonElement>> EmailEntriesAsync(Tenant owner, Guid application) =>
        [.. (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "EmailSent")];

    private static async Task<JsonElement> WaitForEmailEntryAsync(Tenant owner, Guid application)
    {
        await Wait.UntilAsync(async () => (await EmailEntriesAsync(owner, application)).Count > 0, "the EmailSent entry on the application's timeline", Ct);
        return (await EmailEntriesAsync(owner, application))[0];
    }
}
