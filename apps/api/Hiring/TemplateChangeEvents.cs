using ScreenHire.Application.Templates;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Hiring;

/// <summary>Raises <see cref="TemplateChanged"/> with the save that changed a template, so open dashboards refresh (docs/architecture/realtime.md).</summary>
internal static class TemplateChangeEvents
{
    /// <summary>
    /// Saves the tracked changes and publishes the event in one transaction through the outbox, so the event goes out
    /// only with a save that commits. A caller that retries a failed save can send it twice, which costs an open dashboard
    /// one more refetch. When the caller has a transaction open, this commits it.
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
