using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Automations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Runtime;

namespace ScreenHire.Application.Automations;

/// <summary>
/// Turns a dead-lettered automation action into an automation issue (workflow-engine.md §3, "never silently dropped").
/// The error policy calls <see cref="ReportAsync"/> when it moves any message to the dead-letter table; for an
/// <see cref="ExecuteAutomationAction"/> it publishes <see cref="AutomationActionFailed"/>, which marks the action and
/// its run failed as the message's tenant.
/// </summary>
public static class AutomationFailures
{
    public const string Description = "Record a failed automation action as an automation issue";

    public static async ValueTask ReportAsync(IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(exception);
        if (lifecycle.Envelope?.Message is ExecuteAutomationAction action)
        {
            await new MessageBus(runtime).PublishAsync(
                new AutomationActionFailed(action.OrganizationId, action.ActionRunId, exception.GetType().Name));
        }
    }

    /// <summary>
    /// Services the automation handlers take: the clock, transactional email (hidden when no sender is registered) and
    /// the CallWebhook adapter. <see cref="ScreenHireMessaging.ConfigureHandler"/> registers them, so every host that runs
    /// the handlers has them.
    /// </summary>
    public static IServiceCollection AddScreenHireAutomations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The API and worker register this already; TryAdd covers any other host that runs the handlers.
        services.TryAddSingleton<TimeProvider>(DatabasePrecisionTimeProvider.Instance);
        services.TryAddSingleton<EmailTemplates>();
        services.TryAddSingleton<EmailTemplateStore>();
        services.TryAddSingleton<TransactionalEmail>();

        services.AddOptions<WebhookOptions>().BindConfiguration(WebhookOptions.Section);
        services.TryAddSingleton<IWebhookSender, HttpWebhookSender>();
        return services;
    }
}

/// <summary>Marks a dead-lettered action and its run failed, and raises the issue to the organization's dashboards.</summary>
public static class AutomationActionFailedHandler
{
    public static async Task<OutgoingMessages> Handle(
        AutomationActionFailed message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var action = await db.AutomationActionRuns.SingleOrDefaultAsync(a => a.Id == message.ActionRunId, cancellationToken)
            ?? throw new InvalidOperationException($"Automation action {message.ActionRunId} is not visible to organization {message.OrganizationId}.");
        if (action.Status != AutomationActionStatus.Pending)
        {
            return outgoing;
        }

        var run = await db.AutomationRuns.SingleAsync(r => r.Id == action.RunId, cancellationToken);
        var now = time.GetUtcNow();
        var detail = $"{action.Action.Type} failed after every retry ({message.ExceptionType}). Later actions did not run.";
        action.Fail(detail, now);
        if (run.Status == AutomationRunStatus.Running)
        {
            run.Fail(detail, now);
            outgoing.Add(ExecuteAutomationActionHandler.Finished(run, now));
        }

        return outgoing;
    }
}
