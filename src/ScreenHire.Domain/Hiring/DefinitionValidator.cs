using System.Globalization;
using System.Text.RegularExpressions;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Domain.Hiring;

/// <summary>
/// A problem with a definition. <paramref name="Path"/> is a JSON path such as <c>workflow.stages[1].id</c> for code;
/// <paramref name="Message"/> is a whole sentence for a person that names the thing and says what to do, so a screen can
/// show it as it is.
/// </summary>
public sealed record DefinitionError(string Path, string Message);

/// <summary>
/// Structural validation of hiring template definitions and job workflows, plus the verification guardrails that no saved
/// definition may break (ADR-0004, workflow-engine.md §2). The API runs it before saving; the workflow engine can run
/// it again before executing. Request bodies arrive through a serializer that does not enforce nullability, so every
/// member is checked for null here.
/// </summary>
/// <remarks>
/// Every message is written for someone who has never seen the JSON: it names the stage, automation, step, question or
/// scorecard by the name they gave it (<see cref="Quote"/>, with a plain noun when the name is blank), says what to do,
/// and uses the words of the screen (a "Send a form" step, not <c>SendQuestionnaire</c>).
/// </remarks>
public static partial class DefinitionValidator
{
    /// <summary>Facts a condition may test besides <c>answers.{questionId}</c>; their types are in <see cref="FactSchema"/>.</summary>
    public static IReadOnlySet<string> KnownFacts { get; } =
        new HashSet<string>(FactSchema.ApplicationFacts.Select(f => f.Name), StringComparer.Ordinal);

    // Timing facts stay (application.hoursInStage, TimeElapsed); the workflow engine skips them for an application with
    // an accommodation (workflow-engine.md §2, guardrail 4). FactSchema says why accommodation is not a fact.
    private const string AnswersPrefix = FactSchema.AnswersPrefix;

    // Kinds the definition model names but the engine does not run yet. Refused at save, so no definition carries one
    // the engine would silently ignore. Verification is phase 2 (CLAUDE.md non-negotiable 3); candidate replies are not
    // tracked yet. Each value is the first sentence of the message; the fix follows it.
    private static readonly Dictionary<ActionType, string> UnavailableActions = new()
    {
        [ActionType.RequestVerification] = "Asking for a check is not available yet.",
    };

    private static readonly Dictionary<TriggerType, string> UnavailableTriggers = new()
    {
        [TriggerType.VerificationCompleted] = "Starting when a check finishes is not available yet.",
        [TriggerType.CandidateReplied] = "Starting when a candidate replies is not available yet.",
    };

    // What an automation that uses a verification signal may do: route and step up, never reject
    // (workflow-engine.md §2, guardrail 1). MoveToStage is further limited to SignalSafeStages. SendQuestionnaire is
    // not here: asking a candidate for more because a signal fired would act on the signal as a finding about them, and
    // neither is ScheduleInterview, which offers the candidate an interview.
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
    public const int MaxSections = 20;
    public const int MaxInterviewers = 5;
    public const int MaxInterviewWindowDays = 60;

    // Question ids the apply form gives meaning to (ApplicationForm): its resume question is there even when the apply
    // questionnaire has none, so a follow-up question with one of these ids would make answers.{id} name two questions.
    private static readonly HashSet<string> ApplyFormQuestionIds =
        [Applications.ApplicationForm.ResumeQuestionId, Applications.ApplicationForm.LinkedInExportQuestionId];

    public static IReadOnlyList<DefinitionError> Validate(HiringTemplateDefinition? definition)
    {
        var errors = new List<DefinitionError>();
        if (definition is null)
        {
            errors.Add(new("", "This template is empty. Add at least one stage."));
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
            errors.Add(new(path, "The hiring process is missing. Add its stages and questions."));
            return;
        }

        var stages = ValidateStages(workflow.Stages, Join(path, "stages"), template, errors);
        var questions = ValidateQuestionnaire(workflow.Questionnaire, Join(path, "questionnaire"), errors);
        var followUps = ValidateFollowUps(workflow.FollowUpQuestionnaires, Join(path, "followUpQuestionnaires"), questions, errors);

        if (workflow.VerificationPolicy is null)
        {
            errors.Add(new(Join(path, "verificationPolicy"), "The setting for whether applicants may use AI is missing. Choose one."));
        }
        else if (!Enum.IsDefined(workflow.VerificationPolicy.AiUse))
        {
            errors.Add(new(Join(path, "verificationPolicy.aiUse"), "The setting for whether applicants may use AI is not recognised. Choose another."));
        }

        ValidateAutomations(workflow.Automations, Join(path, "automations"), new(stages, questions, followUps), errors);
    }

    private static Dictionary<string, WorkflowStage> ValidateStages(
        IReadOnlyList<WorkflowStage>? stages, string path, bool template, List<DefinitionError> errors)
    {
        var byId = new Dictionary<string, WorkflowStage>(StringComparer.Ordinal);
        if (stages is null || stages.Count == 0)
        {
            errors.Add(new(path, "Add at least one stage. Applications need somewhere to start."));
            return byId;
        }

        if (stages.Count > MaxStages)
        {
            errors.Add(new(path, $"There are too many stages. Keep it to {MaxStages} or fewer."));
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

            var stageName = Quote(stage.Name, "this stage");
            if (ValidateId(stage.Id, Join(stagePath, "id"), stageName, errors) && !byId.TryAdd(stage.Id, stage))
            {
                errors.Add(new(Join(stagePath, "id"), $"The id '{stage.Id}' of {stageName} is already used by another stage. Use a different id."));
            }

            ValidateText(stage.Name, Join(stagePath, "name"), 80, required: true, $"The name of stage {i + 1}", errors);
            if (!Enum.IsDefined(stage.Kind))
            {
                errors.Add(new(Join(stagePath, "kind"), $"The type of {stageName} is not recognised. Choose another type."));
            }

            if (stage.SlaDays is < 1 or > 365)
            {
                errors.Add(new(Join(stagePath, "slaDays"), $"The time limit for {stageName} must be between 1 and 365 days. Change it, or clear it."));
            }

            ValidateStageScorecard(stage, stagePath, template, errors);
        }

        // Applications start in the first stage, so it has to be the one Applied stage.
        if (stages[0] is { } first && first.Kind != StageKind.Applied)
        {
            errors.Add(new($"{path}[0].kind",
                $"The first stage must be the Applied stage, where new applications start. Change the type of {Quote(first.Name, "this stage")}, or move the Applied stage to the top."));
        }

        if (stages.Count(s => s?.Kind == StageKind.Applied) > 1)
        {
            errors.Add(new(path, "There can be only one Applied stage. Change the type of the extra ones."));
        }

        return byId;
    }

    private static void ValidateStageScorecard(WorkflowStage stage, string path, bool template, List<DefinitionError> errors)
    {
        if (!stage.HasScorecard())
        {
            return;
        }

        var stageName = Quote(stage.Name, "this stage");
        if (Enum.IsDefined(stage.Kind) && stage.Kind.IsTerminal())
        {
            errors.Add(new(Join(path, stage.ScorecardTemplateId is null ? "scorecard" : "scorecardTemplateId"),
                $"{Cap(stageName)} ends the application, so it cannot have a scorecard. Remove the scorecard."));
        }

        if (stage.ScorecardTemplateId is { } key && !BuiltInScorecards.IsWellFormed(key))
        {
            errors.Add(new(Join(path, "scorecardTemplateId"),
                $"The scorecard chosen for {stageName} does not exist. Choose one from the list."));
        }

        if (template)
        {
            if (stage.Scorecard is not null)
            {
                errors.Add(new(Join(path, "scorecard"),
                    $"{Cap(stageName)} holds its own scorecard, but a template can only point to one from the library. Choose a library scorecard instead."));
            }

            return;
        }

        // A job's stage holds its own copy. One that only names a library scorecard is copied by the API when the job is
        // saved, and the builder validates both kinds of workflow through this overload.
        if (stage.Scorecard is not { } scorecard)
        {
            return;
        }

        var owner = $"the scorecard on {stageName}";
        ValidateText(scorecard.Name, Join(path, "scorecard.name"), ScorecardTemplate.MaxNameLength, required: true, $"The name of {owner}", errors);
        errors.AddRange(ScorecardQuestionProblems(scorecard.Questions, Join(path, "scorecard.questions"), owner));
    }

    /// <summary>
    /// The problems with a scorecard's questions, at paths under <paramref name="path"/>: 1 to
    /// <see cref="MaxScorecardQuestions"/> questions with unique ids, options on the choice kinds only, and nothing that
    /// rates the person rather than the job. The library's scorecards and a job's copy share these rules.
    /// </summary>
    public static IReadOnlyList<DefinitionError> ValidateScorecardQuestions(IReadOnlyList<ScorecardQuestion>? questions, string path) =>
        ScorecardQuestionProblems(questions, path, "this scorecard");

    // owner is a phrase such as "this scorecard" or "the scorecard on \"Screening\"", so each message says where it is.
    private static List<DefinitionError> ScorecardQuestionProblems(IReadOnlyList<ScorecardQuestion>? questions, string path, string owner)
    {
        var errors = new List<DefinitionError>();
        if (questions is null or { Count: 0 })
        {
            errors.Add(new(path, $"{Cap(owner)} has no questions. Add at least one, or remove the scorecard."));
            return errors;
        }

        if (questions.Count > MaxScorecardQuestions)
        {
            errors.Add(new(path, $"{Cap(owner)} has too many questions. Keep it to {MaxScorecardQuestions} or fewer."));
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < questions.Count; i++)
        {
            var questionPath = $"{path}[{i}]";
            if (questions[i] is not { } question)
            {
                errors.Add(new(questionPath, $"A question in {owner} is missing. Remove the empty one."));
                continue;
            }

            var described = Described(question.Label, i + 1, owner);
            if (ValidateId(question.Id, Join(questionPath, "id"), described, errors) && !ids.Add(question.Id))
            {
                errors.Add(new(Join(questionPath, "id"), $"The id '{question.Id}' of {described} is already used by another question. Use a different id."));
            }

            ValidateText(question.Label, Join(questionPath, "label"), 120, required: true, $"The text of question {i + 1} in {owner}", errors);
            ValidateText(question.Guidance, Join(questionPath, "guidance"), 500, required: false, $"The guidance for {described}", errors);
            if (!Enum.IsDefined(question.Type))
            {
                errors.Add(new(Join(questionPath, "type"), $"The answer type of {described} is not recognised. Choose another type."));
            }
            else if (question.Type is ScorecardQuestionType.SingleChoice or ScorecardQuestionType.MultiChoice)
            {
                ValidateOptions(question.Options, Join(questionPath, "options"), described, errors);
            }
            else if (question.Options is { Count: > 0 })
            {
                errors.Add(new(Join(questionPath, "options"),
                    $"{Cap(described)} is not a choice question, so it cannot have options. Remove the options, or change its answer type to a choice."));
            }

            var texts = new List<(string? Text, string Member)> { (question.Label, "label"), (question.Guidance, "guidance") };
            texts.AddRange((question.Options ?? []).Select((o, n) => (o?.Label, $"options[{n}].label")));
            foreach (var (text, member) in texts)
            {
                if (text is not null && PersonalJudgement().Match(text) is { Success: true } match)
                {
                    errors.Add(new(Join(questionPath, member),
                        $"{Cap(described)} asks about \"{match.Value}\". Scorecards rate what the job needs, such as a skill or experience, " +
                        "never personality, emotion or honesty. Reword it."));
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
            errors.Add(new(path, "The application questionnaire is missing. Add one, or use an empty one for no questions."));
            return byId;
        }

        const string owner = "the application questionnaire";
        ValidateQuestions(questions, path, followUp: false, owner, byId, errors);
        ValidateSections(questionnaire.Sections, questions, path, owner, errors);
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
            errors.Add(new(path, $"There are too many follow-up questionnaires. Keep it to {MaxFollowUpQuestionnaires} or fewer."));
        }

        for (var i = 0; i < followUps.Count; i++)
        {
            var followUpPath = $"{path}[{i}]";
            if (followUps[i] is not { } followUp)
            {
                errors.Add(new(followUpPath, "A follow-up questionnaire is missing. Remove the empty one."));
                continue;
            }

            var owner = Ref("follow-up questionnaire", followUp.Name);
            if (ValidateId(followUp.Id, Join(followUpPath, "id"), Quote(followUp.Name, "this follow-up questionnaire"), errors) && !byId.TryAdd(followUp.Id, followUp))
            {
                errors.Add(new(Join(followUpPath, "id"), $"{Cap(owner)} has the id '{followUp.Id}', which another follow-up questionnaire already has. Use a different id."));
            }

            ValidateText(followUp.Name, Join(followUpPath, "name"), 80, required: true, $"The name of follow-up questionnaire {i + 1}", errors);
            if (followUp.Questions is not { Count: > 0 } followUpQuestions)
            {
                errors.Add(new(Join(followUpPath, "questions"), $"{Cap(owner)} has no questions. Add one, or delete it."));
                continue;
            }

            ValidateQuestions(followUpQuestions, followUpPath, followUp: true, owner, questions, errors);
            ValidateSections(followUp.Sections, followUpQuestions, followUpPath, owner, errors);
        }

        return byId;
    }

    private static void ValidateQuestions(
        IReadOnlyList<Question> questions, string path, bool followUp, string owner, Dictionary<string, Question> byId, List<DefinitionError> errors)
    {
        if (questions.Count > MaxQuestions)
        {
            errors.Add(new(Join(path, "questions"), $"{Cap(owner)} has too many questions. Keep it to {MaxQuestions} or fewer."));
        }

        for (var i = 0; i < questions.Count; i++)
        {
            var questionPath = $"{path}.questions[{i}]";
            var question = questions[i];
            if (question is null)
            {
                errors.Add(new(questionPath, $"A question in {owner} is missing. Remove the empty one."));
                continue;
            }

            var described = Described(question.Label, i + 1, owner);
            if (ValidateId(question.Id, Join(questionPath, "id"), described, errors))
            {
                if (followUp && ApplyFormQuestionIds.Contains(question.Id))
                {
                    errors.Add(new(Join(questionPath, "id"),
                        $"The id '{question.Id}' of {described} belongs to a question on the application form. Choose another id."));
                }
                else if (!byId.TryAdd(question.Id, question))
                {
                    errors.Add(new(Join(questionPath, "id"), followUp
                        ? $"The id '{question.Id}' of {described} is already used by another question. Ids must be different across all questionnaires."
                        : $"The id '{question.Id}' of {described} is already used by another question. Use a different id."));
                }
            }

            ValidateQuestion(question, questionPath, i + 1, owner, described, errors);
            if (followUp && question.Type == QuestionType.File)
            {
                // The follow-up page works without JavaScript and takes no uploads (workflow-engine.md §8).
                errors.Add(new(Join(questionPath, "type"),
                    $"{Cap(described)} asks for a file, which a follow-up questionnaire cannot do. Ask for files on the application form, or change the question type."));
            }
        }
    }

    // Named pages of a questionnaire (careers apply-flow.md §7). The questions stay one flat list, which every reader of
    // answers.{id} uses; the sections only group it, so they must cover it exactly, once each and in its order.
    private static void ValidateSections(
        IReadOnlyList<QuestionSection>? sections, IReadOnlyList<Question> questions, string path, string owner, List<DefinitionError> errors)
    {
        if (sections is not { Count: > 0 })
        {
            return;
        }

        var sectionsPath = Join(path, "sections");
        if (sections.Count > MaxSections)
        {
            errors.Add(new(sectionsPath, $"{Cap(owner)} has too many sections. Keep it to {MaxSections} or fewer."));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var placed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var known = questions.Where(q => q is not null).Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var labels = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var question in questions.Where(q => q is not null && q.Id is not null))
        {
            labels.TryAdd(question.Id, question.Label);
        }

        string QuestionName(string? id) => id is not null && labels.TryGetValue(id, out var label) ? Quote(label, $"'{id}'") : $"'{id}'";

        for (var i = 0; i < sections.Count; i++)
        {
            var sectionPath = $"{sectionsPath}[{i}]";
            if (sections[i] is not { } section)
            {
                errors.Add(new(sectionPath, $"A section in {owner} is missing. Remove the empty one."));
                continue;
            }

            var sectionName = Ref("section", section.Name);
            if (ValidateId(section.Id, Join(sectionPath, "id"), Quote(section.Name, "this section"), errors) && !ids.Add(section.Id))
            {
                errors.Add(new(Join(sectionPath, "id"), $"{Cap(sectionName)} has the id '{section.Id}', which another section already has. Use a different id."));
            }

            ValidateText(section.Name, Join(sectionPath, "name"), 80, required: true, $"The name of section {i + 1} in {owner}", errors);
            if (section.QuestionIds is not { Count: > 0 } questionIds)
            {
                errors.Add(new(sectionPath, $"{Cap(sectionName)} has no questions. Add one, or delete the section."));
                continue;
            }

            foreach (var questionId in questionIds)
            {
                if (questionId is null || !known.Contains(questionId))
                {
                    errors.Add(new(Join(sectionPath, "questionIds"),
                        $"{Cap(sectionName)} lists a question ('{questionId}') that is not in {owner}. Remove it from the section."));
                }
                else if (!seen.Add(questionId))
                {
                    errors.Add(new(Join(sectionPath, "questionIds"),
                        $"The question {QuestionName(questionId)} is in {sectionName} and in an earlier section. Keep it in one section."));
                }
                else
                {
                    placed.Add(questionId);
                }
            }
        }

        if (known.Except(seen, StringComparer.Ordinal).FirstOrDefault() is { } missing)
        {
            errors.Add(new(sectionsPath, $"Every question in {owner} must be in a section, and {QuestionName(missing)} is in none. Put each question in one."));
        }
        else if (!placed.SequenceEqual(questions.Where(q => q is not null).Select(q => q.Id), StringComparer.Ordinal))
        {
            errors.Add(new(sectionsPath, $"The sections of {owner} list the questions in a different order from the questionnaire. Put them in the same order."));
        }
    }

    private static void ValidateQuestion(Question question, string path, int number, string owner, string described, List<DefinitionError> errors)
    {
        ValidateText(question.Label, Join(path, "label"), 500, required: true, $"The text of question {number} in {owner}", errors);
        ValidateText(question.HelpText, Join(path, "helpText"), 1000, required: false, $"The help text for {described}", errors);
        if (!Enum.IsDefined(question.Type))
        {
            errors.Add(new(Join(path, "type"), $"The type of {described} is not recognised. Choose another type."));
            return;
        }

        var isChoice = question.Type is QuestionType.SingleChoice or QuestionType.MultiChoice;
        if (isChoice)
        {
            ValidateOptions(question.Options, Join(path, "options"), described, errors);
        }
        else if (question.Options is { Count: > 0 })
        {
            errors.Add(new(Join(path, "options"),
                $"{Cap(described)} is not a choice question, so it cannot have options. Remove the options, or change the question type to a choice."));
        }

        if (question.Knockout is not null)
        {
            if (question.Type != QuestionType.YesNo)
            {
                errors.Add(new(Join(path, "knockout"),
                    $"{Cap(described)} is a knockout question, but only yes or no questions can be. Turn knockout off, or change the question type to yes or no."));
            }

            ValidateText(question.Knockout.Reason, Join(path, "knockout.reason"), 500, required: false, $"The knockout reason for {described}", errors);
        }

        if (question.Type == QuestionType.Number)
        {
            if (question.Min is { } min && question.Max is { } max && min > max)
            {
                errors.Add(new(Join(path, "max"), $"The maximum for {described} is lower than its minimum. Raise the maximum or lower the minimum."));
            }
        }
        else if (question.Min is not null || question.Max is not null)
        {
            errors.Add(new(Join(path, question.Min is not null ? "min" : "max"),
                $"{Cap(described)} is not a number question, so it cannot have a minimum or maximum. Remove them."));
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
                ? $"{Cap(described)} is not a text question, so it has no maximum length. Remove it."
                : $"The maximum length for {described} must be between 1 and {textLimit.ToString(CultureInfo.InvariantCulture)} characters. Change the number."));
        }

        if (question.AcceptedFileTypes is { Count: > 0 } fileTypes)
        {
            if (question.Type != QuestionType.File)
            {
                errors.Add(new(Join(path, "acceptedFileTypes"),
                    $"{Cap(described)} is not a file question, so it cannot list file types. Remove them."));
            }
            else if (fileTypes.Count > 20 || fileTypes.Any(t => t is null || !FileExtension().IsMatch(t)))
            {
                errors.Add(new(Join(path, "acceptedFileTypes"),
                    $"The file types for {described} must be up to 20 extensions such as .pdf. Fix the list."));
            }
        }
    }

    private static void ValidateOptions(IReadOnlyList<QuestionOption>? options, string path, string owner, List<DefinitionError> errors)
    {
        if (options is null || options.Count < 2)
        {
            errors.Add(new(path, $"{Cap(owner)} needs at least 2 options. Add some."));
            return;
        }

        if (options.Count > MaxOptions)
        {
            errors.Add(new(path, $"{Cap(owner)} has too many options. Keep it to {MaxOptions} or fewer."));
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
        {
            var optionPath = $"{path}[{i}]";
            if (options[i] is not { } option)
            {
                errors.Add(new(optionPath, $"An option in {owner} is missing. Remove the empty one."));
                continue;
            }

            var optionName = Quote(option.Label, $"option {i + 1}");
            if (ValidateId(option.Id, Join(optionPath, "id"), $"{optionName} in {owner}", errors) && !ids.Add(option.Id))
            {
                errors.Add(new(Join(optionPath, "id"), $"The id '{option.Id}' of the option {optionName} in {owner} is already used by another option. Use a different id."));
            }

            ValidateText(option.Label, Join(optionPath, "label"), 200, required: true, $"The text of option {i + 1} in {owner}", errors);
        }
    }

    private static void ValidateAutomations(
        IReadOnlyList<Automation>? automations, string path, WorkflowParts parts, List<DefinitionError> errors)
    {
        var (stages, questions, _) = parts;
        if (automations is null)
        {
            errors.Add(new(path, "The automations list is missing. Use an empty list for none."));
            return;
        }

        if (automations.Count > MaxAutomations)
        {
            errors.Add(new(path, $"There are too many automations. Keep it to {MaxAutomations} or fewer."));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < automations.Count; i++)
        {
            var automationPath = $"{path}[{i}]";
            var automation = automations[i];
            if (automation is null)
            {
                errors.Add(new(automationPath, "An automation is missing. Remove the empty one."));
                continue;
            }

            var quoted = Quote(automation.Name, "this automation");
            var named = Ref("automation", automation.Name);
            if (ValidateId(automation.Id, Join(automationPath, "id"), quoted, errors) && !ids.Add(automation.Id))
            {
                errors.Add(new(Join(automationPath, "id"), $"The id '{automation.Id}' of {quoted} is already used by another automation. Use a different id."));
            }

            ValidateText(automation.Name, Join(automationPath, "name"), 120, required: true, $"The name of automation {i + 1}", errors);
            ValidateTrigger(automation.Trigger, Join(automationPath, "trigger"), quoted, named, parts, errors);
            ValidateConditions(automation.Conditions, Join(automationPath, "conditions"), quoted, named, automation.Trigger?.Type, questions, errors);
            ValidateActions(automation.Actions, Join(automationPath, "actions"), quoted, named, parts, errors);
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

    // A verification signal drives the automation directly: its trigger or a fact it reads.
    private static bool DrivenBySignal(Automation automation) =>
        automation.Trigger?.Type is TriggerType.VerificationAssessed or TriggerType.VerificationCompleted
        || automation.Conditions?.Rules?.Any(rule => rule is not null && FactSchema.IsSignal(rule.Fact)) == true;

    /// <summary>
    /// The automations something can drive, directly or at one remove. For verification signals, directly
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

    // quoted is the automation's name in quotes (or "this automation"), for "in X"; named is "the automation \"X\"", for
    // the start of a sentence.
    private static void ValidateTrigger(
        AutomationTrigger? trigger, string path, string quoted, string named, WorkflowParts parts, List<DefinitionError> errors)
    {
        var stages = parts.Stages;
        if (trigger is null)
        {
            errors.Add(new(path, $"{Cap(named)} has no trigger. Choose what starts it."));
            return;
        }

        if (!Enum.IsDefined(trigger.Type))
        {
            errors.Add(new(Join(path, "type"), $"{Cap(named)} has a trigger that is not recognised. Choose another trigger."));
            return;
        }

        var triggerContext = $"the trigger of {quoted}";
        var triggerName = TriggerName(trigger.Type);
        if (UnavailableTriggers.TryGetValue(trigger.Type, out var unavailable))
        {
            errors.Add(new(Join(path, "type"), $"{unavailable} Choose another trigger for {named}."));
        }

        var needsStage = trigger.Type is TriggerType.StageEntered or TriggerType.StageExited or TriggerType.TimeElapsed;
        if (needsStage)
        {
            ValidateStageReference(trigger.StageId, Join(path, "stageId"), stages, triggerContext, errors);
        }
        else if (trigger.Type == TriggerType.ReviewerDecision && trigger.StageId is not null)
        {
            // Optional: a decision on any stage's scorecard, or only on this one's.
            ValidateStageReference(trigger.StageId, Join(path, "stageId"), stages, triggerContext, errors);
            if (stages.GetValueOrDefault(trigger.StageId) is { } stage && !stage.HasScorecard())
            {
                errors.Add(new(Join(path, "stageId"),
                    $"{Cap(Quote(stage.Name, "this stage"))} has no scorecard, so no reviewer decision is recorded there. Choose a stage with a scorecard, or leave the stage empty."));
            }
        }
        else if (trigger.Type == TriggerType.SlaBreached)
        {
            // Optional: any stage's time limit, or only this one's.
            if (trigger.StageId is not null)
            {
                ValidateStageReference(trigger.StageId, Join(path, "stageId"), stages, triggerContext, errors);
                if (stages.GetValueOrDefault(trigger.StageId) is { } stage && !stage.HasSla())
                {
                    var stageName = Cap(Quote(stage.Name, "this stage"));
                    errors.Add(new(Join(path, "stageId"), Enum.IsDefined(stage.Kind) && stage.Kind.IsTerminal()
                        ? $"{stageName} ends the application, so its time limit never passes. Choose another stage."
                        : $"{stageName} has no time limit, so it is never overdue. Set its time limit in days, or choose another stage."));
                }
            }
            else if (stages.Count > 0 && !stages.Values.Any(s => s.HasSla()))
            {
                errors.Add(new(Join(path, "type"), $"No stage has a time limit, so {named} never starts. Set a time limit in days on a stage."));
            }
        }
        else if (trigger.StageId is not null)
        {
            errors.Add(new(Join(path, "stageId"), $"The \"{triggerName}\" trigger in {quoted} does not use a stage. Remove the stage."));
        }

        if (trigger.Type == TriggerType.QuestionnaireCompleted)
        {
            // Optional, as a ReviewerDecision's stage is: without one it fires on any follow-up questionnaire, which keeps
            // definitions saved before follow-ups existed valid.
            if (trigger.QuestionnaireId is not null)
            {
                ValidateFollowUpReference(trigger.QuestionnaireId, Join(path, "questionnaireId"), parts.FollowUps, triggerContext, errors);
            }
        }
        else if (trigger.QuestionnaireId is not null)
        {
            errors.Add(new(Join(path, "questionnaireId"), $"The \"{triggerName}\" trigger in {quoted} does not use a form. Remove it."));
        }

        if (trigger.Type == TriggerType.TimeElapsed)
        {
            if (trigger.AfterHours is not (>= 1 and <= 8760))
            {
                errors.Add(new(Join(path, "afterHours"), $"The wait in {quoted} must be between 1 and 8760 hours. Change the number of hours."));
            }
        }
        else if (trigger.AfterHours is not null)
        {
            errors.Add(new(Join(path, "afterHours"), $"The \"{triggerName}\" trigger in {quoted} does not wait, so it has no hours. Remove the hours."));
        }
    }

    private static void ValidateConditions(
        AutomationConditions? conditions, string path, string quoted, string named, TriggerType? trigger,
        Dictionary<string, Question> questions, List<DefinitionError> errors)
    {
        if (conditions is null)
        {
            return;
        }

        if (!Enum.IsDefined(conditions.Match))
        {
            errors.Add(new(Join(path, "match"), $"The conditions of {quoted} must match all rules or any rule. Choose one."));
        }

        if (conditions.Rules is not { Count: > 0 and <= MaxRules } rules)
        {
            errors.Add(new(Join(path, "rules"),
                $"The conditions of {quoted} need between 1 and {MaxRules} rules. Add one, or remove the conditions so it always runs."));
            return;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rulePath = $"{path}.rules[{i}]";
            if (rules[i] is not { } rule)
            {
                errors.Add(new(rulePath, $"A condition in {quoted} is empty. Remove it."));
                continue;
            }

            var condition = $"condition {i + 1} of {quoted}";
            if (FactSchema.IsRating(rule.Fact))
            {
                // Not an unknown fact: a refused one. The candidate's rating informs people and is never a condition.
                errors.Add(new(Join(rulePath, "fact"),
                    $"{Cap(condition)} uses a rating. A rating helps people decide, so no automation acts on it or on a scorecard's answers (ADR-0013). " +
                    "To act on a reviewer's recommendation, start the automation on a reviewer decision and test the reviewer's recommendation."));
                continue;
            }

            var fact = FactSchema.Find(rule.Fact, questions);
            if (fact is null)
            {
                errors.Add(new(Join(rulePath, "fact"), rule.Fact is null
                    ? $"{Cap(condition)} has nothing to test. Choose what it tests."
                    : rule.Fact.StartsWith(AnswersPrefix, StringComparison.Ordinal)
                        ? $"{Cap(condition)} tests an answer to a question that does not exist ('{rule.Fact[AnswersPrefix.Length..]}'). Choose another question."
                        : $"{Cap(condition)} tests something that is not available ('{rule.Fact}'). Choose another."));
            }
            else if (fact.Category == FactCategory.Reviewer && trigger != TriggerType.ReviewerDecision)
            {
                errors.Add(new(Join(rulePath, "fact"),
                    $"{Cap(named)} cannot test a reviewer's recommendation, because only an automation that starts on a reviewer decision has one. Change the trigger, or remove {condition}."));
            }
            else if (trigger == TriggerType.SlaBreached && FactSchema.IsTimingOrTelemetry(fact.Name))
            {
                // The engine skips an automation with a timing condition for an application with an accommodation, and a
                // skipped reminder would leave that candidate waiting with nobody told (workflow-engine.md section 8).
                errors.Add(new(Join(rulePath, "fact"),
                    $"{Cap(named)} starts when a time limit passes, which already says how long the candidate waited. A condition on time would skip the " +
                    $"reminder for a candidate with an accommodation, who would then wait with nobody told. Remove {condition}."));
            }
            else if (Enum.IsDefined(rule.Operator) && !FactSchema.OperatorsFor(fact.Type).Contains(rule.Operator))
            {
                errors.Add(new(Join(rulePath, "operator"),
                    $"{Cap(condition)} cannot test {FactLabel(fact, questions)} with \"{OperatorLabel(rule.Operator)}\". " +
                    $"Choose one of: {string.Join(", ", FactSchema.OperatorsFor(fact.Type).Order().Select(OperatorLabel))}."));
            }
            else
            {
                ValidateOperands(rule, fact, rulePath, condition, questions, errors);
            }

            switch (rule.Operator)
            {
                case ConditionOperator.Exists:
                    if (rule.Value is not null || rule.Values is not null)
                    {
                        errors.Add(new(rulePath, $"{Cap(condition)} only checks that something is filled in, so it takes no value. Remove the value."));
                    }

                    break;
                case ConditionOperator.In:
                    if (rule.Value is not null || rule.Values is not { Count: > 0 and <= 50 } || rule.Values.Any(v => v is null || v.Length > 200))
                    {
                        errors.Add(new(Join(rulePath, "values"),
                            $"{Cap(condition)} needs between 1 and 50 values to choose from, each up to 200 characters. Fix the list."));
                    }

                    break;
                case var known when Enum.IsDefined(known):
                    if (rule.Values is not null || string.IsNullOrEmpty(rule.Value) || rule.Value.Length > 200)
                    {
                        errors.Add(new(Join(rulePath, "value"), $"{Cap(condition)} needs one value of up to 200 characters. Type a single value."));
                    }

                    break;
                default:
                    errors.Add(new(Join(rulePath, "operator"), $"{Cap(condition)} has a comparison that is not recognised. Choose another."));
                    break;
            }
        }
    }

    private static void ValidateActions(
        IReadOnlyList<AutomationAction>? actions, string path, string quoted, string named, WorkflowParts parts, List<DefinitionError> errors)
    {
        if (actions is null || actions.Count == 0)
        {
            errors.Add(new(path, $"{Cap(named)} has no steps. Add at least one."));
            return;
        }

        if (actions.Count > MaxActions)
        {
            errors.Add(new(path, $"{Cap(named)} has too many steps. Keep it to {MaxActions} or fewer."));
            return;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var actionPath = $"{path}[{i}]";
            if (actions[i] is not { } action)
            {
                errors.Add(new(actionPath, $"Step {i + 1} in {quoted} is empty. Remove it."));
                continue;
            }

            if (!Enum.IsDefined(action.Type))
            {
                errors.Add(new(Join(actionPath, "type"), $"Step {i + 1} in {quoted} is not a step the product knows. Choose another step."));
                continue;
            }

            var step = new Step(quoted, i + 1, actions.Count, action.Type);
            if (UnavailableActions.TryGetValue(action.Type, out var unavailable))
            {
                errors.Add(new(Join(actionPath, "type"), $"{unavailable} Remove {step.Phrase}."));
            }

            ValidateActionFields(action, actionPath, step, parts, errors);
        }
    }

    // Each action type's required and optional fields; anything else set on it is an error, so a definition never
    // carries a value the engine would silently ignore.
    private static void ValidateActionFields(AutomationAction action, string path, Step step, WorkflowParts parts, List<DefinitionError> errors)
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
            ActionType.ScheduleInterview => (ActionField.InterviewerUserIds | ActionField.InterviewMinutes | ActionField.InterviewWindowDays | ActionField.InterviewTimeZone, ActionField.None),
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
        present |= action.InterviewerUserIds is not null ? ActionField.InterviewerUserIds : ActionField.None;
        present |= action.InterviewMinutes is not null ? ActionField.InterviewMinutes : ActionField.None;
        present |= action.InterviewWindowDays is not null ? ActionField.InterviewWindowDays : ActionField.None;
        present |= action.InterviewTimeZone is not null ? ActionField.InterviewTimeZone : ActionField.None;

        foreach (var field in Enum.GetValues<ActionField>().Where(f => f != ActionField.None))
        {
            var name = JsonName(field);
            var (need, plain, fix) = FieldWords(field);
            if (required.HasFlag(field) && !present.HasFlag(field))
            {
                errors.Add(new(Join(path, name), field == ActionField.Tag
                    ? $"The tag in {step.Where} has no name. Type one, or remove that step."
                    : $"{Cap(step.Phrase)} needs {need}. {fix}, or remove that step."));
            }
            else if (present.HasFlag(field) && !required.HasFlag(field) && !optional.HasFlag(field))
            {
                errors.Add(new(Join(path, name), $"The {plain} is not used by {step.Phrase}. Remove it."));
            }
        }

        if (action.StageId is not null && action.Type == ActionType.MoveToStage)
        {
            ValidateStageReference(action.StageId, Join(path, "stageId"), stages, step.Phrase, errors);
        }

        if (action.QuestionnaireId is not null && action.Type == ActionType.SendQuestionnaire)
        {
            ValidateFollowUpReference(action.QuestionnaireId, Join(path, "questionnaireId"), parts.FollowUps, step.Phrase, errors);
        }

        if (action.EmailTemplate is not null && !EmailTemplateIds.IsWellFormed(action.EmailTemplate))
        {
            errors.Add(new(Join(path, "emailTemplate"), $"{Cap(step.Phrase)} uses an email template that does not exist. Choose one from the list."));
        }
        else if (action.Type == ActionType.SendEmail && action.EmailTemplate == EmailTemplateIds.ApplicationRejected)
        {
            // The rejection email tells a candidate they are out, so it goes only with a Reject action, which the
            // knockout and opt-in rules guard (workflow-engine.md §2, guardrail 2).
            errors.Add(new(Join(path, "emailTemplate"),
                $"{Cap(step.Phrase)} cannot send the rejection email. Only a \"{StepName(ActionType.Reject)}\" step sends it. Use \"{StepName(ActionType.QueueForRejectionReview)}\" so a person decides."));
        }
        else if (action.Type == ActionType.SendEmail && action.EmailTemplate == EmailTemplateIds.QuestionnaireRequest)
        {
            // Its link is made for one questionnaire request, which only a SendQuestionnaire action creates.
            errors.Add(new(Join(path, "emailTemplate"),
                $"{Cap(step.Phrase)} cannot send the follow-up questions email. Only a \"{StepName(ActionType.SendQuestionnaire)}\" step sends it, with its link."));
        }
        else if (action.EmailTemplate == EmailTemplateIds.TalentPoolInvitation)
        {
            // Its links are for one candidate and one job, which only a person's Invite to apply chooses.
            errors.Add(new(Join(path, "emailTemplate"),
                $"{Cap(step.Phrase)} cannot send the invitation to apply. A person sends it from saved candidates."));
        }
        else if (action.EmailTemplate is not null && EmailTemplateIds.IsInterview(action.EmailTemplate))
        {
            // Their links are for one interview request, which only scheduling an interview creates.
            errors.Add(new(Join(path, "emailTemplate"),
                $"{Cap(step.Phrase)} cannot send an interview email. Those go out when an interview is offered, with their links."));
        }
        else if (action.Type == ActionType.Reject && action.EmailTemplate is not null && action.EmailTemplate != EmailTemplateIds.ApplicationRejected)
        {
            errors.Add(new(Join(path, "emailTemplate"),
                $"{Cap(step.Phrase)} always sends the rejection email, with its reason. Remove the email template."));
        }

        ValidateActionText(action.Tag, Join(path, "tag"), 50, ActionField.Tag, step, errors);
        ValidateActionText(action.Reason, Join(path, "reason"), 500, ActionField.Reason, step, errors);
        ValidateActionText(action.Note, Join(path, "note"), 1000, ActionField.Note, step, errors);
        ValidateActionText(action.VerificationProvider, Join(path, "verificationProvider"), 50, ActionField.VerificationProvider, step, errors);
        if (action.Type == ActionType.ScheduleInterview)
        {
            ValidateInterviewFields(action, path, step, errors);
        }

        if (action.Url is not null && !(Uri.TryCreate(action.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && action.Url.Length <= 2000))
        {
            errors.Add(new(Join(path, "url"), $"The web address of {step.Phrase} must be a full address that starts with https://. Fix it."));
        }
    }

    // A text member that is present but blank, or longer than the engine stores. Whether it is required is the field
    // check's job in ValidateActionFields.
    private static void ValidateActionText(string? text, string path, int maxLength, ActionField field, Step step, List<DefinitionError> errors)
    {
        var (_, plain, fix) = FieldWords(field);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (text is not null)
            {
                errors.Add(new(path, field == ActionField.Tag
                    ? $"The tag in {step.Where} has no name. Type one, or remove that step."
                    : $"The {plain} of {step.Phrase} is blank. {fix}, or remove that step."));
            }
        }
        else if (text.Length > maxLength)
        {
            errors.Add(new(path, $"The {plain} of {step.Phrase} is too long. Keep it to {maxLength.ToString(CultureInfo.InvariantCulture)} characters or fewer."));
        }
    }

    // The shape of a ScheduleInterview action. That each interviewer is a member is checked where there is a database
    // (the API at save, the worker at run time); this class has none.
    private static void ValidateInterviewFields(AutomationAction action, string path, Step step, List<DefinitionError> errors)
    {
        if (action.InterviewerUserIds is { } interviewers
            && (interviewers.Count is < 1 or > MaxInterviewers || interviewers.Distinct().Count() != interviewers.Count || interviewers.Contains(Guid.Empty)))
        {
            errors.Add(new(Join(path, "interviewerUserIds"),
                $"{Cap(step.Phrase)} needs between 1 and {MaxInterviewers} different interviewers. Choose who should interview."));
        }

        if (action.InterviewMinutes is { } minutes && !InterviewRequest.AllowedDurations.Contains(minutes))
        {
            errors.Add(new(Join(path, "interviewMinutes"),
                $"The interview length of {step.Phrase} must be {string.Join(", ", InterviewRequest.AllowedDurations)} minutes. Choose one."));
        }

        if (action.InterviewWindowDays is { } days && (days < 1 || days > MaxInterviewWindowDays))
        {
            errors.Add(new(Join(path, "interviewWindowDays"),
                $"The days the candidate gets to pick a time in {step.Phrase} must be between 1 and {MaxInterviewWindowDays}. Change the number."));
        }

        if (action.InterviewTimeZone is not null && !InterviewRequest.TryFindZone(action.InterviewTimeZone, out _))
        {
            errors.Add(new(Join(path, "interviewTimeZone"), $"The time zone of {step.Phrase} is not recognised. Choose one from the list."));
        }
    }

    /// <summary>
    /// The non-negotiables (ADR-0004, workflow-engine.md §2): an automation driven by a verification signal
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

        var quoted = Quote(automation.Name, "this automation");
        var named = Ref("automation", automation.Name);
        var rules = automation.Conditions?.Rules?.Where(r => r is not null).ToList() ?? [];

        for (var i = 0; i < automation.Actions.Count; i++)
        {
            if (automation.Actions[i] is not { } action)
            {
                continue;
            }

            var actionPath = $"{path}.actions[{i}]";
            var step = new Step(quoted, i + 1, automation.Actions.Count, action.Type);
            if (action is { Type: ActionType.SendEmail, EmailTemplate: EmailTemplateIds.StageChange }
                && automation.Trigger is { Type: TriggerType.StageEntered, StageId: { } enteredId }
                && stages.GetValueOrDefault(enteredId)?.Kind is StageKind.Rejected or StageKind.Withdrawn)
            {
                // The worker skips it anyway (the email says "the next step"); refusing it here says why at save.
                errors.Add(new(Join(actionPath, "emailTemplate"),
                    $"{Cap(step.Phrase)} would tell the candidate they moved to the next step, so it is not sent when an application enters a rejected or withdrawn stage. Remove that step."));
                continue;
            }

            if (automation.Trigger?.Type == TriggerType.SlaBreached && !SlaBreachActions.Contains(action.Type))
            {
                errors.Add(new(actionPath,
                    $"{Cap(step.Phrase)} is not allowed after a time limit passes. A time limit measures the hiring team's delay, not the candidate, so {named} may only " +
                    $"tell the team, ask a teammate to review, add a task or add a tag, and nothing it does may count against the candidate. Remove that step."));
                continue;
            }

            var target = action.StageId is null ? null : stages.GetValueOrDefault(action.StageId);
            if (signalDriven && (!SignalSafeActions.Contains(action.Type)
                || (action.Type == ActionType.MoveToStage && target is not null && !SignalSafeStages.Contains(target.Kind))))
            {
                errors.Add(new(actionPath,
                    $"{Cap(step.Phrase)} is not allowed, because {named} acts on a check result, directly or through a tag or stage that another such automation sets. " +
                    "It may only ask for a check, ask a teammate to review, add a tag or move to a screening, assessment, verification or interview stage. " +
                    "A check can only ask for a closer look and never drives a rejection. A person decides."));
                continue;
            }

            // At one remove from a breach (a tag it adds, a stage reached through one), the same list as the breach itself,
            // plus a move into a review stage as a signal may make. Not Custom, not an email: either could be a rejection
            // under another name, and the team's delay never counts against the candidate.
            if (delayDriven && automation.Trigger?.Type != TriggerType.SlaBreached && !SlaBreachActions.Contains(action.Type)
                && !(action.Type == ActionType.MoveToStage && target is not null && SignalSafeStages.Contains(target.Kind)))
            {
                errors.Add(new(actionPath,
                    $"{Cap(step.Phrase)} is not allowed, because {named} acts on a tag or stage that a passed time limit sets. A time limit measures the hiring " +
                    "team's delay and never counts against the candidate, so it may only tell the team, ask a teammate to review, add a task, " +
                    "add a tag or move to a screening, assessment, verification or interview stage."));
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
                    $"{Cap(step.Phrase)} rejects candidates, which only a knockout automation may do: every condition must be a knockout question " +
                    $"answered with its disqualifying answer. Use \"{StepName(ActionType.QueueForRejectionReview)}\" so a person decides."));
            }
            else if (!automation.AllowAutoReject)
            {
                errors.Add(new(Join(path, "allowAutoReject"),
                    $"{Cap(named)} would reject candidates without a person, which needs an explicit opt-in. " +
                    $"Turn on automatic rejection for it, or use \"{StepName(ActionType.QueueForRejectionReview)}\" so a person decides."));
            }
            else if (action.Type == ActionType.MoveToStage)
            {
                errors.Add(new(actionPath,
                    $"{Cap(step.Phrase)} moves candidates to a rejected or withdrawn stage. Use a \"{StepName(ActionType.Reject)}\" step with a reason the candidate sees instead."));
            }
        }
    }

    private static void ValidatePostingDefaults(PostingDefaults? defaults, string path, List<DefinitionError> errors)
    {
        if (defaults is null)
        {
            errors.Add(new(path, "The job posting defaults are missing. Use an empty set for none."));
            return;
        }

        if (defaults.EmploymentType is { } employmentType && !Enum.IsDefined(employmentType))
        {
            errors.Add(new(Join(path, "employmentType"), "The default employment type is not recognised. Choose another."));
        }

        if (defaults.WorkplaceType is { } workplaceType && !Enum.IsDefined(workplaceType))
        {
            errors.Add(new(Join(path, "workplaceType"), "The default workplace type is not recognised. Choose another."));
        }

        if (defaults.SalaryPeriod is { } period && !Enum.IsDefined(period))
        {
            errors.Add(new(Join(path, "salaryPeriod"), "The default salary period is not recognised. Choose another."));
        }

        ValidateText(defaults.Department, Join(path, "department"), 100, required: false, "The default department", errors);
        if (defaults.SalaryCurrency is not null && !CurrencyCode().IsMatch(defaults.SalaryCurrency))
        {
            errors.Add(new(Join(path, "salaryCurrency"), "The default currency must be a three-letter code such as GBP. Fix it."));
        }

        if (defaults.ValidForDays is < 1 or > 365)
        {
            errors.Add(new(Join(path, "validForDays"), "The default posting length must be between 1 and 365 days. Change the number."));
        }

        ValidateText(defaults.Description, Join(path, "description"), 50_000, required: false, "The default job description", errors);
    }

    // The operands a typed fact can hold: yes or no for a yes/no answer, option ids for a choice answer, a number for a
    // number fact. Anything else could never match, which is a silent bug in a saved rule.
    private static void ValidateOperands(
        AutomationCondition rule, FactDefinition fact, string path, string condition, Dictionary<string, Question> questions, List<DefinitionError> errors)
    {
        var operands = rule.Operator == ConditionOperator.In ? rule.Values ?? [] : rule.Value is null ? [] : [rule.Value];
        var operandPath = Join(path, rule.Operator == ConditionOperator.In ? "values" : "value");
        switch (fact.Type)
        {
            case FactType.YesNo when operands.Any(o => o is not ("yes" or "no")):
                errors.Add(new(operandPath, $"{Cap(condition)} tests a yes or no answer, so its value must be \"yes\" or \"no\"."));
                break;
            case FactType.Number when operands.Any(o => !FactSchema.TryParseNumber(o, out _)):
                errors.Add(new(operandPath, $"{Cap(condition)} tests a number, so its value must be a number such as 48."));
                break;
            case FactType.Recommendation when operands.FirstOrDefault(o => o is null || !Enum.GetNames<Recommendation>().Contains(o, StringComparer.Ordinal)) is { } notRecommendation:
                errors.Add(new(operandPath, $"{Cap(condition)} compares with '{notRecommendation}', which is not a recommendation. Use one of: {string.Join(", ", Enum.GetNames<Recommendation>())}."));
                break;
            case FactType.Choice or FactType.MultiChoice:
                var options = questions[fact.Name[AnswersPrefix.Length..]].Options?.Where(o => o is not null).Select(o => o.Id).ToHashSet(StringComparer.Ordinal) ?? [];
                if (operands.FirstOrDefault(o => o is not null && !options.Contains(o)) is { } unknown)
                {
                    errors.Add(new(operandPath, $"{Cap(condition)} compares an answer with '{unknown}', which is not one of the question's options. Choose one of its options."));
                }

                break;
        }
    }

    // answers.{id} EqualTo the knockout's disqualifying answer ("yes" or "no"), not negated: anything else could reject
    // an eligible candidate.
    private static bool IsKnockoutRule(AutomationCondition rule, Dictionary<string, Question> questions) =>
        rule.Fact is { } fact
        && fact.StartsWith(AnswersPrefix, StringComparison.Ordinal)
        && questions.TryGetValue(fact[AnswersPrefix.Length..], out var question)
        && question.Knockout is { } knockout
        && rule is { Operator: ConditionOperator.EqualTo, Negate: false }
        && rule.Value == (knockout.DisqualifyingAnswer ? "yes" : "no");

    // context is a phrase that says where the reference is, such as "the trigger of \"Chase\"".
    private static void ValidateFollowUpReference(
        string questionnaireId, string path, Dictionary<string, FollowUpQuestionnaire> followUps, string context, List<DefinitionError> errors)
    {
        if (!followUps.ContainsKey(questionnaireId))
        {
            errors.Add(new(path, $"{Cap(context)} points to a form that does not exist. Choose another."));
        }
    }

    private static void ValidateStageReference(
        string? stageId, string path, Dictionary<string, WorkflowStage> stages, string context, List<DefinitionError> errors)
    {
        if (stageId is null)
        {
            errors.Add(new(path, $"{Cap(context)} needs a stage. Choose one."));
        }
        else if (!stages.ContainsKey(stageId))
        {
            errors.Add(new(path, $"{Cap(context)} points to a stage that does not exist. Choose another stage."));
        }
    }

    // subject is the thing the id belongs to, already quoted: "this stage", or "\"Screening\"".
    private static bool ValidateId(string? id, string path, string subject, List<DefinitionError> errors)
    {
        if (id is not null && Identifier().IsMatch(id))
        {
            return true;
        }

        errors.Add(new(path, $"The id of {subject} must be 1 to 40 characters: lowercase letters, digits, '-' or '_', starting with a letter. Change it."));
        return false;
    }

    // what is the start of a sentence naming the text ("The name of stage 2"), so the message can say which one.
    private static void ValidateText(string? text, string path, int maxLength, bool required, string what, List<DefinitionError> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required || text is not null)
            {
                errors.Add(new(path, required ? $"{what} is blank. Fill it in." : $"{what} is blank. Fill it in, or remove it."));
            }
        }
        else if (text.Length > maxLength)
        {
            errors.Add(new(path, $"{what} is too long. Keep it to {maxLength.ToString(CultureInfo.InvariantCulture)} characters or fewer."));
        }
    }

    private static string Join(string path, string member) => path.Length == 0 ? member : $"{path}.{member}";

    // A name in straight quotes, or the plain noun the caller gives when the person left it blank. Names are cut at 50
    // characters so one long name cannot bury the fix.
    private static string Quote(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var trimmed = name.Trim();
        return trimmed.Length <= 50 ? $"\"{trimmed}\"" : $"\"{trimmed[..47]}...\"";
    }

    // "the stage \"Screening\"", or "this stage" without a name.
    private static string Ref(string noun, string? name) =>
        string.IsNullOrWhiteSpace(name) ? $"this {noun}" : $"the {noun} {Quote(name, "")}";

    // "the question \"Why this job?\" in the application questionnaire", or "question 2 in ..." without a label.
    private static string Described(string? label, int number, string owner) =>
        string.IsNullOrWhiteSpace(label) ? $"question {number} in {owner}" : $"the question {Quote(label, "")} in {owner}";

    private static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // What the dashboard calls each step, so a message says "Add a tag" and not AddTag.
    private static string StepName(ActionType type) => type switch
    {
        ActionType.SendEmail => "Send an email",
        ActionType.SendQuestionnaire => "Send a form",
        ActionType.RequestVerification => "Ask for a check",
        ActionType.MoveToStage => "Move to a stage",
        ActionType.AddTag => "Add a tag",
        ActionType.AssignReviewer => "Ask a teammate to review",
        ActionType.NotifyTeam => "Tell the team",
        ActionType.ScheduleInterview => "Offer an interview",
        ActionType.CreateTask => "Add a task",
        ActionType.CallWebhook => "Call a web address",
        ActionType.QueueForRejectionReview => "Queue for rejection review",
        ActionType.Reject => "Reject",
        _ => "Step",
    };

    private static string TriggerName(TriggerType type) => type switch
    {
        TriggerType.ApplicationSubmitted => "Application received",
        TriggerType.StageEntered => "Moved into a stage",
        TriggerType.StageExited => "Moved out of a stage",
        TriggerType.QuestionnaireCompleted => "Form completed",
        TriggerType.VerificationAssessed => "Check result ready",
        TriggerType.VerificationCompleted => "Check finished",
        TriggerType.ReviewerDecision => "Reviewer decision",
        TriggerType.TimeElapsed => "Time passed",
        TriggerType.CandidateReplied => "Candidate replied",
        TriggerType.InterviewScheduled => "Interview scheduled",
        TriggerType.Manual => "Started by hand",
        TriggerType.SlaBreached => "Time limit passed",
        _ => "Trigger",
    };

    private static string OperatorLabel(ConditionOperator op) => op switch
    {
        ConditionOperator.EqualTo => "is",
        ConditionOperator.In => "is one of",
        ConditionOperator.Contains => "contains",
        ConditionOperator.GreaterThan => "is more than",
        ConditionOperator.GreaterThanOrEqual => "is at least",
        ConditionOperator.LessThan => "is less than",
        ConditionOperator.LessThanOrEqual => "is at most",
        ConditionOperator.Exists => "is filled in",
        _ => op.ToString(),
    };

    private static string FactLabel(FactDefinition fact, Dictionary<string, Question> questions)
    {
        if (fact.Category == FactCategory.Answer && questions.TryGetValue(fact.Name[AnswersPrefix.Length..], out var question))
        {
            return $"the answer to {Quote(question.Label, "that question")}";
        }

        return fact.Name switch
        {
            FactSchema.Tags => "the tags",
            FactSchema.Source => "the source",
            FactSchema.HoursInStage => "the hours in the stage",
            FactSchema.VerificationAuthenticity => "the authenticity check",
            FactSchema.VerificationAiAssistance => "the AI-assistance check",
            FactSchema.VerificationResult => "the check result",
            FactSchema.ReviewerRecommendation => "the reviewer's recommendation",
            _ => fact.Name,
        };
    }

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
        ActionField.InterviewerUserIds => "interviewerUserIds",
        ActionField.InterviewMinutes => "interviewMinutes",
        ActionField.InterviewWindowDays => "interviewWindowDays",
        ActionField.InterviewTimeZone => "interviewTimeZone",
        _ => field.ToString(),
    };

    // What a step field is called on screen: what a step "needs" (with its article), the bare name, and the fix.
    private static (string Need, string Plain, string Fix) FieldWords(ActionField field) => field switch
    {
        ActionField.StageId => ("a stage", "stage", "Choose one"),
        ActionField.EmailTemplate => ("an email template", "email template", "Choose one"),
        ActionField.Tag => ("a tag name", "tag name", "Type one"),
        ActionField.Reason => ("a reason", "reason", "Type one"),
        ActionField.Url => ("a web address", "web address", "Type one"),
        ActionField.Note => ("a note", "note", "Type one"),
        ActionField.AssigneeUserId => ("a teammate", "teammate", "Choose one"),
        ActionField.VerificationProvider => ("a check provider", "check provider", "Choose one"),
        ActionField.QuestionnaireId => ("a form", "form", "Choose one"),
        ActionField.InterviewerUserIds => ("at least one interviewer", "interviewer list", "Choose at least one"),
        ActionField.InterviewMinutes => ("an interview length", "interview length", "Choose one"),
        ActionField.InterviewWindowDays => ("a number of days for the candidate to pick a time", "number of days to pick a time", "Type a number"),
        ActionField.InterviewTimeZone => ("a time zone", "time zone", "Choose one"),
        _ => (field.ToString(), field.ToString(), "Fill it in"),
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
        InterviewerUserIds = 512,
        InterviewMinutes = 1024,
        InterviewWindowDays = 2048,
        InterviewTimeZone = 4096,
    }

    /// <summary>
    /// One step of an automation, for the words in its messages. <paramref name="Automation"/> is the automation's name
    /// already in quotes. With one step the message says "the "Add a tag" step"; with several it adds the number, so
    /// "remove that step" is never ambiguous.
    /// </summary>
    private sealed record Step(string Automation, int Number, int Count, ActionType Type)
    {
        public string Phrase => Count > 1
            ? $"step {Number} (\"{StepName(Type)}\") in {Automation}"
            : $"the \"{StepName(Type)}\" step in {Automation}";

        public string Where => Count > 1 ? $"step {Number} of {Automation}" : Automation;
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
