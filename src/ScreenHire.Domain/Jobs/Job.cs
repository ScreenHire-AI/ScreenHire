using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Jobs;

/// <summary>
/// A job opening. The canonical job model is a superset of schema.org <c>JobPosting</c>
/// (docs/architecture/job-distribution.md §2). Its lifecycle is draft, then open, then closed. Each publish of new
/// content freezes a <see cref="JobVersion"/>, and the job carries its own copy of a hiring workflow
/// (docs/architecture/workflow-engine.md §1), usually from a template version.
/// </summary>
public sealed class Job : ITenantOwned
{
    private DateTimeOffset? validThrough;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Title { get; set; }

    /// <summary>URL-safe identifier, unique within the organization. Fixed once the job has been published.</summary>
    public required string Slug { get; set; }

    /// <summary>Rich text as sanitized HTML. The API sanitizes it on the way in; nothing else writes it.</summary>
    public string Description { get; set; } = "";

    public string? Department { get; set; }

    /// <summary>On-site, hybrid or remote: schema.org <c>jobLocationType</c> is TELECOMMUTE for remote.</summary>
    public WorkplaceType WorkplaceType { get; set; }

    public EmploymentType EmploymentType { get; set; }

    /// <summary>Where the job is based. On-site and hybrid jobs have at least one.</summary>
    public List<JobLocation> Locations { get; set; } = [];

    /// <summary>ISO 3166-1 alpha-2 countries a remote candidate must be in (schema.org <c>applicantLocationRequirements</c>).</summary>
    public List<string> ApplicantLocationRequirements { get; set; } = [];

    public decimal? SalaryMin { get; private set; }

    public decimal? SalaryMax { get; private set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string? SalaryCurrency { get; private set; }

    public SalaryPeriod? SalaryPeriod { get; private set; }

    /// <summary>When the posting expires (schema.org <c>validThrough</c>). Job distribution tears it down then.</summary>
    public DateTimeOffset? ValidThrough
    {
        get => validThrough;
        set => validThrough = value?.ToDatabasePrecision();
    }

    /// <summary>This job's own copy of the hiring workflow. Template changes never reach it silently.</summary>
    public HiringWorkflow Workflow { get; private set; } = HiringWorkflow.Default;

    /// <summary>Applications record the workflow version they entered under (workflow-engine.md §3).</summary>
    public int WorkflowVersion { get; private set; } = 1;

    /// <summary>The template the workflow was copied from, if any.</summary>
    public Guid? SourceTemplateId { get; private set; }

    public Guid? SourceTemplateVersionId { get; private set; }

    /// <summary>The copied template version's number; a published version above it means an update is available.</summary>
    public int? SourceTemplateVersion { get; private set; }

    public JobStatus Status { get; private set; } = JobStatus.Draft;

    /// <summary>
    /// The hiring team: the organization member ids (<see cref="OrganizationMember.Id"/>) of the people who work on this
    /// job. A member on Only jobs they're on sees the job, and everything under it, only while they are here (ADR-0015).
    /// Member ids rather than user ids, so someone who leaves and rejoins gets a new id and no old team counts again.
    /// Kept on the job's own row, so a new job carries its creator from the first insert.
    /// </summary>
    public List<Guid> HiringTeam { get; private set; } = [];

    /// <summary>
    /// Replaces the hiring team with <paramref name="memberIds"/>, without duplicates, and returns who joined and who
    /// left. The caller has checked that every id is a current member.
    /// </summary>
    public HiringTeamChange SetHiringTeam(IEnumerable<Guid> memberIds)
    {
        var after = memberIds.Distinct().ToList();
        var change = new HiringTeamChange([.. after.Except(HiringTeam)], [.. HiringTeam.Except(after)]);
        if (!change.IsEmpty)
        {
            HiringTeam = after;
        }

        return change;
    }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The newest <see cref="JobVersion"/>'s number, or 0 if the job has never been published.</summary>
    public int PublishedVersion { get; private set; }

    public JobSalary? Salary => SalaryCurrency is null ? null : new(SalaryMin, SalaryMax, SalaryCurrency, SalaryPeriod ?? Jobs.SalaryPeriod.Year);

    public void SetSalary(JobSalary? salary)
    {
        SalaryMin = salary?.Min;
        SalaryMax = salary?.Max;
        SalaryCurrency = salary?.Currency;
        SalaryPeriod = salary?.Period;
    }

    /// <summary>The posting content as it is now.</summary>
    public JobSnapshot Snapshot() =>
        new(Title, Slug, Description, Department, EmploymentType, WorkplaceType, [.. Locations], [.. ApplicantLocationRequirements], Salary, ValidThrough);

    /// <summary>
    /// Replaces the posting content. A closed job is final, and a published job keeps its slug, because its URL is live
    /// on the careers page and on job boards. Returns whether anything changed.
    /// </summary>
    public bool Revise(JobSnapshot content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Status == JobStatus.Closed)
        {
            throw new InvalidOperationException("A closed job cannot be changed; create a new job instead.");
        }

        if (content.Slug != Slug && PublishedAt is not null)
        {
            throw new InvalidOperationException("A published job keeps its slug, because its URL is already live.");
        }

        if (content == Snapshot())
        {
            return false;
        }

        Title = content.Title;
        Slug = content.Slug;
        Description = content.Description;
        Department = content.Department;
        EmploymentType = content.EmploymentType;
        WorkplaceType = content.WorkplaceType;
        Locations = [.. content.Locations];
        ApplicantLocationRequirements = [.. content.ApplicantLocationRequirements];
        SetSalary(content.Salary);
        ValidThrough = content.ValidThrough;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Gives a new job a copy of a published template version's workflow and remembers where it came from, so "update
    /// available" can be detected later (workflow-engine.md §1).
    /// </summary>
    /// <remarks>
    /// The template's stages name library scorecards; the job copies their questions here, from the built-ins or from
    /// <paramref name="scorecards"/>, the organization's own the template names, which the caller loaded.
    /// </remarks>
    public void UseTemplate(HiringTemplateVersion version, IReadOnlyDictionary<string, Scorecard>? scorecards = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Status != TemplateVersionStatus.Published)
        {
            throw new InvalidOperationException("Jobs are created from published template versions only.");
        }

        if (PublishedAt is not null)
        {
            throw new InvalidOperationException("A published job keeps its workflow; templates apply to new jobs.");
        }

        Workflow = WithScorecards(version.Definition.Workflow, scorecards);
        SourceTemplateId = version.TemplateId;
        SourceTemplateVersionId = version.Id;
        SourceTemplateVersion = version.Version;
    }

    /// <summary>
    /// Replaces the job's workflow with a recruiter's edit of it and starts a new workflow version. The caller has
    /// validated <paramref name="workflow"/> and checked that no application sits in a stage it drops. The source
    /// template stays recorded, so the template diff shows this job's edits against the template's newest version.
    /// </summary>
    public void ReviseWorkflow(HiringWorkflow workflow, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (Status == JobStatus.Closed)
        {
            throw new InvalidOperationException("A closed job cannot be changed; create a new job instead.");
        }

        Workflow = workflow;
        WorkflowVersion++;
        UpdatedAt = now;
    }

    /// <summary>
    /// Replaces the job's workflow with a newer published version of its source template, the recruiter's "apply
    /// update" (workflow-engine.md §1). It replaces the job's own edits too. Unlike <see cref="UseTemplate"/>, an open job
    /// may take it: a person asked for the change, so nothing is mutated silently.
    /// </summary>
    public void ApplyTemplateUpdate(HiringTemplateVersion version, DateTimeOffset now, IReadOnlyDictionary<string, Scorecard>? scorecards = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Status != TemplateVersionStatus.Published)
        {
            throw new InvalidOperationException("Jobs take published template versions only.");
        }

        if (version.TemplateId != SourceTemplateId)
        {
            throw new InvalidOperationException("A job takes updates from the template it was created from only.");
        }

        ReviseWorkflow(WithScorecards(version.Definition.Workflow, scorecards), now);
        SourceTemplateVersionId = version.Id;
        SourceTemplateVersion = version.Version;
    }

    // A job holds its own copy of every scorecard its template names (workflow-engine.md section 8).
    private static HiringWorkflow WithScorecards(HiringWorkflow workflow, IReadOnlyDictionary<string, Scorecard>? scorecards)
    {
        var copied = workflow.WithScorecardCopies(scorecards ?? new Dictionary<string, Scorecard>(), out var missing);
        return missing.Count == 0
            ? copied
            : throw new InvalidOperationException($"The scorecard of stage {workflow.Stages[missing[0]].Id} is not in the library; load it first.");
    }

    /// <summary>
    /// Opens the job and freezes its content as a new <see cref="JobVersion"/>, which the caller saves. Publishing an
    /// open job whose content equals <paramref name="latest"/> changes nothing and returns null, so a repeated click
    /// raises no second event. <paramref name="latest"/> is the job's newest version, or null if it has none.
    /// </summary>
    public JobVersion? Publish(DateTimeOffset now, JobVersion? latest = null)
    {
        if (Status == JobStatus.Closed)
        {
            throw new InvalidOperationException("A closed job cannot be republished; create a new job instead.");
        }

        if (ValidThrough is { } validThrough && validThrough <= now)
        {
            throw new InvalidOperationException("The posting's validThrough date has passed; move it forward before publishing.");
        }

        var snapshot = Snapshot();
        if (Status == JobStatus.Open && latest is not null && latest.Snapshot == snapshot)
        {
            return null;
        }

        Status = JobStatus.Open;
        PublishedAt ??= now;
        UpdatedAt = now;
        PublishedVersion = Math.Max(PublishedVersion, latest?.Version ?? 0) + 1;
        return new JobVersion
        {
            OrganizationId = OrganizationId,
            JobId = Id,
            Version = PublishedVersion,
            Snapshot = snapshot,
            PublishedAt = now,
        };
    }

    /// <summary>Closes the job. Closing a closed job changes nothing and returns false.</summary>
    public bool Close(DateTimeOffset now)
    {
        if (Status == JobStatus.Closed)
        {
            return false;
        }

        Status = JobStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;
        return true;
    }
}

public enum JobStatus
{
    Draft,
    Open,
    Closed,
}

public enum WorkplaceType
{
    Onsite,
    Hybrid,
    Remote,
}

public enum EmploymentType
{
    FullTime,
    PartTime,
    Contract,
    Temporary,
    Internship,
}

/// <summary>Who joined (<paramref name="Added"/>) and who left (<paramref name="Removed"/>) a hiring team, by member id.</summary>
public sealed record HiringTeamChange(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
}
