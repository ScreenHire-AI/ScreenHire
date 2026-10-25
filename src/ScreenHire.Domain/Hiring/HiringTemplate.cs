namespace ScreenHire.Domain.Hiring;

/// <summary>
/// A reusable bundle of a hiring workflow and posting defaults for a type of role (workflow-engine.md §1). Its content
/// lives in numbered <see cref="HiringTemplateVersion"/>s: at most one draft, which can change, and published versions,
/// which never do. A system template ships with the product, belongs to no organization (<see cref="OrganizationId"/>
/// is null) and is read-only; organizations copy it to customise it.
/// </summary>
/// <remarks>
/// Not <c>ITenantOwned</c>, whose organization is never null: <c>ScreenHireDbContext</c> gives templates their own query
/// filter (own organization or system), mirroring their row-level security policies.
/// </remarks>
public sealed class HiringTemplate
{
    private readonly List<HiringTemplateVersion> versions = [];

    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The owning organization, or null for a system template.</summary>
    public Guid? OrganizationId { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>The template this one was copied from, a system template or the organization's own, if any.</summary>
    public Guid? CopiedFromTemplateId { get; init; }

    /// <summary>The newest published version's number, or null if none is published yet.</summary>
    public int? LatestPublishedVersion { get; private set; }

    public bool IsSystem => OrganizationId is null;

    /// <summary>Every version, oldest first. Load them with the template before calling the methods below.</summary>
    public IReadOnlyList<HiringTemplateVersion> Versions => versions;

    public HiringTemplateVersion? Draft => versions.SingleOrDefault(v => v.Status == TemplateVersionStatus.Draft);

    /// <summary>
    /// A new organization template whose first version is a draft holding <paramref name="definition"/>, copied from
    /// the template <paramref name="copiedFrom"/> when one is given.
    /// </summary>
    public static HiringTemplate Create(
        Guid organizationId, string name, string? description, HiringTemplateDefinition definition, DateTimeOffset now, Guid? userId, Guid? copiedFrom = null)
    {
        var template = new HiringTemplate
        {
            OrganizationId = organizationId, Name = name, Description = description, CreatedAt = now, CopiedFromTemplateId = copiedFrom,
        };
        template.CreateDraft(definition, now, userId);
        return template;
    }

    /// <summary>Starts the next version as a draft. A template has at most one draft; system templates have none.</summary>
    public HiringTemplateVersion CreateDraft(HiringTemplateDefinition definition, DateTimeOffset now, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        EnsureEditable();
        if (Draft is not null)
        {
            throw new InvalidOperationException($"Version {Draft.Version} is still a draft; publish or edit it instead.");
        }

        var draft = new HiringTemplateVersion
        {
            OrganizationId = OrganizationId,
            TemplateId = Id,
            Version = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1,
            Definition = definition,
            CreatedAt = now,
            CreatedBy = userId,
        };
        versions.Add(draft);
        UpdatedAt = now;
        return draft;
    }

    /// <summary>Publishes the draft <paramref name="version"/>. From now on it never changes, and new jobs can use it.</summary>
    public void Publish(HiringTemplateVersion version, DateTimeOffset now, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(version);
        EnsureEditable();
        if (!versions.Contains(version))
        {
            throw new ArgumentException("The version belongs to another template.", nameof(version));
        }

        version.MarkPublished(now, userId);
        LatestPublishedVersion = Math.Max(LatestPublishedVersion ?? 0, version.Version);
        UpdatedAt = now;
    }

    public void Rename(string name, string? description, DateTimeOffset now)
    {
        EnsureEditable();
        Name = name;
        Description = description;
        UpdatedAt = now;
    }

    private void EnsureEditable()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System templates are read-only; copy one to customise it.");
        }
    }
}

/// <summary>
/// One numbered version of a <see cref="HiringTemplate"/>. A draft's definition can be replaced; a published version is
/// immutable, and the database refuses to change one (its row-level security policy updates drafts only).
/// </summary>
public sealed class HiringTemplateVersion
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The template's organization, or null for a system template.</summary>
    public Guid? OrganizationId { get; init; }

    public Guid TemplateId { get; init; }

    public int Version { get; init; }

    public TemplateVersionStatus Status { get; private set; } = TemplateVersionStatus.Draft;

    public required HiringTemplateDefinition Definition { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public Guid? PublishedBy { get; private set; }

    /// <summary>Replaces a draft's definition.</summary>
    public void Revise(HiringTemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Status != TemplateVersionStatus.Draft)
        {
            throw new InvalidOperationException($"Version {Version} is published and cannot change; create a new version.");
        }

        if (OrganizationId is null)
        {
            throw new InvalidOperationException("System templates are read-only; copy one to customise it.");
        }

        Definition = definition;
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

public enum TemplateVersionStatus
{
    Draft,
    Published,
}
