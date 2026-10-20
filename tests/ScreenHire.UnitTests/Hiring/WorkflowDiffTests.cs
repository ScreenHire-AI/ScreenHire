using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

public sealed class WorkflowDiffTests
{
    private static readonly HiringWorkflow Base = HiringWorkflow.Default;

    [Fact]
    public void Identical_workflows_have_no_changes()
    {
        WorkflowDiff.Compare(Base, Base with { Stages = [.. Base.Stages] }).Should().BeEmpty();
    }

    [Fact]
    public void A_renamed_stage_is_one_change_found_by_its_id()
    {
        var after = Base with { Stages = [.. Base.Stages.Select(s => s.Id == "screening" ? s with { Name = "Phone screen" } : s)] };

        WorkflowDiff.Compare(Base, after).Should().Equal(
            new WorkflowChange("stages[screening].name", WorkflowChangeKind.Changed, "Screening", "Phone screen"));
    }

    [Fact]
    public void Added_and_removed_stages_and_optional_members_are_reported()
    {
        var after = Base with
        {
            Stages =
            [
                .. Base.Stages.Where(s => s.Id != "offer").Select(s => s.Id == "interview" ? s with { SlaDays = 5 } : s),
                new WorkflowStage("reference", "References", StageKind.Custom),
            ],
        };

        var changes = WorkflowDiff.Compare(Base, after);

        changes.Should().BeEquivalentTo(new[]
        {
            new WorkflowChange("stages[interview].slaDays", WorkflowChangeKind.Added, null, "5"),
            new WorkflowChange("stages[offer]", WorkflowChangeKind.Removed, """{"id":"offer","name":"Offer","kind":"Offer"}""", null),
            new WorkflowChange("stages[reference]", WorkflowChangeKind.Added, null, """{"id":"reference","name":"References","kind":"Custom"}"""),
        });
    }

    [Fact]
    public void Reordering_stages_is_a_change_of_its_own()
    {
        var after = Base with { Stages = [Base.Stages[0], Base.Stages[2], Base.Stages[1], .. Base.Stages.Skip(3)] };

        WorkflowDiff.Compare(Base, after).Should().ContainSingle()
            .Which.Should().Be(new WorkflowChange(
                "stages.order", WorkflowChangeKind.Changed,
                "applied, screening, interview, offer, hired, rejected, withdrawn",
                "applied, interview, screening, offer, hired, rejected, withdrawn"));
    }

    [Fact]
    public void The_first_question_or_automation_added_to_an_empty_list_is_found_by_id()
    {
        var after = Base with
        {
            Questionnaire = new Questionnaire([new Question("portfolio", QuestionType.Url, "Portfolio")]),
            IntegrityPolicy = new IntegrityPolicy(AiUsePolicy.NotAllowed),
        };

        WorkflowDiff.Compare(Base, after).Select(c => (c.Path, c.Kind)).Should().BeEquivalentTo(new[]
        {
            ("questionnaire.questions[portfolio]", WorkflowChangeKind.Added),
            ("integrityPolicy.aiUse", WorkflowChangeKind.Changed),
        });
    }
}
