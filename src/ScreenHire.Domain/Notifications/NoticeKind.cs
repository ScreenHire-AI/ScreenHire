namespace ScreenHire.Domain.Notifications;

/// <summary>
/// A row of the person's notification choices on Your account (docs/architecture/notifications.md, "Each person's
/// choices"): what they can be told about, by toast, email and Slack. Stored and sent as text; new rows are appended.
/// </summary>
public enum NoticeKind
{
    Mentions,
    Replies,
    Interviews,
    Tasks,
    NewApplications,
    TeammateChanges,
    VerificationSuggested,
    AiUseAndBilling,
}

/// <summary>How a person wants a kind by email: each one as it happens, in the daily digest, or not at all.</summary>
public enum EmailChoice
{
    Instantly,
    Digest,
    Off,
}

/// <summary>
/// Which jobs the job rows (new applications, teammates' changes, verification suggested) cover: the jobs whose hiring
/// team the person is on, or every job they can see.
/// </summary>
public enum NoticeScope
{
    MyJobs,
    Everything,
}

/// <summary>
/// One person's choice for one row, each channel null while they have not changed it, so the default (which may follow the
/// organization's alert settings) still applies.
/// </summary>
public sealed record NoticeChoice(NoticeKind Kind, EmailChoice? Email = null, bool? Slack = null);

/// <summary>
/// The rows a person changed from their defaults. A row they never touched, or set back to its default, is absent, so a
/// later change to an organization default reaches them.
/// </summary>
public sealed record NoticeChoices
{
    public static NoticeChoices Default { get; } = new();

    public IReadOnlyList<NoticeChoice> Choices { get; init; } = [];

    public NoticeChoice? For(NoticeKind kind) => Choices.FirstOrDefault(c => c.Kind == kind);

    public bool Equals(NoticeChoices? other) => other is not null && Choices.SequenceEqual(other.Choices);

    public override int GetHashCode() => Choices.Count;
}
