namespace ScreenHire.Domain.Hiring;

/// <summary>
/// The email templates a <see cref="ActionType.SendEmail"/> or <see cref="ActionType.Reject"/> action, or a person
/// sending by hand, can name. Fourteen are built in, and every organization may edit their wording; an organization's own
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
    /// Invites a saved candidate to apply for another job, with that job's link and a signed link to stop
    /// being kept. Only a person's Invite to apply sends it (docs/architecture/talent-pool.md): never an automation, and
    /// never as an ordinary email.
    /// </summary>
    public const string TalentPoolInvitation = "talent-pool-invitation";

    /// <summary>
    /// Offers the candidate an interview, with the signed link to pick a time (docs/architecture/interview-scheduling.md).
    /// Only scheduling sends it, because the link belongs to one interview request.
    /// </summary>
    public const string InterviewInvitation = "interview-invitation";

    /// <summary>Confirms a booked or moved interview to the candidate: the time, the meeting link and the link to change it. Only scheduling sends it.</summary>
    public const string InterviewConfirmation = "interview-confirmation";

    /// <summary>Tells the candidate an interview was cancelled. Only scheduling sends it.</summary>
    public const string InterviewCancelled = "interview-cancelled";

    /// <summary>
    /// Tells the candidate the team offered new times for a booked interview, with the signed link to pick one. The booked time
    /// stays until they do. Only scheduling sends it, because the link belongs to one interview request.
    /// </summary>
    public const string InterviewNewTimes = "interview-new-times";

    /// <summary>Reminds the candidate of a booked interview, a day and an hour before it: the time, the meeting link and the link to change it. Only scheduling sends it.</summary>
    public const string InterviewReminder = "interview-reminder";

    /// <summary>
    /// Asks the candidate when they are free for an interview (tell us when you're free), with the signed link to suggest
    /// times. Only scheduling sends it.
    /// </summary>
    public const string InterviewInvitationSuggest = "interview-invitation-suggest";

    /// <summary>
    /// Offers the candidate one or two times the team picked (we'll suggest a time), with the signed link to accept one or ask
    /// for another. Only scheduling sends it.
    /// </summary>
    public const string InterviewInvitationProposed = "interview-invitation-proposed";

    /// <summary>Tells the candidate the team changed how they pick a time, with the same signed link. Only scheduling sends it.</summary>
    public const string InterviewOptionsUpdated = "interview-options-updated";

    /// <summary>Asks the candidate for more times when none of theirs works, with the team's message and the signed link. Only scheduling sends it.</summary>
    public const string InterviewMoreTimes = "interview-more-times";

    /// <summary>The built-in templates, in the order the dashboard lists them.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } =
    [
        ApplicationReceived, StageChange, QuestionnaireRequest, ApplicationRejected, TalentPoolInvitation, InterviewInvitation, InterviewConfirmation,
        InterviewCancelled, InterviewNewTimes, InterviewReminder, InterviewInvitationSuggest, InterviewInvitationProposed, InterviewOptionsUpdated,
        InterviewMoreTimes,
    ];

    /// <summary>ScreenHire's names for the built-in templates, which organizations cannot rename.</summary>
    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ApplicationReceived] = "Application received",
        [StageChange] = "Moved to the next step",
        [QuestionnaireRequest] = "Follow-up questions",
        [ApplicationRejected] = "Not progressing",
        [TalentPoolInvitation] = "Invitation to apply",
        [InterviewInvitation] = "Pick an interview time",
        [InterviewConfirmation] = "Interview confirmed",
        [InterviewCancelled] = "Interview cancelled",
        [InterviewNewTimes] = "New interview times",
        [InterviewReminder] = "Interview reminder",
        [InterviewInvitationSuggest] = "Tell us when you're free",
        [InterviewInvitationProposed] = "Times we suggest",
        [InterviewOptionsUpdated] = "Interview options updated",
        [InterviewMoreTimes] = "More times, please",
    };

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) 
        {
            ApplicationReceived, StageChange, QuestionnaireRequest, ApplicationRejected, TalentPoolInvitation, InterviewInvitation,
            InterviewConfirmation, InterviewCancelled, InterviewNewTimes, InterviewReminder, InterviewInvitationSuggest, InterviewInvitationProposed,
            InterviewOptionsUpdated, InterviewMoreTimes,
        };

    /// <summary>
    /// Whether only its own action sends <paramref name="id"/>: the rejection email (Reject), the follow-up questions
    /// email (SendQuestionnaire) and the invitation to apply (a person's Invite to apply). None goes as an ordinary
    /// email by hand, and SendEmail never names one.
    /// </summary>
    public static bool IsActionOnly(string id) => id is ApplicationRejected or QuestionnaireRequest or TalentPoolInvitation || IsInterview(id);

    /// <summary>Whether <paramref name="id"/> is one of the nine interview emails, which only interview scheduling sends.</summary>
    public static bool IsInterview(string id) =>
        id is InterviewInvitation or InterviewConfirmation or InterviewCancelled or InterviewNewTimes or InterviewReminder
            or InterviewInvitationSuggest or InterviewInvitationProposed or InterviewOptionsUpdated or InterviewMoreTimes;

    /// <summary>Whether <paramref name="id"/> is sent only by an invitation flow with links of its own: the talent pool's or scheduling's. The composer never offers it.</summary>
    public static bool IsInvitationOnly(string id) => id == TalentPoolInvitation || IsInterview(id);

    /// <summary>Whether <paramref name="id"/> names one of an organization's own templates: a Guid in its 36-character form.</summary>
    public static bool IsOrganizationTemplate(string id) => Guid.TryParseExact(id, "D", out _);

    /// <summary>Whether <paramref name="id"/> could name a template: a built-in id, or an organization template's id.</summary>
    public static bool IsWellFormed(string id) => All.Contains(id) || IsOrganizationTemplate(id);
}
