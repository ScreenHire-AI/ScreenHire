using System.Globalization;
using System.Text.RegularExpressions;
using ScreenHire.Domain.Automations;

namespace ScreenHire.Domain.Hiring;

/// <summary>A problem with a definition, at a JSON path such as <c>workflow.stages[1].id</c>.</summary>
public sealed record DefinitionError(string Path, string Message);

/// <summary>
/// Structural validation of hiring template definitions and job workflows, plus the integrity guardrails that no saved
/// definition may break (ADR-0004, workflow-engine.md §2). The API runs it before saving; the workflow engine can run
/// it again before executing. Request bodies arrive through a serializer that does not enforce nullability, so every
/// member is checked for null here.
/// </summary>
public static partial class DefinitionValidator
{
    /// <summary>Facts a condition may test besides <c>answers.{questionId}</c>; their types are in <see cref="FactSchema"/>.</summary>
    public static IReadOnlySet<string> KnownFacts { get; } =
        new HashSet<string>(FactSchema.ApplicationFacts.Select(f => f.Name), StringComparer.Ordinal);

    // Timing facts stay (application.hoursInStage, TimeElapsed); the workflow engine skips them for an application with
    // an accommodation (workflow-engine.md §2, guardrail 4). FactSchema says why accommodation is not a fact.
    private const string AnswersPrefix = FactSchema.AnswersPrefix;

    // Kinds the definition model names but the engine does not run yet. Refused at save, so no definition carries one
    // the engine would silently ignore. Verification is phase 2 (CLAUDE.md non-negotiable 3); the others need features
    // that do not exist yet (scheduling, candidate replies).
    private static readonly Dictionary<ActionType, string> UnavailableActions = new()
    {
        [ActionType.RequestVerification] = "Verification requests arrive in phase 2 and cannot be used yet.",
        [ActionType.ScheduleInterview] = "Interview scheduling cannot be used yet.",
    };

    private static readonly Dictionary<TriggerType, string> UnavailableTriggers = new()
    {
        [TriggerType.VerificationCompleted] = "Verification arrives in phase 2; this trigger cannot be used yet.",
        [TriggerType.CandidateReplied] = "Candidate replies are not tracked yet; this trigger cannot be used yet.",
        [TriggerType.InterviewScheduled] = "Interview scheduling is not available yet; this trigger cannot be used yet.",
    };

    // What an automation that uses an integrity or verification signal may do: route and step up, never reject
    // (workflow-engine.md §2, guardrail 1). MoveToStage is further limited to SignalSafeStages. SendQuestionnaire is
    // not here: asking a candidate for more because a signal fired would act on the signal as a finding about them.
    private static readonly HashSet<ActionType> SignalSafeActions =
        [ActionType.RequestVerification, ActionType.AssignReviewer, ActionType.AddTag, ActionType.MoveToStage];

    // Review stages a signal may route an application into. Not Custom: a custom stage named "Not progressing" would be
    // a rejection under another name. Not Offer or the terminal kinds either.
    private static readonly HashSet<StageKind> SignalSafeStages =
        [StageKind.Screening, StageKind.Assessment, StageKind.Verification, StageKind.Interview];

    // What an SlaBreached automation may do. A breach measures the hiring team's delay, not the candidate, so it may only
    // remind the team; nothing it does may count against the candidate (workflow-engine.md section 8).
    private static readonly HashSet<ActionType> SlaBreachActions =
        [ActionType.NotifyTeam, ActionType.AssignReviewer, ActionType.CreateTask, ActionType.AddTag];

    public const int MaxStages = 30;
    public const int MaxQuestions = 50;
    public const int MaxOptions = 50;
    public const int MaxAutomations = 50;
    public const int MaxRules = 20;
    public const int MaxActions = 10;
    public const int MaxScorecardQuestions = 20;
    public const int MaxFollowUpQuestionnaires = 10;

    // Question ids the apply form gives meaning to (ApplicationForm): its resume question is there even when the apply
    // questionnaire has none, so a follow-up question with one of these ids would make answers.{id} name two questions.
    private static readonly HashSet<string> ApplyFormQuestionIds =
        [Applications.ApplicationForm.ResumeQuestionId, Applications.ApplicationForm.LinkedInExportQuestionId];

    public static IReadOnlyList<DefinitionError> Validate(HiringTemplateDefinition? definition)
    {
        var errors = new List<DefinitionError>();
        if (definition is null)
        {
            errors.Add(new("", "A definition is required."));
            return errors;
        }

        ValidateWorkflow(definition.Workflow, "workflow", template: true, errors);
        ValidatePostingDefaults(definition.PostingDefaults, "postingDefaults", errors);
        return errors;
    }

    public static IReadOnlyList<DefinitionError> Validate(HiringWorkflow? workflow)
    {
        var errors = new List<DefinitionError>();
        ValidateWorkflow(workflow, "", template: false, errors);
        return errors;
    }

    // A hiring template's stages name library scorecards; a job's stages hold their own copies (workflow-engine.md §8).
    private static void ValidateWorkflow(HiringWorkflow? workflow, string path, bool template, List<DefinitionError> errors)
    {
        if (workflow is null)
        {
            errors.Add(new(path, "A workflow is required."));
            return;
        }

        var stages = ValidateStages(workflow.Stages, Join(path, "stages"), template, errors);
        var questions = ValidateQuestionnaire(workflow.Questionnaire, Join(path, "questionnaire"), errors);
        var followUps = ValidateFollowUps(workflow.FollowUpQuestionnaires, Join(path, "followUpQuestionnaires"), questions, errors);

        if (workflow.IntegrityPolicy is null)
        {
            errors.Add(new(Join(path, "integrityPolicy"), "An integrity policy is required."));
        }
        else if (!Enum.IsDefined(workflow.IntegrityPolicy.AiUse))
        {
            errors.Add(new(Join(path, "integrityPolicy.aiUse"), "Unknown AI-use policy."));
        }

        ValidateAutomations(workflow.Automations, Join(path, "automations"), new(stages, questions, followUps), errors);
    }

    private static Dictionary<string, WorkflowStage> ValidateStages(
        IReadOnlyList<WorkflowStage>? stages, string path, bool template, List<DefinitionError> errors)
    {
        var byId = new Dictionary<string, WorkflowStage>(StringComparer.Ordinal);
        if (stages is null || stages.Count == 0)
        {
            errors.Add(new(path, "A workflow needs at least one stage."));
            return byId;
        }

        if (stages.Count > MaxStages)
        {
            errors.Add(new(path, $"A workflow has at most {MaxStages} stages."));
        }

        for (var i = 0; i < stages.Count; i++)
        {
            var stagePath = $"{path}[{i}]";
            var stage = stages[i];
            if (stage is null)
            {
                errors.Add(new(stagePath, "A stage is required."));
                continue;
            }

            if (ValidateId(stage.Id, Join(stagePath, "id"), errors) && !byId.TryAdd(stage.Id, stage))
            {
                errors.Add(new(Join(stagePath, "id"), $"Stage id '{stage.Id}' is used twice."));
            }

            ValidateText(stage.Name, Join(stagePath, "name"), 80, required: true, errors);
            if (!Enum.IsDefined(stage.Kind))
            {
                errors.Add(new(Join(stagePath, "kind"), "Unknown stage kind."));
            }

            if (stage.SlaDays is < 1 or > 365)
            {
                errors.Add(new(Join(stagePath, "slaDays"), "An SLA is between 1 and 365 days."));
            }

            ValidateStageScorecard(stage, stagePath, template, errors);
        }

        // Applications start in the first stage, so it has to be the one Applied stage.
        if (stages[0] is { } first && first.Kind != StageKind.Applied)
        {
            errors.Add(new($"{path}[0].kind", "The first stage must be the Applied stage, where new applications start."));
        }

        if (stages.Count(s => s?.Kind == StageKind.Applied) > 1)
        {
            errors.Add(new(path, "A workflow has exactly one Applied stage."));
        }

        return byId;
    }

    private static void ValidateStageScorecard(WorkflowStage stage, string path, bool template, List<DefinitionError> errors)
    {
        if (!stage.HasScorecard())
        {
            return;
        }

        if (Enum.IsDefined(stage.Kind) && stage.Kind.IsTerminal())
        {
            errors.Add(new(Join(path, stage.ScorecardTemplateId is null ? "scorecard" : "scorecardTemplateId"),
                "A hired, rejected or withdrawn stage ends the application, so it has no scorecard."));
        }

        if (stage.ScorecardTemplateId is { } key && !BuiltInScorecards.IsWellFormed(key))
        {
            errors.Add(new(Join(path, "scorecardTemplateId"),
                $"Unknown scorecard. Use one of {string.Join(", ", BuiltInScorecards.All.Select(b => b.Id))}, or the id of one of the organization's scorecards."));
        }

        if (template)
        {
            if (stage.Scorecard is not null)
            {
                errors.Add(new(Join(path, "scorecard"),
                    "A hiring template's stage names a scorecard from the library in scorecardTemplateId; each job copies its questions."));
            }

            return;
        }

        // A job's stage holds its own copy. One that only names a library scorecard is copied by the API when the job is
        // saved, and the builder validates both kinds of workflow through this overload.
        if (stage.Scorecard is not { } scorecard)
        {
            return;
        }

        ValidateText(scorecard.Name, Join(path, "scorecard.name"), ScorecardTemplate.MaxNameLength, required: true, errors);
        errors.AddRange(ValidateScorecardQuestions(scorecard.Questions, Join(path, "scorecard.questions")));
    }

    /// <summary>
    /// The problems with a scorecard's questions, at paths under <paramref name="path"/>: 1 to
    /// <see cref="MaxScorecardQuestions"/> questions with unique ids, options on the choice kinds only, and nothing that
    /// rates the person rather than the job. The library's scorecards and a job's copy share these rules.
    /// </summary>
    public static IReadOnlyList<DefinitionError> ValidateScorecardQuestions(IReadOnlyList<ScorecardQuestion>? questions, string path)
    {
        var errors = new List<DefinitionError>();
        if (questions is not { Count: > 0 and <= MaxScorecardQuestions })
        {
            errors.Add(new(path, $"A scorecard asks between 1 and {MaxScorecardQuestions} questions; leave the scorecard out for none."));
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < questions.Count; i++)
        {
            var questionPath = $"{path}[{i}]";
            if (questions[i] is not { } question)
            {
                errors.Add(new(questionPath, "A question is required."));
                continue;
            }

            if (ValidateId(question.Id, Join(questionPath, "id"), errors) && !ids.Add(question.Id))
            {
                errors.Add(new(Join(questionPath, "id"), $"Question id '{question.Id}' is used twice."));
            }

            ValidateText(question.Label, Join(questionPath, "label"), 120, required: true, errors);
            ValidateText(question.Guidance, Join(questionPath, "guidance"), 500, required: false, errors);
            if (!Enum.IsDefined(question.Type))
            {
                errors.Add(new(Join(questionPath, "type"), "Unknown answer kind."));
            }
            else if (question.Type is ScorecardQuestionType.SingleChoice or ScorecardQuestionType.MultiChoice)
            {
                ValidateOptions(question.Options, Join(questionPath, "options"), errors);
            }
            else if (question.Options is { Count: > 0 })
            {
                errors.Add(new(Join(questionPath, "options"), "Only choice questions have options."));
            }

            var texts = new List<(string? Text, string Member)> { (question.Label, "label"), (question.Guidance, "guidance") };
            texts.AddRange((question.Options ?? []).Select((o, n) => (o?.Label, $"options[{n}].label")));
            foreach (var (text, member) in texts)
            {
                if (text is not null && PersonalJudgement().Match(text) is { Success: true } match)
                {
                    errors.Add(new(Join(questionPath, member),
                        $"Ask about what the job needs, such as a skill or experience, not \"{match.Value}\". Scorecards never rate " +
                        "personality, emotion or honesty (CLAUDE.md non-negotiable 3)."));
                }
            }
        }

        return errors;
    }

    private static Dictionary<string, Question> ValidateQuestionnaire(Questionnaire? questionnaire, string path, List<DefinitionError> errors)
    {
        var byId = new Dictionary<string, Question>(StringComparer.Ordinal);
        if (questionnaire?.Questions is not { } questions)
        {
            errors.Add(new(path, "A questionnaire is required; use an empty question list for none."));
            return byId;
        }

        ValidateQuestions(questions, path, followUp: false, byId, errors);
        return byId;
    }

    // Each follow-up's questions join the apply questionnaire's in one dictionary: ids are unique across all of them, and
    // conditions and the knockout rules read every question the same way. Returns the follow-ups by id.
    private static Dictionary<string, FollowUpQuestionnaire> ValidateFollowUps(
        IReadOnlyList<FollowUpQuestionnaire>? followUps, string path, Dictionary<string, Question> questions, List<DefinitionError> errors)
    {
        var byId = new Dictionary<string, FollowUpQuestionnaire>(StringComparer.Ordinal);
        if (followUps is null)
        {
            return byId;
        }

        if (followUps.Count > MaxFollowUpQuestionnaires)
        {
            errors.Add(new(path, $"A workflow has at most {MaxFollowUpQuestionnaires} follow-up questionnaires."));
        }

        for (var i = 0; i < followUps.Count; i++)
        {
            var followUpPath = $"{path}[{i}]";
            if (followUps[i] is not { } followUp)
            {
                errors.Add(new(followUpPath, "A questionnaire is required."));
                continue;
            }

            if (ValidateId(followUp.Id, Join(followUpPath, "id"), errors) && !byId.TryAdd(followUp.Id, followUp))
            {
                errors.Add(new(Join(followUpPath, "id"), $"Questionnaire id '{followUp.Id}' is used twice."));
            }

            ValidateText(followUp.Name, Join(followUpPath, "name"), 80, required: true, errors);
            if (followUp.Questions is not { Count: > 0 } followUpQuestions)
            {
                errors.Add(new(Join(followUpPath, "questions"), "A follow-up questionnaire asks at least one question."));
                continue;
            }

            ValidateQuestions(followUpQuestions, followUpPath, followUp: true, questions, errors);
        }

        return byId;
    }

    private static void ValidateQuestions(
        IReadOnlyList<Question> questions, string path, bool followUp, Dictionary<string, Question> byId, List<DefinitionError> errors)
    {
        if (questions.Count > MaxQuestions)
        {
            errors.Add(new(Join(path, "questions"), $"A questionnaire has at most {MaxQuestions} questions."));
        }

        for (var i = 0; i < questions.Count; i++)
        {
            var questionPath = $"{path}.questions[{i}]";
            var question = questions[i];
            if (question is null)
            {
                errors.Add(new(questionPath, "A question is required."));
                continue;
            }

            if (ValidateId(question.Id, Join(questionPath, "id"), errors))
            {
                if (followUp && ApplyFormQuestionIds.Contains(question.Id))
                {
                    errors.Add(new(Join(questionPath, "id"), $"'{question.Id}' is a question on the apply form. Choose another id."));
                }
                else if (!byId.TryAdd(question.Id, question))
                {
                    errors.Add(new(Join(questionPath, "id"), followUp
                        ? $"Question id '{question.Id}' is used twice. Ids are unique across all of a workflow's questionnaires."
                        : $"Question id '{question.Id}' is used twice."));
                }
            }

            ValidateQuestion(question, questionPath, errors);
            if (followUp && question.Type == QuestionType.File)
            {
                // The follow-up page works without JavaScript and takes no uploads (workflow-engine.md §8).
                errors.Add(new(Join(questionPath, "type"), "A follow-up questionnaire cannot ask for a file. Ask for files on the apply form."));
            }
        }
    }

    private static void ValidateQuestion(Question question, string path, List<DefinitionError> errors)
    {
        ValidateText(question.Label, Join(path, "label"), 500, required: true, errors);
        ValidateText(question.HelpText, Join(path, "helpText"), 1000, required: false, errors);
        if (!Enum.IsDefined(question.Type))
        {
            errors.Add(new(Join(path, "type"), "Unknown question type."));
            return;
        }

        var isChoice = question.Type is QuestionType.SingleChoice or QuestionType.MultiChoice;
        if (isChoice)
        {
            ValidateOptions(question.Options, Join(path, "options"), errors);
        }
        else if (question.Options is { Count: > 0 })
        {
            errors.Add(new(Join(path, "options"), "Only choice questions have options."));
        }

        if (question.Knockout is not null)
        {
            if (question.Type != QuestionType.YesNo)
            {
                errors.Add(new(Join(path, "knockout"), "Only yes/no questions can be knockouts."));
            }

            ValidateText(question.Knockout.Reason, Join(path, "knockout.reason"), 500, required: false, errors);
        }

        if (question.Type == QuestionType.Number)
        {
            if (question.Min is { } min && question.Max is { } max && min > max)
            {
                errors.Add(new(Join(path, "max"), "The maximum is below the minimum."));
            }
        }
        else if (question.Min is not null || question.Max is not null)
        {
            errors.Add(new(Join(path, question.Min is not null ? "min" : "max"), "Only number questions have a minimum or maximum."));
        }

        var textLimit = question.Type switch
        {
            QuestionType.ShortText => 500,
            QuestionType.LongText => 10_000,
            _ => 0,
        };
        if (question.MaxLength is { } maxLength && (textLimit == 0 || maxLength < 1 || maxLength > textLimit))
        {
            errors.Add(new(Join(path, "maxLength"), textLimit == 0
                ? "Only text questions have a maximum length."
                : $"A maximum length is between 1 and {textLimit.ToString(CultureInfo.InvariantCulture)}."));
        }

        if (question.AcceptedFileTypes is { Count: > 0 } fileTypes)
        {
            if (question.Type != QuestionType.File)
            {
                errors.Add(new(Join(path, "acceptedFileTypes"), "Only file questions have accepted file types."));
            }
            else if (fileTypes.Count > 20 || fileTypes.Any(t => t is null || !FileExtension().IsMatch(t)))
            {
                errors.Add(new(Join(path, "acceptedFileTypes"), "Accepted file types are up to 20 extensions such as .pdf."));
            }
        }
    }

    private static void ValidateOptions(IReadOnlyList<QuestionOption>? options, string path, List<DefinitionError> errors)
    {
        if (options is null || options.Count < 2 || options.Count > MaxOptions)
        {
            errors.Add(new(path, $"A choice question has between 2 and {MaxOptions} options."));
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
        {
            var optionPath = $"{path}[{i}]";
            if (options[i] is not { } option)
            {
                errors.Add(new(optionPath, "An option is required."));
                continue;
            }

            if (ValidateId(option.Id, Join(optionPath, "id"), errors) && !ids.Add(option.Id))
            {
                errors.Add(new(Join(optionPath, "id"), $"Option id '{option.Id}' is used twice."));
            }

            ValidateText(option.Label, Join(optionPath, "label"), 200, required: true, errors);
        }
    }

    private static void ValidateAutomations(
        IReadOnlyList<Automation>? automations, string path, WorkflowParts parts, List<DefinitionError> errors)
    {
        var (stages, questions, _) = parts;
        if (automations is null)
        {
            errors.Add(new(path, "Automations are required; use an empty list for none."));
            return;
        }

        if (automations.Count > MaxAutomations)
        {
            errors.Add(new(path, $"A workflow has at most {MaxAutomations} automations."));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < automations.Count; i++)
        {
            var automationPath = $"{path}[{i}]";
            var automation = automations[i];
            if (automation is null)
            {
                errors.Add(new(automationPath, "An automation is required."));
                continue;
            }

            if (ValidateId(automation.Id, Join(automationPath, "id"), errors) && !ids.Add(automation.Id))
            {
                errors.Add(new(Join(automationPath, "id"), $"Automation id '{automation.Id}' is used twice."));
            }

            ValidateText(automation.Name, Join(automationPath, "name"), 120, required: true, errors);
            ValidateTrigger(automation.Trigger, Join(automationPath, "trigger"), parts, errors);
            ValidateConditions(automation.Conditions, Join(automationPath, "conditions"), automation.Trigger?.Type, questions, errors);
            ValidateActions(automation.Actions, Join(automationPath, "actions"), parts, errors);
        }

        var signalDriven = DrivenAutomations(automations, DrivenBySignal);
        var delayDriven = DrivenAutomations(automations, a => a.Trigger?.Type == TriggerType.SlaBreached);
        for (var i = 0; i < automations.Count; i++)
        {
            if (automations[i] is { } automation)
            {
                ValidateGuardrails(automation, $"{path}[{i}]", signalDriven.Contains(i), delayDriven.Contains(i), stages, questions, errors);
            }
        }
    }

    // An integrity or verification signal drives the automation directly: its trigger or a fact it reads.
    private static bool DrivenBySignal(Automation automation) =>
        automation.Trigger?.Type is TriggerType.IntegrityAssessed or TriggerType.VerificationCompleted
        || automation.Conditions?.Rules?.Any(rule => rule is not null && FactSchema.IsSignal(rule.Fact)) == true;

    /// <summary>
    /// The automations something can drive, directly or at one remove. For integrity and verification signals, directly
    /// means a signal trigger or a signal fact; for an SLA breach, the SlaBreached trigger. At one remove: a condition on a tag that a signal-driven automation adds, or a stage
    /// trigger on a stage one moves applications into, or any StageExited trigger once one moves applications at all
    /// (the stage a move leaves is wherever the application is when the action runs, which no definition can know).
    /// A ReviewerDecision trigger counts like a stage trigger: on a stage a signal moves applications into, or with no
    /// stage once a signal moves applications at all. A person's scorecard sits in between, but the scorecard may be
    /// filled only because a signal put the candidate in front of that reviewer, so the guardrail stays conservative.
    /// An SlaBreached trigger without a stage counts the same way: it fires on whatever stage the application is in.
    /// Otherwise a signal could tag or route an application and a second automation could act on the tag or the stage,
    /// laundering the signal past guardrail 1, or a breach past "never counts against the candidate". Repeated until
    /// nothing new is found, so chains of any length count.
    /// </summary>
    private static HashSet<int> DrivenAutomations(IReadOnlyList<Automation> automations, Func<Automation, bool> drivenDirectly)
    {
        var driven = new HashSet<int>();
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stages = new HashSet<string>(StringComparer.Ordinal);
        var movesOut = false;
        bool found;
        do
        {
            found = false;
            for (var i = 0; i < automations.Count; i++)
            {
                if (driven.Contains(i) || automations[i] is not { } automation
                    || !(drivenDirectly(automation) || IsDrivenAtOneRemove(automation, tags, stages, movesOut)))
                {
                    continue;
                }

                driven.Add(i);
                found = true;
                foreach (var action in automation.Actions?.Where(a => a is not null) ?? [])
                {
                    if (action.Type == ActionType.AddTag && action.Tag is not null)
                    {
                        tags.Add(action.Tag);
                    }
                    else if (action.Type == ActionType.MoveToStage && action.StageId is not null)
                    {
                        stages.Add(action.StageId);
                        movesOut = true;
                    }
                }
            }
        }
        while (found);

        return driven;
    }

    private static bool IsDrivenAtOneRemove(Automation automation, HashSet<string> taintedTags, HashSet<string> taintedStages, bool drivenMovesOut)
    {
        if ((automation.Trigger?.StageId is { } stageId && taintedStages.Contains(stageId))
            || (automation.Trigger?.Type == TriggerType.StageExited && drivenMovesOut)
            || (automation.Trigger is { Type: TriggerType.ReviewerDecision or TriggerType.SlaBreached, StageId: null } && drivenMovesOut))
        {
            return true;
        }

        return automation.Conditions?.Rules?.Any(rule => rule is not null
            && rule.Fact == FactSchema.Tags && ReadsTaintedTag(rule, taintedTags)) == true;
    }

    // A rule reads a tainted tag if it would hold for an application carrying that tag, judged by the evaluator itself so
    // the validator and the worker can never compare tags differently. A negated rule depends on the tag's absence, which
    // a signal decides as much as its presence, so it counts whenever a signal adds any tag.
    private static bool ReadsTaintedTag(AutomationCondition rule, HashSet<string> taintedTags) =>
        taintedTags.Count > 0
        && (rule.Negate || taintedTags.Any(tag => AutomationEvaluator.Holds(rule, new AutomationFacts([], [tag]))));

    private static void ValidateTrigger(AutomationTrigger? trigger, string path, WorkflowParts parts, List<DefinitionError> errors)
    {
        var stages = parts.Stages;
        if (trigger is null)
        {
            errors.Add(new(path, "A trigger is required."));
            return;
        }

        if (!Enum.IsDefined(trigger.Type))
        {
            errors.Add(new(Join(path, "type"), "Unknown trigger."));
            return;
        }

        if (UnavailableTriggers.TryGetValue(trigger.Type, out var unavailable))
        {
            errors.Add(new(Join(path, "type"), unavailable));
        }

        var needsStage = trigger.Type is TriggerType.StageEntered or TriggerType.StageExited or TriggerType.TimeElapsed;
        if (needsStage)
        {
            ValidateStageReference(trigger.StageId, Join(path, "stageId"), stages, errors);
        }
        else if (trigger.Type == TriggerType.ReviewerDecision && trigger.StageId is not null)
        {
            // Optional: a decision on any stage's scorecard, or only on this one's.
            ValidateStageReference(trigger.StageId, Join(path, "stageId"), stages, errors);
            if (stages.GetValueOrDefault(trigger.StageId) is { } stage && !stage.HasScorecard())
            {
                errors.Add(new(Join(path, "stageId"), $"{stage.Name} has no scorecard, so no reviewer decision is recorded there."));
            }
        }
        else if (trigger.Type == TriggerType.SlaBreached)
        {
            // Optional: any stage's time limit, or only this one's.
            if (trigger.StageId is not null)
            {
                ValidateStageReference(trigger.StageId, Join(path, "stageId"), stages, errors);
                if (stages.GetValueOrDefault(trigger.StageId) is { } stage && !stage.HasSla())
                {
                    errors.Add(new(Join(path, "stageId"), Enum.IsDefined(stage.Kind) && stage.Kind.IsTerminal()
                        ? $"{stage.Name} ends the application, so its time limit never passes."
                        : $"{stage.Name} has no time limit, so it is never overdue. Set its time limit in days."));
                }
            }
            else if (stages.Count > 0 && !stages.Values.Any(s => s.HasSla()))
            {
                errors.Add(new(Join(path, "type"), "No stage has a time limit, so this never fires. Set a stage's time limit in days."));
            }
        }
        else if (trigger.StageId is not null)
        {
            errors.Add(new(Join(path, "stageId"), $"A {trigger.Type} trigger has no stage."));
        }

        if (trigger.Type == TriggerType.QuestionnaireCompleted)
        {
            // Optional, as a ReviewerDecision's stage is: without one it fires on any follow-up questionnaire, which keeps
            // definitions saved before follow-ups existed valid.
            if (trigger.QuestionnaireId is not null)
            {
                ValidateFollowUpReference(trigger.QuestionnaireId, Join(path, "questionnaireId"), parts.FollowUps, errors);
            }
        }
        else if (trigger.QuestionnaireId is not null)
        {
            errors.Add(new(Join(path, "questionnaireId"), $"A {trigger.Type} trigger has no questionnaire."));
        }

        if (trigger.Type == TriggerType.TimeElapsed)
        {
            if (trigger.AfterHours is not (>= 1 and <= 8760))
            {
                errors.Add(new(Join(path, "afterHours"), "A time trigger waits between 1 and 8760 hours."));
            }
        }
        else if (trigger.AfterHours is not null)
        {
            errors.Add(new(Join(path, "afterHours"), "Only a TimeElapsed trigger waits."));
        }
    }

    private static void ValidateConditions(
        AutomationConditions? conditions, string path, TriggerType? trigger, Dictionary<string, Question> questions, List<DefinitionError> errors)
    {
        if (conditions is null)
        {
            return;
        }

        if (!Enum.IsDefined(conditions.Match))
        {
            errors.Add(new(Join(path, "match"), "Match is All or Any."));
        }

        if (conditions.Rules is not { Count: > 0 and <= MaxRules } rules)
        {
            errors.Add(new(Join(path, "rules"), $"Conditions have between 1 and {MaxRules} rules; omit them to always run."));
            return;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rulePath = $"{path}.rules[{i}]";
            if (rules[i] is not { } rule)
            {
                errors.Add(new(rulePath, "A rule is required."));
                continue;
            }

            if (FactSchema.IsRating(rule.Fact))
            {
                // Not an unknown fact: a refused one. The candidate's rating informs people and is never a condition.
                errors.Add(new(Join(rulePath, "fact"),
                    "The candidate's rating informs people, who decide: no automation acts on it or on a scorecard's answers (ADR-0013). " +
                    "Use a ReviewerDecision trigger and reviewer.recommendation to act on one reviewer's recommendation."));
                continue;
            }

            var fact = FactSchema.Find(rule.Fact, questions);
            if (fact is null)
            {
                errors.Add(new(Join(rulePath, "fact"), rule.Fact?.StartsWith(AnswersPrefix, StringComparison.Ordinal) == true
                    ? $"No question has the id '{rule.Fact[AnswersPrefix.Length..]}'."
                    : $"Unknown fact '{rule.Fact}'."));
            }
            else if (fact.Category == FactCategory.Reviewer && trigger != TriggerType.ReviewerDecision)
            {
                errors.Add(new(Join(rulePath, "fact"), "Only a ReviewerDecision automation has a reviewer's recommendation to test."));
            }
            else if (trigger == TriggerType.SlaBreached && FactSchema.IsTimingOrTelemetry(fact.Name))
            {
                // The engine skips an automation with a timing condition for an application with an accommodation, and a
                // skipped reminder would leave that candidate waiting with nobody told (workflow-engine.md section 8).
                errors.Add(new(Join(rulePath, "fact"),
                    "A time limit passing already says how long the candidate waited. A timing condition here would skip the " +
                    "reminder for a candidate with an accommodation, who would then wait with nobody told."));
            }
            else if (Enum.IsDefined(rule.Operator) && !FactSchema.OperatorsFor(fact.Type).Contains(rule.Operator))
            {
                errors.Add(new(Join(rulePath, "operator"),
                    $"{rule.Operator} does not apply to {fact.Name}, a {Describe(fact.Type)} fact. Use {string.Join(", ", FactSchema.OperatorsFor(fact.Type).Order())}."));
            }
            else
            {
                ValidateOperands(rule, fact, rulePath, questions, errors);
            }

            switch (rule.Operator)
            {
                case ConditionOperator.Exists:
                    if (rule.Value is not null || rule.Values is not null)
                    {
                        errors.Add(new(rulePath, "Exists takes no value."));
                    }

                    break;
                case ConditionOperator.In:
                    if (rule.Value is not null || rule.Values is not { Count: > 0 and <= 50 } || rule.Values.Any(v => v is null || v.Length > 200))
                    {
                        errors.Add(new(Join(rulePath, "values"), "In takes between 1 and 50 values in values, and no value."));
                    }

                    break;
                case var known when Enum.IsDefined(known):
                    if (rule.Values is not null || string.IsNullOrEmpty(rule.Value) || rule.Value.Length > 200)
                    {
                        errors.Add(new(Join(rulePath, "value"), $"{rule.Operator} takes one value of up to 200 characters, and no values."));
                    }

                    break;
                default:
                    errors.Add(new(Join(rulePath, "operator"), "Unknown operator."));
                    break;
            }
        }
    }

    private static void ValidateActions(IReadOnlyList<AutomationAction>? actions, string path, WorkflowParts parts, List<DefinitionError> errors)
    {
        if (actions is not { Count: > 0 and <= MaxActions })
        {
            errors.Add(new(path, $"An automation has between 1 and {MaxActions} actions."));
            return;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var actionPath = $"{path}[{i}]";
            if (actions[i] is not { } action)
            {
                errors.Add(new(actionPath, "An action is required."));
                continue;
            }

            if (!Enum.IsDefined(action.Type))
            {
                errors.Add(new(Join(actionPath, "type"), "Unknown action."));
                continue;
            }

            if (UnavailableActions.TryGetValue(action.Type, out var unavailable))
            {
                errors.Add(new(Join(actionPath, "type"), unavailable));
            }

            ValidateActionFields(action, actionPath, parts, errors);
        }
    }

    // Each action type's required and optional fields; anything else set on it is an error, so a definition never
    // carries a value the engine would silently ignore.
    private static void ValidateActionFields(AutomationAction action, string path, WorkflowParts parts, List<DefinitionError> errors)
    {
        var stages = parts.Stages;
        var (required, optional) = action.Type switch
        {
            ActionType.SendEmail => (ActionField.EmailTemplate, ActionField.None),
            ActionType.SendQuestionnaire => (ActionField.QuestionnaireId, ActionField.None),
            ActionType.RequestVerification => (ActionField.None, ActionField.VerificationProvider),
            ActionType.MoveToStage => (ActionField.StageId, ActionField.None),
            ActionType.AddTag => (ActionField.Tag, ActionField.None),
            ActionType.AssignReviewer => (ActionField.None, ActionField.AssigneeUserId),
            ActionType.NotifyTeam => (ActionField.Note, ActionField.None),
            ActionType.ScheduleInterview => (ActionField.Url, ActionField.None),
            ActionType.CreateTask => (ActionField.Note, ActionField.None),
            ActionType.CallWebhook => (ActionField.Url, ActionField.None),
            ActionType.QueueForRejectionReview => (ActionField.Reason, ActionField.None),
            ActionType.Reject => (ActionField.Reason, ActionField.EmailTemplate),
            _ => (ActionField.None, ActionField.None),
        };

        var present = ActionField.None;
        present |= action.StageId is not null ? ActionField.StageId : ActionField.None;
        present |= action.EmailTemplate is not null ? ActionField.EmailTemplate : ActionField.None;
        present |= action.Tag is not null ? ActionField.Tag : ActionField.None;
        present |= action.Reason is not null ? ActionField.Reason : ActionField.None;
        present |= action.Url is not null ? ActionField.Url : ActionField.None;
        present |= action.Note is not null ? ActionField.Note : ActionField.None;
        present |= action.AssigneeUserId is not null ? ActionField.AssigneeUserId : ActionField.None;
        present |= action.VerificationProvider is not null ? ActionField.VerificationProvider : ActionField.None;
        present |= action.QuestionnaireId is not null ? ActionField.QuestionnaireId : ActionField.None;

        foreach (var field in Enum.GetValues<ActionField>().Where(f => f != ActionField.None))
        {
            var name = JsonName(field);
            if (required.HasFlag(field) && !present.HasFlag(field))
            {
                errors.Add(new(Join(path, name), $"{action.Type} needs {name}."));
            }
            else if (present.HasFlag(field) && !required.HasFlag(field) && !optional.HasFlag(field))
            {
                errors.Add(new(Join(path, name), $"{action.Type} does not take {name}."));
            }
        }

        if (action.StageId is not null && action.Type == ActionType.MoveToStage)
        {
            ValidateStageReference(action.StageId, Join(path, "stageId"), stages, errors);
        }

        if (action.QuestionnaireId is not null && action.Type == ActionType.SendQuestionnaire)
        {
            ValidateFollowUpReference(action.QuestionnaireId, Join(path, "questionnaireId"), parts.FollowUps, errors);
        }

        if (action.EmailTemplate is not null && !EmailTemplateIds.IsWellFormed(action.EmailTemplate))
        {
            errors.Add(new(Join(path, "emailTemplate"),
                $"Unknown email template. Use one of {string.Join(", ", EmailTemplateIds.All.Order(StringComparer.Ordinal))}, or the id of one of the organization's templates."));
        }
        else if (action.Type == ActionType.SendEmail && action.EmailTemplate == EmailTemplateIds.ApplicationRejected)
        {
            // The rejection email tells a candidate they are out, so it goes only with a Reject action, which the
            // knockout and opt-in rules guard (workflow-engine.md §2, guardrail 2).
            errors.Add(new(Join(path, "emailTemplate"), "The rejection email is sent only by a Reject action. Use QueueForRejectionReview so a person decides."));
        }
        else if (action.Type == ActionType.SendEmail && action.EmailTemplate == EmailTemplateIds.QuestionnaireRequest)
        {
            // Its link is made for one questionnaire request, which only a SendQuestionnaire action creates.
            errors.Add(new(Join(path, "emailTemplate"), "The follow-up questions email is sent only by a SendQuestionnaire action, with its link."));
        }
        else if (action.Type == ActionType.Reject && action.EmailTemplate is not null && action.EmailTemplate != EmailTemplateIds.ApplicationRejected)
        {
            errors.Add(new(Join(path, "emailTemplate"), $"A Reject action sends {EmailTemplateIds.ApplicationRejected}, with its reason; leave emailTemplate out."));
        }

        ValidateText(action.Tag, Join(path, "tag"), 50, required: false, errors);
        ValidateText(action.Reason, Join(path, "reason"), 500, required: false, errors);
        ValidateText(action.Note, Join(path, "note"), 1000, required: false, errors);
        ValidateText(action.VerificationProvider, Join(path, "verificationProvider"), 50, required: false, errors);
        if (action.Url is not null && !(Uri.TryCreate(action.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && action.Url.Length <= 2000))
        {
            errors.Add(new(Join(path, "url"), "The URL must be an absolute https URL."));
        }
    }

    /// <summary>
    /// The non-negotiables (ADR-0004, workflow-engine.md §2): an automation driven by an integrity or verification signal
    /// may only route or step up, and only an opted-in knockout automation may reject without a person.
    /// </summary>
    private static void ValidateGuardrails(
        Automation automation,
        string path,
        bool signalDriven,
        bool delayDriven,
        Dictionary<string, WorkflowStage> stages,
        Dictionary<string, Question> questions,
        List<DefinitionError> errors)
    {
        if (automation.Actions is null)
        {
            return;
        }

        var rules = automation.Conditions?.Rules?.Where(r => r is not null).ToList() ?? [];

        for (var i = 0; i < automation.Actions.Count; i++)
        {
            if (automation.Actions[i] is not { } action)
            {
                continue;
            }

            var actionPath = $"{path}.actions[{i}]";
            if (action is { Type: ActionType.SendEmail, EmailTemplate: EmailTemplateIds.StageChange }
                && automation.Trigger is { Type: TriggerType.StageEntered, StageId: { } enteredId }
                && stages.GetValueOrDefault(enteredId)?.Kind is StageKind.Rejected or StageKind.Withdrawn)
            {
                // The worker skips it anyway (the email says "the next step"); refusing it here says why at save.
                errors.Add(new(Join(actionPath, "emailTemplate"),
                    "The stage-change email tells the candidate they moved to the next step, so it is not sent on entering a rejected or withdrawn stage."));
                continue;
            }

            if (automation.Trigger?.Type == TriggerType.SlaBreached && !SlaBreachActions.Contains(action.Type))
            {
                errors.Add(new(actionPath,
                    "A time limit passing measures the hiring team's delay, not the candidate, so this automation may only notify " +
                    "the team, assign a reviewer, create a task or add a tag. Nothing it does may count against the candidate."));
                continue;
            }

            var target = action.StageId is null ? null : stages.GetValueOrDefault(action.StageId);
            if (signalDriven && (!SignalSafeActions.Contains(action.Type)
                || (action.Type == ActionType.MoveToStage && target is not null && !SignalSafeStages.Contains(target.Kind))))
            {
                errors.Add(new(actionPath,
                    "An automation that uses an integrity or verification signal, directly or through a tag or stage another such " +
                    "automation sets, may only request verification, assign a reviewer, add a tag or move to a screening, " +
                    "assessment, verification or interview stage. Integrity signals never drive rejection (ADR-0004)."));
                continue;
            }

            // At one remove from a breach (a tag it adds, a stage reached through one), the same list as the breach itself,
            // plus a move into a review stage as a signal may make. Not Custom, not an email: either could be a rejection
            // under another name, and the team's delay never counts against the candidate.
            if (delayDriven && automation.Trigger?.Type != TriggerType.SlaBreached && !SlaBreachActions.Contains(action.Type)
                && !(action.Type == ActionType.MoveToStage && target is not null && SignalSafeStages.Contains(target.Kind)))
            {
                errors.Add(new(actionPath,
                    "This automation acts on a tag or stage that a passed time limit sets. A time limit measures the hiring " +
                    "team's delay and never counts against the candidate, so this automation may only notify the team, assign " +
                    "a reviewer, create a task, add a tag or move to a screening, assessment, verification or interview stage."));
                continue;
            }

            var rejects = action.Type == ActionType.Reject
                || (action.Type == ActionType.MoveToStage && target?.Kind is StageKind.Rejected or StageKind.Withdrawn);

            if (!rejects)
            {
                continue;
            }

            var onlyKnockouts = rules.Count > 0 && rules.All(r => IsKnockoutRule(r, questions));
            if (!onlyKnockouts)
            {
                errors.Add(new(actionPath,
                    "Only a knockout automation may reject: every condition must be a knockout question equal to its " +
                    "disqualifying answer. Use QueueForRejectionReview so a person decides."));
            }
            else if (!automation.AllowAutoReject)
            {
                errors.Add(new(Join(path, "allowAutoReject"),
                    "Rejecting without a person needs an explicit opt-in: set allowAutoReject, or use QueueForRejectionReview."));
            }
            else if (action.Type == ActionType.MoveToStage)
            {
                errors.Add(new(actionPath, "Reject with a reason the candidate sees instead of moving to a rejected stage."));
            }
        }
    }

    private static void ValidatePostingDefaults(PostingDefaults? defaults, string path, List<DefinitionError> errors)
    {
        if (defaults is null)
        {
            errors.Add(new(path, "Posting defaults are required; use an empty object for none."));
            return;
        }

        if (defaults.EmploymentType is { } employmentType && !Enum.IsDefined(employmentType))
        {
            errors.Add(new(Join(path, "employmentType"), "Unknown employment type."));
        }

        if (defaults.WorkplaceType is { } workplaceType && !Enum.IsDefined(workplaceType))
        {
            errors.Add(new(Join(path, "workplaceType"), "Unknown workplace type."));
        }

        if (defaults.SalaryPeriod is { } period && !Enum.IsDefined(period))
        {
            errors.Add(new(Join(path, "salaryPeriod"), "Unknown salary period."));
        }

        ValidateText(defaults.Department, Join(path, "department"), 100, required: false, errors);
        if (defaults.SalaryCurrency is not null && !CurrencyCode().IsMatch(defaults.SalaryCurrency))
        {
            errors.Add(new(Join(path, "salaryCurrency"), "A currency is an ISO 4217 code such as GBP."));
        }

        if (defaults.ValidForDays is < 1 or > 365)
        {
            errors.Add(new(Join(path, "validForDays"), "A posting runs for between 1 and 365 days."));
        }

        ValidateText(defaults.Description, Join(path, "description"), 50_000, required: false, errors);
    }

    // The operands a typed fact can hold: yes or no for a yes/no answer, option ids for a choice answer, a number for a
    // number fact. Anything else could never match, which is a silent bug in a saved rule.
    private static void ValidateOperands(
        AutomationCondition rule, FactDefinition fact, string path, Dictionary<string, Question> questions, List<DefinitionError> errors)
    {
        var operands = rule.Operator == ConditionOperator.In ? rule.Values ?? [] : rule.Value is null ? [] : [rule.Value];
        var operandPath = Join(path, rule.Operator == ConditionOperator.In ? "values" : "value");
        switch (fact.Type)
        {
            case FactType.YesNo when operands.Any(o => o is not ("yes" or "no")):
                errors.Add(new(operandPath, "A yes/no answer is \"yes\" or \"no\"."));
                break;
            case FactType.Number when operands.Any(o => !FactSchema.TryParseNumber(o, out _)):
                errors.Add(new(operandPath, $"{fact.Name} is a number; compare it with a number such as 48."));
                break;
            case FactType.Recommendation when operands.FirstOrDefault(o => o is null || !Enum.GetNames<Recommendation>().Contains(o, StringComparer.Ordinal)) is { } notRecommendation:
                errors.Add(new(operandPath, $"'{notRecommendation}' is not a recommendation. Use {string.Join(", ", Enum.GetNames<Recommendation>())}."));
                break;
            case FactType.Choice or FactType.MultiChoice:
                var options = questions[fact.Name[AnswersPrefix.Length..]].Options?.Where(o => o is not null).Select(o => o.Id).ToHashSet(StringComparer.Ordinal) ?? [];
                if (operands.FirstOrDefault(o => o is not null && !options.Contains(o)) is { } unknown)
                {
                    errors.Add(new(operandPath, $"The question has no option with the id '{unknown}'; compare option ids, not labels."));
                }

                break;
        }
    }

    private static string Describe(FactType type) => type switch
    {
        FactType.TextList => "list",
        FactType.MultiChoice => "multiple-choice",
        FactType.YesNo => "yes/no",
        _ => type.ToString().ToLowerInvariant(),
    };

    // answers.{id} EqualTo the knockout's disqualifying answer ("yes" or "no"), not negated: anything else could reject
    // an eligible candidate.
    private static bool IsKnockoutRule(AutomationCondition rule, Dictionary<string, Question> questions) =>
        rule.Fact is { } fact
        && fact.StartsWith(AnswersPrefix, StringComparison.Ordinal)
        && questions.TryGetValue(fact[AnswersPrefix.Length..], out var question)
        && question.Knockout is { } knockout
        && rule is { Operator: ConditionOperator.EqualTo, Negate: false }
        && rule.Value == (knockout.DisqualifyingAnswer ? "yes" : "no");

    private static void ValidateFollowUpReference(
        string questionnaireId, string path, Dictionary<string, FollowUpQuestionnaire> followUps, List<DefinitionError> errors)
    {
        if (!followUps.ContainsKey(questionnaireId))
        {
            errors.Add(new(path, $"No follow-up questionnaire has the id '{questionnaireId}'."));
        }
    }

    private static void ValidateStageReference(string? stageId, string path, Dictionary<string, WorkflowStage> stages, List<DefinitionError> errors)
    {
        if (stageId is null)
        {
            errors.Add(new(path, "A stage id is required."));
        }
        else if (!stages.ContainsKey(stageId))
        {
            errors.Add(new(path, $"No stage has the id '{stageId}'."));
        }
    }

    private static bool ValidateId(string? id, string path, List<DefinitionError> errors)
    {
        if (id is not null && Identifier().IsMatch(id))
        {
            return true;
        }

        errors.Add(new(path, "An id is 1 to 40 lowercase letters, digits, '-' or '_', starting with a letter."));
        return false;
    }

    private static void ValidateText(string? text, string path, int maxLength, bool required, List<DefinitionError> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required || text is not null)
            {
                errors.Add(new(path, required ? "This is required." : "Leave this out rather than sending it blank."));
            }
        }
        else if (text.Length > maxLength)
        {
            errors.Add(new(path, $"At most {maxLength.ToString(CultureInfo.InvariantCulture)} characters."));
        }
    }

    private static string Join(string path, string member) => path.Length == 0 ? member : $"{path}.{member}";

    private static string JsonName(ActionField field) => field switch
    {
        ActionField.StageId => "stageId",
        ActionField.EmailTemplate => "emailTemplate",
        ActionField.Tag => "tag",
        ActionField.Reason => "reason",
        ActionField.Url => "url",
        ActionField.Note => "note",
        ActionField.AssigneeUserId => "assigneeUserId",
        ActionField.VerificationProvider => "verificationProvider",
        ActionField.QuestionnaireId => "questionnaireId",
        _ => field.ToString(),
    };

    [Flags]
    private enum ActionField
    {
        None = 0,
        StageId = 1,
        EmailTemplate = 2,
        Tag = 4,
        Reason = 8,
        Url = 16,
        Note = 32,
        AssigneeUserId = 64,
        VerificationProvider = 128,
        QuestionnaireId = 256,
    }

    /// <summary>What the automations of one workflow may refer to: its stages, every question by id, and its follow-ups.</summary>
    private sealed record WorkflowParts(
        Dictionary<string, WorkflowStage> Stages,
        Dictionary<string, Question> Questions,
        Dictionary<string, FollowUpQuestionnaire> FollowUps);

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,39}$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^\.[a-z0-9]{1,10}$")]
    private static partial Regex FileExtension();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    // A backstop for scorecard questions that rate the person rather than the job (CLAUDE.md non-negotiable 3). Whole words only,
    // and not words with a fair job meaning: "data integrity", "energy sector knowledge", "character encoding" and
    // "statistical confidence" pass. It catches the obvious cases; a criterion's meaning is for the template builder's
    // guidance and review of system templates (workflow-engine.md §7).
    [GeneratedRegex(@"\b(personality|personalities|emotion|emotions|emotional|emotionally|honest|honesty|dishonest|dishonesty|truthful|truthfulness|lying|liar|trustworthy|trustworthiness|sincere|sincerity|temperament|likeable|likeability|likability|culture fit|cultural fit|gut feeling|attitude|enthusiasm|enthusiastic|passion|passionate|demeanor|demeanour|motivation|motivated|charisma|charismatic|vibe|vibes)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonalJudgement();
}
