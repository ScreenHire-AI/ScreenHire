using ScreenHire.Application.Templates;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Hiring;

/// <summary>Raises <see cref="TemplateChanged"/> with the save that changed a template, so open dashboards refresh (docs/architecture/realtime.md).</summary>
internal static class TemplateChangeEvents
{
    /// <summary>
    /// Saves the tracked changes and publishes the event in one transaction through the outbox: the event goes out only
    /// if the save commits, and never twice for one save.
    /// </summary>
    public static async Task SaveTemplateChangeAsync(
        this IDbContextOutbox<ScreenHireDbContext> outbox, TemplateKind kind, string templateId, ITenantContext tenant, TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(time);
        await outbox.PublishAsync(new TemplateChanged(tenant.OrganizationId!.Value, kind, templateId, time.GetUtcNow(), tenant.UserId));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}
