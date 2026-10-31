namespace ScreenHire.Domain.Hiring;

/// <summary>
/// The email templates a <see cref="ActionType.SendEmail"/> or <see cref="ActionType.Reject"/> action, or a person
/// sending by hand, can name. Four are built in, and every organization may edit their wording; an organization's own
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

    /// <summary>
    /// Asks the candidate to answer a follow-up questionnaire, with the signed link to it. Only a SendQuestionnaire action
    /// sends it, because the link belongs to one questionnaire request: never a SendEmail action or a person by hand.
    /// </summary>
    public const string QuestionnaireRequest = "questionnaire-request";

    /// <summary>
    /// Invites a candidate in the talent pool to apply for another job, with that job's link and a signed link to stop
    /// being kept. Only a person's Invite to apply sends it (docs/architecture/talent-pool.md): never an automation, and
    /// never as an ordinary email.
    /// </summary>
    public const string TalentPoolInvitation = "talent-pool-invitation";

    /// <summary>The built-in templates, in the order the dashboard lists them.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = [ApplicationReceived, StageChange, QuestionnaireRequest, ApplicationRejected, TalentPoolInvitation];

    /// <summary>ScreenHire's names for the built-in templates, which organizations cannot rename.</summary>
    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ApplicationReceived] = "Application received",
        [StageChange] = "Moved to the next step",
        [QuestionnaireRequest] = "Follow-up questions",
        [ApplicationRejected] = "Not progressing",
        [TalentPoolInvitation] = "Invitation to apply",
    };

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ApplicationReceived, StageChange, QuestionnaireRequest, ApplicationRejected, TalentPoolInvitation };

    /// <summary>
    /// Whether only its own action sends <paramref name="id"/>: the rejection email (Reject), the follow-up questions
    /// email (SendQuestionnaire) and the talent pool invitation (a person's Invite to apply). None goes as an ordinary
    /// email by hand, and SendEmail never names one.
    /// </summary>
    public static bool IsActionOnly(string id) => id is ApplicationRejected or QuestionnaireRequest or TalentPoolInvitation;

    /// <summary>Whether <paramref name="id"/> names one of an organization's own templates: a Guid in its 36-character form.</summary>
    public static bool IsOrganizationTemplate(string id) => Guid.TryParseExact(id, "D", out _);

    /// <summary>Whether <paramref name="id"/> could name a template: a built-in id, or an organization template's id.</summary>
    public static bool IsWellFormed(string id) => All.Contains(id) || IsOrganizationTemplate(id);
}
