using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Slack;

/// <summary>
/// Slack's Web API (https://docs.slack.dev/reference/methods) with the configured app. Every method is a form POST that
/// answers <c>{"ok": true, ...}</c> or <c>{"ok": false, "error": "code"}</c>; a refusal throws
/// <see cref="SlackApiException"/> with the code. Nothing here logs a token or a message's text.
/// </summary>
/// <remarks>
/// One long-lived <see cref="HttpClient"/> of its own rather than one from the HTTP client factory, as
/// <c>HttpWebhookSender</c> does: the service defaults add the standard resilience handler to every factory client, and
/// its retries would post a message twice. The worker's retry policy is the one place a failed call is tried again.
/// </remarks>
public sealed class SlackApiClient : ISlackClient, IDisposable
{
    /// <summary>What the bot needs: post to public channels without joining them, read the channel list, open DMs.</summary>
    public static readonly IReadOnlyList<string> BotScopes = ["chat:write", "chat:write.public", "channels:read", "groups:read", "im:write"];

    /// <summary>Sign in with Slack: who the person is, and their name to show which account is linked.</summary>
    public static readonly IReadOnlyList<string> LinkScopes = ["openid", "profile"];

    /// <summary>The most channels the picker lists: five pages of Slack's largest page.</summary>
    public const int MaxChannels = 1000;

    private const int PageSize = 200;

    private readonly SlackOptions options;
    private readonly HttpClient client;

    public SlackApiClient(IOptions<SlackOptions> options)
        : this(options, new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
    }

    /// <summary>With <paramref name="handler"/> in place of the network, for tests.</summary>
    public SlackApiClient(IOptions<SlackOptions> options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        client = new HttpClient(handler) { BaseAddress = this.options.ApiBaseUrl, Timeout = this.options.Timeout };
    }

    public Uri InstallUrl(string state, Uri redirectUri) =>
        Authorize("oauth/v2/authorize", new()
        {
            ["client_id"] = ClientId,
            ["scope"] = string.Join(',', BotScopes),
            ["redirect_uri"] = redirectUri.ToString(),
            ["state"] = state,
        });

    public Uri LinkUrl(string state, Uri redirectUri, string teamId) =>
        Authorize("openid/connect/authorize", new()
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["scope"] = string.Join(' ', LinkScopes),
            ["redirect_uri"] = redirectUri.ToString(),
            ["state"] = state,
            ["team"] = teamId,
        });

    public async Task<SlackInstall> CompleteInstallAsync(string code, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        using var answer = await CallAsync("oauth.v2.access", null, new()
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri.ToString(),
        }, cancellationToken);
        var root = answer.RootElement;
        if (Text(root, "token_type") != "bot")
        {
            throw new SlackApiException("not_a_bot_token");
        }

        var team = root.TryGetProperty("team", out var t) ? t : default;
        return new SlackInstall(Required(team, "id"), Text(team, "name") ?? "", Required(root, "bot_user_id"), Required(root, "access_token"));
    }

    public async Task<SlackIdentity> CompleteLinkAsync(string code, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        string accessToken;
        using (var token = await CallAsync("openid.connect.token", null, new()
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri.ToString(),
            ["grant_type"] = "authorization_code",
        }, cancellationToken))
        {
            accessToken = Required(token.RootElement, "access_token");
        }

        // The person's claims straight from Slack over TLS, so no ID token needs its signature checked.
        using var info = await CallAsync("openid.connect.userInfo", accessToken, [], cancellationToken);
        var claims = info.RootElement;
        return new SlackIdentity(
            Required(claims, "https://slack.com/team_id"), Required(claims, "https://slack.com/user_id"), Text(claims, "name") ?? "");
    }

    public async Task<IReadOnlyList<SlackChannel>> ListChannelsAsync(string token, CancellationToken cancellationToken)
    {
        var channels = new List<SlackChannel>();
        string? cursor = null;
        do
        {
            var form = new Dictionary<string, string>
            {
                ["types"] = "public_channel,private_channel",
                ["exclude_archived"] = "true",
                ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
            };
            if (cursor is not null)
            {
                form["cursor"] = cursor;
            }

            using var page = await CallAsync("conversations.list", token, form, cancellationToken);
            var root = page.RootElement;
            if (root.TryGetProperty("channels", out var list))
            {
                channels.AddRange(list.EnumerateArray().Select(Channel));
            }

            cursor = root.TryGetProperty("response_metadata", out var meta) ? Text(meta, "next_cursor") : null;
        }
        while (!string.IsNullOrEmpty(cursor) && channels.Count < MaxChannels);

        return [.. channels.Where(c => !c.IsArchived && (!c.IsPrivate || c.IsMember)).Take(MaxChannels)];
    }

    public async Task<SlackChannel?> GetChannelAsync(string token, string channelId, CancellationToken cancellationToken)
    {
        try
        {
            using var answer = await CallAsync("conversations.info", token, new() { ["channel"] = channelId }, cancellationToken);
            return Channel(answer.RootElement.GetProperty("channel"));
        }
        catch (SlackApiException e) when (e.Code == "channel_not_found")
        {
            return null;
        }
    }

    public async Task<string> OpenDirectMessageAsync(string token, string slackUserId, CancellationToken cancellationToken)
    {
        using var answer = await CallAsync("conversations.open", token, new() { ["users"] = slackUserId }, cancellationToken);
        return Required(answer.RootElement.GetProperty("channel"), "id");
    }

    public async Task PostMessageAsync(string token, string conversation, string text, CancellationToken cancellationToken)
    {
        // No previews: every link is to the dashboard, which needs a sign-in Slack's crawler does not have.
        using var _ = await CallAsync("chat.postMessage", token, new()
        {
            ["channel"] = conversation,
            ["text"] = text,
            ["unfurl_links"] = "false",
            ["unfurl_media"] = "false",
        }, cancellationToken);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var _ = await CallAsync("auth.revoke", token, [], cancellationToken);
    }

    public void Dispose() => client.Dispose();

    private string ClientId => options.ClientId ?? throw new InvalidOperationException("No Slack client id is configured.");

    private string ClientSecret => options.ClientSecret ?? throw new InvalidOperationException("No Slack client secret is configured.");

    private Uri Authorize(string path, Dictionary<string, string> query) =>
        new(options.AuthorizeBaseUrl, $"{path}?{string.Join('&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))}");

    private async Task<JsonDocument> CallAsync(string method, string? token, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, method) { Content = new FormUrlEncodedContent(form) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new SlackApiException("unreachable", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SlackApiException("timeout", e);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new SlackApiException("ratelimited");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SlackApiException(string.Create(CultureInfo.InvariantCulture, $"http_{(int)response.StatusCode}"));
            }

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            }
            catch (JsonException e)
            {
                throw new SlackApiException("unreadable_answer", e);
            }

            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                return document;
            }

            var code = root.ValueKind == JsonValueKind.Object ? Text(root, "error") ?? "unknown" : "unknown";
            document.Dispose();
            throw new SlackApiException(code);
        }
    }

    private static SlackChannel Channel(JsonElement channel) =>
        new(Required(channel, "id"), Text(channel, "name") ?? "", Flag(channel, "is_private"), Flag(channel, "is_archived"), Flag(channel, "is_member"));

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Required(JsonElement element, string name) =>
        Text(element, name) is { Length: > 0 } value ? value : throw new SlackApiException("unreadable_answer");

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
