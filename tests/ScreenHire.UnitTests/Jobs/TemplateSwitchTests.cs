using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

/// <summary>
/// A job switching to another template (workflow-engine.md section 7, Tailoring a job): the job's side, the stage each
/// candidate is preselected to, and the timeline entry each application gets.
/// </summary>
public sealed class TemplateSwitchTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    private static readonly Question Notice = new("notice", QuestionType.ShortText, "Notice period");

    private static readonly HiringWorkflow Kitchen = HiringWorkflow.Default with
    {
        Stages =
        [
            new("new", "New", StageKind.Applied),
            new("call", "Phone call", StageKind.Screening),
            new("trial", "Trial shift", StageKind.Interview),
            new("hired", "Hired", StageKind.Hired),
            new("no", "Not this time", StageKind.Rejected),
        ],
        Questionnaire = new Questionnaire([new("shifts", QuestionType.ShortText, "Which shifts suit you?")]),
    };

    [Fact]
    public void Switching_takes_another_templates_version_on_an_open_job_and_keeps_what_it_drops_as_former()
    {
        var job = NewJob();
        job.ReviseWorkflow(HiringWorkflow.Default with { Questionnaire = new Questionnaire([Notice]) }, Monday);
        job.Publish(Monday);
        var kitchen = Published("Kitchen", Kitchen);

        job.SwitchTemplate(kitchen, Monday.AddDays(1));

        job.Workflow.Should().Be(Kitchen);
        job.SourceTemplateId.Should().Be(kitchen.TemplateId);
        job.SourceTemplateVersionId.Should().Be(kitchen.Id);
        job.SourceTemplateVersion.Should().Be(1);
        job.WorkflowVersion.Should().Be(3);
        job.Former.Stages.Select(s => s.Id).Should().Equal("applied", "screening", "interview", "offer", "rejected", "withdrawn");
        job.Former.Questions.Should().Equal(Notice);
        job.StageName("screening").Should().Be("Screening", "a stage the pipeline lost keeps its name for the timeline");
        job.StageName("call").Should().Be("Phone call");
        job.StageName("hired").Should().Be("Hired", "a stage both pipelines have reads as the current one");
    }

    [Fact]
    public void A_stage_dropped_again_replaces_its_older_former_copy_and_a_kept_one_is_never_former()
    {
        var job = NewJob();
        job.ReviseWorkflow(HiringWorkflow.Default with { Stages = [.. HiringWorkflow.Default.Stages.Select(s => s.Id == "offer" ? s with { Name = "First offer" } : s)] }, Monday);
        job.ReviseWorkflow(HiringWorkflow.Default with { Stages = [.. HiringWorkflow.Default.Stages.Where(s => s.Id != "offer")] }, Monday);
        job.ReviseWorkflow(HiringWorkflow.Default, Monday);
        job.ReviseWorkflow(HiringWorkflow.Default with { Stages = [.. HiringWorkflow.Default.Stages.Where(s => s.Id != "offer")] }, Monday);

        job.Former.Stages.Should().Equal(new FormerStage("offer", "Offer", StageKind.Offer));
    }

    [Fact]
    public void A_closed_job_or_an_unpublished_version_cannot_be_switched_to()
    {
        var closed = NewJob();
        closed.Publish(Monday);
        closed.Close(Monday);
        var open = NewJob();
        var template = HiringTemplate.Create(open.OrganizationId, "Kitchen", null, new(Kitchen, PostingDefaults.None), Monday, null);

        var onClosed = () => closed.SwitchTemplate(Published("Kitchen", Kitchen), Monday);
        var toDraft = () => open.SwitchTemplate(template.Versions[0], Monday);

        onClosed.Should().Throw<InvalidOperationException>().WithMessage("*closed job*");
        toDraft.Should().Throw<InvalidOperationException>().WithMessage("*published*");
        open.WorkflowVersion.Should().Be(1);
        open.SourceTemplateId.Should().BeNull();
    }

    [Theory]
    [InlineData("applied", "new")]
    [InlineData("screening", "call")]
    [InlineData("interview", "trial")]
    [InlineData("hired", "hired")]
    [InlineData("rejected", "no")]
    public void A_stage_is_preselected_to_the_new_stage_of_its_kind(string from, string expected)
    {
        var stage = HiringWorkflow.Default.FindStage(from)!;

        StageMapping.Suggest(HiringWorkflow.Default, stage, Kitchen).Id.Should().Be(expected);
    }

    [Fact]
    public void Among_stages_of_one_kind_the_same_name_wins_then_the_same_place()
    {
        var from = HiringWorkflow.Default with
        {
            Stages = [new("a", "Applied", StageKind.Applied), new("i1", "Intro", StageKind.Interview), new("i2", "Panel", StageKind.Interview), new("i3", "Final", StageKind.Interview)],
        };
        var to = HiringWorkflow.Default with
        {
            Stages = [new("b", "Applied", StageKind.Applied), new("j1", "Chat", StageKind.Interview), new("j2", "panel ", StageKind.Interview)],
        };

        StageMapping.Suggest(from, from.FindStage("i2")!, to).Id.Should().Be("j2", "the name matches, whatever its case and spaces");
        StageMapping.Suggest(from, from.FindStage("i1")!, to).Id.Should().Be("j1", "the first interview goes to the first interview");
        StageMapping.Suggest(from, from.FindStage("i3")!, to).Id.Should().Be("j2", "past the last of its kind, the last one there is");
    }

    [Fact]
    public void With_no_stage_of_its_kind_the_same_name_wins_else_the_first_stage()
    {
        var from = HiringWorkflow.Default;

        StageMapping.Suggest(from, from.FindStage("offer")!, Kitchen).Id.Should().Be("new", "Kitchen has no offer stage or one named Offer");
        StageMapping.Suggest(from, new WorkflowStage("x", "Trial shift", StageKind.Custom), Kitchen).Id.Should().Be("trial");
    }

    [Fact]
    public void Changing_pipeline_moves_the_application_without_bringing_a_saved_candidate_back()
    {
        var (job, application) = Application();
        application.MoveTo(job.Workflow.FindStage("screening")!, Monday, Recruiter, null);
        application.KeepInMind(job.Workflow.FindStage("screening"), Monday.AddHours(1), Recruiter, "Good fit for a later role");
        var versionId = Guid.CreateVersion7();

        var entry = application.ChangePipeline(job.Workflow.FindStage("screening")!, Kitchen.FindStage("call")!, versionId, Monday.AddDays(1), Recruiter);

        application.CurrentStageId.Should().Be("call");
        application.StageEnteredAt.Should().Be(Monday.AddDays(1));
        application.KeptInMindAt.Should().NotBeNull("a pipeline change is not a person bringing the candidate back");
        entry.Type.Should().Be(ApplicationEventType.StageChanged);
        entry.Data.Should().Be(new ApplicationEventData("screening", "call", TemplateVersionId: versionId));
        entry.ActorUserId.Should().Be(Recruiter);
    }

    [Fact]
    public void A_stage_that_carries_over_still_gets_an_entry_and_keeps_its_time_in_stage()
    {
        var (job, application) = Application();

        var entry = application.ChangePipeline(
            job.Workflow.FindStage("applied")!, new WorkflowStage("applied", "Applied", StageKind.Applied), Guid.CreateVersion7(), Monday.AddDays(2), Recruiter);

        application.CurrentStageId.Should().Be("applied");
        application.StageEnteredAt.Should().Be(Monday, "the candidate is still where they were");
        entry.Data.FromStageId.Should().Be("applied");
        entry.Data.ToStageId.Should().Be("applied");
    }

    [Fact]
    public void An_ended_application_mapped_to_a_stage_that_ends_one_keeps_when_it_ended_for_retention()
    {
        var (job, application) = Application();
        application.MoveTo(job.Workflow.FindStage("rejected")!, Monday.AddHours(1), Recruiter, null);

        application.ChangePipeline(job.Workflow.FindStage("rejected")!, Kitchen.FindStage("no")!, Guid.CreateVersion7(), Monday.AddDays(30), Recruiter);

        application.CurrentStageId.Should().Be("no");
        application.StageEnteredAt.Should().Be(
            Monday.AddHours(1), "retention counts from when the application ended, and a template switch does not start it again");
    }

    private static HiringTemplateVersion Published(string name, HiringWorkflow workflow)
    {
        var template = HiringTemplate.Create(Guid.CreateVersion7(), name, null, new(workflow, PostingDefaults.None), Monday, null);
        template.Publish(template.Versions[0], Monday, null);
        return template.Versions[0];
    }

    private static (Job Job, JobApplication Application) Application()
    {
        var job = NewJob();
        var candidate = Candidate.Create(job.OrganizationId, "Robin", "Example", "robin@example.com", null, Monday);
        var intake = new ApplicationIntake(ApplicationSource.CareersSite, [], FutureRoles: new FutureRolesConsent(Monday));
        return (job, JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, null, intake).Application);
    }

    private static Job NewJob() => new() { OrganizationId = Guid.CreateVersion7(), Title = "Line cook", Slug = "line-cook", Description = "<p>Cook.</p>" };
}
