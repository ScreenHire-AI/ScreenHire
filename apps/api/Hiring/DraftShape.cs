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
        Require(workflow.VerificationPolicy is not null, "verificationPolicy", "Send the verification policy.");
        Require(workflow.Questionnaire?.Questions is not null, "questionnaire.questions", "Send the questionnaire's questions, an empty list if there are none.");
        Require(workflow.Stages?.All(s => s is not null) ?? true, "stages", "Every stage must be an object.");
        Require(workflow.Automations?.All(a => a?.Actions is not null && a.Trigger is not null) ?? true, "automations", "Every automation needs a trigger and its steps.");
        Require(workflow.FollowUpQuestionnaires?.All(f => f?.Questions is not null) ?? true, "followUpQuestionnaires", "Every follow-up questionnaire needs its questions.");
        Require(workflow.Questionnaire?.Questions?.All(q => q is not null) ?? true, "questionnaire.questions", "Every question must be an object.");
        return missing;
    }
}
