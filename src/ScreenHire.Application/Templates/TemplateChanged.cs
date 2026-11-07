using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Templates;

/// <summary>The three kinds of template an organization edits on the Templates page.</summary>
public enum TemplateKind
{
    /// <summary>A hiring template: its name, a draft version, an archive, its default hiring team.</summary>
    HiringTemplate,

    /// <summary>An email template, including a built-in one the organization changed or reset.</summary>
    EmailTemplate,

    /// <summary>A library scorecard.</summary>
    ScorecardTemplate,
}

/// <summary>
/// Someone in the organization changed a template, so the open dashboards refresh the template lists and pickers. It
/// carries no content: a dashboard reads the template again. <see cref="TemplateId"/> is a text because a built-in email
/// template or scorecard has a name for an id, not a Guid. <see cref="ActorUserId"/> is the member who made the change
/// (null for an API key). <see cref="PreviousEditorUserId"/> is the member who changed an email template or a scorecard
/// last, read from its change history before this save, and null when that was the actor, an API key or ScreenHire, when
/// nobody had, and for a hiring template: only the first two kinds toast the previous editor, and a hiring template's
/// draft is not published work. Publishing a hiring template version raises <see cref="HiringTemplateVersionPublished"/>
/// instead, which also marks the jobs that copied an older version.
/// </summary>
public sealed record TemplateChanged(
    Guid OrganizationId, TemplateKind Kind, string TemplateId, DateTimeOffset ChangedAt, Guid? ActorUserId = null,
    Guid? PreviousEditorUserId = null) : ITenantEvent;
