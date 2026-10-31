using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Slack;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Application.Slack;

/// <summary>
/// The local sandbox workspace: Slack's part of every flow with no third party, for Development and tests, as the
/// sandbox board is for job distribution. Its authorize pages are the API's own <see cref="AuthorizePath"/>, which
/// sends the browser straight back with a code, and what it "posts" lands in <c>slack_sandbox_messages</c>, where
/// the tests and <c>GET /v1/slack/sandbox/messages</c> read it.
/// </summary>
/// <remarks>
/// Its channels exercise each answer the page explains: <c>#old-hiring</c> is archived (never listed, refused on
/// save) and <c>#announcements</c> only lets admins post, so a post there fails with <c>restricted_action</c>.
/// The tenant is the concrete <see cref="TenantContext"/>, not <see cref="ITenantContext"/>: <c>SlackAccess</c> holds this
/// client, so Wolverine's generated handler code builds it, and a handler that takes both types gets two variables of the
/// same name and does not compile (the tenant middleware already takes <see cref="TenantContext"/>).
/// </remarks>
public sealed class SandboxSlackClient(ScreenHireDbContext db, TenantContext tenant, TimeProvider time) : ISlackClient
{
    public const string AuthorizePath = "/v1/slack/sandbox/authorize";

    public const string TeamId = "T0SANDBOX";

    public const string TeamName = "ScreenHire sandbox";

    public const string BotUserId = "U0SANDBOXBOT";

    public const string InstallCode = "sandbox-install";

    /// <summary>The prefix of a link's code; the signed-in person's user id follows.</summary>
    public const string LinkCodePrefix = "sandbox-link.";

    private const string TokenPrefix = "xoxb-sandbox-";

    public static readonly IReadOnlyList<SlackChannel> Channels =
    [
        new("C0SBHIRING", "hiring", IsPrivate: false, IsArchived: false, IsMember: true),
        new("C0SBRECRUIT", "recruiting", IsPrivate: false, IsArchived: false, IsMember: false),
        new("C0SBGENERAL", "general", IsPrivate: false, IsArchived: false, IsMember: false),
        new("C0SBANNOUNCE", "announcements", IsPrivate: false, IsArchived: false, IsMember: false),
        new("C0SBOLDHIRE", "old-hiring", IsPrivate: false, IsArchived: true, IsMember: false),
    ];

    /// <summary>The channel where only admins may post, so the app's posts fail.</summary>
    public const string RestrictedChannelId = "C0SBANNOUNCE";

    /// <summary>The sandbox's id for a ScreenHire user: stable, so linking twice gives the same account.</summary>
    public static string SlackUserIdFor(Guid userId) =>
        $"U0SB{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N"))))[..8]}";

    /// <summary>The direct message the sandbox opens with a person.</summary>
    public static string DirectMessageId(string slackUserId) => $"D{slackUserId}";

    public Uri InstallUrl(string state, Uri redirectUri) => Authorize("install", state, redirectUri);

    public Uri LinkUrl(string state, Uri redirectUri, string teamId) => Authorize("link", state, redirectUri);

    public Task<SlackInstall> CompleteInstallAsync(string code, Uri redirectUri, CancellationToken cancellationToken) =>
        code == InstallCode && tenant.OrganizationId is { } organizationId
            ? Task.FromResult(new SlackInstall(TeamId, TeamName, BotUserId, $"{TokenPrefix}{organizationId:N}"))
            : throw new SlackApiException("invalid_code");

    public async Task<SlackIdentity> CompleteLinkAsync(string code, Uri redirectUri, CancellationToken cancellationToken)
    {
        // The code comes back in the address, so it only names the person signed in here: anyone else's id would read
        // another person's name, in any organization (Users is not a tenant table).
        if (!code.StartsWith(LinkCodePrefix, StringComparison.Ordinal) || !Guid.TryParse(code[LinkCodePrefix.Length..], out var userId)
            || userId != tenant.UserId)
        {
            throw new SlackApiException("invalid_code");
        }

        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName ?? u.Email).SingleOrDefaultAsync(cancellationToken)
            ?? throw new SlackApiException("invalid_code");
        return new SlackIdentity(TeamId, SlackUserIdFor(userId), name);
    }

    public Task<IReadOnlyList<SlackChannel>> ListChannelsAsync(string token, CancellationToken cancellationToken)
    {
        Check(token);
        return Task.FromResult<IReadOnlyList<SlackChannel>>([.. Channels.Where(c => !c.IsArchived)]);
    }

    public Task<SlackChannel?> GetChannelAsync(string token, string channelId, CancellationToken cancellationToken)
    {
        Check(token);
        return Task.FromResult(Channels.FirstOrDefault(c => c.Id == channelId));
    }

    public Task<string> OpenDirectMessageAsync(string token, string slackUserId, CancellationToken cancellationToken)
    {
        Check(token);
        return slackUserId.StartsWith("U0SB", StringComparison.Ordinal)
            ? Task.FromResult(DirectMessageId(slackUserId))
            : throw new SlackApiException("user_not_found");
    }

    public async Task PostMessageAsync(string token, string conversation, string text, CancellationToken cancellationToken)
    {
        Check(token);
        if (conversation == RestrictedChannelId)
        {
            throw new SlackApiException("restricted_action");
        }

        if (Channels.FirstOrDefault(c => c.Id == conversation) is { IsArchived: true })
        {
            throw new SlackApiException("is_archived");
        }

        if (!conversation.StartsWith('D') && Channels.All(c => c.Id != conversation))
        {
            throw new SlackApiException("channel_not_found");
        }

        db.SlackSandboxMessages.Add(new SlackSandboxMessage
        {
            OrganizationId = tenant.OrganizationId ?? throw new InvalidOperationException("The sandbox posts only as an organization."),
            Conversation = conversation,
            Text = text,
            PostedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RevokeAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;

    // A token of another organization is refused as Slack refuses a token of another workspace.
    private void Check(string token)
    {
        if (token != $"{TokenPrefix}{tenant.OrganizationId:N}")
        {
            throw new SlackApiException("invalid_auth");
        }
    }

    private static Uri Authorize(string kind, string state, Uri redirectUri) =>
        new(redirectUri, $"{AuthorizePath}?kind={kind}&state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri.ToString())}");
}
