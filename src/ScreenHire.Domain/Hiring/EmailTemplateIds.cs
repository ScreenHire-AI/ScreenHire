namespace ScreenHire.Domain.Hiring;

/// <summary>
/// The transactional email templates a <see cref="ActionType.SendEmail"/> or <see cref="ActionType.Reject"/> action can
/// name. The validator refuses any other id, and the worker renders each one from its embedded template files.
/// </summary>
public static class EmailTemplateIds
{
    /// <summary>Tells the candidate their application arrived.</summary>
    public const string ApplicationReceived = "application-received";

    /// <summary>Tells the candidate their application moved to a new stage.</summary>
    public const string StageChange = "stage-change";

    /// <summary>Tells the candidate they are not progressing, with the reason. What a Reject action sends by default.</summary>
    public const string ApplicationRejected = "application-rejected";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ApplicationReceived, StageChange, ApplicationRejected };
}
