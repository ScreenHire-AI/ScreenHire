namespace ScreenHire.Domain.Applications;

/// <summary>
/// Which timeline entries count as activity for an application's "last activity": anything the candidate did (applied,
/// answered a questionnaire) or the team did, by hand or through an automation (moved, noted, tagged, emailed, scored,
/// gave a task). A finished task counts too, from the task's own completion time, because completing a task writes no
/// timeline entry.
/// </summary>
/// <remarks>
/// <see cref="ApplicationEventType.VerificationAssessed"/> does not count: it is the engine's work, not the candidate's or
/// the team's, and an API key never learns that an assessment ran or when (ADR-0011), which a last-activity time that
/// moved with it would tell. Every event type is in exactly one of the two sets; a unit test holds that, so a new type
/// is a decision.
/// </remarks>
public static class ApplicationActivity
{
    /// <summary>The timeline entries that move an application's last activity.</summary>
    public static readonly ApplicationEventType[] CountedTypes =
    [
        ApplicationEventType.Submitted,
        ApplicationEventType.StageChanged,
        ApplicationEventType.TagAdded,
        ApplicationEventType.TagRemoved,
        ApplicationEventType.NoteAdded,
        ApplicationEventType.NoteEdited,
        ApplicationEventType.NoteDeleted,
        ApplicationEventType.ScorecardSubmitted,
        ApplicationEventType.EmailSent,
        // A follow-up questionnaire's link is an email to the candidate.
        ApplicationEventType.QuestionnaireSent,
        ApplicationEventType.QuestionnaireCompleted,
        ApplicationEventType.TaskCreated,
        // A person kept the candidate in mind, or invited them to apply for another job (docs/architecture/talent-pool.md).
        ApplicationEventType.KeptInMind,
        ApplicationEventType.InvitedToApply,
    ];

    /// <summary>
    /// The timeline entries that leave it as it is: the verification engine's, and the candidate leaving the talent pool,
    /// which the daily expiry sweep writes with nobody involved, and whose other causes (a move back, a new application)
    /// come with an entry that counts.
    /// </summary>
    public static readonly ApplicationEventType[] NotCounted = [ApplicationEventType.VerificationAssessed, ApplicationEventType.LeftTalentPool];

    /// <summary>
    /// The application's last activity: the latest counted timeline entry or finished task, and never earlier than when it
    /// was submitted.
    /// </summary>
    public static DateTimeOffset LastAt(DateTimeOffset submittedAt, DateTimeOffset? lastEventAt, DateTimeOffset? lastTaskCompletedAt)
    {
        var last = submittedAt;
        if (lastEventAt > last)
        {
            last = lastEventAt.Value;
        }

        if (lastTaskCompletedAt > last)
        {
            last = lastTaskCompletedAt.Value;
        }

        return last;
    }
}
