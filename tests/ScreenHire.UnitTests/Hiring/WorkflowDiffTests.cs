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

    // A job's own follow-up edits show in the diff, so applying a template update never drops them unseen.
    [Fact]
    public void Follow_up_questionnaires_and_their_questions_are_matched_by_id()
    {
        var question = new Question("start_date", QuestionType.ShortText, "When could you start?");
        var before = Base with { FollowUpQuestionnaires = [new FollowUpQuestionnaire("availability", "Availability", [question])] };
        var after = Base with
        {
            FollowUpQuestionnaires = [new FollowUpQuestionnaire("availability", "Start date", [question with { Label = "When can you start?" }])],
        };

        WorkflowDiff.Compare(before, after).Should().BeEquivalentTo(new[]
        {
            new WorkflowChange("followUpQuestionnaires[availability].name", WorkflowChangeKind.Changed, "Availability", "Start date"),
            new WorkflowChange("followUpQuestionnaires[availability].questions[start_date].label", WorkflowChangeKind.Changed, "When could you start?", "When can you start?"),
        });
        WorkflowDiff.Compare(Base, before).Should().ContainSingle().Which.Path.Should().Be("followUpQuestionnaires");
    }

    // A job copied a form before forms named their library form: the template's linked copy asks the same, so no change.
    [Fact]
    public void A_forms_link_to_its_library_form_is_not_a_change_but_its_questions_are()
    {
        var question = new Question("start_date", QuestionType.ShortText, "When could you start?");
        var own = Base with { FollowUpQuestionnaires = [new FollowUpQuestionnaire("availability", "Availability", [question])] };
        var linked = Base with
        {
            FollowUpQuestionnaires = [new FollowUpQuestionnaire("availability", "Availability", [question], FormTemplateId: Guid.CreateVersion7(), FormVersion: 2)],
        };

        WorkflowDiff.Compare(own, linked).Should().BeEmpty();
        WorkflowDiff.Compare(own, linked with { FollowUpQuestionnaires = [linked.FollowUpQuestionnaires![0] with { Questions = [question with { Label = "Earliest start?" }] }] })
            .Select(change => change.Path).Should().Equal("followUpQuestionnaires[availability].questions[start_date].label");
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
        };

        WorkflowDiff.Compare(Base, after).Select(c => (c.Path, c.Kind)).Should().BeEquivalentTo(new[]
        {
            ("questionnaire.questions[portfolio]", WorkflowChangeKind.Added),
        });
    }
}
