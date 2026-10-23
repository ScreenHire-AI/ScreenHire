namespace ScreenHire.Domain.Hiring;

/// <summary>
/// The email templates a <see cref="ActionType.SendEmail"/> or <see cref="ActionType.Reject"/> action, or a person
/// sending by hand, can name. Three are built in, and every organization may edit their wording; an organization's own
/// templates (<see cref="EmailTemplate"/>) are named by their id as a Guid string. The validator checks the shape of an
/// id; the API checks that an organization template exists when a person saves a workflow that names it.
/// </summary>
public static class EmailTemplateIds
{
    /// <summary>Tells the candidate their application arrived.</summary>
    public const string ApplicationReceived = "application-received";

    /// <summary>Tells the candidate their application moved to a new stage.</summary>
    public const string StageChange = "stage-change";

    /// <summary>
    /// Tells the candidate they are not progressing, with the reason. Only a Reject action sends it (workflow-engine.md
    /// §2, guardrail 2): never a SendEmail action, and never a person sending by hand.
    /// </summary>
    public const string ApplicationRejected = "application-rejected";

    /// <summary>The built-in templates, in the order the dashboard lists them.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = [ApplicationReceived, StageChange, ApplicationRejected];

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ApplicationReceived, StageChange, ApplicationRejected };

    /// <summary>Whether <paramref name="id"/> names one of an organization's own templates: a Guid in its 36-character form.</summary>
    public static bool IsOrganizationTemplate(string id) => Guid.TryParseExact(id, "D", out _);

    /// <summary>Whether <paramref name="id"/> could name a template: a built-in id, or an organization template's id.</summary>
    public static bool IsWellFormed(string id) => All.Contains(id) || IsOrganizationTemplate(id);
}
