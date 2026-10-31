using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Webhooks;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Slack;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.Attributes;

namespace ScreenHire.Application.Slack;

/// <summary>
/// Post one event to the organization's channel, if it picked that event. Cascaded by the handler of the domain event
/// it comes from, beside the webhook fan-out, so it retries and fails apart from that handler's own work. Ids only.
/// </summary>
/// <remarks><c>eventId</c>: the webhook event's id for the same domain event, stable across redeliveries.</remarks>
public sealed record PostSlackChannelMessage(Guid OrganizationId, SlackChannelEvent Event, Guid EventId, Guid JobId, Guid? ApplicationId)
    : ITenantMessage
{
    public string IdempotencyKey => $"channel:{EventId}";
}

/// <summary>
/// Send one person a direct message about a mention or a task given to them, if they linked Slack and want that
/// kind. Ids only. <c>sourceId</c> is the mention or the task.
/// </summary>
public sealed record SendSlackDirectMessage(
    Guid OrganizationId, SlackDirectMessageKind Kind, Guid SourceId, Guid RecipientUserId, Guid ApplicationId, Guid JobId, Guid? ActorUserId)
    : ITenantMessage
{
    public string IdempotencyKey => $"dm:{Kind}:{SourceId}:{RecipientUserId}";
}

/// <summary>
/// The channel messages each domain event raises, beside its webhook event (<see cref="WebhookEvents"/>) and with the
/// same id. Null when this host has no Slack, so nothing is queued for nothing.
/// </summary>
public static class SlackEvents
{
    public static PostSlackChannelMessage? From(ApplicationSubmitted e, SlackAccess slack)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(slack);
        return slack.IsConfigured
            ? new(e.OrganizationId, SlackChannelEvent.ApplicationSubmitted,
                WebhookEvents.EventId(WebhookEventTypes.ApplicationSubmitted, e.EventId.ToString()), e.JobId, e.ApplicationId)
            : null;
    }

    public static PostSlackChannelMessage? From(ApplicationMoved e, SlackAccess slack)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(slack);
        return slack.IsConfigured
            ? new(e.OrganizationId, SlackChannelEvent.ApplicationMoved,
                WebhookEvents.EventId(WebhookEventTypes.ApplicationMoved, e.EventId.ToString()), e.JobId, e.ApplicationId)
            : null;
    }

    /// <summary>A failed or blocked run, the two outcomes the automation issues list shows.</summary>
    public static PostSlackChannelMessage? From(AutomationRunFinished e, SlackAccess slack)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(slack);
        return slack.IsConfigured && e.Status is AutomationRunStatus.Failed or AutomationRunStatus.Blocked
            ? new(e.OrganizationId, SlackChannelEvent.AutomationFailed,
                WebhookEvents.EventId(WebhookEventTypes.AutomationFailed, e.RunId.ToString()), e.JobId, e.ApplicationId)
            : null;
    }

    /// <summary>The first publish only: republishing changed content is not news for the channel.</summary>
    public static PostSlackChannelMessage? From(JobPublished e, SlackAccess slack)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(slack);
        return slack.IsConfigured && e.Version == 1
            ? new(e.OrganizationId, SlackChannelEvent.JobPublished,
                WebhookEvents.EventId(WebhookEventTypes.JobPublished, $"{e.JobId}:{e.Version}"), e.JobId, null)
            : null;
    }
}

/// <summary>
/// Posts a channel event (docs/architecture/slack.md). Idempotent three ways: the inbox, <c>[Deduplicated]</c> on the
/// message's key, and the <see cref="SlackMessageRecord"/> written with the post. A crash between Slack's answer and the
/// commit can repeat one message, as email can. A refusal a retry cannot fix is recorded on the workspace for the page
/// to explain; anything else throws into the retry policy.
/// </summary>
public static partial class PostSlackChannelMessageHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(PostSlackChannelMessage.IdempotencyKey))]
    public static async Task Handle(
        PostSlackChannelMessage message,
        ScreenHireDbContext db,
        SlackAccess slack,
        SlackTokens tokens,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<PostSlackChannelMessage> logger,
        CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured)
        {
            return;
        }

        var installation = await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken);
        if (installation is not { IsDisconnected: false, ChannelId: { } channelId } || !installation.Events.Contains(message.Event)
            || await SlackRecords.ExistAsync(db, message.IdempotencyKey, cancellationToken))
        {
            return; // no workspace, no channel, not wanted, or posted already
        }

        var text = await ComposeAsync(db, message, links.Value, cancellationToken);
        if (text is null)
        {
            LogNotPosted(logger, message.Event, message.OrganizationId, "the record is gone");
            return;
        }

        var now = time.GetUtcNow();
        if (tokens.Unprotect(message.OrganizationId, installation.ProtectedBotToken) is not { } token)
        {
            installation.RecordProblem(SlackProblem.Disconnected, now);
            return;
        }

        try
        {
            await slack.Client.PostMessageAsync(token, channelId, text, cancellationToken);
        }
        catch (SlackApiException e) when (e.Problem is { } problem)
        {
            // In a channel, Slack's "cannot message" codes mean the workspace stops the app posting there.
            installation.RecordProblem(problem == SlackProblem.CannotMessage ? SlackProblem.CannotPost : problem, now);
            SlackRecords.Add(db, message.OrganizationId, message.IdempotencyKey, SlackMessageOutcome.Refused, e.Code, now);
            LogNotPosted(logger, message.Event, message.OrganizationId, e.Code);
            return;
        }

        installation.ClearChannelProblem();
        SlackRecords.Add(db, message.OrganizationId, message.IdempotencyKey, SlackMessageOutcome.Sent, null, now);
        LogPosted(logger, message.Event, message.OrganizationId);
    }

    private static async Task<string?> ComposeAsync(
        ScreenHireDbContext db, PostSlackChannelMessage message, DashboardLinkOptions links, CancellationToken cancellationToken)
    {
        if (message.Event == SlackChannelEvent.JobPublished)
        {
            var title = await db.Jobs.AsNoTracking().Where(j => j.Id == message.JobId).Select(j => j.Title).SingleOrDefaultAsync(cancellationToken);
            return title is null ? null : SlackMessages.JobPublished(title, links.Url($"/jobs/{message.JobId}"));
        }

        if (message.ApplicationId is not { } applicationId
            || await SlackRecords.PlaceAsync(db, applicationId, cancellationToken) is not { } place)
        {
            return null;
        }

        var path = message.Event == SlackChannelEvent.AutomationFailed
            ? $"/applications/{applicationId}?tab=activity"
            : $"/applications/{applicationId}";
        return SlackMessages.ForChannel(message.Event, place.Candidate, place.Job, links.Url(path));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted {Event} to Slack for organization {OrganizationId}")]
    private static partial void LogPosted(ILogger logger, SlackChannelEvent @event, Guid organizationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Did not post {Event} to Slack for organization {OrganizationId}: {Reason}")]
    private static partial void LogNotPosted(ILogger logger, SlackChannelEvent @event, Guid organizationId, string reason);
}

/// <summary>
/// Sends a person a direct message about their mention or task, with the same three layers of idempotency as
/// <see cref="PostSlackChannelMessageHandler"/>. Only to someone who linked Slack, wants that kind, did not do it
/// themselves and still sees the job (ADR-0015); a mention removed or a task done since sends nothing.
/// </summary>
public static partial class SendSlackDirectMessageHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendSlackDirectMessage.IdempotencyKey))]
    public static async Task Handle(
        SendSlackDirectMessage message,
        ScreenHireDbContext db,
        SlackAccess slack,
        SlackTokens tokens,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<SendSlackDirectMessage> logger,
        CancellationToken cancellationToken)
    {
        if (!slack.IsConfigured || message.RecipientUserId == message.ActorUserId)
        {
            return;
        }

        var installation = await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken);
        var link = installation is null
            ? null
            : await db.SlackAccountLinks.SingleOrDefaultAsync(l => l.UserId == message.RecipientUserId, cancellationToken);
        if (installation is not { IsDisconnected: false } || link is null
            || !(message.Kind == SlackDirectMessageKind.Mention ? link.Mentions : link.Tasks)
            || await SlackRecords.ExistAsync(db, message.IdempotencyKey, cancellationToken))
        {
            return; // no workspace, not linked, not wanted, or sent already
        }

        var stillThere = message.Kind == SlackDirectMessageKind.Mention
            ? await db.ApplicationNoteMentions.AsNoTracking().AnyAsync(m => m.Id == message.SourceId, cancellationToken)
            : await db.ApplicationTasks.AsNoTracking().AnyAsync(t => t.Id == message.SourceId && t.CompletedAt == null, cancellationToken);
        if (!stillThere
            || !await JobAccessQueries.SeesJobAsync(db, message.OrganizationId, message.RecipientUserId, message.JobId, cancellationToken)
            || await SlackRecords.PlaceAsync(db, message.ApplicationId, cancellationToken) is not { } place)
        {
            LogNotSent(logger, message.Kind, message.SourceId, "gone, done, or the person no longer sees the job");
            return;
        }

        var text = message.Kind == SlackDirectMessageKind.Mention
            ? SlackMessages.Mentioned(place.Candidate, place.Job, links.Value.Url(await NotePathAsync(db, message, cancellationToken)))
            : SlackMessages.TaskGiven(place.Candidate, place.Job, links.Value.Url($"/applications/{message.ApplicationId}"));

        var now = time.GetUtcNow();
        if (tokens.Unprotect(message.OrganizationId, installation.ProtectedBotToken) is not { } token)
        {
            installation.RecordProblem(SlackProblem.Disconnected, now);
            return;
        }

        try
        {
            var conversation = await slack.Client.OpenDirectMessageAsync(token, link.SlackUserId, cancellationToken);
            await slack.Client.PostMessageAsync(token, conversation, text, cancellationToken);
        }
        catch (SlackApiException e) when (e.Problem is { } problem)
        {
            if (problem == SlackProblem.Disconnected)
            {
                installation.RecordProblem(problem, now);
            }
            else
            {
                link.RecordProblem(now);
            }

            SlackRecords.Add(db, message.OrganizationId, message.IdempotencyKey, SlackMessageOutcome.Refused, e.Code, now);
            LogNotSent(logger, message.Kind, message.SourceId, e.Code);
            return;
        }

        link.ClearProblem();
        SlackRecords.Add(db, message.OrganizationId, message.IdempotencyKey, SlackMessageOutcome.Sent, null, now);
    }

    private static async Task<string> NotePathAsync(ScreenHireDbContext db, SendSlackDirectMessage message, CancellationToken cancellationToken)
    {
        var noteId = await db.ApplicationNoteMentions.AsNoTracking()
            .Where(m => m.Id == message.SourceId).Select(m => m.NoteId).SingleAsync(cancellationToken);
        return MentionEmail.NotePath(message.ApplicationId, noteId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Did not send the Slack {Kind} message for {SourceId}: {Reason}")]
    private static partial void LogNotSent(ILogger logger, SlackDirectMessageKind kind, Guid sourceId, string reason);
}

internal static class SlackRecords
{
    public static Task<bool> ExistAsync(ScreenHireDbContext db, string key, CancellationToken cancellationToken) =>
        db.SlackMessages.AsNoTracking().AnyAsync(m => m.IdempotencyKey == key, cancellationToken);

    public static void Add(ScreenHireDbContext db, Guid organizationId, string key, SlackMessageOutcome outcome, string? errorCode, DateTimeOffset now) =>
        db.SlackMessages.Add(new SlackMessageRecord
        {
            OrganizationId = organizationId,
            IdempotencyKey = key,
            Outcome = outcome,
            ErrorCode = errorCode?[..Math.Min(errorCode.Length, SlackMessageRecord.MaxErrorLength)],
            At = now,
        });

    /// <summary>The candidate's name and the job's title for an application, or null when it is gone.</summary>
    public static async Task<(string Candidate, string Job)?> PlaceAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var place = await (
            from application in db.Applications.AsNoTracking()
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join job in db.Jobs on application.JobId equals job.Id
            where application.Id == applicationId
            select new { Candidate = candidate.FullName, Job = job.Title }).SingleOrDefaultAsync(cancellationToken);
        return place is null ? null : (place.Candidate, place.Job);
    }
}
