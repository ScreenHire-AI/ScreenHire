using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One follow-up questionnaire asked of one application's candidate (workflow-engine.md §8): the request a
/// SendQuestionnaire action makes, the link it emails, and once the candidate answers, the answers. There is one per
/// application and questionnaire, on a unique index: sending again repeats the link while it is unanswered, and does
/// nothing once it is answered.
/// </summary>
/// <remarks>
/// The answers are kept with the questions as the candidate saw them (<see cref="Questions"/>), as a scorecard keeps its
/// criteria's labels, so a later change to the workflow cannot make them unreadable. Integrity analysis does not read
/// them; it reads the apply form only.
/// </remarks>
public sealed class QuestionnaireRequest : ITenantOwned
{
    /// <summary>How long an emailed link stays open. Sending again issues a fresh one.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The follow-up questionnaire's id in the job's workflow.</summary>
    public required string QuestionnaireId { get; init; }

    /// <summary>The questionnaire's name when it was last sent, so the record reads after the workflow changes.</summary>
    public string QuestionnaireName { get; private set; } = "";

    public DateTimeOffset FirstSentAt { get; init; }

    public DateTimeOffset LastSentAt { get; private set; }

    /// <summary>When the latest link stops working. Sending again moves it on.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public int TimesSent { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The <see cref="ApplicationEventType.QuestionnaireCompleted"/> timeline entry, which the trigger is keyed on.</summary>
    public Guid? CompletedEventId { get; private set; }

    /// <summary>The questions the candidate answered, copied from the workflow at completion.</summary>
    public IReadOnlyList<Question> Questions { get; private set; } = [];

    /// <summary>The candidate's answers, in the form automation conditions read them, empty ones left out.</summary>
    public IReadOnlyList<ApplicationAnswer> Answers { get; private set; } = [];

    public bool IsCompleted => CompletedAt is not null;

    /// <summary>A new request for <paramref name="questionnaire"/>, sent now.</summary>
    public static QuestionnaireRequest Create(JobApplication application, FollowUpQuestionnaire questionnaire, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(questionnaire);
        var request = new QuestionnaireRequest
        {
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            JobId = application.JobId,
            QuestionnaireId = questionnaire.Id,
            FirstSentAt = now,
        };
        request.Resend(questionnaire, now);
        return request;
    }

    /// <summary>Sends the link again: a fresh expiry, counted. Only an unanswered request is sent again.</summary>
    public void Resend(FollowUpQuestionnaire questionnaire, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(questionnaire);
        if (IsCompleted)
        {
            throw new InvalidOperationException("An answered questionnaire is not sent again.");
        }

        QuestionnaireName = questionnaire.Name;
        LastSentAt = now;
        ExpiresAt = now + LinkLifetime;
        TimesSent++;
    }

    /// <summary>Whether the link sent last has run out at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>The timeline entry for a send, linked to the automation run that sent it.</summary>
    public ApplicationEvent SentEvent(DateTimeOffset now, Guid? automationRunId) =>
        NewEvent(ApplicationEventType.QuestionnaireSent, now, automationRunId);

    /// <summary>
    /// Records the candidate's answers and returns the timeline entry that raises the QuestionnaireCompleted trigger. The
    /// caller has checked the answers against <paramref name="questionnaire"/> (<see cref="ApplicationForm.Validate"/>).
    /// </summary>
    public ApplicationEvent Complete(FollowUpQuestionnaire questionnaire, IReadOnlyList<ApplicationAnswer> answers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(questionnaire);
        ArgumentNullException.ThrowIfNull(answers);
        if (IsCompleted)
        {
            throw new InvalidOperationException("The questionnaire was answered already.");
        }

        var entry = NewEvent(ApplicationEventType.QuestionnaireCompleted, now, automationRunId: null);
        Questions = [.. questionnaire.Questions];
        Answers = ApplicationForm.Normalize(answers);
        CompletedAt = now;
        CompletedEventId = entry.Id;
        return entry;
    }

    private ApplicationEvent NewEvent(ApplicationEventType type, DateTimeOffset now, Guid? automationRunId) =>
        new()
        {
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            Type = type,
            Data = new ApplicationEventData(AutomationRunId: automationRunId, QuestionnaireId: QuestionnaireId, QuestionnaireRequestId: Id),
            OccurredAt = now,
        };
}
