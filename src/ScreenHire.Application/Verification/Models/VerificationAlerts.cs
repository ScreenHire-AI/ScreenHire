using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Notifications;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Verification;

/// <summary>
/// An application first reached Verification suggested. Ids only: the handler tells the people the organization chose
/// (verification-engine.md §10), and what it says carries no level, reason, evidence or candidate name.
/// </summary>
/// <remarks><c>assessmentEventId</c> is the timeline entry of the assessment, stable across redeliveries.</remarks>
public sealed record NotifyVerificationSuggested(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid AssessmentEventId) : ITenantMessage;

/// <summary>
/// The organization's AI use needs telling to the people who see it: a budget share passed, the cap reached, or a spike
/// (ai-usage.md). <paramref name="Key"/> is the <see cref="AiUsageAlertKeys"/> key, which the handler claims so the alert goes out
/// once. <paramref name="Percent"/> is the budget share for a budget alert.
/// </summary>
public sealed record NotifyAiUsage(Guid OrganizationId, AiUsageAlertKind Kind, string Key, int Percent = 0) : ITenantMessage;

/// <summary>What an AI usage alert is about.</summary>
public enum AiUsageAlertKind
{
    Budget,
    Cap,
    Spike,
}

/// <summary>The wording of the suggested-verification alert, shared by email and Slack. Fixed text, ids and links only.</summary>
public static class VerificationAlertWording
{
    public const string SuggestedSubject = "An application needs a closer look";

    public static string Suggested(string job) => $"An application for {job} needs a closer look.";

    public static EmailMessage SuggestedEmail(string to, string job, Uri link)
    {
        var line = Suggested(job);
        return new EmailMessage(
            to, SuggestedSubject,
            $"{line}\n\nOpen it to see why: {link}",
            $"<p>{WebUtility.HtmlEncode(line)}</p><p><a href=\"{WebUtility.HtmlEncode(link.ToString())}\">Open it to see why</a>.</p>");
    }
}

/// <summary>The words of the AI usage alerts, shared by the bell, email and Slack. Fixed text and numbers only.</summary>
public static class AiUsageAlertWording
{
    public static string Subject(AiUsageAlertKind kind, int percent) => kind switch
    {
        AiUsageAlertKind.Budget => $"AI usage is at {percent}% of this month's budget",
        AiUsageAlertKind.Cap => "AI checks are paused: this month's cap is reached",
        _ => "AI usage yesterday was well above the recent average",
    };

    public static string Body(AiUsageAlertKind kind, int percent, string organization) => kind switch
    {
        AiUsageAlertKind.Budget =>
            $"{organization} has used {percent}% of this month's AI budget. The budget only warns. Set a cap in Verification to pause the AI checks at a limit.",
        AiUsageAlertKind.Cap =>
            $"{organization} reached its monthly AI cap, so the AI checks are paused until next month or until the cap is raised. The rule-based checks keep running, and a paused check is never a mark against a candidate.",
        _ => $"{organization} used much more AI yesterday than on a usual day. Open the usage page to see which calls.",
    };

    public static EmailMessage Email(string to, AiUsageAlertKind kind, int percent, string organization, Uri link)
    {
        var body = Body(kind, percent, organization);
        return new EmailMessage(
            to, Subject(kind, percent),
            $"{body}\n\nOpen Verification: {link}",
            $"<p>{WebUtility.HtmlEncode(body)}</p><p><a href=\"{WebUtility.HtmlEncode(link.ToString())}\">Open Verification</a>.</p>");
    }
}

/// <summary>
/// Tells the people who review verification that an application needs a closer look, by email and by Slack direct
/// message, as the organization chose. Only to members who hold Review verification and see the job (ADR-0015), never
/// to a channel, which has no such limits. Nothing is sent when the host has no email sender, no Slack, or the
/// organization turned both off. Delivery is at least once; the Slack record makes a repeated DM impossible, and a crash
/// between an email and the commit can repeat one email, as with a mention.
/// </summary>
public static partial class NotifyVerificationSuggestedHandler
{
    public static async Task Handle(
        NotifyVerificationSuggested message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        SlackAccess slack,
        SlackTokens tokens,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<NotifyVerificationSuggested> logger,
        CancellationToken cancellationToken)
    {
        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is not { SuggestedAlertEmail: true } and not { SuggestedAlertSlack: true })
        {
            return;
        }

        var link = links.Value.Url($"/applications/{message.ApplicationId}");
        var job = await db.Jobs.AsNoTracking().Where(j => j.Id == message.JobId).Select(j => j.Title).SingleOrDefaultAsync(cancellationToken);
        if (link is null || job is null)
        {
            LogNotSent(logger, message.ApplicationId, "no dashboard address, or the job is gone");
            return;
        }

        var recipients = await VerificationRecipients.WithPermissionAsync(db, Permission.ReviewVerification, message.OrganizationId, message.JobId, cancellationToken);
        if (settings.SuggestedAlertEmail && email.IsConfigured)
        {
            foreach (var to in recipients.Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await email.SendAsync(VerificationAlertWording.SuggestedEmail(to, job, link), cancellationToken);
            }
        }

        if (settings.SuggestedAlertSlack && slack.IsConfigured)
        {
            var wording = SlackMessages.VerificationSuggested(job, link);
            foreach (var recipient in recipients)
            {
                await VerificationSlackDirectMessages.SendAsync(
                    db, slack, tokens, message.OrganizationId, recipient.UserId, $"vs:{message.AssessmentEventId}:{recipient.UserId}", wording, time.GetUtcNow(), cancellationToken);
            }
        }

        LogNotified(logger, message.ApplicationId, recipients.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Verification suggested for application {ApplicationId}: nobody was told: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid applicationId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Verification suggested for application {ApplicationId}: {Members} members could be told")]
    private static partial void LogNotified(ILogger logger, Guid applicationId, int members);
}

/// <summary>
/// Tells the people who see AI usage that a budget share was passed, the cap reached or a day spiked: a bell notification
/// for each of them, and an email and a Slack direct message when the organization chose them (ai-usage.md). The handler
/// claims the alert key first (a unique row), so two checks finishing together send once and a redelivery after the commit
/// finds the key taken. A crash between the sends and the commit sends again, as with <see cref="NotifyVerificationSuggestedHandler"/>.
/// </summary>
public static partial class NotifyAiUsageHandler
{
    public static async Task<OutgoingMessages> Handle(
        NotifyAiUsage message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        SlackAccess slack,
        SlackTokens tokens,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<NotifyAiUsage> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var now = time.GetUtcNow();
        // One statement in the handler's transaction: a second message for the same key waits on the unique index and then does nothing.
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ai_usage_alerts (id, organization_id, key, raised_at)
            VALUES ({Guid.CreateVersion7()}, {message.OrganizationId}, {message.Key}, {now})
            ON CONFLICT (organization_id, key) DO NOTHING
            """,
            cancellationToken);
        if (claimed == 0)
        {
            return outgoing;
        }

        var (kind, payload) = message.Kind switch
        {
            AiUsageAlertKind.Budget => (NotificationKinds.AiUsageBudget, new Dictionary<string, string> { ["percent"] = message.Percent.ToString(CultureInfo.InvariantCulture) }),
            AiUsageAlertKind.Cap => (NotificationKinds.AiUsageCap, new Dictionary<string, string>()),
            _ => (NotificationKinds.AiUsageSpike, new Dictionary<string, string> { ["day"] = message.Key.Split(':')[0] }),
        };
        outgoing.Add(new RaiseNotification(message.OrganizationId, Permission.ViewAiUsage, kind, message.Key, payload, "/verification/usage"));

        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var link = links.Value.Url("/verification/usage");
        var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == message.OrganizationId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (settings is not ({ BudgetAlertEmail: true } or { BudgetAlertSlack: true }) || link is null || organization is null)
        {
            return outgoing;
        }

        var recipients = await VerificationRecipients.WithPermissionAsync(db, Permission.ViewAiUsage, message.OrganizationId, null, cancellationToken);
        if (settings.BudgetAlertEmail && email.IsConfigured)
        {
            foreach (var to in recipients.Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await email.SendAsync(AiUsageAlertWording.Email(to, message.Kind, message.Percent, organization, link), cancellationToken);
            }
        }

        if (settings.BudgetAlertSlack && slack.IsConfigured)
        {
            var wording = SlackMessages.AiUsage(AiUsageAlertWording.Subject(message.Kind, message.Percent), link);
            foreach (var recipient in recipients)
            {
                await VerificationSlackDirectMessages.SendAsync(
                    db, slack, tokens, message.OrganizationId, recipient.UserId, $"au:{message.Key}:{recipient.UserId}", wording, now, cancellationToken);
            }
        }

        LogNotified(logger, message.OrganizationId, recipients.Count);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI usage alert for organization {OrganizationId}: {Members} members could be told")]
    private static partial void LogNotified(ILogger logger, Guid organizationId, int members);
}

/// <summary>A member an alert may go to.</summary>
public sealed record VerificationRecipient(Guid UserId, string Email);

internal static class VerificationRecipients
{
    /// <summary>
    /// Members who hold <paramref name="permission"/> and, when a job is given, see it (ADR-0015); without a job, members who see
    /// every job.
    /// </summary>
    public static async Task<List<VerificationRecipient>> WithPermissionAsync(
        ScreenHireDbContext db, Permission permission, Guid organizationId, Guid? jobId, CancellationToken cancellationToken)
    {
        var members = await (
            from member in db.OrganizationMembers.AsNoTracking()
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            join role in db.CustomRoles.AsNoTracking() on member.CustomRoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new { member.Id, member.Role, member.JobAccess, Permissions = role == null ? null : role.Permissions, user.Email, UserId = user.Id })
            .ToListAsync(cancellationToken);
        var team = jobId is { } id
            ? await db.Jobs.AsNoTracking().Where(j => j.Id == id).Select(j => j.HiringTeam).SingleOrDefaultAsync(cancellationToken)
            : null;
        return
        [
            .. members
                .Where(m => (m.Role == OrganizationRole.Custom ? RolePermissions.ForCustom(m.Permissions ?? []) : RolePermissions.Of(m.Role)).Contains(permission)
                    && (JobAccessRules.SeesAllJobs(m.Role, m.JobAccess) || (team is not null && team.Contains(m.Id))))
                .Select(m => new VerificationRecipient(m.UserId, m.Email)),
        ];
    }
}

internal static class VerificationSlackDirectMessages
{
    /// <summary>
    /// Sends one direct message to a person who linked Slack, once for <paramref name="key"/> (the Slack record). A refusal a
    /// retry cannot fix is recorded for the page to explain, as for a mention.
    /// </summary>
    public static async Task SendAsync(
        ScreenHireDbContext db, SlackAccess slack, SlackTokens tokens, Guid organizationId, Guid userId, string key, string text,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var installation = await db.SlackInstallations.SingleOrDefaultAsync(cancellationToken);
        var link = installation is null ? null : await db.SlackAccountLinks.SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (installation is not { IsDisconnected: false } || link is null || await SlackRecords.ExistAsync(db, key, cancellationToken))
        {
            return; // no workspace, not linked, or sent already
        }

        if (tokens.Unprotect(organizationId, installation.ProtectedBotToken) is not { } token)
        {
            installation.RecordProblem(Domain.Slack.SlackProblem.Disconnected, now);
            return;
        }

        try
        {
            var conversation = await slack.Client.OpenDirectMessageAsync(token, link.SlackUserId, cancellationToken);
            await slack.Client.PostMessageAsync(token, conversation, text, cancellationToken);
        }
        catch (SlackApiException e) when (e.Problem is { } problem)
        {
            if (problem == Domain.Slack.SlackProblem.Disconnected)
            {
                installation.RecordProblem(problem, now);
            }
            else
            {
                link.RecordProblem(now);
            }

            SlackRecords.Add(db, organizationId, key, Domain.Slack.SlackMessageOutcome.Refused, e.Code, now);
            return;
        }

        link.ClearProblem();
        SlackRecords.Add(db, organizationId, key, Domain.Slack.SlackMessageOutcome.Sent, null, now);
    }
}
