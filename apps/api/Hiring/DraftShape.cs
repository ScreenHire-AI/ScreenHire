using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The least a draft must have to be stored: every part the rest of the product reads without a null check is there.
/// A draft with any other problem still saves (docs/architecture/workflow-engine.md section 4), and the validator's problems
/// come back with it; only testing and publishing wait for them. The request body reaches the API through a serializer
/// that does not enforce nullability, so this is what keeps a stored draft readable.
/// </summary>
internal static class DraftShape
{
    /// <summary>What is missing from a template definition, under the validator's path names, or nothing.</summary>
    public static IReadOnlyList<DefinitionError> Problems(HiringTemplateDefinition? definition)
    {
        if (definition is null)
        {
            return [new DefinitionError("", "Send a definition with a workflow and its posting defaults.")];
        }

        List<DefinitionError> missing = [.. Problems(definition.Workflow, "workflow")];
        if (definition.PostingDefaults is null)
        {
            missing.Add(new DefinitionError("postingDefaults", "Send the posting defaults."));
        }

        return missing;
    }

    /// <summary>What is missing from a workflow, under <paramref name="prefix"/>, or nothing.</summary>
    public static IReadOnlyList<DefinitionError> Problems(HiringWorkflow? workflow, string prefix = "")
    {
        if (workflow is null)
        {
            return [new DefinitionError(prefix, "Send a workflow with its stages, questionnaire and automations.")];
        }

        List<DefinitionError> missing = [];
        void Require(bool present, string path, string message)
        {
            if (!present)
            {
                missing.Add(new DefinitionError(prefix.Length == 0 ? path : $"{prefix}.{path}", message));
            }
        }

        Require(workflow.Stages is not null, "stages", "Send the stages.");
        Require(workflow.Automations is not null, "automations", "Send the automations, an empty list if there are none.");
        Require(workflow.Questionnaire?.Questions is not null, "questionnaire.questions", "Send the questionnaire's questions, an empty list if there are none.");
        Require(workflow.Stages?.All(s => s is not null) ?? true, "stages", "Every stage must be an object.");
        Require(workflow.Automations?.All(a => a?.Actions is not null && a.Trigger is not null) ?? true, "automations", "Every automation needs a trigger and its steps.");
        Require(workflow.FollowUpQuestionnaires?.All(f => f?.Questions is not null) ?? true, "followUpQuestionnaires", "Every follow-up questionnaire needs its questions.");
        Require(workflow.Questionnaire?.Questions?.All(q => q is not null) ?? true, "questionnaire.questions", "Every question must be an object.");

        // Change history matches the parts of two versions by id and words each automation's rules, so a draft without
        // an id, or with a null step or rule, would fail the save (DefinitionChanges). The validator's other checks on
        // ids (their form, duplicates) are problems the draft saves with.
        var stages = workflow.Stages?.Where(s => s is not null) ?? [];
        Require(stages.All(s => s.Id is not null), "stages", "Every stage needs an id.");
        Require(
            stages.All(s => s.Scorecard is null || (s.Scorecard.Questions?.All(q => q?.Id is not null) ?? false)),
            "stages", "Every scorecard needs its questions, each with an id.");
        var automations = workflow.Automations?.Where(a => a is not null) ?? [];
        Require(automations.All(a => a.Id is not null), "automations", "Every automation needs an id.");
        Require(automations.All(a => a.Actions?.All(step => step is not null) ?? true), "automations", "Every step must be an object.");
        Require(
            automations.All(a => a.Conditions is null || (a.Conditions.Rules?.All(rule => rule?.Fact is not null) ?? false)),
            "automations", "Every condition needs its rules, each with a fact.");
        var followUps = workflow.FollowUpQuestionnaires?.Where(f => f is not null) ?? [];
        Require(followUps.All(f => f.Id is not null), "followUpQuestionnaires", "Every follow-up questionnaire needs an id.");
        Require(
            followUps.All(f => f.Questions?.All(q => q is not null) ?? true),
            "followUpQuestionnaires", "Every question must be an object.");
        var questions = (workflow.Questionnaire?.Questions ?? [])
            .Concat(followUps.SelectMany(f => f.Questions ?? []))
            .Where(q => q is not null);
        Require(questions.All(q => q.Id is not null), "questionnaire.questions", "Every question needs an id.");
        var sections = (workflow.Questionnaire?.Sections ?? []).Concat(followUps.SelectMany(f => f.Sections ?? []));
        Require(sections.All(s => s?.Id is not null), "questionnaire.sections", "Every section needs an id.");
        return missing;
    }
}
