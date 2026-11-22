using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Hiring;

/// <summary>
/// One of an organization's library forms (workflow-engine.md §7, Forms): a follow-up questionnaire built once and used
/// by any number of hiring templates. Its content lives in numbered <see cref="FormTemplateVersion"/>s like a hiring
/// template's: at most one draft, which can change, and published versions, which never do. A hiring template copies a
/// published version into its follow-up questionnaire and names it (<see cref="FollowUpQuestionnaire.FormTemplateId"/>,
/// <see cref="FollowUpQuestionnaire.FormVersion"/>); publishing a newer version changes no template until someone
/// updates it.
/// </summary>
public sealed class FormTemplate : ITenantOwned
{
    private readonly List<FormTemplateVersion> versions = [];

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The newest version's name, kept here for lists and the history.</summary>
    public required string Name { get; set; }

    /// <summary>What the form is for. Set when it is made and never changed, so no answer ever changes kind.</summary>
    public FormKind Kind { get; init; }

    /// <summary>The library form this one was copied from, if any.</summary>
    public Guid? CopiedFromFormId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The newest published version's number, or null if none is published yet.</summary>
    public int? LatestPublishedVersion { get; private set; }

    /// <summary>When the organization archived it, or null. An archived form leaves the pickers and reads only.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Every version, oldest first. Load them with the form before calling the methods below.</summary>
    public IReadOnlyList<FormTemplateVersion> Versions => versions;

    public FormTemplateVersion? Draft => versions.SingleOrDefault(v => v.Status == TemplateVersionStatus.Draft);

    /// <summary>The newest published version, or null.</summary>
    public FormTemplateVersion? LatestPublished =>
        versions.Where(v => v.Status == TemplateVersionStatus.Published).MaxBy(v => v.Version);

    /// <summary>A new library form whose first version is a draft holding <paramref name="content"/>.</summary>
    public static FormTemplate Create(
        Guid organizationId, FormKind kind, FormContent content, DateTimeOffset now, Guid? userId, Guid? copiedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var form = new FormTemplate
        {
            OrganizationId = organizationId, Name = content.Name, Kind = kind, CopiedFromFormId = copiedFrom, CreatedAt = now, UpdatedAt = now,
        };
        form.CreateDraft(content, now, userId);
        return form;
    }

    /// <summary>Starts the next version as a draft. A form has at most one draft.</summary>
    public FormTemplateVersion CreateDraft(FormContent content, DateTimeOffset now, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureEditable();
        if (Draft is not null)
        {
            throw new InvalidOperationException($"Version {Draft.Version} is still a draft; publish or edit it instead.");
        }

        var draft = new FormTemplateVersion
        {
            OrganizationId = OrganizationId,
            FormTemplateId = Id,
            Version = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1,
            Content = content,
            CreatedAt = now,
            CreatedBy = userId,
        };
        versions.Add(draft);
        Name = content.Name;
        UpdatedAt = now;
        return draft;
    }

    /// <summary>Replaces the draft's content.</summary>
    public void Revise(FormTemplateVersion version, FormContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(content);
        EnsureEditable();
        EnsureOwn(version);
        version.Revise(content);
        Name = content.Name;
        UpdatedAt = now;
    }

    /// <summary>Publishes the draft <paramref name="version"/>. From now on it never changes, and templates can use it.</summary>
    public void Publish(FormTemplateVersion version, DateTimeOffset now, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(version);
        EnsureEditable();
        EnsureOwn(version);
        version.MarkPublished(now, userId);
        LatestPublishedVersion = Math.Max(LatestPublishedVersion ?? 0, version.Version);
        UpdatedAt = now;
    }

    /// <summary>Takes the form out of the pickers. Templates and jobs that use it keep their copies.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (ArchivedAt is not null)
        {
            return;
        }

        ArchivedAt = now;
        UpdatedAt = now;
    }

    public void Unarchive(DateTimeOffset now)
    {
        if (ArchivedAt is null)
        {
            return;
        }

        ArchivedAt = null;
        UpdatedAt = now;
    }

    /// <summary>A hiring template's copy of <paramref name="version"/>, under the follow-up id <paramref name="followUpId"/>.</summary>
    public FollowUpQuestionnaire ToFollowUp(FormTemplateVersion version, string followUpId)
    {
        ArgumentNullException.ThrowIfNull(version);
        EnsureOwn(version);
        return new(followUpId, version.Content.Name, version.Content.Questions, version.Content.Sections,
            Id, version.Version, Kind == FormKind.Standard ? null : Kind);
    }

    private void EnsureOwn(FormTemplateVersion version)
    {
        if (!versions.Contains(version))
        {
            throw new ArgumentException("The version belongs to another form.", nameof(version));
        }
    }

    private void EnsureEditable()
    {
        if (ArchivedAt is not null)
        {
            throw new InvalidOperationException("The form is archived; restore it to change it.");
        }
    }
}

/// <summary>
/// One numbered version of a <see cref="FormTemplate"/>. A draft's content can be replaced; a published version is
/// immutable, and the database refuses to change one (its row-level security policy updates drafts only).
/// </summary>
public sealed class FormTemplateVersion : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid FormTemplateId { get; init; }

    public int Version { get; init; }

    public TemplateVersionStatus Status { get; private set; } = TemplateVersionStatus.Draft;

    public required FormContent Content { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public Guid? PublishedBy { get; private set; }

    internal void Revise(FormContent content)
    {
        if (Status != TemplateVersionStatus.Draft)
        {
            throw new InvalidOperationException($"Version {Version} is published and cannot change; create a new version.");
        }

        Content = content;
    }

    internal void MarkPublished(DateTimeOffset now, Guid? userId)
    {
        if (Status != TemplateVersionStatus.Draft)
        {
            throw new InvalidOperationException($"Version {Version} is already published.");
        }

        Status = TemplateVersionStatus.Published;
        PublishedAt = now;
        PublishedBy = userId;
    }
}
