using ScreenHire.Domain.Slack;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// Slack for the signed-in member (docs/architecture/slack.md). <c>available</c> is false when this ScreenHire has no
/// Slack app, and then nothing else is set. <c>workspace</c> is null until someone connects one. <c>account</c> is the
/// member's own link. <c>settings</c> is there only for someone who manages integrations and sees every job. The bot
/// token is never in any answer.
/// </summary>
/// <remarks><c>sandbox</c>: true when this is the local sandbox workspace, not Slack.</remarks>
public sealed record SlackStatus(bool Available, bool Sandbox, SlackWorkspace? Workspace, SlackAccount? Account, SlackSettings? Settings);

/// <summary>The connected workspace, as every member sees it. <c>disconnected</c>: Slack stopped taking the app's token.</summary>
public sealed record SlackWorkspace(string TeamName, bool Disconnected);

/// <summary>
/// The member's linked Slack account: its name, and whether mentions and tasks given to them arrive as direct
/// messages. <c>problem</c> is <c>CannotMessage</c> when Slack refused the last one.
/// </summary>
public sealed record SlackAccount(string SlackName, DateTimeOffset LinkedAt, bool Mentions, bool Tasks, SlackProblem? Problem);

/// <summary>
/// The workspace's settings: the channel (null until picked), the events posted there, who connected it and when,
/// and why posting stopped working, if it did.
/// </summary>
public sealed record SlackSettings(
    SlackChannelChoice? Channel,
    IReadOnlyList<SlackChannelEvent> Events,
    DateTimeOffset ConnectedAt,
    string? ConnectedBy,
    SlackProblem? Problem,
    DateTimeOffset? ProblemAt);

/// <summary>A channel by Slack's id and its name, without the <c>#</c>.</summary>
public sealed record SlackChannelChoice(string Id, string Name);

/// <summary>A channel the app can post to. <c>isPrivate</c>: only for invited members, the app among them.</summary>
public sealed record SlackChannelOption(string Id, string Name, bool IsPrivate);

/// <summary>Where to send the browser next: Slack's page (or the sandbox's) for an OAuth round trip.</summary>
public sealed record SlackAuthorization(string AuthorizeUrl);

/// <summary>Links the member's Slack account. <c>returnUrl</c>: the dashboard path to come back to, a local path only.</summary>
public sealed record StartSlackLinkRequest(string? ReturnUrl);

/// <summary>The channel to post to, by its id from the channel list, and the events posted there (none is allowed).</summary>
public sealed record UpdateSlackChannelRequest(string ChannelId, IReadOnlyList<SlackChannelEvent> Events);

/// <summary>Whether mentions and tasks given to the member arrive as direct messages.</summary>
public sealed record UpdateSlackAccountRequest(bool Mentions, bool Tasks);

/// <summary>Whether the test message reached the channel, and if not, why.</summary>
public sealed record SlackTestResult(bool Posted, SlackProblem? Problem);

/// <summary>A message the local sandbox workspace received: its channel or direct message id, its text and when.</summary>
public sealed record SlackSandboxMessageEntry(string Conversation, string Text, DateTimeOffset PostedAt);
