using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.History;

/// <summary>
/// The entity types whose changes <see cref="ChangeHistoryInterceptor"/> records, and how each says what changed.
/// Tracking another kind is one line here (docs/architecture/change-history.md, "Tracking another kind").
/// </summary>
/// <remarks>
/// A list on top of EF's change tracker, rather than recording every entry it holds, because it is an allow-list:
/// the interceptor never reads an entity that is not here, so a webhook's signing secret can never reach the history. A
/// row here that holds a secret (an API key, an invitation) is seen through a view without it, so its describer never
/// sees the secret. <c>TrackedKindsTests</c> checks that no describer's view has a secret-like property.
/// </remarks>
public static class TrackedKinds
{
    public static IReadOnlyList<TrackedKind> All { get; } =
    [
        Track<HiringTemplate>(ChangeSubjectKind.HiringTemplate, t => t.Id.ToString("D"), TemplateAsync, creates: (_, _) => true),
        Track<HiringTemplateVersion>(ChangeSubjectKind.HiringTemplate, v => v.TemplateId.ToString("D"), TemplateVersionAsync),
        // A built-in's first edit adds its row too, but that is an edit of ScreenHire's wording, not a new template.
        Track<EmailTemplate>(ChangeSubjectKind.EmailTemplate, t => t.Key, EmailTemplateAsync, creates: (t, _) => t.BuiltInId is null),
        Track<Job>(ChangeSubjectKind.Job, j => j.Id.ToString("D"), JobAsync, creates: (_, _) => true),
        Track<ScorecardTemplate>(ChangeSubjectKind.ScorecardTemplate, t => t.Key, ScorecardTemplateAsync, creates: (_, _) => true),
        Track<CustomRole>(ChangeSubjectKind.Role, r => r.Id.ToString("D"), RoleChanges.Describe, creates: (_, _) => true),

        // One organization row holds the name and the brand colour, which two settings pages edit: each has a history of
        // its own, and a save that changes only one of them records nothing for the other.
        Track<Organization>(ChangeSubjectKind.Organization, o => o.Id.ToString("D"), SettingsChanges.Name),
        Track<Organization>(ChangeSubjectKind.Branding, o => o.Id.ToString("D"), SettingsChanges.Branding),
        Track<IntegritySettings>(ChangeSubjectKind.IntegritySettings, s => s.OrganizationId.ToString("D"), SettingsChanges.Integrity),

        // The Team page's one history. Its first member arrives with the organization, and that save creates the team.
        Track<OrganizationMember>(ChangeSubjectKind.Team, m => m.OrganizationId.ToString("D"), MemberAsync, creates: WithOrganization),
        // Invitations and API keys hold a secret, so their describers see a view without it.
        Track<OrganizationInvitation, InvitationFacts>(ChangeSubjectKind.Team, i => i.OrganizationId.ToString("D"), InvitationFacts.Of, InvitationAsync),
        Track<ApiKey, ApiKeyFacts>(ChangeSubjectKind.ApiKeys, k => k.OrganizationId.ToString("D"), ApiKeyFacts.Of, ApiKeyChanges.Describe),
    ];

    private static readonly ILookup<Type, TrackedKind> ByType = All.ToLookup(k => k.EntityType);

    /// <summary>The tracked kinds of <paramref name="entityType"/>, none when its changes are not recorded.</summary>
    public static IReadOnlyList<TrackedKind> FindAll(Type entityType) => [.. ByType[entityType]];

    /// <summary>
    /// A kind whose lines need nothing beyond the entity before and after the save. <c>creates</c> says whether adding the
    /// entity creates its subject, so Last updated says Created; null for never.
    /// </summary>
    public static TrackedKind Track<T>(
        ChangeSubjectKind subject, Func<T, string> subjectId, Func<T?, T?, IReadOnlyList<ChangeLine>> describe,
        Func<T, ChangeContext, bool>? creates = null)
        where T : class =>
        new TrackedKind<T, T>(subject, subjectId, e => e, (before, after, _, _) => ValueTask.FromResult(describe(before, after)), creates);

    /// <summary>
    /// A kind whose lines also need something the save does not hold, such as the name of a source template. <c>creates</c>
    /// as above.
    /// </summary>
    public static TrackedKind Track<T>(
        ChangeSubjectKind subject, Func<T, string> subjectId, ChangeDescriber<T> describe, Func<T, ChangeContext, bool>? creates = null)
        where T : class =>
        new TrackedKind<T, T>(subject, subjectId, e => e, describe, creates);

    /// <summary>
    /// A kind whose row holds a secret: its describer sees only <paramref name="view"/>, the values its lines need, so
    /// the secret never reaches a line. <c>TrackedKindsTests</c> checks the view, not the entity.
    /// </summary>
    public static TrackedKind Track<T, TView>(
        ChangeSubjectKind subject, Func<T, string> subjectId, Func<T, TView> view, Func<TView?, TView?, IReadOnlyList<ChangeLine>> describe)
        where T : class
        where TView : class =>
        new TrackedKind<T, TView>(subject, subjectId, view, (before, after, _, _) => ValueTask.FromResult(describe(before, after)), null);

    /// <summary>A kind seen through a view whose lines also need something the save does not hold, such as a role's name.</summary>
    public static TrackedKind Track<T, TView>(
        ChangeSubjectKind subject, Func<T, string> subjectId, Func<T, TView> view, ChangeDescriber<TView> describe)
        where T : class
        where TView : class =>
        new TrackedKind<T, TView>(subject, subjectId, view, describe, null);

    private static async ValueTask<IReadOnlyList<ChangeLine>> TemplateAsync(
        HiringTemplate? before, HiringTemplate? after, ChangeContext context, CancellationToken cancellationToken) =>
        HiringTemplateChanges.Template(
            before, after, before is null ? await context.TemplateNameAsync(after?.CopiedFromTemplateId, cancellationToken) : null);

    private static async ValueTask<IReadOnlyList<ChangeLine>> TemplateVersionAsync(
        HiringTemplateVersion? before, HiringTemplateVersion? after, ChangeContext context, CancellationToken cancellationToken)
    {
        if (before is not null || after is null)
        {
            return HiringTemplateChanges.Version(before, after);
        }

        // Version 1 arrives with its template, and "created the template" says it.
        if (context.Adds<HiringTemplate>(t => t.Id == after.TemplateId))
        {
            return HiringTemplateChanges.Version(null, after, addedWithTemplate: true);
        }

        return HiringTemplateChanges.Version(null, after, await context.PreviousDefinitionAsync(after.TemplateId, after.Version, cancellationToken));
    }

    private static ValueTask<IReadOnlyList<ChangeLine>> EmailTemplateAsync(
        EmailTemplate? before, EmailTemplate? after, ChangeContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(EmailTemplateChanges.Describe(before, after, context.BuiltInWording((after ?? before)?.BuiltInId)));

    private static async ValueTask<IReadOnlyList<ChangeLine>> ScorecardTemplateAsync(
        ScorecardTemplate? before, ScorecardTemplate? after, ChangeContext context, CancellationToken cancellationToken) =>
        ScorecardTemplateChanges.Describe(
            before, after, before is null ? await context.ScorecardNameAsync(after?.CopiedFrom, cancellationToken) : null);

    private static async ValueTask<IReadOnlyList<ChangeLine>> JobAsync(
        Job? before, Job? after, ChangeContext context, CancellationToken cancellationToken)
    {
        // Only a new job, or one that took a template update, names its template.
        var named = after is not null && (before is null || before.SourceTemplateVersion != after.SourceTemplateVersion);
        return JobChanges.Describe(before, after, named ? await context.TemplateNameAsync(after!.SourceTemplateId, cancellationToken) : null);
    }

    private static bool WithOrganization(OrganizationMember member, ChangeContext context) =>
        context.Adds<Organization>(o => o.Id == member.OrganizationId);

    private static async ValueTask<IReadOnlyList<ChangeLine>> MemberAsync(
        OrganizationMember? before, OrganizationMember? after, ChangeContext context, CancellationToken cancellationToken) =>
        TeamChanges.Member(
            before,
            after,
            before is null ? null : await context.RoleNameAsync(before.Role, before.CustomRoleId, cancellationToken),
            after is null ? null : await context.RoleNameAsync(after.Role, after.CustomRoleId, cancellationToken),
            before is null && after is not null && WithOrganization(after, context));

    private static async ValueTask<IReadOnlyList<ChangeLine>> InvitationAsync(
        InvitationFacts? before, InvitationFacts? after, ChangeContext context, CancellationToken cancellationToken) =>
        TeamChanges.Invitation(
            before, after, before is null && after is not null ? await context.RoleNameAsync(after.Role, after.CustomRoleId, cancellationToken) : null);
}

/// <summary>Says what changed in one entity: <paramref name="before"/> is null when the save adds it, <paramref name="after"/> when it deletes it.</summary>
public delegate ValueTask<IReadOnlyList<ChangeLine>> ChangeDescriber<T>(T? before, T? after, ChangeContext context, CancellationToken cancellationToken)
    where T : class;

/// <summary>One entity type in <see cref="TrackedKinds"/>: the subject its rows belong to and how to describe a change.</summary>
public abstract class TrackedKind
{
    public abstract Type EntityType { get; }

    /// <summary>The type its describer sees: the entity itself, or a view of it without its secret.</summary>
    public abstract Type ViewType { get; }

    public abstract ChangeSubjectKind Subject { get; }

    public abstract string SubjectId(object entity);

    /// <summary>Whether adding <paramref name="entity"/> creates its subject, rather than changing one that exists.</summary>
    public abstract bool Creates(object entity, ChangeContext context);

    /// <summary>The lines for one tracked entry: the entity as loaded (its original values) against the entity now.</summary>
    public abstract ValueTask<IReadOnlyList<ChangeLine>> DescribeAsync(EntityEntry entry, ChangeContext context, CancellationToken cancellationToken);
}

internal sealed class TrackedKind<T, TView>(
    ChangeSubjectKind subject, Func<T, string> subjectId, Func<T, TView> view, ChangeDescriber<TView> describe, Func<T, ChangeContext, bool>? creates)
    : TrackedKind
    where T : class
    where TView : class
{
    public override Type EntityType => typeof(T);

    public override Type ViewType => typeof(TView);

    public override ChangeSubjectKind Subject => subject;

    public override string SubjectId(object entity) => subjectId((T)entity);

    public override bool Creates(object entity, ChangeContext context) => creates?.Invoke((T)entity, context) ?? false;

    public override ValueTask<IReadOnlyList<ChangeLine>> DescribeAsync(EntityEntry entry, ChangeContext context, CancellationToken cancellationToken)
    {
        // JSON columns snapshot their original value through their comparer, so a definition's "before" is its own copy.
        var before = entry.State == EntityState.Added ? null : view((T)entry.OriginalValues.ToObject());
        var after = entry.State == EntityState.Deleted ? null : view((T)entry.Entity);
        return describe(before, after, context, cancellationToken);
    }
}
