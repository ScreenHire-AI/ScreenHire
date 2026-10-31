using ScreenHire.Domain.Jobs;

namespace ScreenHire.Domain.Hiring;

// The hiring template definition (docs/architecture/workflow-engine.md §1, §2). These records are the contract: they
// are stored as JSONB, sent over the API as they are, and the API publishes a JSON Schema generated from them, so the
// dashboard, API clients and the workflow engine share one shape. Rules for changing them:
// - No polymorphism and no untyped members (object, JsonElement): each element is one flat record with a kind enum and
//   optional per-kind fields, which DefinitionValidator checks. Orval and the JSON Schema stay simple that way.
// - Add members as optional (a default value), never rename or remove one: published versions are immutable JSONB rows
//   that must keep deserializing. The one exception so far is the v0.2 scorecard's criteria, which migration
//   AddScorecardTemplates rewrote in every stored definition and workflow, in the same transaction, before removing them.
// - Element ids are stable keys (stages, questions, automations). Diffs match by id, and facts such as
//   `answers.work_authorization` refer to them.

/// <summary>
/// What a hiring template versions and a job copies: the stage pipeline, the screening questionnaire, the integrity
/// policy, the automations and the follow-up questionnaires a <see cref="ActionType.SendQuestionnaire"/> action sends.
/// </summary>
/// <remarks>
/// <c>followUpQuestionnaires</c>: Optional; questionnaires an automation emails to the candidate after they applied.
/// Question ids are unique across the apply questionnaire and every follow-up, so <c>answers.{id}</c> names one question.
/// </remarks>
public sealed record HiringWorkflow(
    IReadOnlyList<WorkflowStage> Stages,
    Questionnaire Questionnaire,
    IntegrityPolicy IntegrityPolicy,
    IReadOnlyList<Automation> Automations,
    IReadOnlyList<FollowUpQuestionnaire>? FollowUpQuestionnaires = null)
{
    /// <summary>
    /// The workflow a job gets when it is created without a template. A job holds its own copy of each scorecard, so its
    /// stages carry the built-in scorecards' questions as well as their ids.
    /// </summary>
    public static HiringWorkflow Default { get; } = new(
        [
            new("applied", "Applied", StageKind.Applied),
            new("screening", "Screening", StageKind.Screening, SlaDays: 3,
                Scorecard: BuiltInScorecards.Screening.ToScorecard(), ScorecardTemplateId: BuiltInScorecards.Screening.Id),
            new("interview", "Interview", StageKind.Interview,
                Scorecard: BuiltInScorecards.Interview.ToScorecard(), ScorecardTemplateId: BuiltInScorecards.Interview.Id),
            new("offer", "Offer", StageKind.Offer),
            new("hired", "Hired", StageKind.Hired),
            new("rejected", "Rejected", StageKind.Rejected),
            new("withdrawn", "Withdrawn", StageKind.Withdrawn),
        ],
        new Questionnaire([]),
        new IntegrityPolicy(AiUsePolicy.AllowedWithDisclosure),
        []);

    /// <summary>The stage with <paramref name="stageId"/>, or null.</summary>
    public WorkflowStage? FindStage(string stageId) => Stages.FirstOrDefault(s => s.Id == stageId);

    /// <summary>The follow-up questionnaire with <paramref name="questionnaireId"/>, or null.</summary>
    public FollowUpQuestionnaire? FindFollowUp(string? questionnaireId) =>
        FollowUpQuestionnaires?.FirstOrDefault(q => q?.Id == questionnaireId);

    /// <summary>
    /// This workflow as a hiring template holds it: each stage names its library scorecard and holds no copy of the
    /// questions (workflow-engine.md section 8).
    /// </summary>
    public HiringWorkflow WithScorecardReferences() =>
        this with { Stages = [.. Stages.Select(s => s.Scorecard is null ? s : s with { Scorecard = null })] };

    /// <summary>
    /// This workflow as a job holds it: every stage that names a library scorecard and holds no copy gets one, from the
    /// built-ins or from <paramref name="library"/>, the organization's own keyed by id. A job keeps that copy, so editing
    /// the library never changes the questions a job's candidates answer. <paramref name="missing"/> lists the indexes of
    /// the stages whose scorecard is in neither.
    /// </summary>
    public HiringWorkflow WithScorecardCopies(IReadOnlyDictionary<string, Scorecard> library, out IReadOnlyList<int> missing)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (!Stages.Any(s => s is { Scorecard: null, ScorecardTemplateId: not null }))
        {
            missing = [];
            return this;
        }

        var notFound = new List<int>();
        var stages = new List<WorkflowStage>(Stages.Count);
        for (var i = 0; i < Stages.Count; i++)
        {
            var stage = Stages[i];
            if (stage is { Scorecard: null, ScorecardTemplateId: { } key })
            {
                if ((BuiltInScorecards.Find(key)?.ToScorecard() ?? library.GetValueOrDefault(key)) is { } copy)
                {
                    stage = stage with { Scorecard = copy };
                }
                else
                {
                    notFound.Add(i);
                }
            }

            stages.Add(stage);
        }

        missing = notFound;
        return this with { Stages = stages };
    }

    /// <summary>The library scorecards the stages name, each once.</summary>
    public IReadOnlyList<string> ScorecardTemplateIds() =>
        [.. (Stages ?? []).Select(s => s?.ScorecardTemplateId).OfType<string>().Distinct(StringComparer.Ordinal)];
}

/// <summary>
/// A questionnaire the hiring team sends after the candidate applied, with a <see cref="ActionType.SendQuestionnaire"/>
/// action: the candidate answers it on the careers site through a signed link. Completing it fires
/// <see cref="TriggerType.QuestionnaireCompleted"/> automations that name it. File questions are not offered: the page
/// works without JavaScript and takes no uploads.
/// </summary>
/// <remarks>
/// <c>id</c>: Stable key, unique among the workflow's follow-up questionnaires; the trigger and the action name it.
/// <c>name</c>: What the hiring team calls it, such as "Availability"; the candidate sees it as the page's heading.
/// <c>sections</c>: Optional; named pages of its questions, as on <see cref="Questionnaire"/>.
/// </remarks>
public sealed record FollowUpQuestionnaire(
    string Id, string Name, IReadOnlyList<Question> Questions, IReadOnlyList<QuestionSection>? Sections = null);

/// <summary>A hiring template version's full definition: the workflow a job copies, plus defaults for the job's posting.</summary>
public sealed record HiringTemplateDefinition(HiringWorkflow Workflow, PostingDefaults PostingDefaults)
{
    public static HiringTemplateDefinition Default { get; } = new(HiringWorkflow.Default.WithScorecardReferences(), PostingDefaults.None);
}

/// <summary>One step of the pipeline. <c>hired</c>, <c>rejected</c> and <c>withdrawn</c> are terminal.</summary>
/// <remarks>
/// <c>id</c>: Stable key, unique within the workflow: lowercase letters, digits, <c>-</c> and <c>_</c>.
/// <c>slaDays</c>: Optional time limit: candidates should not wait longer than this in the stage. It measures the hiring
/// team's delay, never the candidate: past it the application shows as overdue and <see cref="TriggerType.SlaBreached"/>
/// fires. A hired, rejected or withdrawn stage ends the application, so a limit there is ignored.
/// <c>scorecard</c>: A job's own copy of the stage's scorecard, made when the job took the workflow, so every candidate
/// for the job answers the same questions. Only in a job's workflow; a hiring template's stage names its scorecard in
/// <c>scorecardTemplateId</c> instead.
/// <c>scorecardTemplateId</c>: Optional: the library scorecard reviewers fill in this stage, a built-in's id or one of the
/// organization's own. Not on a terminal stage.
/// </remarks>
public sealed record WorkflowStage(
    string Id, string Name, StageKind Kind, int? SlaDays = null, Scorecard? Scorecard = null, string? ScorecardTemplateId = null)
{
    // Methods, not properties: a property would join the serialized definition and its JSON Schema.

    /// <summary>Whether reviewers fill a scorecard in this stage: it names a library scorecard or holds a copy.</summary>
    public bool HasScorecard() => Scorecard is not null || ScorecardTemplateId is not null;

    /// <summary>Whether the stage's time limit counts: it has one, and the stage does not end the application.</summary>
    public bool HasSla() => SlaDays is not null && Enum.IsDefined(Kind) && !Kind.IsTerminal();

    /// <summary>
    /// When an application that entered this stage at <paramref name="enteredAt"/> passes its time limit, or null when
    /// the limit does not count (<see cref="HasSla"/>). The overdue status and the SlaBreached timer both use it.
    /// </summary>
    public DateTimeOffset? SlaDueAt(DateTimeOffset enteredAt) => HasSla() ? enteredAt.AddDays(SlaDays!.Value) : null;
}

/// <summary>
/// A job's copy of a library scorecard: the questions a reviewer answers for a stage, then an overall
/// <see cref="Recommendation"/> and a note, which every scorecard has. Submitting one raises the
/// <see cref="TriggerType.ReviewerDecision"/> trigger with the recommendation as the <c>reviewer.recommendation</c> fact.
/// Rating questions average into the candidate's rating (ADR-0013, <c>RatingRule</c>); nothing else on a scorecard
/// does, and no automation reads the rating. Questions are about the job (skills, experience, the work itself), never
/// personality, emotion or honesty (CLAUDE.md non-negotiable 3); <see cref="DefinitionValidator"/> refuses questions that
/// name those.
/// </summary>
/// <remarks>
/// <c>name</c>: The library scorecard's name when the job copied it, such as "Technical interview".
/// </remarks>
public sealed record Scorecard(string Name, IReadOnlyList<ScorecardQuestion> Questions);

/// <summary>One scorecard question. <see cref="Options"/> apply to the choice types only.</summary>
/// <remarks>
/// <c>id</c>: Stable key, unique within the scorecard; a submitted answer refers to it.
/// <c>required</c>: A reviewer answers it to submit. A rating question left unanswered adds nothing to the rating.
/// <c>guidance</c>: Optional: what a reviewer looks for, shown under the label.
/// </remarks>
public sealed record ScorecardQuestion(
    string Id,
    ScorecardQuestionType Type,
    string Label,
    bool Required = false,
    string? Guidance = null,
    IReadOnlyList<QuestionOption>? Options = null);

/// <summary>
/// A scorecard question's answer kind. The General kinds share <see cref="QuestionType"/>'s names. The Rating kinds are
/// the ones the candidate's rating averages: <see cref="Rating5"/> (1 to 5), <see cref="Rating10"/> (1 to 10) and
/// <see cref="RatingYesNo"/> (yes or no).
/// </summary>
public enum ScorecardQuestionType
{
    YesNo,
    SingleChoice,
    MultiChoice,
    ShortText,
    LongText,
    Rating5,
    Rating10,
    RatingYesNo,
}

public static class ScorecardQuestionTypes
{
    /// <summary>Whether answers of this kind count toward the candidate's rating (<c>RatingRule</c>).</summary>
    public static bool IsRating(this ScorecardQuestionType type) =>
        type is ScorecardQuestionType.Rating5 or ScorecardQuestionType.Rating10 or ScorecardQuestionType.RatingYesNo;
}

/// <summary>
/// A v0.2 reviewer's rating of one criterion, on the labelled scale scorecards had before answer kinds. Kept so the
/// scorecards submitted then read as they were. They never count toward the candidate's rating: these labels were never
/// numbers.
/// </summary>
public enum CriterionRating
{
    NotDemonstrated,
    PartlyDemonstrated,
    Demonstrated,
    StronglyDemonstrated,
}

/// <summary>A reviewer's overall recommendation for the stage. Automations test it as <c>reviewer.recommendation</c>.</summary>
public enum Recommendation
{
    StrongNo,
    No,
    Yes,
    StrongYes,
}

public static class StageKinds
{
    /// <summary>Hired, rejected and withdrawn end an application.</summary>
    public static bool IsTerminal(this StageKind kind) => kind is StageKind.Hired or StageKind.Rejected or StageKind.Withdrawn;
}

public enum StageKind
{
    Applied,
    Screening,
    Assessment,
    Verification,
    Interview,
    Offer,
    Hired,
    Rejected,
    Withdrawn,
    Custom,
}

/// <summary>The screening questions a candidate answers when applying.</summary>
/// <remarks>
/// <c>sections</c>: Optional; the questions grouped into named pages, one page each on the careers site. Every question
/// is in exactly one section, and the sections list them in the order of <c>questions</c>. Without sections the careers
/// site splits the questions into pages of the organization's page size (<c>questionsPerPage</c>).
/// </remarks>
public sealed record Questionnaire(IReadOnlyList<Question> Questions, IReadOnlyList<QuestionSection>? Sections = null);

/// <summary>One named page of a questionnaire: its name, the page's heading, and the ids of its questions in order.</summary>
/// <remarks>
/// <c>id</c>: Stable key, unique within the questionnaire.
/// </remarks>
public sealed record QuestionSection(string Id, string Name, IReadOnlyList<string> QuestionIds);

/// <summary>
/// One screening question. Which optional members apply depends on <see cref="Type"/>: <see cref="Options"/> for the
/// choice types, <see cref="Knockout"/> for <see cref="QuestionType.YesNo"/>, <see cref="Min"/> and <see cref="Max"/>
/// for <see cref="QuestionType.Number"/>, <see cref="MaxLength"/> for the text types and
/// <see cref="AcceptedFileTypes"/> for <see cref="QuestionType.File"/>.
/// </summary>
/// <remarks>
/// <c>id</c>: Stable key, unique across the workflow's questionnaires; conditions read the answer as <c>answers.{Id}</c>.
/// </remarks>
public sealed record Question(
    string Id,
    QuestionType Type,
    string Label,
    bool Required = false,
    string? HelpText = null,
    IReadOnlyList<QuestionOption>? Options = null,
    Knockout? Knockout = null,
    decimal? Min = null,
    decimal? Max = null,
    int? MaxLength = null,
    IReadOnlyList<string>? AcceptedFileTypes = null);

public enum QuestionType
{
    ShortText,
    LongText,
    SingleChoice,
    MultiChoice,
    YesNo,
    Number,
    Url,
    File,
}

/// <remarks>
/// <c>id</c>: Stable key, unique within the question; answers and conditions use it, not the label.
/// </remarks>
public sealed record QuestionOption(string Id, string Label);

/// <summary>
/// Marks a yes/no question as an objective eligibility check (work authorisation, a required licence). The answer
/// <see cref="DisqualifyingAnswer"/> makes the candidate ineligible. It never rejects anyone by itself: an automation
/// decides, and by default it only queues the application for a person to review (workflow-engine.md §2).
/// </summary>
/// <remarks>
/// <c>reason</c>: Shown to the candidate if a rejection follows from this answer.
/// </remarks>
public sealed record Knockout(bool DisqualifyingAnswer, string? Reason = null);

/// <summary>The template's integrity settings (ADR-0004). Only the AI-use policy exists so far.</summary>
public sealed record IntegrityPolicy(AiUsePolicy AiUse);

/// <summary>What the employer allows candidates to do with AI tools while applying.</summary>
public enum AiUsePolicy
{
    Allowed,
    AllowedWithDisclosure,
    NotAllowed,
}

/// <summary>Values a new job starts with when it is created from the template. Every member is optional.</summary>
/// <remarks>
/// <c>validForDays</c>: Days from publishing until the posting expires (<c>validThrough</c>).
/// <c>description</c>: Rich-text HTML; the API sanitizes it on the way in.
/// </remarks>
public sealed record PostingDefaults(
    EmploymentType? EmploymentType = null,
    WorkplaceType? WorkplaceType = null,
    string? Department = null,
    string? SalaryCurrency = null,
    SalaryPeriod? SalaryPeriod = null,
    int? ValidForDays = null,
    string? Description = null)
{
    public static PostingDefaults None { get; } = new();
}

/// <summary>
/// Trigger, conditions, actions (workflow-engine.md §2). Stored and validated structurally here; the workflow engine
/// runs them. <see cref="DefinitionValidator"/> refuses an automation that would reject anyone because of an integrity
/// signal (ADR-0004).
/// </summary>
/// <remarks>
/// <c>allowAutoReject</c> is the explicit per-rule opt-in a knockout automation needs before it may reject without a
/// person (workflow-engine.md §2, guardrail 2). Without it, the automation can only queue for rejection review.
/// </remarks>
public sealed record Automation(
    string Id,
    string Name,
    AutomationTrigger Trigger,
    AutomationConditions? Conditions,
    IReadOnlyList<AutomationAction> Actions,
    bool Enabled = true,
    bool AllowAutoReject = false);

/// <remarks>
/// <c>stageId</c>: For <see cref="TriggerType.StageEntered"/>, <see cref="TriggerType.StageExited"/> and <see cref="TriggerType.TimeElapsed"/>;
/// optional for <see cref="TriggerType.ReviewerDecision"/>, where it limits the trigger to that stage's scorecards, and for
/// <see cref="TriggerType.SlaBreached"/>, where it limits the trigger to that stage's time limit.
/// <c>afterHours</c>: For <see cref="TriggerType.TimeElapsed"/>: hours since the application entered <see cref="StageId"/>.
/// <c>questionnaireId</c>: For <see cref="TriggerType.QuestionnaireCompleted"/>: the follow-up questionnaire whose
/// answers fire it. Without one it fires on any follow-up questionnaire, which is how definitions saved before follow-ups
/// existed still read. The apply questionnaire never fires it; <see cref="TriggerType.ApplicationSubmitted"/> does.
/// </remarks>
public sealed record AutomationTrigger(TriggerType Type, string? StageId = null, int? AfterHours = null, string? QuestionnaireId = null);

public enum TriggerType
{
    ApplicationSubmitted,
    StageEntered,
    StageExited,
    QuestionnaireCompleted,
    IntegrityAssessed,
    VerificationCompleted,
    ReviewerDecision,
    TimeElapsed,
    CandidateReplied,
    InterviewScheduled,
    Manual,

    /// <summary>
    /// An application has been in a stage longer than the stage's <see cref="WorkflowStage.SlaDays"/>: once per stage
    /// entry. It measures the team's delay, so its actions may only remind the team (notify, assign a reviewer, create a
    /// task, add a tag), and it runs for every application, an accommodation included (workflow-engine.md §8).
    /// </summary>
    SlaBreached,
}

/// <summary>
/// Conditions combined with <see cref="Match"/>; a rule with <see cref="AutomationCondition.Negate"/> is a "not". One
/// level on purpose: a recursive tree would make the JSON Schema and the generated client recursive too.
/// </summary>
public sealed record AutomationConditions(ConditionMatch Match, IReadOnlyList<AutomationCondition> Rules);

public enum ConditionMatch
{
    All,
    Any,
}

/// <summary>
/// One test of an application fact, such as <c>answers.work_authorization EqualTo no</c> (yes/no answers are
/// <c>yes</c> and <c>no</c>, choice answers are option ids). Facts are
/// <c>answers.{questionId}</c>, <c>integrity.authenticity</c>, <c>integrity.aiAssistance</c>,
/// <c>verification.result</c>, <c>application.tags</c>, <c>application.source</c>,
/// <c>application.hoursInStage</c> and, for a <see cref="TriggerType.ReviewerDecision"/> automation only,
/// <c>reviewer.recommendation</c>.
/// </summary>
/// <remarks>
/// <c>value</c>: The operand for every operator except <see cref="ConditionOperator.In"/> and <see cref="ConditionOperator.Exists"/>.
/// <c>values</c>: The operands for <see cref="ConditionOperator.In"/>.
/// </remarks>
public sealed record AutomationCondition(
    string Fact,
    ConditionOperator Operator,
    string? Value = null,
    IReadOnlyList<string>? Values = null,
    bool Negate = false);

public enum ConditionOperator
{
    EqualTo,
    In,
    Contains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Exists,
}

/// <summary>
/// One action. Which optional members apply depends on <see cref="Type"/>; <see cref="DefinitionValidator"/> checks
/// that each type has what it needs and nothing else.
/// </summary>
/// <remarks>
/// <c>stageId</c>: For <see cref="ActionType.MoveToStage"/>.
/// <c>emailTemplate</c>: For <see cref="ActionType.SendEmail"/> and, optionally, <see cref="ActionType.Reject"/>.
/// <c>tag</c>: For <see cref="ActionType.AddTag"/>.
/// <c>reason</c>: For <see cref="ActionType.QueueForRejectionReview"/> and <see cref="ActionType.Reject"/>; the candidate sees a rejection's reason.
/// <c>url</c>: For <see cref="ActionType.ScheduleInterview"/> and <see cref="ActionType.CallWebhook"/> (https only).
/// <c>note</c>: For <see cref="ActionType.NotifyTeam"/> and <see cref="ActionType.CreateTask"/>.
/// <c>assigneeUserId</c>: Optional for <see cref="ActionType.AssignReviewer"/>; empty means the job's default reviewer.
/// <c>verificationProvider</c>: Optional for <see cref="ActionType.RequestVerification"/>.
/// <c>questionnaireId</c>: For <see cref="ActionType.SendQuestionnaire"/>: the follow-up questionnaire to send.
/// </remarks>
public sealed record AutomationAction(
    ActionType Type,
    string? StageId = null,
    string? EmailTemplate = null,
    string? Tag = null,
    string? Reason = null,
    string? Url = null,
    string? Note = null,
    Guid? AssigneeUserId = null,
    string? VerificationProvider = null,
    string? QuestionnaireId = null);

public enum ActionType
{
    SendEmail,
    SendQuestionnaire,
    RequestVerification,
    MoveToStage,
    AddTag,
    AssignReviewer,
    NotifyTeam,
    ScheduleInterview,
    CreateTask,
    CallWebhook,
    QueueForRejectionReview,
    Reject,
}
