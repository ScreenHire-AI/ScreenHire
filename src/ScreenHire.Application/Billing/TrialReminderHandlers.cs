using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Billing;

/// <summary>
/// An organization's free trial started (its billing account was just made): the worker schedules the trial's reminders
/// (docs/architecture/billing.md, "Trial reminders"). Scheduling is idempotent on the account, so a second one changes nothing.
/// </summary>
public sealed record TrialStarted(Guid OrganizationId) : ITenantMessage;

/// <summary>
/// Sent once when the worker starts: asks every organization to schedule its trial's reminders, so one that began its trial before
/// reminders existed gets them. Each organization's account remembers it was done, so a restart schedules nothing twice.
/// </summary>
public sealed record CatchUpTrialReminders;

/// <summary>
/// Emails the Owners that the trial ending at <paramref name="TrialEndsAt"/> is <paramref name="DaysBefore"/> days away (0 on the
/// day it ends). <paramref name="Key"/> is built from the organization, the trial and the day.
/// </summary>
public sealed record RemindTrialEnding(Guid OrganizationId, DateTimeOffset TrialEndsAt, int DaysBefore, string Key) : ITenantMessage
{
    public static RemindTrialEnding For(Guid organizationId, DateTimeOffset trialEndsAt, int daysBefore) =>
        new(organizationId, trialEndsAt, daysBefore, TrialReminderKeys.For(organizationId, trialEndsAt, daysBefore));
}

/// <summary>Emails one Owner a trial reminder. <paramref name="Key"/> is the reminder's and the Owner's.</summary>
public sealed record EmailTrialEnding(Guid OrganizationId, Guid UserId, DateTimeOffset TrialEndsAt, int DaysBefore, string Key) : ITenantMessage
{
    public static EmailTrialEnding For(RemindTrialEnding reminder, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        return new(reminder.OrganizationId, userId, reminder.TrialEndsAt, reminder.DaysBefore, string.Create(CultureInfo.InvariantCulture, $"{reminder.Key}:{userId:N}"));
    }
}

/// <summary>
/// Puts the trial in the Owners' bell: "ends in N days" when <paramref name="Ended"/> is false, and "has ended" (replacing the
/// first, so it comes back unread) when it is true.
/// </summary>
public sealed record ShowTrialNotice(Guid OrganizationId, DateTimeOffset TrialEndsAt, bool Ended) : ITenantMessage;

public static class TrialReminderKeys
{
    public static string For(Guid organizationId, DateTimeOffset trialEndsAt, int daysBefore) =>
        string.Create(CultureInfo.InvariantCulture, $"trial:{organizationId:N}:{trialEndsAt.ToUnixTimeSeconds()}:{daysBefore}");

    /// <summary>
    /// The id of the <c>billing_events</c> row that says the reminders of the trial ending at <paramref name="trialEndsAt"/> are
    /// scheduled. It cannot be a provider's event id, which all start <c>evt_</c> or <c>sandbox_</c>.
    /// </summary>
    public static string ScheduledRecord(DateTimeOffset trialEndsAt) =>
        string.Create(CultureInfo.InvariantCulture, $"trial-reminders-scheduled:{trialEndsAt.ToUnixTimeSeconds()}");
}

/// <summary>Where the trial's links and notices point: Settings, Billing.</summary>
public static class TrialReminderLinks
{
    public const string BillingPath = "/settings/billing";
}

public static class CatchUpTrialRemindersHandler
{
    // Like the other fan-outs: row-level security gives the worker no read across organizations, so it asks each one in turn.
    public static async Task<OutgoingMessages> Handle(CatchUpTrialReminders message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        outgoing.AddRange(organizations.Select(id => new TrialStarted(id)));
        return outgoing;
    }
}

public static partial class TrialStartedHandler
{
    public static async Task<OutgoingMessages> Handle(
        TrialStarted message, ScreenHireDbContext db, IOptions<BillingOptions> options, TimeProvider time, ILogger<TrialStarted> logger, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        // Read only: the account has an optimistic concurrency token, and a write here would collide with a checkout or a webhook
        // that saves the same row in the first moments after sign-up (and the sandbox route ignores that collision).
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (account is null || account.Status != SubscriptionStatus.None)
        {
            return outgoing; // no trial, or a subscription already started
        }

        // The record that this trial's reminders are scheduled is a row of its own, unique per organization and id: a second
        // handling (a redelivery, a restart's catch-up) finds it and schedules nothing. A race between two inserts fails one
        // on the unique index, and its retry finds the row.
        var now = time.GetUtcNow();
        var recordId = TrialReminderKeys.ScheduledRecord(account.TrialEndsAt);
        if (await db.BillingEvents.AnyAsync(e => e.EventId == recordId, cancellationToken))
        {
            return outgoing; // scheduled before
        }

        db.BillingEvents.Add(new BillingEventRecord { OrganizationId = message.OrganizationId, EventId = recordId, ReceivedAt = now });

        foreach (var step in TrialReminders.Plan(account.TrialEndsAt, now, options.Value.Trial.RemindDaysBefore))
        {
            if (step.Kind == TrialStepKind.Email)
            {
                outgoing.Schedule(RemindTrialEnding.For(message.OrganizationId, account.TrialEndsAt, step.DaysBefore), step.At);
            }
            else
            {
                outgoing.Schedule(new ShowTrialNotice(message.OrganizationId, account.TrialEndsAt, step.Kind == TrialStepKind.EndedNotice), step.At);
            }
        }

        LogScheduled(logger, message.OrganizationId);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: trial reminders scheduled")]
    private static partial void LogScheduled(ILogger logger, Guid organizationId);
}

public static class RemindTrialEndingHandler
{
    // Does nothing once a subscription has started or the trial is not the one this was scheduled for, whatever the clock says.
    [Deduplicated(ValueSource.InputMember, nameof(RemindTrialEnding.Key))]
    public static async Task<OutgoingMessages> Handle(RemindTrialEnding message, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (account is null || !TrialReminders.Applies(account, message.TrialEndsAt))
        {
            return outgoing;
        }

        var owners = await NotificationRecipients.ForAsync(db, message.OrganizationId, Permission.ManageBilling, cancellationToken);
        outgoing.AddRange(owners.Select(o => o.UserId).Distinct().Select(userId => EmailTrialEnding.For(message, userId)));
        return outgoing;
    }
}

public static partial class EmailTrialEndingHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(EmailTrialEnding.Key))]
    public static async Task Handle(
        EmailTrialEnding message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        PlanCatalogue catalogue,
        IOptions<BillingOptions> options,
        IOptions<DashboardLinkOptions> links,
        ILogger<EmailTrialEnding> logger,
        CancellationToken cancellationToken)
    {
        var link = links.Value.Url(TrialReminderLinks.BillingPath);
        if (!email.IsConfigured || link is null)
        {
            LogNotEmailed(logger, message.OrganizationId, "this host has no email sender or no dashboard address");
            return;
        }

        // A plan may have started since the Owners were picked.
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (account is null || !TrialReminders.Applies(account, message.TrialEndsAt))
        {
            return;
        }

        var organization = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == message.OrganizationId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        // Looked up among the Owners again: a member removed or demoted since the reminder fired gets nothing.
        var to = (await NotificationRecipients.ForAsync(db, message.OrganizationId, Permission.ManageBilling, cancellationToken))
            .FirstOrDefault(r => r.UserId == message.UserId)?.Email;
        if (organization is null || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        var plan = catalogue.Find(options.Value.Trial.Plan)?.Name;
        await email.SendAsync(TrialEndingEmail.Compose(to, organization, plan, message.TrialEndsAt, message.DaysBefore, link), cancellationToken);
        LogEmailed(logger, message.OrganizationId, message.UserId, message.DaysBefore);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: an Owner was not emailed about the trial ending: {Reason}")]
    private static partial void LogNotEmailed(ILogger logger, Guid organizationId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: emailed Owner {UserId} a trial reminder, {DaysBefore} days before the end")]
    private static partial void LogEmailed(ILogger logger, Guid organizationId, Guid userId, int daysBefore);
}

public static class ShowTrialNoticeHandler
{
    public static async Task<OutgoingMessages> Handle(
        ShowTrialNotice message, ScreenHireDbContext db, PlanCatalogue catalogue, IOptions<BillingOptions> options, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (account is null || !TrialReminders.Applies(account, message.TrialEndsAt))
        {
            return outgoing;
        }

        var payload = new Dictionary<string, string>
        {
            ["endsAt"] = account.TrialEndsAt.ToString("O", CultureInfo.InvariantCulture),
            ["plan"] = catalogue.Find(options.Value.Trial.Plan)?.Name ?? "",
        };
        if (message.Ended)
        {
            // The ending notice goes and the ended one arrives unread, even for an Owner who had read or dismissed the first.
            outgoing.Add(new ClearNotification(message.OrganizationId, NotificationKinds.TrialEnding, NotificationKinds.TrialKey));
            outgoing.Add(Raise(message.OrganizationId, NotificationKinds.TrialEnded, payload));
        }
        else
        {
            outgoing.Add(Raise(message.OrganizationId, NotificationKinds.TrialEnding, payload));
        }

        return outgoing;
    }

    private static RaiseNotification Raise(Guid organizationId, string kind, IReadOnlyDictionary<string, string> payload) =>
        new(organizationId, Permission.ManageBilling, kind, NotificationKinds.TrialKey, payload, TrialReminderLinks.BillingPath);
}

/// <summary>The email Owners get before the trial ends: ScreenHire's own wording, the plan, the date, what happens and a link.</summary>
public static class TrialEndingEmail
{
    public static EmailMessage Compose(string to, string organizationName, string? planName, DateTimeOffset endsAt, int daysBefore, Uri link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentNullException.ThrowIfNull(link);
        var when = daysBefore switch
        {
            <= 0 => "today",
            1 => "tomorrow",
            _ => string.Create(CultureInfo.InvariantCulture, $"in {daysBefore} days"),
        };
        var date = endsAt.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var first = $"Your free ScreenHire trial for {organizationName} ends {when}, on {date} (UTC).";
        var plan = string.IsNullOrWhiteSpace(planName) ? "" : $" During the trial you have the {planName} plan.";
        var what = "Nothing is deleted when it ends. Your open jobs stay open, and your candidates and their history stay where they are. To open a new job after that, you will need a plan.";
        var text = $"""
            {first}{plan}

            {what}

            Choose a plan in Billing: {link}
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(first + plan)}</p>
            <p>{WebUtility.HtmlEncode(what)}</p>
            <p><a href="{WebUtility.HtmlEncode(link.ToString())}">Choose a plan in Billing</a></p>
            """;
        var subject = daysBefore switch
        {
            <= 0 => "Your ScreenHire trial ends today",
            1 => "Your ScreenHire trial ends tomorrow",
            _ => string.Create(CultureInfo.InvariantCulture, $"Your ScreenHire trial ends in {daysBefore} days"),
        };
        return new EmailMessage(to, subject, text, html);
    }
}

/// <summary>Asks for the catch-up once, when the worker starts. A one-shot message, not a loop: nothing is scheduled again later.</summary>
public sealed class TrialReminderCatchUp(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new CatchUpTrialReminders());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class TrialReminderRegistration
{
    /// <summary>
    /// The billing settings and the plan catalogue the reminders read in the worker: the trial's plan, the days and any plan
    /// name override. Nothing here talks to a payment provider. Registered by the worker's messaging setup.
    /// </summary>
    public static IServiceCollection AddScreenHireTrialReminders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<BillingOptions>().BindConfiguration(BillingOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingTrialGate>());
        services.TryAddSingleton<PlanCatalogue>();
        return services;
    }
}
