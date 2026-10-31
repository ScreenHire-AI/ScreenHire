namespace ScreenHire.Domain.Organizations;

/// <summary>
/// What a member's dashboard tells them about teammates' changes, as short toasts (docs/architecture/realtime.md,
/// "Teammates' changes as toasts"). One per membership, so each organization keeps its own. Everything starts on, and
/// <see cref="Off"/> holds the kinds turned off, so a kind added later starts on as well.
/// </summary>
public sealed record NotificationSettings
{
    /// <summary>Everything on, the settings of every member who has not changed them.</summary>
    public static NotificationSettings Default { get; } = new();

    /// <summary>Whether teammates' changes show as toasts at all.</summary>
    public bool Toasts { get; init; } = true;

    /// <summary>Whether a toast closes on its own after a few seconds; off keeps each until it is dismissed (WCAG 2.2.1).</summary>
    public bool CloseToasts { get; init; } = true;

    /// <summary>The kinds of change that do not toast.</summary>
    public IReadOnlyList<ToastKind> Off { get; init; } = [];

    public bool Equals(NotificationSettings? other) =>
        other is not null && Toasts == other.Toasts && CloseToasts == other.CloseToasts && Off.SequenceEqual(other.Off);

    public override int GetHashCode() => HashCode.Combine(Toasts, CloseToasts, Off.Count);
}

/// <summary>
/// A kind of teammate's change a member can turn off. The first six are about a candidate on a job whose hiring team
/// they are on; <see cref="Tasks"/> is a task assigned to them; <see cref="Templates"/> a hiring template they changed
/// last, published by someone else. Stored and sent as text.
/// </summary>
public enum ToastKind
{
    Moves,
    NewCandidates,
    Notes,
    Tags,
    Emails,
    Scorecards,
    Tasks,
    Templates,
}
