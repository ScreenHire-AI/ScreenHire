using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// The real Web API client against a stub handler standing in for Slack's servers (https://docs.slack.dev/reference/methods):
/// the authorize URLs, the form each method posts, how answers and failures map to <see cref="SlackApiException"/> codes,
/// and the channel list's paging and filtering. No network.
/// </summary>
public sealed class SlackApiClientTests : IDisposable
{
    private static readonly Uri Redirect = new("https://app.example.org/v1/slack/oauth/callback");

    private readonly StubSlack slack = new();

    private readonly List<SlackApiClient> clients = [];

    public void Dispose()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        slack.Dispose();
    }

    private SlackApiClient Client()
    {
        var client = new SlackApiClient(Options.Create(new SlackOptions { ClientId = "1234.5678", ClientSecret = "fictional-secret" }), slack);
        clients.Add(client);
        return client;
    }

    [Fact]
    public void The_install_url_asks_for_the_bot_scopes_and_carries_the_redirect_and_the_state()
    {
        var url = Client().InstallUrl("state value/1", Redirect);

        url.Host.Should().Be("slack.com");
        url.AbsolutePath.Should().Be("/oauth/v2/authorize");
        var query = QueryHelpers.ParseQuery(url.Query);
        query["client_id"].ToString().Should().Be("1234.5678");
        query["scope"].ToString().Should().Be("chat:write,chat:write.public,channels:read,groups:read,im:write");
        query["redirect_uri"].ToString().Should().Be(Redirect.ToString());
        query["state"].ToString().Should().Be("state value/1");
        query.ContainsKey("client_secret").Should().BeFalse("the secret never goes through the browser");
    }

    [Fact]
    public void The_link_url_signs_in_with_slack_for_the_workspace()
    {
        var url = Client().LinkUrl("the-state", Redirect, "T0FICTION");

        url.AbsolutePath.Should().Be("/openid/connect/authorize");
        var query = QueryHelpers.ParseQuery(url.Query);
        query["response_type"].ToString().Should().Be("code");
        query["scope"].ToString().Should().Be("openid profile");
        query["team"].ToString().Should().Be("T0FICTION");
        query["state"].ToString().Should().Be("the-state");
        query["redirect_uri"].ToString().Should().Be(Redirect.ToString());
        query["client_id"].ToString().Should().Be("1234.5678");
    }

    [Fact]
    public async Task Completing_an_install_reads_the_workspace_and_the_bot_token()
    {
        slack.Answer("""
            {"ok":true,"token_type":"bot","access_token":"xoxb-fictional","bot_user_id":"U0BOT","team":{"id":"T0FICTION","name":"Fiction Studio"}}
            """);

        var install = await Client().CompleteInstallAsync("the-code", Redirect, TestContext.Current.CancellationToken);

        install.Should().Be(new SlackInstall("T0FICTION", "Fiction Studio", "U0BOT", "xoxb-fictional"));
        var call = slack.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be("oauth.v2.access");
        call.Form["code"].Should().Be("the-code");
        call.Form["client_id"].Should().Be("1234.5678");
        call.Form["client_secret"].Should().Be("fictional-secret");
        call.Form["redirect_uri"].Should().Be(Redirect.ToString());
        call.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task An_install_that_is_not_a_bot_token_is_refused()
    {
        slack.Answer("""{"ok":true,"token_type":"user","access_token":"xoxp-fictional","bot_user_id":"U0BOT","team":{"id":"T0","name":"X"}}""");

        var failure = await Assert.ThrowsAsync<SlackApiException>(
            () => Client().CompleteInstallAsync("c", Redirect, TestContext.Current.CancellationToken));

        failure.Code.Should().Be("not_a_bot_token");
    }

    [Fact]
    public async Task Slack_saying_not_ok_throws_with_its_error_code()
    {
        slack.Answer("""{"ok":false,"error":"invalid_code"}""");

        var failure = await Assert.ThrowsAsync<SlackApiException>(
            () => Client().CompleteInstallAsync("stale", Redirect, TestContext.Current.CancellationToken));

        failure.Code.Should().Be("invalid_code");
    }

    [Fact]
    public async Task A_refusal_without_a_code_is_unknown_and_an_unreadable_answer_has_its_own_code()
    {
        slack.Answer("""{"ok":false}""");
        slack.Answer("<html>Bad gateway</html>");

        var client = Client();
        (await Assert.ThrowsAsync<SlackApiException>(() => client.RevokeAsync("t", TestContext.Current.CancellationToken))).Code.Should().Be("unknown");
        (await Assert.ThrowsAsync<SlackApiException>(() => client.RevokeAsync("t", TestContext.Current.CancellationToken))).Code.Should().Be("unreadable_answer");
    }

    [Fact]
    public async Task Rate_limiting_and_server_errors_get_codes_the_worker_retries()
    {
        slack.Status(HttpStatusCode.TooManyRequests);
        slack.Status(HttpStatusCode.InternalServerError);
        slack.Status(HttpStatusCode.ServiceUnavailable);

        var client = Client();
        var codes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            codes.Add((await Assert.ThrowsAsync<SlackApiException>(
                () => client.PostMessageAsync("t", "C0", "hi", TestContext.Current.CancellationToken))).Code);
        }

        codes.Should().Equal("ratelimited", "http_500", "http_503");
        codes.Select(SlackErrors.ProblemFor).Should().AllSatisfy(problem => problem.Should().BeNull());
    }

    [Fact]
    public async Task A_post_is_one_request_with_the_bearer_token_and_no_link_previews()
    {
        slack.Answer("""{"ok":true}""");

        await Client().PostMessageAsync("xoxb-fictional", "C0HIRING", "<https://app.example.org/x|Robin> applied", TestContext.Current.CancellationToken);

        var call = slack.Calls.Should().ContainSingle("a retry here would post the message twice").Which;
        call.Method.Should().Be("chat.postMessage");
        call.Authorization.Should().Be("Bearer xoxb-fictional");
        call.Form["channel"].Should().Be("C0HIRING");
        call.Form["text"].Should().Be("<https://app.example.org/x|Robin> applied");
        call.Form["unfurl_links"].Should().Be("false");
        call.Form["unfurl_media"].Should().Be("false");
    }

    [Fact]
    public async Task Completing_a_link_exchanges_the_code_then_asks_userinfo_with_the_access_token()
    {
        slack.Answer("""{"ok":true,"access_token":"xoxp-link-token"}""");
        slack.Answer("""
            {"ok":true,"name":"Robin Ellis","https://slack.com/team_id":"T0FICTION","https://slack.com/user_id":"U0ROBIN"}
            """);

        var identity = await Client().CompleteLinkAsync("link-code", Redirect, TestContext.Current.CancellationToken);

        identity.Should().Be(new SlackIdentity("T0FICTION", "U0ROBIN", "Robin Ellis"));
        slack.Calls.Select(c => c.Method).Should().Equal("openid.connect.token", "openid.connect.userInfo");
        slack.Calls[0].Form["grant_type"].Should().Be("authorization_code");
        slack.Calls[0].Form["code"].Should().Be("link-code");
        slack.Calls[1].Authorization.Should().Be("Bearer xoxp-link-token");
    }

    [Fact]
    public async Task The_channel_list_follows_the_cursor_and_drops_archived_and_private_channels_the_app_is_not_in()
    {
        slack.Answer("""
            {"ok":true,"channels":[
              {"id":"C1","name":"hiring","is_private":false,"is_archived":false,"is_member":true},
              {"id":"C2","name":"old","is_private":false,"is_archived":true,"is_member":false}],
             "response_metadata":{"next_cursor":"page2"}}
            """);
        slack.Answer("""
            {"ok":true,"channels":[
              {"id":"C3","name":"leads","is_private":true,"is_archived":false,"is_member":true},
              {"id":"C4","name":"secret","is_private":true,"is_archived":false,"is_member":false},
              {"id":"C5","name":"general","is_private":false,"is_archived":false,"is_member":false}],
             "response_metadata":{"next_cursor":""}}
            """);

        var channels = await Client().ListChannelsAsync("xoxb-fictional", TestContext.Current.CancellationToken);

        channels.Select(c => c.Id).Should().Equal("C1", "C3", "C5");
        slack.Calls.Should().HaveCount(2);
        slack.Calls.Select(c => c.Method).Should().AllBe("conversations.list");
        slack.Calls[0].Form.Should().NotContainKey("cursor");
        slack.Calls[1].Form["cursor"].Should().Be("page2");
        slack.Calls[0].Form["exclude_archived"].Should().Be("true");
        slack.Calls[0].Authorization.Should().Be("Bearer xoxb-fictional");
    }

    [Fact]
    public async Task A_channel_slack_cannot_find_is_null_and_other_refusals_throw()
    {
        slack.Answer("""{"ok":false,"error":"channel_not_found"}""");
        slack.Answer("""{"ok":false,"error":"token_revoked"}""");

        var client = Client();
        (await client.GetChannelAsync("t", "C404", TestContext.Current.CancellationToken)).Should().BeNull();
        (await Assert.ThrowsAsync<SlackApiException>(() => client.GetChannelAsync("t", "C1", TestContext.Current.CancellationToken)))
            .Problem.Should().Be(ScreenHire.Domain.Slack.SlackProblem.Disconnected);
    }

    [Fact]
    public async Task Opening_a_direct_message_returns_the_conversation_id()
    {
        slack.Answer("""{"ok":true,"channel":{"id":"D0ROBIN"}}""");

        var id = await Client().OpenDirectMessageAsync("t", "U0ROBIN", TestContext.Current.CancellationToken);

        id.Should().Be("D0ROBIN");
        slack.Calls.Single().Form["users"].Should().Be("U0ROBIN");
    }

    /// <summary>Answers the client's calls in order and records each: its method, form and Authorization header, read before the client disposes the request.</summary>
    private sealed class StubSlack : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> answers = new();

        public List<Call> Calls { get; } = [];

        public void Answer(string json) => answers.Enqueue((HttpStatusCode.OK, json));

        public void Status(HttpStatusCode status) => answers.Enqueue((status, ""));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = request.Content is null
                ? []
                : QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.Value.ToString());
            Calls.Add(new Call(request.RequestUri!.AbsolutePath.Split('/')[^1], form, request.Headers.Authorization?.ToString()));
            var (status, body) = answers.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed record Call(string Method, Dictionary<string, string> Form, string? Authorization);
}
