using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Slack;

/// <summary>
/// An organization's Slack workspace (docs/architecture/slack.md): the bot token ScreenHire posts with, protected at
/// rest, the channel the team picked and the events posted there. One per organization. The row holds a secret, so
/// change history never reads it (ADR-0016): the page says when it was connected and by whom instead.
/// </summary>
public sealed class SlackInstallation : ITenantOwned
{
    public const int MaxTeamNameLength = 200;

    public const int MaxIdLength = 40;

    public const int MaxChannelNameLength = 200;

    public const int MaxTokenLength = 4000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>Slack's id for the workspace (<c>T...</c>).</summary>
    public string TeamId { get; private set; } = "";

    public string TeamName { get; private set; } = "";

    /// <summary>The app's bot user in that workspace (<c>U...</c>).</summary>
    public string BotUserId { get; private set; } = "";

    /// <summary>The bot token, protected with ASP.NET Core Data Protection under the organization's own purpose. Never sent anywhere.</summary>
    public string ProtectedBotToken { get; private set; } = "";

    /// <summary>The channel posts go to, or null until someone picks one.</summary>
    public string? ChannelId { get; private set; }

    /// <summary>The channel's name when it was picked, for the page; Slack's own name may have changed since.</summary>
    public string? ChannelName { get; private set; }

    /// <summary>The events posted to the channel.</summary>
    public List<SlackChannelEvent> Events { get; private set; } = [];

    public DateTimeOffset ConnectedAt { get; private set; }

    /// <summary>Who connected the workspace last (null once they leave the organization's users).</summary>
    public Guid? ConnectedByUserId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Why posting stopped working, until a post succeeds, the channel changes or the workspace is reconnected.</summary>
    public SlackProblem? Problem { get; private set; }

    public DateTimeOffset? ProblemAt { get; private set; }

    /// <summary>Posting stopped because the token no longer works; only connecting again helps.</summary>
    public bool IsDisconnected => Problem == SlackProblem.Disconnected;

    public static SlackInstallation Connect(
        Guid organizationId, string teamId, string teamName, string botUserId, string protectedBotToken, Guid? userId, DateTimeOffset now) =>
        new()
        {
            OrganizationId = organizationId,
            TeamId = teamId,
            TeamName = teamName,
            BotUserId = botUserId,
            ProtectedBotToken = protectedBotToken,
            ConnectedAt = now,
            ConnectedByUserId = userId,
            UpdatedAt = now,
        };

    /// <summary>
    /// Connects again, with a new token. The same workspace keeps its channel and events; another one starts over,
    /// and the caller removes the account links, which belong to the old workspace. True when the workspace changed.
    /// </summary>
    public bool Reconnect(string teamId, string teamName, string botUserId, string protectedBotToken, Guid? userId, DateTimeOffset now)
    {
        var changed = !string.Equals(teamId, TeamId, StringComparison.Ordinal);
        TeamId = teamId;
        TeamName = teamName;
        BotUserId = botUserId;
        ProtectedBotToken = protectedBotToken;
        ConnectedAt = now;
        ConnectedByUserId = userId;
        UpdatedAt = now;
        Problem = null;
        ProblemAt = null;
        if (changed)
        {
            ChannelId = null;
            ChannelName = null;
            Events = [];
        }

        return changed;
    }

    /// <summary>Picks the channel and the events posted to it. A problem with the old channel no longer applies.</summary>
    public void ChooseChannel(string channelId, string channelName, IEnumerable<SlackChannelEvent> events, DateTimeOffset now)
    {
        ChannelId = channelId;
        ChannelName = channelName;
        Events = [.. events.Distinct().Order()];
        UpdatedAt = now;
        if (Problem is not SlackProblem.Disconnected)
        {
            Problem = null;
            ProblemAt = null;
        }
    }

    public void RecordProblem(SlackProblem problem, DateTimeOffset now)
    {
        if (Problem == problem)
        {
            return;
        }

        Problem = problem;
        ProblemAt = now;
    }

    /// <summary>A post went through, so a channel problem is over. Being disconnected is only cleared by connecting again.</summary>
    public void ClearChannelProblem()
    {
        if (Problem is { } problem && problem != SlackProblem.Disconnected)
        {
            Problem = null;
            ProblemAt = null;
        }
    }
}

/// <summary>What the team can have posted to its channel. The names are stored; never rename one.</summary>
public enum SlackChannelEvent
{
    ApplicationSubmitted,
    ApplicationMoved,
    AutomationFailed,
    JobPublished,
}

/// <summary>Why Slack stopped taking ScreenHire's messages, in the terms the page explains.</summary>
public enum SlackProblem
{
    /// <summary>The token was revoked or the app removed from the workspace.</summary>
    Disconnected,

    /// <summary>The channel was deleted, or the app can no longer find it.</summary>
    ChannelNotFound,

    /// <summary>A private channel the app was never invited to, or was removed from.</summary>
    NotInChannel,

    ChannelArchived,

    /// <summary>The workspace's settings stop the app posting there.</summary>
    CannotPost,

    /// <summary>Slack would not open a direct message with the person.</summary>
    CannotMessage,
}
