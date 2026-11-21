using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

public sealed class WorkflowTagsTests
{
    private static readonly HiringWorkflow Workflow = HiringWorkflow.Default with
    {
        Automations =
        [
            new("hot", "Hot candidates", new AutomationTrigger(TriggerType.ApplicationSubmitted), null,
                [new AutomationAction(ActionType.AddTag, Tag: "Sr"), new AutomationAction(ActionType.NotifyTeam, Note: "Sr applied")]),
            new("review", "Review seniors", new AutomationTrigger(TriggerType.StageEntered, StageId: "screening"),
                new AutomationConditions(ConditionMatch.Any,
                [
                    new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, Value: "sr"),
                    new AutomationCondition(FactSchema.Tags, ConditionOperator.In, Values: ["Sr", "Senior", "Lead"]),
                    new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, Value: "Se"),
                    new AutomationCondition(FactSchema.Source, ConditionOperator.EqualTo, Value: "Sr"),
                ]),
                [new AutomationAction(ActionType.NotifyTeam, Note: "Look")]),
        ],
    };

    [Fact]
    public void Named_lists_each_tag_an_add_step_or_a_tag_condition_names_once_ignoring_case()
    {
        WorkflowTags.Named(Workflow).Should().Equal("Sr", "Senior", "Lead", "Se");
        WorkflowTags.Names(Workflow, " SENIOR ").Should().BeTrue();
        WorkflowTags.Names(Workflow, "Referral").Should().BeFalse("another fact's value or a note is not a tag");
    }

    [Fact]
    public void Rename_changes_whole_tag_operands_only_and_keeps_one_of_a_merged_pair()
    {
        var renamed = WorkflowTags.Rename(Workflow, "sr", "Senior")!;

        renamed.Automations[0].Actions.Should().Equal(
            new AutomationAction(ActionType.AddTag, Tag: "Senior"), new AutomationAction(ActionType.NotifyTeam, Note: "Sr applied"));
        renamed.Automations[1].Conditions!.Rules.Should().BeEquivalentTo(
            [
                new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, Value: "Senior"),
                new AutomationCondition(FactSchema.Tags, ConditionOperator.In, Values: ["Senior", "Lead"]),
                new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, Value: "Se"),
                new AutomationCondition(FactSchema.Source, ConditionOperator.EqualTo, Value: "Sr"),
            ],
            options => options.WithStrictOrdering());
        renamed.Stages.Should().BeSameAs(Workflow.Stages);
    }

    [Fact]
    public void Rename_of_a_tag_the_workflow_never_names_changes_nothing()
    {
        WorkflowTags.Rename(Workflow, "Referral", "Referred").Should().BeNull();
    }
}
