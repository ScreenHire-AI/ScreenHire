using ScreenHire.Application.Templates;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Hiring;

/// <summary>Raises <see cref="TemplateChanged"/> with the save that changed a template, so open dashboards refresh (docs/architecture/realtime.md).</summary>
internal static class TemplateChangeEvents
{
    /// <summary>
    /// Saves the tracked changes and publishes the event in one transaction through the outbox, so the event goes out
    /// only with a save that commits. A caller that retries a failed save can send it twice, which costs an open dashboard
    /// one more refetch. When the caller has a transaction open, this commits it. For an email template or a scorecard it
    /// first reads who changed the template last, before this save writes its own change.
    /// </summary>
    public static async Task SaveTemplateChangeAsync(
        this IDbContextOutbox<ScreenHireDbContext> outbox, TemplateKind kind, string templateId, ITenantContext tenant, TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(time);
        var previousEditor = await PreviousEditorAsync(outbox.DbContext, kind, templateId, tenant.UserId, cancellationToken);
        await outbox.PublishAsync(new TemplateChanged(tenant.OrganizationId!.Value, kind, templateId, time.GetUtcNow(), tenant.UserId, previousEditor));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    // The member who changed the template last, when that is someone other than the actor: a later change by the actor, a
    // key or ScreenHire means nobody else was the last to change it (as for a published hiring template version). A hiring
    // template tells no one: its edits are drafts.
    private static async Task<Guid?> PreviousEditorAsync(
        ScreenHireDbContext db, TemplateKind kind, string templateId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        ChangeSubjectKind? subject = kind switch
        {
            TemplateKind.EmailTemplate => ChangeSubjectKind.EmailTemplate,
            TemplateKind.ScorecardTemplate => ChangeSubjectKind.ScorecardTemplate,
            _ => null,
        };
        if (subject is not { } subjectKind)
        {
            return null;
        }

        var last = await ChangeHistoryQueries.LastAsync(db, subjectKind, templateId, cancellationToken);
        return last?.UserId is { } editor && editor != actorUserId ? editor : null;
    }
}
