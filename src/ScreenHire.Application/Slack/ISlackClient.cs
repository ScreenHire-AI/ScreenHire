using ScreenHire.Domain.Slack;

namespace ScreenHire.Application.Slack;

/// <summary>
/// What ScreenHire asks of Slack (docs/architecture/slack.md): the two OAuth flows, the channel list, and posting.
/// <see cref="SlackApiClient"/> calls Slack's Web API; <see cref="SandboxSlackClient"/> is the local stand-in. A call
/// Slack refuses throws <see cref="SlackApiException"/> with Slack's error code.
/// </summary>
public interface ISlackClient
{
    /// <summary>Where to send the browser to add the app to a workspace.</summary>
    Uri InstallUrl(string state, Uri redirectUri);

    /// <summary>Where to send the browser to sign in with Slack, in <paramref name="teamId"/>'s workspace.</summary>
    Uri LinkUrl(string state, Uri redirectUri, string teamId);

    /// <summary>Exchanges the install's code for the workspace and its bot token.</summary>
    Task<SlackInstall> CompleteInstallAsync(string code, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>Exchanges a sign-in's code for who signed in, and in which workspace.</summary>
    Task<SlackIdentity> CompleteLinkAsync(string code, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>The channels the app can post to: public ones, and private ones it was invited to. Never archived ones.</summary>
    Task<IReadOnlyList<SlackChannel>> ListChannelsAsync(string token, CancellationToken cancellationToken);

    /// <summary>One channel, or null when Slack cannot find it.</summary>
    Task<SlackChannel?> GetChannelAsync(string token, string channelId, CancellationToken cancellationToken);

    /// <summary>The app's direct message with the person, opened if it is not yet.</summary>
    Task<string> OpenDirectMessageAsync(string token, string slackUserId, CancellationToken cancellationToken);

    /// <summary>Posts <paramref name="text"/> (Slack mrkdwn) to a channel or direct message.</summary>
    Task PostMessageAsync(string token, string conversation, string text, CancellationToken cancellationToken);

    /// <summary>Gives the token back, so it stops working.</summary>
    Task RevokeAsync(string token, CancellationToken cancellationToken);
}

public sealed record SlackInstall(string TeamId, string TeamName, string BotUserId, string BotToken);

public sealed record SlackIdentity(string TeamId, string UserId, string Name);

public sealed record SlackChannel(string Id, string Name, bool IsPrivate, bool IsArchived, bool IsMember);

/// <summary>Slack refused a call, with its error code (such as <c>not_in_channel</c>), or did not answer properly.</summary>
public sealed class SlackApiException : Exception
{
    public SlackApiException()
        : this("unknown")
    {
    }

    public SlackApiException(string code)
        : base($"Slack answered {code}.")
    {
        Code = code;
    }

    public SlackApiException(string code, Exception innerException)
        : base($"Slack answered {code}.", innerException)
    {
        Code = code;
    }

    public string Code { get; } = "unknown";

    /// <summary>What a retry cannot fix, or null when the call may work if tried again (<see cref="SlackErrors"/>).</summary>
    public SlackProblem? Problem => SlackErrors.ProblemFor(Code);
}

/// <summary>
/// Which of Slack's error codes mean a retry is pointless, and what the page says about them. Everything else (rate
/// limits, Slack's own outages, timeouts, a code this list does not know) is worth retrying, so it throws into the
/// worker's retry policy and ends in the dead-letter table if it never works.
/// </summary>
public static class SlackErrors
{
    public static SlackProblem? ProblemFor(string code) => code switch
    {
        "invalid_auth" or "not_authed" or "token_revoked" or "token_expired" or "account_inactive" or "missing_scope"
            or "team_access_not_granted" or "not_allowed_token_type" or "app_uninstalled" => SlackProblem.Disconnected,
        "channel_not_found" => SlackProblem.ChannelNotFound,
        "not_in_channel" => SlackProblem.NotInChannel,
        "is_archived" => SlackProblem.ChannelArchived,
        "no_permission" or "posting_to_channel_denied" or "restricted_action" => SlackProblem.CannotPost,
        _ when code.StartsWith("restricted_action_", StringComparison.Ordinal) => SlackProblem.CannotPost,
        "user_not_found" or "user_disabled" or "cannot_dm_bot" or "messages_tab_disabled" or "user_not_visible" => SlackProblem.CannotMessage,
        _ => null,
    };

    /// <summary>A problem posting to the channel: there, Slack's "cannot message" codes mean the workspace stops the app posting.</summary>
    public static SlackProblem InChannel(SlackProblem problem) =>
        problem == SlackProblem.CannotMessage ? SlackProblem.CannotPost : problem;
}
