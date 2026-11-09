using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Verification;

/// <summary>
/// An application first reached Verification suggested. Ids only: the handler tells the people the organization chose
/// (verification-engine.md §10), and what it says carries no level, reason, evidence or candidate name.
/// </summary>
/// <remarks><c>assessmentEventId</c> is the timeline entry of the assessment, stable across redeliveries.</remarks>
public sealed record NotifyVerificationSuggested(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid AssessmentEventId) : ITenantMessage;

/// <summary>This month's tokens reached 80% of the organization's budget. <c>month</c> is <c>yyyy-MM</c> (UTC).</summary>
public sealed record NotifyBudgetThreshold(Guid OrganizationId, string Month) : ITenantMessage;

/// <summary>The wording of both alerts, shared by email and Slack. Fixed text, ids and links only.</summary>
public static class VerificationAlertWording
{
    /// <summary>The share of the monthly token budget at which the budget alert goes out.</summary>
    public const double BudgetThreshold = 0.8;

    public const string SuggestedSubject = "An application needs a closer look";

    public const string BudgetSubject = "Verification tokens are at 80% of this month's budget";

    public static string Suggested(string job) => $"An application for {job} needs a closer look.";

    public static EmailMessage SuggestedEmail(string to, string job, Uri link)
    {
        var line = Suggested(job);
        return new EmailMessage(
            to, SuggestedSubject,
            $"{line}\n\nOpen it to see why: {link}",
            $"<p>{WebUtility.HtmlEncode(line)}</p><p><a href=\"{WebUtility.HtmlEncode(link.ToString())}\">Open it to see why</a>.</p>");
    }

    public static EmailMessage BudgetEmail(string to, string organization, Uri link) =>
        new(
            to, BudgetSubject,
            $"{organization} has used 80% of this month's AI token budget for the consistency check. Past the budget the check pauses until next month, or you can use your own key.\n\nOpen Verification settings: {link}",
            $"<p>{WebUtility.HtmlEncode(organization)} has used 80% of this month's AI token budget for the consistency check. Past the budget the check pauses until next month, or you can use your own key.</p><p><a href=\"{WebUtility.HtmlEncode(link.ToString())}\">Open Verification settings</a>.</p>");
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
/// Tells the people who can change settings that the month's tokens passed 80% of the budget, once a month: the handler claims
/// the month on the settings row, so of two checks that finish together one sends, and a redelivery after the commit finds
/// the month taken. A crash between the sends and the commit sends again, as with <see cref="NotifyVerificationSuggestedHandler"/>.
/// </summary>
public static partial class NotifyBudgetThresholdHandler
{
    public static async Task Handle(
        NotifyBudgetThreshold message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        SlackAccess slack,
        SlackTokens tokens,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<NotifyBudgetThreshold> logger,
        CancellationToken cancellationToken)
    {
        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null || settings.UsesOwnKey || settings.BudgetAlertMonth == message.Month
            || settings is not { BudgetAlertEmail: true } and not { BudgetAlertSlack: true })
        {
            return;
        }

        var link = links.Value.Url("/verification");
        var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == message.OrganizationId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (link is null || organization is null)
        {
            LogNotSent(logger, message.OrganizationId, "no dashboard address, or the organization is gone");
            return;
        }

        // One conditional statement in the handler's transaction, so a second check's message waits on the row and then changes
        // nothing. An update outside the change tracker, and UpdatedAt left alone, because this is bookkeeping: nobody made this
        // change, and History does not read this column.
        var claimed = await db.VerificationSettings
            .Where(s => s.BudgetAlertMonth == null || s.BudgetAlertMonth != message.Month)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.BudgetAlertMonth, message.Month), cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var recipients = await VerificationRecipients.WithPermissionAsync(db, Permission.EditSettings, message.OrganizationId, null, cancellationToken);
        if (settings.BudgetAlertEmail && email.IsConfigured)
        {
            foreach (var to in recipients.Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await email.SendAsync(VerificationAlertWording.BudgetEmail(to, organization, link), cancellationToken);
            }
        }

        if (settings.BudgetAlertSlack && slack.IsConfigured)
        {
            var wording = SlackMessages.VerificationBudget(link);
            foreach (var recipient in recipients)
            {
                await VerificationSlackDirectMessages.SendAsync(
                    db, slack, tokens, message.OrganizationId, recipient.UserId, $"vb:{message.Month}:{recipient.UserId}", wording, time.GetUtcNow(), cancellationToken);
            }
        }

        LogNotified(logger, message.OrganizationId, recipients.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Budget alert for organization {OrganizationId}: nobody was told: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid organizationId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Budget alert for organization {OrganizationId}: {Members} members could be told")]
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
