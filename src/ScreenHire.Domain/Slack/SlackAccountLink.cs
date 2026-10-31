using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Slack;

/// <summary>
/// One member's Slack account in their organization's workspace, linked by them through Sign in with Slack, so
/// ScreenHire can send them their own mentions and tasks as direct messages. Goes with the workspace.
/// </summary>
public sealed class SlackAccountLink : ITenantOwned
{
    public const int MaxNameLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid InstallationId { get; init; }

    public Guid UserId { get; init; }

    /// <summary>Slack's id for the person in the workspace (<c>U...</c> or <c>W...</c>).</summary>
    public string SlackUserId { get; private set; } = "";

    /// <summary>Their name in Slack when they linked, to show which account it is.</summary>
    public string SlackName { get; private set; } = "";

    public DateTimeOffset LinkedAt { get; private set; }

    /// <summary>A direct message when someone mentions them in a note.</summary>
    public bool Mentions { get; set; } = true;

    /// <summary>A direct message when someone gives them a task.</summary>
    public bool Tasks { get; set; } = true;

    /// <summary><see cref="SlackProblem.CannotMessage"/> once Slack refused a direct message, until one goes through or they link again.</summary>
    public SlackProblem? Problem { get; private set; }

    public DateTimeOffset? ProblemAt { get; private set; }

    public static SlackAccountLink Create(Guid organizationId, Guid installationId, Guid userId, string slackUserId, string slackName, DateTimeOffset now) =>
        new()
        {
            OrganizationId = organizationId,
            InstallationId = installationId,
            UserId = userId,
            SlackUserId = slackUserId,
            SlackName = slackName,
            LinkedAt = now,
        };

    /// <summary>Links again, perhaps to another account: whatever Slack refused before no longer applies.</summary>
    public void Relink(string slackUserId, string slackName, DateTimeOffset now)
    {
        SlackUserId = slackUserId;
        SlackName = slackName;
        LinkedAt = now;
        Problem = null;
        ProblemAt = null;
    }

    public void RecordProblem(DateTimeOffset now)
    {
        if (Problem is null)
        {
            Problem = SlackProblem.CannotMessage;
            ProblemAt = now;
        }
    }

    public void ClearProblem()
    {
        Problem = null;
        ProblemAt = null;
    }
}
