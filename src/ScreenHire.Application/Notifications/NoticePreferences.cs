using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Notifications;

/// <summary>
/// The organization's alert settings from Verification's Notifications tab, read as every member's starting value for the
/// verification and AI use rows (docs/architecture/notifications.md, "Each person's choices"). A person's own choice wins.
/// </summary>
public sealed record OrganizationAlertDefaults(bool SuggestedEmail, bool SuggestedSlack, bool BudgetEmail, bool BudgetSlack)
{
    public static OrganizationAlertDefaults None { get; } = new(false, false, false, false);

    /// <summary>The current tenant's alert settings; none saved is everything off, as Verification shows it.</summary>
    public static async Task<OrganizationAlertDefaults> ReadAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return settings is null
            ? None
            : new(settings.SuggestedAlertEmail, settings.SuggestedAlertSlack, settings.BudgetAlertEmail, settings.BudgetAlertSlack);
    }
}

/// <summary>
/// One row of the notification grid on Your account: the toasts its In the app switch covers, whether it has a Slack
/// message, whether the person's scope narrows it to their jobs, and who sees it. The one list both the API and the
/// worker read, so a new row is one entry here (and its line in the dashboard's <c>noticeRows</c>).
/// </summary>
/// <param name="Kind">The row.</param>
/// <param name="Toasts">The toast kinds its In the app switch turns on and off; none when nothing toasts for it.</param>
/// <param name="HasSlack">Whether ScreenHire sends a Slack direct message for it.</param>
/// <param name="Scoped">Whether My jobs or Everything I can see decides whose jobs it covers.</param>
/// <param name="Audience">The permissions, any of which shows the row; none shows it to everyone.</param>
public sealed record NoticeRow(NoticeKind Kind, IReadOnlyList<ToastKind> Toasts, bool HasSlack, bool Scoped, IReadOnlyList<Permission> Audience)
{
    public bool IsFor(IReadOnlySet<Permission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return Audience.Count == 0 || Audience.Any(permissions.Contains);
    }
}

public static class NoticeRows
{
    public static IReadOnlyList<NoticeRow> All { get; } =
    [
        new(NoticeKind.Mentions, [ToastKind.Mentions], HasSlack: true, Scoped: false, []),
        new(NoticeKind.Replies, [ToastKind.Replies], HasSlack: false, Scoped: false, []),
        new(NoticeKind.Interviews, [], HasSlack: false, Scoped: false, []),
        new(NoticeKind.Tasks, [ToastKind.Tasks], HasSlack: true, Scoped: false, []),
        new(NoticeKind.NewApplications, [ToastKind.NewCandidates], HasSlack: false, Scoped: true, []),
        new(NoticeKind.TeammateChanges,
            [ToastKind.Moves, ToastKind.Notes, ToastKind.Tags, ToastKind.Emails, ToastKind.Scorecards, ToastKind.Templates],
            HasSlack: false, Scoped: true, []),
        new(NoticeKind.VerificationSuggested, [], HasSlack: true, Scoped: false, [Permission.ReviewVerification]),
        new(NoticeKind.AiUseAndBilling, [], HasSlack: true, Scoped: false, [Permission.ViewAiUsage, Permission.ManageBilling]),
    ];

    public static NoticeRow For(NoticeKind kind) => All.Single(r => r.Kind == kind);
}

/// <summary>Each row's starting value, before the person changes it.</summary>
public static class NoticeDefaults
{
    /// <summary>
    /// The email default. Verification suggested and the AI use alerts follow the organization's alert settings. The trial's
    /// reminders (<paramref name="handlesBilling"/>) start at Instantly, as they have always gone to the people who handle
    /// billing, and the grid shows that default to everyone who handles billing, so Off is a choice they can save. Someone who
    /// handles billing and never chose still gets the AI use alerts only while the organization's switch is on.
    /// </summary>
    public static EmailChoice Email(NoticeKind kind, OrganizationAlertDefaults alerts, bool handlesBilling = false)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        return kind switch
        {
            NoticeKind.Mentions or NoticeKind.Replies or NoticeKind.Interviews => EmailChoice.Instantly,
            NoticeKind.Tasks or NoticeKind.NewApplications => EmailChoice.Digest,
            NoticeKind.VerificationSuggested => alerts.SuggestedEmail ? EmailChoice.Instantly : EmailChoice.Off,
            NoticeKind.AiUseAndBilling => alerts.BudgetEmail || handlesBilling ? EmailChoice.Instantly : EmailChoice.Off,
            _ => EmailChoice.Off,
        };
    }

    /// <summary>
    /// The email default every reader of the row agrees on, or null when they differ. They differ only on AI use and billing
    /// for someone who handles billing while the organization's AI usage email is off: the trial's reminders start at
    /// Instantly and the AI alerts at Off, so whatever the person saves there is kept, and both read it.
    /// </summary>
    public static EmailChoice? SharedEmail(NoticeKind kind, OrganizationAlertDefaults alerts, bool handlesBilling)
    {
        var own = Email(kind, alerts, handlesBilling);
        return own == Email(kind, alerts) ? own : null;
    }

    /// <summary>
    /// The Slack default for the rows that have one. Mentions and tasks are on (the Slack link keeps that choice); the
    /// verification and AI use messages follow the organization's alert settings.
    /// </summary>
    public static bool Slack(NoticeKind kind, OrganizationAlertDefaults alerts)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        return kind switch
        {
            NoticeKind.Mentions or NoticeKind.Tasks => true,
            NoticeKind.VerificationSuggested => alerts.SuggestedSlack,
            NoticeKind.AiUseAndBilling => alerts.BudgetSlack,
            _ => false,
        };
    }
}

/// <summary>What a person chose for a row: their own choice when they made one, the default otherwise.</summary>
public static class PersonNotices
{
    public static EmailChoice Email(MemberPreferences? preferences, NoticeKind kind, OrganizationAlertDefaults alerts, bool handlesBilling = false) =>
        preferences?.Notices.For(kind)?.Email ?? NoticeDefaults.Email(kind, alerts, handlesBilling);

    public static bool Slack(MemberPreferences? preferences, NoticeKind kind, OrganizationAlertDefaults alerts) =>
        preferences?.Notices.For(kind)?.Slack ?? NoticeDefaults.Slack(kind, alerts);

    /// <summary>One person's preferences in an organization, or null while they have saved none.</summary>
    public static Task<MemberPreferences?> ForUserAsync(
        ScreenHireDbContext db, Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.MemberPreferences.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId, cancellationToken);
    }

    /// <summary>The preferences of several people at once, by user id; people who saved none are missing.</summary>
    public static async Task<Dictionary<Guid, MemberPreferences>> ForUsersAsync(
        ScreenHireDbContext db, Guid organizationId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.MemberPreferences.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && userIds.Contains(p.UserId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(p => p.UserId);
    }

    /// <summary>
    /// Keeps only what differs from this person's defaults, so a choice set back to its default follows a later change to the
    /// organization's alert settings. A row whose readers start from different defaults (<see cref="NoticeDefaults.SharedEmail"/>
    /// is null) keeps whatever was chosen. Rows are kept in the grid's order.
    /// </summary>
    public static Domain.Notifications.NoticeChoices Normalize(
        IEnumerable<NoticeChoice> choices, Func<NoticeKind, EmailChoice?> emailDefault, Func<NoticeKind, bool> slackDefault)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(emailDefault);
        ArgumentNullException.ThrowIfNull(slackDefault);
        var byKind = choices.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Last());
        var kept = new List<NoticeChoice>();
        foreach (var row in NoticeRows.All)
        {
            if (!byKind.TryGetValue(row.Kind, out var choice))
            {
                continue;
            }

            var email = choice.Email == emailDefault(row.Kind) ? null : choice.Email;
            var slack = !row.HasSlack || choice.Slack == slackDefault(row.Kind) ? null : choice.Slack;
            if (email is not null || slack is not null)
            {
                kept.Add(new NoticeChoice(row.Kind, email, slack));
            }
        }

        return new Domain.Notifications.NoticeChoices { Choices = kept };
    }
}
