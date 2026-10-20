using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// One run of the LLM consistency check for one application and prompt version (<c>CNT_INCONSISTENCY</c>,
/// integrity-engine.md §10.10): whether it ran, and if it did, the contradictions it found between the resume, the
/// answers and an uploaded LinkedIn export. A unique index on (application, prompt version) means a redelivered message
/// never calls the model twice.
/// </summary>
/// <remarks>
/// The row is claimed (<see cref="ConsistencyCheckStatus.Running"/>) before the model is called and completed after, in
/// separate transactions, so no database transaction is open while the model works.
/// </remarks>
public sealed class ConsistencyCheck : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public required string PromptVersion { get; init; }

    public ConsistencyCheckStatus Status { get; private set; } = ConsistencyCheckStatus.Running;

    /// <summary>Why it did not run or did not finish, in plain language. Null when it completed.</summary>
    public string? Note { get; private set; }

    /// <summary>The model ids the provider reported, once it answered.</summary>
    public string? ExtractionModel { get; private set; }

    public string? ConsistencyModel { get; private set; }

    /// <summary>Each contradiction with its two quoted spans, verified against the text that was sent.</summary>
    public IReadOnlyList<Contradiction> Contradictions { get; private set; } = [];

    /// <summary>Tokens the calls used, for the organization's monthly budget. Counts, never money.</summary>
    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>A claim that is still running, ready to save before the model is called.</summary>
    public static ConsistencyCheck Claim(Guid organizationId, Guid applicationId, string promptVersion, DateTimeOffset now) => new()
    {
        OrganizationId = organizationId,
        ApplicationId = applicationId,
        PromptVersion = promptVersion,
        StartedAt = now,
    };

    /// <summary>Takes over a claim whose worker stopped without finishing it.</summary>
    public void Reclaim(DateTimeOffset now)
    {
        if (Status != ConsistencyCheckStatus.Running)
        {
            throw new InvalidOperationException("Only a running check can be taken over.");
        }

        StartedAt = now;
    }

    /// <summary>The check finished: the contradictions found (possibly none) and what it used.</summary>
    public void Complete(ConsistencyCheckOutcome outcome, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Status = outcome.Status;
        Note = outcome.Note;
        ExtractionModel = outcome.ExtractionModel;
        ConsistencyModel = outcome.ConsistencyModel;
        Contradictions = outcome.Contradictions;
        InputTokens = outcome.InputTokens;
        OutputTokens = outcome.OutputTokens;
        CompletedAt = now;
    }
}

public enum ConsistencyCheckStatus
{
    /// <summary>Claimed; the model is being asked.</summary>
    Running,

    Completed,

    /// <summary>No model is configured, or the model could not be reached or did not answer usably. Not a failure of the application.</summary>
    Unavailable,

    /// <summary>The organization has the check turned off.</summary>
    Disabled,

    /// <summary>The organization's monthly token budget is spent.</summary>
    OverBudget,

    /// <summary>Fewer than two sources have text to compare.</summary>
    NotEnoughSources,
}

/// <summary>How a check ended. Only <see cref="ConsistencyCheckStatus.Completed"/> carries contradictions.</summary>
public sealed record ConsistencyCheckOutcome(
    ConsistencyCheckStatus Status,
    string? Note = null,
    string? ExtractionModel = null,
    string? ConsistencyModel = null,
    IReadOnlyList<Contradiction>? ContradictionsFound = null,
    long InputTokens = 0,
    long OutputTokens = 0)
{
    public IReadOnlyList<Contradiction> Contradictions => ContradictionsFound ?? [];
}

/// <summary>
/// Two statements that cannot both be true, each quoted word for word from its source. The topic is one of a fixed set,
/// so the recruiter-facing wording is ours, never the model's.
/// </summary>
public sealed record Contradiction(ContradictionTopic Topic, CitedSpan Left, CitedSpan Right);

/// <summary>A verbatim quote and the source it came from.</summary>
public sealed record CitedSpan(ConsistencySource Source, string Quote);

/// <summary>Where a statement came from. Only what the candidate supplied (CLAUDE.md non-negotiable 5).</summary>
public enum ConsistencySource
{
    Resume,
    Answers,
    LinkedInExport,
}

/// <summary>
/// The verifiable facts the check compares. Deliberately narrow: work history, education and credentials. Nothing about
/// a person's character, feelings or where they live.
/// </summary>
public enum ContradictionTopic
{
    Employer,
    JobTitle,
    EmploymentDates,
    YearsOfExperience,
    Education,
    Certification,
}
