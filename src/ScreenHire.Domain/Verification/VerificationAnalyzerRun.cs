using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Verification;

/// <summary>
/// One analyzer's pass over one application. The pipeline's idempotency key (verification-engine.md §5): a unique index on
/// (application, analyzer, analyzer version) means a redelivered message, or a second run after a restart, finds the
/// run and writes no signals twice. A new analyzer version runs again and writes its own signals.
/// </summary>
public sealed class VerificationAnalyzerRun : IApplicationScoped
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The analyzer's name, such as <c>network</c>.</summary>
    public required string Analyzer { get; init; }

    public int AnalyzerVersion { get; init; }

    /// <summary>How many signals the run wrote, including none.</summary>
    public int SignalCount { get; init; }

    /// <summary>
    /// Why the run observed nothing although it ran, such as "no IP intelligence configured"; null when it had what it
    /// needed. Lets an audit tell "checked, nothing found" from "could not check".
    /// </summary>
    public string? Note { get; init; }

    public DateTimeOffset CompletedAt { get; init; }
}
