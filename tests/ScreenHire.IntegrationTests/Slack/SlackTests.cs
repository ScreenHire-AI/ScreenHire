using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Slack;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Slack;

/// <summary>
/// Slack through the running stack, against the local sandbox workspace (docs/architecture/slack.md): connecting the
/// organization's workspace with the OAuth round trip, picking the channel and its events, what each event posts and
/// to whom, direct messages to members who linked their account, disconnecting, and tenant isolation. The sandbox's
/// "Slack" records what it receives in a table the API lists at <c>GET /v1/slack/sandbox/messages</c>, so a test reads
/// what a person would have seen. Each test uses its own organization. A post is awaited by polling the sandbox's
/// messages; "nothing was posted" is asserted only after the handler that would have posted has finished (Wolverine's
/// inbox shows it handled), never after a fixed wait.
/// </summary>
public sealed class SlackTests(AppHostFixture fixture)
{
    private const string Hiring = "C0SBHIRING";
    private const string PostType = ".PostSlackChannelMessage";
    private const string DirectType = ".SendSlackDirectMessage";

    private static readonly string[] AllEvents = ["ApplicationSubmitted", "ApplicationMoved", "AutomationFailed", "JobPublished"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_connects_the_workspace_and_the_page_shows_it_without_the_token()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var before = (await owner.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        before.At("available").GetBoolean().Should().BeTrue();
        before.At("sandbox").GetBoolean().Should().BeTrue();
        before.At("workspace").ValueKind.Should().Be(JsonValueKind.Null);

        var landed = await ConnectAsync(owner);

        landed.PathAndQuery.Should().Be("/settings/slack?slack=connected");
        var status = (await owner.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        status.Text("workspace.teamName").Should().Be("ScreenHire sandbox");
        status.At("workspace.disconnected").GetBoolean().Should().BeFalse();
        status.At("settings.channel").ValueKind.Should().Be(JsonValueKind.Null, "nobody picked a channel yet");
        status.At("settings.events").GetArrayLength().Should().Be(0);
        status.At("settings.connectedBy").GetString().Should().NotBeNullOrEmpty();
        status.Body.GetRawText().Should().NotContainEquivalentOf("xoxb").And.NotContainEquivalentOf("token");

        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT protected_bot_token FROM slack_installations WHERE organization_id = @org", connection);
        command.Parameters.AddWithValue("org", owner.OrganizationId);
        var stored = (string)(await command.ExecuteScalarAsync(Ct))!;
        stored.Should().NotBeEmpty().And.NotContain("xoxb", "the bot token is protected at rest");
    }

    [Fact]
    public async Task A_callback_that_does_not_belong_to_the_browser_and_person_that_started_it_installs_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Admin");
        var state = StateOf((await owner.PostAsync("/v1/slack/install")).Expect(HttpStatusCode.OK).Text("authorizeUrl"));
        var cookie = StateCookie(owner);
        cookie.Should().NotBeNull("the install sets the nonce cookie the callback checks");

        // Another person's browser, carrying the same cookie and state: the state names the person who started it. Once
        // for a teammate in the same organization (only the person differs) and once for someone in another organization.
        foreach (var stranger in new[] { teammate, other })
        {
            stranger.Session.Cookies.Add(
                new Uri(stranger.Session.BaseAddress, "/v1/slack/oauth/callback"), new System.Net.Cookie(cookie!.Name, cookie.Value, "/v1/slack/oauth/callback"));
            (await CallbackAsync(stranger, $"code=sandbox-install&state={Uri.EscapeDataString(state)}")).PathAndQuery
                .Should().Be("/settings/slack?slack=failed");
        }

        // The same person without the cookie: a state copied out of the browser that started it.
        cookie.Expired = true;
        (await CallbackAsync(owner, $"code=sandbox-install&state={Uri.EscapeDataString(state)}")).PathAndQuery
            .Should().Be("/settings/slack?slack=failed");

        (await owner.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Null);
        (await other.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Null);
        (await teammate.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_sandbox_link_code_naming_someone_else_links_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        (await ConnectAsync(owner)).PathAndQuery.Should().Be("/settings/slack?slack=connected");
        var authorize = (await owner.PostAsync("/v1/slack/account/link", new JsonObject { ["returnUrl"] = "/settings/slack" }))
            .Expect(HttpStatusCode.OK).Text("authorizeUrl");

        // The code is in the address, so a person could swap in another's id, here someone in another organization.
        var code = $"{SandboxSlackClient.LinkCodePrefix}{other.UserId:N}";
        (await CallbackAsync(owner, $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(StateOf(authorize))}")).PathAndQuery
            .Should().Be("/settings/slack?slack-account=failed");

        (await owner.GetAsync("/v1/slack")).At("account").ValueKind.Should().Be(JsonValueKind.Null, "no account is linked, so no name is shown");
    }

    [Fact]
    public async Task Connecting_the_same_workspace_again_keeps_the_channel_the_events_and_the_linked_accounts()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, ["JobPublished", "ApplicationSubmitted"]);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await LinkAccountAsync(robin);
        var firstTime = (await owner.GetAsync("/v1/slack")).At("settings.connectedAt").GetDateTimeOffset();

        (await ConnectAsync(owner)).PathAndQuery.Should().Be("/settings/slack?slack=connected");

        var status = (await owner.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        status.At("settings.connectedAt").GetDateTimeOffset().Should().BeAfter(firstTime, "the second install did reconnect");
        status.Text("settings.channel.id").Should().Be(Hiring);
        status.Text("settings.channel.name").Should().Be("hiring");
        status.At("settings.events").EnumerateArray().Select(e => e.GetString()).Should().Equal("ApplicationSubmitted", "JobPublished");
        var account = (await robin.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        account.At("account").ValueKind.Should().Be(JsonValueKind.Object, "the same workspace keeps its links");
        account.Text("account.slackName").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_slack_account_another_member_linked_moves_to_the_member_who_signs_in_to_it()
    {
        // The sandbox gives each ScreenHire user their own Slack user, so two members cannot reach the same one through
        // its consent page. Real Slack can: one person holding two ScreenHire accounts. The first member's link is put
        // in the table as that earlier round trip would have left it, naming the second member's Slack user.
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var first = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var second = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await using (var db = await TenantDbAsync(owner.OrganizationId))
        {
            var installation = await db.SlackInstallations.SingleAsync(Ct);
            db.SlackAccountLinks.Add(SlackAccountLink.Create(
                owner.OrganizationId, installation.Id, first.UserId, SandboxSlackClient.SlackUserIdFor(second.UserId), "Shared account", installation.ConnectedAt));
            await db.SaveChangesAsync(Ct);
        }

        (await first.GetAsync("/v1/slack")).At("account").ValueKind.Should().Be(JsonValueKind.Object);
        (await LinkAsync(second)).PathAndQuery.Should().Be("/settings/slack?slack-account=linked");

        (await second.GetAsync("/v1/slack")).At("account").ValueKind.Should().Be(JsonValueKind.Object);
        (await first.GetAsync("/v1/slack")).At("account").ValueKind.Should().Be(JsonValueKind.Null, "a Slack account belongs to one member");
        await using var check = await TenantDbAsync(owner.OrganizationId);
        (await check.SlackAccountLinks.ToListAsync(Ct)).Should().ContainSingle().Which.UserId.Should().Be(second.UserId);
    }

    [Fact]
    public async Task A_state_that_does_not_read_is_expired_and_a_cancelled_install_is_denied()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        (await CallbackAsync(owner, "code=sandbox-install&state=garbage")).PathAndQuery.Should().Be("/settings/slack?slack=expired");

        var state = StateOf((await owner.PostAsync("/v1/slack/install")).Expect(HttpStatusCode.OK).Text("authorizeUrl"));
        (await CallbackAsync(owner, $"error=access_denied&state={Uri.EscapeDataString(state)}")).PathAndQuery
            .Should().Be("/settings/slack?slack=denied");

        (await owner.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_callback_with_nobody_signed_in_goes_to_sign_in()
    {
        using var anonymous = new ApiSession(fixture);

        using var response = await anonymous.Client.GetAsync("/v1/slack/oauth/callback?code=sandbox-install&state=garbage", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.Should().StartWith("/sign-in?returnUrl=");
    }

    [Fact]
    public async Task A_recruiter_cannot_manage_the_workspace_and_sees_it_without_its_settings()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");

        (await recruiter.PostAsync("/v1/slack/install")).Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Not in your role");
        await ConnectAsync(owner);
        (await recruiter.PutAsync("/v1/slack/channel", ChannelBody(Hiring, AllEvents))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.DeleteAsync("/v1/slack")).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync("/v1/slack/test")).Expect(HttpStatusCode.Forbidden);
        (await recruiter.GetAsync("/v1/slack/channels")).Expect(HttpStatusCode.Forbidden);

        var seen = (await recruiter.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        seen.Text("workspace.teamName").Should().Be("ScreenHire sandbox", "every member sees which workspace is connected");
        seen.At("settings").ValueKind.Should().Be(JsonValueKind.Null);
        (await owner.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Object, "the refused calls changed nothing");
    }

    [Fact]
    public async Task A_role_with_manage_integrations_on_only_their_own_jobs_cannot_manage_the_workspace()
    {
        // A channel post reaches everyone in the channel, about every job, so managing it needs all jobs (ADR-0015).
        // Owners and members always see every job; a custom role with the permission may not.
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var roleId = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Integrations", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.Created).Id();
        (await owner.PutAsync($"/v1/organization/roles/{roleId}", new JsonObject
        {
            ["name"] = "Integrations", ["permissions"] = new JsonArray("ViewHiring", "ManageIntegrations"),
        })).Expect(HttpStatusCode.OK);
        using var member = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.GiveRoleAsync(member, roleId);

        (await member.PostAsync("/v1/slack/install")).Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs");
        await ConnectAsync(owner);
        (await member.GetAsync("/v1/slack/channels")).Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs");
        (await member.PutAsync("/v1/slack/channel", ChannelBody(Hiring, AllEvents))).Expect(HttpStatusCode.Forbidden);
        (await member.PostAsync("/v1/slack/test")).Expect(HttpStatusCode.Forbidden);
        (await member.DeleteAsync("/v1/slack")).Expect(HttpStatusCode.Forbidden);

        var seen = (await member.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        seen.Text("workspace.teamName").Should().Be("ScreenHire sandbox");
        seen.At("settings").ValueKind.Should().Be(JsonValueKind.Null);
        (await owner.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Object, "the refused calls changed nothing");
    }

    [Fact]
    public async Task The_channel_must_be_one_slack_lists_and_the_app_can_post_to()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);

        var channels = (await owner.GetAsync("/v1/slack/channels")).Expect(HttpStatusCode.OK);
        channels.Body.EnumerateArray().Select(c => c.GetProperty("id").GetString()).Should().Equal("C0SBANNOUNCE", "C0SBGENERAL", "C0SBHIRING", "C0SBRECRUIT");

        (await owner.PutAsync("/v1/slack/channel", ChannelBody("C0NOSUCH", AllEvents))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("channelId");
        (await owner.PutAsync("/v1/slack/channel", ChannelBody("C0SBOLDHIRE", AllEvents))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("channelId");
        (await owner.GetAsync("/v1/slack")).At("settings.channel").ValueKind.Should().Be(JsonValueKind.Null, "a refused channel is not saved");

        var saved = (await owner.PutAsync("/v1/slack/channel", ChannelBody(Hiring, ["JobPublished", "ApplicationSubmitted"]))).Expect(HttpStatusCode.OK);
        saved.Text("settings.channel.id").Should().Be(Hiring);
        saved.Text("settings.channel.name").Should().Be("hiring");
        saved.At("settings.events").EnumerateArray().Select(e => e.GetString()).Should().Equal("ApplicationSubmitted", "JobPublished");
    }

    [Fact]
    public async Task Each_event_the_team_picked_posts_one_message_naming_the_candidate_and_job_and_nothing_else()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, AllEvents);

        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var published = await WaitForMessageAsync(owner, Hiring, $"/jobs/{job}", "the published job to post");
        published.Should().Contain("Platform Engineer").And.Contain("is published");

        var candidateEmail = ApiSession.NewEmail("candidate");
        var application = (await owner.CreateApplicationAsync(job, candidateEmail, "Robin Ellis")).Id();
        var submitted = await WaitForMessageAsync(owner, Hiring, $"/applications/{application}", "the new application to post");
        submitted.Should().Contain("New application from").And.Contain("Robin Ellis").And.Contain("Platform Engineer");
        submitted.Should().NotContain(candidateEmail);

        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        var moved = await WaitForMessageAsync(owner, Hiring, "stage changed", "the move to post");
        moved.Should().Contain("Robin Ellis").And.Contain("Platform Engineer").And.Contain($"/applications/{application}");
        moved.Should().NotContainEquivalentOf("screening", "no stage name reaches a channel").And.NotContainEquivalentOf("applied");

        (await ChannelMessagesAsync(owner)).Should().HaveCount(3, "one message per event");
    }

    [Fact]
    public async Task A_failed_or_blocked_automation_posts_that_one_failed_without_naming_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, ["AutomationFailed"]);
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Outdated workflow", new JsonObject
        {
            ["id"] = "received",
            ["name"] = "Confirm the application",
            ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = "application-received" }),
        });
        await using (var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                "UPDATE jobs SET workflow = replace(workflow::text, '\"application-received\"', '\"retired-template\"')::jsonb WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", job);
            (await command.ExecuteNonQueryAsync(Ct)).Should().Be(1);
        }

        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Robin Ellis")).Id();

        var text = await WaitForMessageAsync(owner, Hiring, $"/applications/{application}?tab=activity", "the blocked automation to post");
        text.Should().Contain("An automation failed").And.Contain("Robin Ellis").And.Contain("Outdated workflow");
        text.Should().NotContain("Confirm the application").And.NotContain("retired-template").And.NotContain("received");
        (await ChannelMessagesAsync(owner)).Should().ContainSingle("the application's submission was not a picked event");
    }

    [Fact]
    public async Task An_event_the_team_did_not_pick_posts_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, ["JobPublished"]);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);

        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Robin Ellis")).Id();
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(application.ToString(), Ct, PostType) >= 1, "the submission's post to be handled", Ct);
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(application.ToString(), Ct, PostType) >= 2, "the move's post to be handled", Ct);

        (await ChannelMessagesAsync(owner)).Should().BeEmpty("only publishing a job was picked, and nothing was published");

        // The same channel does post what was picked, so the silence above is the choice and not a broken channel.
        await owner.PublishJobAsync(job);
        var text = await WaitForMessageAsync(owner, Hiring, $"/jobs/{job}", "the published job to post");
        text.Should().Contain("is published");
        (await ChannelMessagesAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_replayed_event_posts_once()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, ["ApplicationMoved"]);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Robin Ellis")).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        await WaitForMessageAsync(owner, Hiring, "stage changed", "the move to post");

        // The same ApplicationMoved (same timeline event id) delivered again in a new envelope, as a republish or a
        // recovered outbox would, and handled before the assertion.
        var moved = (await owner.GetAsync($"/v1/applications/{application}/timeline")).At("1");
        var eventId = moved.GetProperty("id").GetGuid();
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var movedBefore = await store.CountHandledAsync(eventId.ToString(), Ct);
        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new ApplicationMoved(
                owner.OrganizationId, application, job, "applied", "screening", eventId, moved.GetProperty("occurredAt").GetDateTimeOffset()));
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(eventId.ToString(), Ct) > movedBefore, "the worker to handle the replayed move", Ct);
        }

        // The replay cascades a second post with the first one's key; it is either dropped by the inbox or handled and
        // found in the record of what was posted. Wait for it to leave the queues either way.
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountQueuedAsync("worker", application.ToString(), Ct, PostType) == 0
                && await store.CountOutgoingAsync(application.ToString(), Ct, PostType) == 0
                && await store.CountIncomingAsync(application.ToString(), Ct, PostType)
                    == await store.CountHandledAsync(application.ToString(), Ct, PostType),
            "the replayed post to be settled", Ct);

        (await ChannelMessagesAsync(owner)).Should().ContainSingle("the replay found the post already made");
        (await store.GetDeadLettersAsync(application.ToString(), Ct)).Should().BeEmpty("the replay was dropped, not failed and retried");
        await using var db = await TenantDbAsync(owner.OrganizationId);
        (await db.SlackMessages.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task A_channel_the_workspace_will_not_let_the_app_post_in_is_recorded_once_and_explained_on_the_page()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, "C0SBANNOUNCE", ["ApplicationSubmitted"]);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();

        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Robin Ellis")).Id();

        ApiResult status = default!;
        await WolverineStore.WaitUntilAsync(async () =>
        {
            status = (await owner.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
            return status.At("settings.problem").ValueKind == JsonValueKind.String;
        }, "the refused post to show as a problem on the page", Ct);
        status.Text("settings.problem").Should().Be("CannotPost");
        (await ChannelMessagesAsync(owner, "C0SBANNOUNCE")).Should().BeEmpty();
        await using (var db = await TenantDbAsync(owner.OrganizationId))
        {
            var records = await db.SlackMessages.ToListAsync(Ct);
            records.Should().ContainSingle("a refusal a retry cannot fix is not retried");
            records[0].Outcome.Should().Be(SlackMessageOutcome.Refused);
            records[0].ErrorCode.Should().Be("restricted_action");
        }

        (await store.CountHandledAsync(application.ToString(), Ct, PostType)).Should().Be(1);
        (await store.GetDeadLettersAsync(application.ToString(), Ct)).Should().BeEmpty();

        // The test message hits the same wall and says so; picking another channel clears the problem.
        var test = (await owner.PostAsync("/v1/slack/test")).Expect(HttpStatusCode.OK);
        test.At("posted").GetBoolean().Should().BeFalse();
        test.Text("problem").Should().Be("CannotPost");
        var fixedStatus = (await owner.PutAsync("/v1/slack/channel", ChannelBody(Hiring, ["ApplicationSubmitted"]))).Expect(HttpStatusCode.OK);
        fixedStatus.At("settings.problem").ValueKind.Should().Be(JsonValueKind.Null);
        var sent = (await owner.PostAsync("/v1/slack/test")).Expect(HttpStatusCode.OK);
        sent.At("posted").GetBoolean().Should().BeTrue();
        (await WaitForMessageAsync(owner, Hiring, "ScreenHire is connected", "the test message to arrive")).Should().NotContain("Robin");
    }

    [Fact]
    public async Task A_mention_and_a_task_reach_the_person_in_a_direct_message_that_quotes_neither()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var robinDm = DirectMessageOf(robin);
        (await robin.PostAsync("/v1/slack/account/link", new JsonObject { ["returnUrl"] = "/settings/slack" })).Expect(HttpStatusCode.OK);
        (await LinkAsync(robin)).PathAndQuery.Should().Be("/settings/slack?slack-account=linked");
        var account = (await robin.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        account.At("account.mentions").GetBoolean().Should().BeTrue();
        account.At("account.tasks").GetBoolean().Should().BeTrue();
        account.At("account.slackName").GetString().Should().NotBeNullOrEmpty();
        var (_, application) = await ApplicationAsync(owner, "Platform Engineer", "Robin Ellis");

        var noteId = (await owner.PostAsync($"/v1/applications/{application}/notes",
            new JsonObject { ["note"] = $"@[{robin.UserId}], ask about the heliotrope lantern." })).Expect(HttpStatusCode.Created).Id("noteId");
        var mention = await WaitForMessageAsync(owner, robinDm, "mentioned", "the mention's direct message");
        mention.Should().Contain("Robin Ellis").And.Contain("Platform Engineer")
            .And.Contain($"/applications/{application}?tab=activity#note-{noteId}");
        mention.Should().NotContain("heliotrope").And.NotContain(robin.UserId.ToString()).And.NotContain("Org ");

        (await owner.PostAsync($"/v1/applications/{application}/tasks",
            new JsonObject { ["note"] = "Check the heliotrope references.", ["assigneeUserId"] = robin.UserId.ToString() })).Expect(HttpStatusCode.Created);
        var task = await WaitForMessageAsync(owner, robinDm, "You have a new task", "the task's direct message");
        task.Should().Contain("Robin Ellis").And.Contain("Platform Engineer").And.Contain($"/applications/{application}");
        task.Should().NotContain("heliotrope");
    }

    [Fact]
    public async Task A_scorecard_requested_of_a_teammate_reaches_them_as_a_task_in_a_direct_message()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await LinkAccountAsync(robin);
        var (_, application) = await ApplicationAsync(owner, "Platform Engineer", "Robin Ellis");
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);

        (await owner.PostAsync($"/v1/applications/{application}/scorecard-requests", new JsonObject
        {
            ["assigneeUserId"] = robin.UserId.ToString(),
            ["scorecardTemplateId"] = "interview",
            ["note"] = "Focus on the heliotrope exercise.",
        })).Expect(HttpStatusCode.Created);

        var text = await WaitForMessageAsync(owner, DirectMessageOf(robin), "You have a new task", "the requested scorecard's direct message");
        text.Should().Contain("Robin Ellis").And.Contain("Platform Engineer").And.Contain($"/applications/{application}");
        text.Should().NotContain("heliotrope", "a direct message never quotes the request's note");
        (await ChannelMessagesAsync(owner, DirectMessageOf(robin))).Should().ContainSingle();
    }

    [Fact]
    public async Task A_reviewer_an_automation_assigns_gets_the_task_in_a_direct_message()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await LinkAccountAsync(robin);
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer", new JsonObject
        {
            ["id"] = "prepare",
            ["name"] = "Ask for a review",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "AssignReviewer", ["assigneeUserId"] = robin.UserId.ToString() }),
        });
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Robin Ellis")).Id();

        (await owner.PostAsync($"/v1/applications/{application}/automations/prepare/run")).Expect(HttpStatusCode.Accepted);

        var text = await WaitForMessageAsync(owner, DirectMessageOf(robin), "You have a new task", "the reviewer's direct message");
        text.Should().Contain("Robin Ellis").And.Contain("Platform Engineer").And.Contain($"/applications/{application}");
        text.Should().NotContain("Ask for a review").And.NotContain("Review this application");
    }

    [Fact]
    public async Task Turning_mentions_off_stops_those_direct_messages_and_leaves_tasks()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await LinkAccountAsync(robin);
        var (_, application) = await ApplicationAsync(owner, "Platform Engineer", "Robin Ellis");
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);

        var off = (await robin.PutAsync("/v1/slack/account", new JsonObject { ["mentions"] = false, ["tasks"] = true })).Expect(HttpStatusCode.OK);
        off.At("mentions").GetBoolean().Should().BeFalse();
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = $"@[{robin.UserId}], one more thing." })).Expect(HttpStatusCode.Created);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(application.ToString(), Ct, DirectType) >= 1, "the mention's message to be handled", Ct);
        (await owner.PostAsync($"/v1/applications/{application}/tasks",
            new JsonObject { ["note"] = "Call them.", ["assigneeUserId"] = robin.UserId.ToString() })).Expect(HttpStatusCode.Created);
        await WaitForMessageAsync(owner, DirectMessageOf(robin), "You have a new task", "the task's direct message");

        (await ChannelMessagesAsync(owner, DirectMessageOf(robin))).Should().ContainSingle("the mention went nowhere and only the task arrived");

        (await robin.DeleteAsync("/v1/slack/account")).Expect(HttpStatusCode.NoContent);
        (await robin.GetAsync("/v1/slack")).At("account").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // A mention or a task can only name someone who sees the candidate, so the API cannot make a message for someone who
    // does not. The one way it happens is a person removed from the hiring team after the message was queued. The test
    // plays that part itself, as the replay test does: it takes the person off the team, then puts the message the API
    // would have queued on the worker's queue.
    [Fact]
    public async Task Someone_taken_off_the_jobs_hiring_team_before_the_message_is_handled_gets_no_direct_message()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var narrow = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        await LinkAccountAsync(narrow);
        var (job, application) = await ApplicationAsync(owner, "Platform Engineer", "Robin Ellis");
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var member = await owner.MemberIdAsync(narrow.UserId);
        var dm = DirectMessageOf(narrow);
        var taskPath = $"/v1/applications/{application}/tasks";
        var removed = (await owner.PostAsync(taskPath, new JsonObject { ["note"] = "First." })).Expect(HttpStatusCode.Created).Id();
        var kept = (await owner.PostAsync(taskPath, new JsonObject { ["note"] = "Second." })).Expect(HttpStatusCode.Created).Id();
        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = publisher.Services.GetRequiredService<IMessageBus>();
        SendSlackDirectMessage MessageFor(Guid task) =>
            new(owner.OrganizationId, SlackDirectMessageKind.Task, task, narrow.UserId, application, job, owner.UserId);

        // On the team, then taken off it (a team always keeps someone, so the owner stays): they cannot see the job any more.
        var ownerMember = await owner.MemberIdAsync(owner.UserId);
        await owner.SetHiringTeamAsync(job, ownerMember, member);
        await owner.SetHiringTeamAsync(job, ownerMember);
        await bus.PublishAsync(MessageFor(removed));
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(removed.ToString(), Ct, DirectType) >= 1, "the first message to be handled", Ct);
        (await ChannelMessagesAsync(owner, dm)).Should().BeEmpty();

        // On the team they see it, and the same kind of message now arrives: the team made the difference.
        await owner.SetHiringTeamAsync(job, ownerMember, member);
        await bus.PublishAsync(MessageFor(kept));
        var text = await WaitForMessageAsync(owner, dm, "You have a new task", "the message once they are on the team");
        text.Should().Contain("Robin Ellis").And.Contain($"/applications/{application}");
        (await ChannelMessagesAsync(owner, dm)).Should().ContainSingle();
    }

    [Fact]
    public async Task Disconnecting_removes_the_workspace_and_every_linked_account_and_later_events_post_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, AllEvents);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await LinkAccountAsync(robin);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);

        (await owner.DeleteAsync("/v1/slack")).Expect(HttpStatusCode.NoContent);

        var gone = (await owner.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        gone.At("available").GetBoolean().Should().BeTrue();
        gone.At("workspace").ValueKind.Should().Be(JsonValueKind.Null);
        gone.At("settings").ValueKind.Should().Be(JsonValueKind.Null);
        (await robin.GetAsync("/v1/slack")).At("account").ValueKind.Should().Be(JsonValueKind.Null, "the accounts went with the workspace");
        (await owner.PutAsync("/v1/slack/channel", ChannelBody(Hiring, AllEvents))).Expect(HttpStatusCode.NotFound);
        (await owner.DeleteAsync("/v1/slack")).Expect(HttpStatusCode.NotFound);

        var (_, application) = await ApplicationAsync(owner, "Platform Engineer", "Robin Ellis");
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = $"@[{robin.UserId}], hello." })).Expect(HttpStatusCode.Created);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(application.ToString(), Ct, PostType) >= 1
                && await store.CountHandledAsync(application.ToString(), Ct, DirectType) >= 1,
            "the posts that found no workspace to be handled", Ct);

        (await MessagesAsync(owner)).Should().BeEmpty("the workspace was disconnected before anything happened");
    }

    [Fact]
    public async Task Removing_a_member_removes_their_linked_slack_account()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(owner);
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await LinkAccountAsync(robin);

        (await owner.DeleteAsync($"/v1/organization/members/{await owner.MemberIdAsync(robin.UserId)}")).Expect(HttpStatusCode.NoContent);

        await using var db = await TenantDbAsync(owner.OrganizationId);
        (await db.SlackAccountLinks.CountAsync(l => l.UserId == robin.UserId, Ct)).Should().Be(0, "no direct message reaches someone who left");
    }

    // The daily sweep fans out one message per organization; the test sends one organization's message with a cutoff
    // far ahead of the clock, so the cron cannot touch the rows, and the other organization's rows must survive.
    [Fact]
    public async Task The_retention_purge_deletes_only_the_old_records_and_sandbox_messages_of_its_organization()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        var cutoff = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedRetentionRowsAsync(first.OrganizationId, cutoff);
        await SeedRetentionRowsAsync(second.OrganizationId, cutoff);

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new PurgeOrganizationSlackMessages(first.OrganizationId, cutoff));

        await WolverineStore.WaitUntilAsync(async () =>
        {
            await using var db = await TenantDbAsync(first.OrganizationId);
            return await db.SlackMessages.CountAsync(Ct) == 1 && await db.SlackSandboxMessages.CountAsync(Ct) == 1;
        }, "the purge to remove the first organization's old rows", Ct);
        await using var purged = await TenantDbAsync(first.OrganizationId);
        (await purged.SlackMessages.SingleAsync(Ct)).IdempotencyKey.Should().Be("newer");
        (await purged.SlackSandboxMessages.SingleAsync(Ct)).Text.Should().Be("newer");
        await using var untouched = await TenantDbAsync(second.OrganizationId);
        (await untouched.SlackMessages.Select(m => m.IdempotencyKey).ToListAsync(Ct)).Should().BeEquivalentTo(["older", "newer"]);
        (await untouched.SlackSandboxMessages.Select(m => m.Text).ToListAsync(Ct)).Should().BeEquivalentTo(["older", "newer"]);
    }

    [Fact]
    public async Task Another_organization_sees_no_workspace_and_none_of_the_messages()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        await SetUpChannelAsync(owner, Hiring, AllEvents);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        await WaitForMessageAsync(owner, Hiring, $"/jobs/{job}", "the published job to post");

        var theirs = (await other.GetAsync("/v1/slack")).Expect(HttpStatusCode.OK);
        theirs.At("available").GetBoolean().Should().BeTrue();
        theirs.At("workspace").ValueKind.Should().Be(JsonValueKind.Null);
        (await MessagesAsync(other)).Should().BeEmpty();
        (await other.GetAsync("/v1/slack/channels")).Expect(HttpStatusCode.NotFound);
        (await other.PutAsync("/v1/slack/channel", ChannelBody(Hiring, AllEvents))).Expect(HttpStatusCode.NotFound);
        (await other.DeleteAsync("/v1/slack")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync("/v1/slack")).At("workspace").ValueKind.Should().Be(JsonValueKind.Object);
    }

    // The page and the API both start from this: the install's authorize URL, then the browser's trip through the
    // sandbox's consent page (none) and the callback, one hop at a time.
    private static async Task<Uri> ConnectAsync(Tenant who)
    {
        var authorize = (await who.PostAsync("/v1/slack/install")).Expect(HttpStatusCode.OK).Text("authorizeUrl");
        return await FollowAsync(who, authorize);
    }

    private static async Task<Uri> LinkAsync(Tenant who)
    {
        var authorize = (await who.PostAsync("/v1/slack/account/link", new JsonObject { ["returnUrl"] = "/settings/slack" }))
            .Expect(HttpStatusCode.OK).Text("authorizeUrl");
        return await FollowAsync(who, authorize);
    }

    private static async Task LinkAccountAsync(Tenant who) =>
        (await LinkAsync(who)).PathAndQuery.Should().Be("/settings/slack?slack-account=linked");

    // The URLs are the dashboard's host, which these tests do not run, so each hop is asked of the API under the same path.
    private static async Task<Uri> FollowAsync(Tenant who, string url)
    {
        var next = OnApi(who, new Uri(url));
        for (var hops = 0; hops < 6; hops++)
        {
            using var response = await who.Session.Client.GetAsync(next, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect, $"{next.AbsolutePath} should redirect");
            var location = response.Headers.Location!;
            var target = location.IsAbsoluteUri ? location : new Uri(who.Session.BaseAddress, location);
            if (target.AbsolutePath is not (SandboxSlackClient.AuthorizePath or "/v1/slack/oauth/callback"))
            {
                return target;
            }

            next = OnApi(who, target);
        }

        throw new InvalidOperationException("The OAuth round trip did not end.");
    }

    private static async Task<Uri> CallbackAsync(Tenant who, string query)
    {
        using var response = await who.Session.Client.GetAsync($"/v1/slack/oauth/callback?{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return response.Headers.Location!;
    }

    private static Uri OnApi(Tenant who, Uri url) => new(who.Session.BaseAddress, url.PathAndQuery);

    private static string StateOf(string authorizeUrl) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(authorizeUrl).Query)["state"].ToString();

    private static System.Net.Cookie? StateCookie(Tenant who) =>
        who.Session.Cookies.GetCookies(new Uri(who.Session.BaseAddress, "/v1/slack/oauth/callback"))["sh.slack-oauth"];

    private static async Task SetUpChannelAsync(Tenant owner, string channelId, string[] events)
    {
        (await ConnectAsync(owner)).PathAndQuery.Should().Be("/settings/slack?slack=connected");
        (await owner.PutAsync("/v1/slack/channel", ChannelBody(channelId, events))).Expect(HttpStatusCode.OK);
    }

    private static JsonObject ChannelBody(string channelId, string[] events) =>
        new() { ["channelId"] = channelId, ["events"] = new JsonArray([.. events.Select(e => (JsonNode)JsonValue.Create(e)!)]) };

    // Slack is set up before anything is created: a job's first publish posts once, at the time it happens.
    private static async Task<(Guid Job, Guid Application)> ApplicationAsync(Tenant owner, string title, string candidate)
    {
        var job = (await owner.CreateJobAsync(Samples.RemoteJob(title))).Id();
        await owner.PublishJobAsync(job);
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), candidate)).Id();
        return (job, application);
    }

    private static string DirectMessageOf(Tenant member) =>
        SandboxSlackClient.DirectMessageId(SandboxSlackClient.SlackUserIdFor(member.UserId));

    // One record and one sandbox message a day before the cutoff, and the same a day after it.
    private async Task SeedRetentionRowsAsync(Guid organizationId, DateTimeOffset cutoff)
    {
        await using var db = await TenantDbAsync(organizationId);
        foreach (var (name, at) in new[] { ("older", cutoff.AddDays(-1)), ("newer", cutoff.AddDays(1)) })
        {
            db.SlackMessages.Add(new SlackMessageRecord { OrganizationId = organizationId, IdempotencyKey = name, Outcome = SlackMessageOutcome.Sent, At = at });
            db.SlackSandboxMessages.Add(new SlackSandboxMessage { OrganizationId = organizationId, Conversation = Hiring, Text = name, PostedAt = at });
        }

        await db.SaveChangesAsync(Ct);
    }

    private async Task<ScreenHire.Infrastructure.Persistence.ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }

    private static async Task<IReadOnlyList<(string Conversation, string Text)>> MessagesAsync(Tenant who)
    {
        var all = (await who.GetAsync("/v1/slack/sandbox/messages")).Expect(HttpStatusCode.OK);
        return [.. all.Body.EnumerateArray().Select(m => (m.GetProperty("conversation").GetString()!, m.GetProperty("text").GetString()!))];
    }

    private static async Task<IReadOnlyList<string>> ChannelMessagesAsync(Tenant who, string conversation = Hiring) =>
        [.. (await MessagesAsync(who)).Where(m => m.Conversation == conversation).Select(m => m.Text)];

    private static async Task<string> WaitForMessageAsync(Tenant who, string conversation, string contains, string because)
    {
        string? found = null;
        await WolverineStore.WaitUntilAsync(async () =>
        {
            found = (await ChannelMessagesAsync(who, conversation)).FirstOrDefault(t => t.Contains(contains, StringComparison.Ordinal));
            return found is not null;
        }, because, Ct);
        return found!;
    }
}
