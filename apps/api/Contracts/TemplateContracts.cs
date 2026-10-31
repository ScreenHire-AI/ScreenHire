using FluentValidation;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// A hiring template in a list. System templates ship with the product and are read-only. An archived template
/// (<c>archived</c>) is out of the pickers: no new job starts from it, and the jobs that did keep their copy.
/// </summary>
public sealed record HiringTemplateSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int? LatestPublishedVersion,
    Guid? LatestPublishedVersionId,
    int? DraftVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    LastUpdate LastUpdated,
    bool Archived);

/// <summary>A hiring template with its versions, oldest first. <c>lastUpdated</c> counts every version's changes.</summary>
public sealed record HiringTemplateDetail(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int? LatestPublishedVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<HiringTemplateVersionSummary> Versions,
    LastUpdate LastUpdated,
    bool Archived);

public sealed record HiringTemplateVersionSummary(
    Guid Id, int Version, TemplateVersionStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

/// <summary>
/// One version of a hiring template with its full definition. The definition's JSON Schema is served at
/// <c>/v1/schemas/hiring-template-definition.json</c>.
/// </summary>
public sealed record HiringTemplateVersionDetail(
    Guid Id,
    Guid TemplateId,
    int Version,
    TemplateVersionStatus Status,
    HiringTemplateDefinition Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    Guid? PublishedBy);

/// <summary>
/// A new organization template, whose version 1 is a draft. Give a <c>definition</c>, or <c>copyFromVersionId</c> to
/// start from an existing version (a system template's, to customise it), or neither for a basic pipeline.
/// </summary>
public sealed record CreateHiringTemplateRequest(
    string Name, string? Description = null, HiringTemplateDefinition? Definition = null, Guid? CopyFromVersionId = null);

/// <summary>Renames a template.</summary>
public sealed record UpdateHiringTemplateRequest(string Name, string? Description = null);

/// <summary>Starts the next version as a draft: the given definition, or a copy of the newest version's.</summary>
public sealed record CreateHiringTemplateVersionRequest(HiringTemplateDefinition? Definition = null);

/// <summary>Replaces a draft version's definition.</summary>
public sealed record UpdateHiringTemplateVersionRequest(HiringTemplateDefinition Definition);

internal static class TemplateMappings
{
    public static HiringTemplateSummary ToSummary(this HiringTemplate template, ChangeStamp? lastChange) =>
        new(template.Id, template.Name, template.Description, template.IsSystem, template.LatestPublishedVersion,
            template.Versions.FirstOrDefault(v => v.Version == template.LatestPublishedVersion)?.Id,
            template.Draft?.Version, template.CreatedAt, template.UpdatedAt, template.Updated(lastChange), template.ArchivedAt is not null);

    public static HiringTemplateDetail ToDetail(this HiringTemplate template, ChangeStamp? lastChange) =>
        new(template.Id, template.Name, template.Description, template.IsSystem, template.LatestPublishedVersion,
            template.CreatedAt, template.UpdatedAt,
            [.. template.Versions.OrderBy(v => v.Version).Select(v => new HiringTemplateVersionSummary(v.Id, v.Version, v.Status, v.CreatedAt, v.PublishedAt))],
            template.Updated(lastChange), template.ArchivedAt is not null);

    // A system template is ScreenHire's: nobody in an organization can change it.
    private static LastUpdate Updated(this HiringTemplate template, ChangeStamp? lastChange) =>
        template.IsSystem
            ? LastUpdate.ByScreenHire(template.UpdatedAt ?? template.CreatedAt)
            : LastUpdate.From(lastChange, template.CreatedAt, template.UpdatedAt);

    public static HiringTemplateVersionDetail ToDetail(this HiringTemplateVersion version) =>
        new(version.Id, version.TemplateId, version.Version, version.Status, version.Definition, version.CreatedAt,
            version.PublishedAt, version.PublishedBy);
}

internal sealed class TemplateNameValidator : AbstractValidator<UpdateHiringTemplateRequest>
{
    public TemplateNameValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
        RuleFor(r => r.Description).MaximumLength(1000);
    }
}

internal sealed class CreateHiringTemplateRequestValidator : AbstractValidator<CreateHiringTemplateRequest>
{
    public CreateHiringTemplateRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
        RuleFor(r => r.Description).MaximumLength(1000);
        RuleFor(r => r.CopyFromVersionId).Null().When(r => r.Definition is not null)
            .WithMessage("Give a definition or a version to copy, not both.");
    }
}
