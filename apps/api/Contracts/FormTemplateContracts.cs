using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// One library form in the list (workflow-engine.md section 7, Forms). <c>name</c> and <c>questions</c> are the newest
/// version's; <c>latestPublishedVersion</c> is the version a hiring template can use, null until one is published, and
/// <c>draftVersion</c> the draft's number while there is one.
/// </summary>
public sealed record FormTemplateSummary(
    Guid Id,
    string Name,
    FormKind Kind,
    int Questions,
    int? LatestPublishedVersion,
    int? DraftVersion,
    LastUpdate LastUpdated,
    bool Archived);

/// <summary>
/// The form library: the organization's forms by name, and whether this deployment offers equality monitoring forms
/// (<c>ScreenHire:Forms:EqualityMonitoring:Enabled</c>, off until a legal review).
/// </summary>
public sealed record FormTemplateList(IReadOnlyList<FormTemplateSummary> Forms, bool EqualityMonitoringEnabled);

/// <summary>One version of a library form, without its content.</summary>
public sealed record FormTemplateVersionSummary(int Version, TemplateVersionStatus Status, string Name, int Questions, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

/// <summary>A library form with its versions, newest last. <c>copiedFromFormId</c>: the form it was copied from, if any.</summary>
public sealed record FormTemplateDetail(
    Guid Id,
    string Name,
    FormKind Kind,
    Guid? CopiedFromFormId,
    int? LatestPublishedVersion,
    IReadOnlyList<FormTemplateVersionSummary> Versions,
    LastUpdate LastUpdated,
    bool Archived);

/// <summary>
/// One version of a library form with its content, which a hiring template copies into a follow-up questionnaire. The
/// content's JSON Schema is the follow-up questionnaire's questions and sections.
/// </summary>
public sealed record FormTemplateVersionDetail(
    Guid FormTemplateId,
    int Version,
    TemplateVersionStatus Status,
    FormKind Kind,
    FormContent Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);

/// <summary>
/// A new library form. Its first version starts as a draft holding <c>content</c>, or a copy of
/// <c>copyFromFormId</c>'s newest version. <c>kind</c> defaults to Standard; EqualityMonitoring only where this deployment
/// offers it. A copy keeps its source's kind.
/// </summary>
public sealed record CreateFormTemplateRequest(FormContent? Content = null, FormKind? Kind = null, Guid? CopyFromFormId = null);

/// <summary>The next version as a draft: <c>content</c>, or a copy of the newest version's when it is left out.</summary>
public sealed record CreateFormTemplateVersionRequest(FormContent? Content = null);

/// <summary>A draft's new content.</summary>
public sealed record UpdateFormTemplateVersionRequest(FormContent Content);

/// <summary>The hiring templates to update to this published version of the form.</summary>
public sealed record ApplyFormToTemplatesRequest(IReadOnlyList<Guid> TemplateIds);

/// <summary>
/// What updating one hiring template did. <c>Published</c>: it had no draft, so the update is its new published version
/// <c>version</c>, which new jobs get; its jobs stay on theirs until someone updates them. <c>UpdatedDraft</c>: its draft
/// <c>version</c> now holds the form, waiting to be published with the rest of the draft. <c>NotUpdated</c>: nothing changed,
/// and <c>problem</c> says why.
/// </summary>
public enum FormUpdateOutcome
{
    Published,
    UpdatedDraft,
    NotUpdated,
}

public sealed record FormTemplateUpdate(Guid TemplateId, string TemplateName, FormUpdateOutcome Outcome, int? Version = null, string? Problem = null);

public sealed record ApplyFormToTemplatesResult(IReadOnlyList<FormTemplateUpdate> Templates);

internal static class FormTemplateMappings
{
    public static FormTemplateSummary ToSummary(this FormTemplate form, ChangeStamp? lastChange)
    {
        var newest = form.Versions.MaxBy(v => v.Version);
        return new(form.Id, newest?.Content.Name ?? form.Name, form.Kind, newest?.Content.Questions?.Count ?? 0, form.LatestPublishedVersion,
            form.Draft?.Version, LastUpdate.From(lastChange, form.CreatedAt, form.UpdatedAt), form.ArchivedAt is not null);
    }

    public static FormTemplateDetail ToDetail(this FormTemplate form, ChangeStamp? lastChange) =>
        new(form.Id, form.Versions.MaxBy(v => v.Version)?.Content.Name ?? form.Name, form.Kind, form.CopiedFromFormId, form.LatestPublishedVersion,
            [.. form.Versions.OrderBy(v => v.Version).Select(v => new FormTemplateVersionSummary(
                v.Version, v.Status, v.Content.Name, v.Content.Questions?.Count ?? 0, v.CreatedAt, v.PublishedAt))],
            LastUpdate.From(lastChange, form.CreatedAt, form.UpdatedAt), form.ArchivedAt is not null);

    public static FormTemplateVersionDetail ToDetail(this FormTemplateVersion version, FormKind kind) =>
        new(version.FormTemplateId, version.Version, version.Status, kind, version.Content, version.CreatedAt, version.PublishedAt);
}
