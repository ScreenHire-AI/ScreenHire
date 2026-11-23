namespace ScreenHire.Domain.Applications;

/// <summary>
/// The principal reason an application was rejected, kept on the move's timeline entry so the team can tell a candidate
/// why in plain words (Colorado's adverse-outcome disclosure, C.R.S. 6-1-1704(3); docs/research/compliance-gap-map.md,
/// CO-5). A short fixed list, so a disclosure reads the same everywhere. There is deliberately no reason about the checks:
/// a check never rejects anyone (ADR-0004), and a person who rejects after asking a candidate to verify writes why in the
/// note with <see cref="Other"/>.
/// </summary>
public enum RejectionReason
{
    /// <summary>The application did not meet a requirement the job states.</summary>
    RequirementNotMet,

    /// <summary>An answer to a knockout question; set by an automation that rejects on one, and by nobody else.</summary>
    KnockoutAnswer,

    /// <summary>Another candidate was a closer fit.</summary>
    AnotherCandidate,

    /// <summary>The position was filled or closed.</summary>
    PositionClosed,

    /// <summary>The candidate did not reply.</summary>
    NoResponse,

    /// <summary>Anything else; the move's note says what.</summary>
    Other,
}
