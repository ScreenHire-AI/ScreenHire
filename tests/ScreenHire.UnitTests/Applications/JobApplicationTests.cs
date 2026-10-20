using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Applications;

public sealed class JobApplicationTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    [Fact]
    public void A_new_application_starts_in_the_first_stage_with_a_submitted_event()
    {
        var (job, candidate) = JobAndCandidate();

        var (application, submitted) = JobApplication.Submit(job, candidate, true, AiUseDeclaration.Used, "  Grammar check  ", Monday, Recruiter);

        application.CurrentStageId.Should().Be("applied");
        application.WorkflowVersion.Should().Be(job.WorkflowVersion);
        application.AccommodationRequested.Should().BeTrue();
        application.AiUseDetails.Should().Be("Grammar check");
        application.StageEnteredAt.Should().Be(Monday);
        submitted.Type.Should().Be(ApplicationEventType.Submitted);
        submitted.ApplicationId.Should().Be(application.Id);
        submitted.OrganizationId.Should().Be(job.OrganizationId);
        submitted.Data.Should().Be(new ApplicationEventData(ToStageId: "applied"));
        submitted.ActorUserId.Should().Be(Recruiter);
    }

    [Fact]
    public void A_closed_job_takes_no_applications()
    {
        var (job, candidate) = JobAndCandidate();
        job.Close(Monday);

        var submit = () => JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, Recruiter);

        submit.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_candidate_of_another_organization_cannot_apply()
    {
        var (job, _) = JobAndCandidate();
        var stranger = Candidate.Create(Guid.CreateVersion7(), "Sam Field", "sam@example.com", null, Monday);

        var submit = () => JobApplication.Submit(job, stranger, false, AiUseDeclaration.NotProvided, null, Monday, Recruiter);

        submit.Should().Throw<InvalidOperationException>().WithMessage("*another organization*");
    }

    [Fact]
    public void Moving_records_where_from_and_where_to_with_the_note()
    {
        var (application, job) = Submitted();

        var moved = application.MoveTo(job.Workflow.FindStage("interview")!, Monday.AddDays(2), Recruiter, " Strong portfolio ");

        moved!.Type.Should().Be(ApplicationEventType.StageChanged);
        moved.Data.Should().Be(new ApplicationEventData("applied", "interview", "Strong portfolio"));
        moved.OccurredAt.Should().Be(Monday.AddDays(2));
        application.CurrentStageId.Should().Be("interview");
        application.StageEnteredAt.Should().Be(Monday.AddDays(2));
    }

    [Fact]
    public void Moving_to_the_current_stage_changes_nothing()
    {
        var (application, job) = Submitted();

        var moved = application.MoveTo(job.Workflow.FindStage("applied")!, Monday.AddDays(1), Recruiter, null);

        moved.Should().BeNull();
        application.StageEnteredAt.Should().Be(Monday);
    }

    [Fact]
    public void Emails_match_whatever_their_case_and_spacing()
    {
        Candidate.Create(Guid.CreateVersion7(), " Alex Rivera ", " Alex.Rivera@Example.com ", " ", Monday)
            .Should().BeEquivalentTo(new { FullName = "Alex Rivera", Email = "Alex.Rivera@Example.com", NormalizedEmail = "alex.rivera@example.com", Phone = (string?)null });
    }

    private static (JobApplication Application, Job Job) Submitted()
    {
        var (job, candidate) = JobAndCandidate();
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, Recruiter);
        return (application, job);
    }

    private static (Job Job, Candidate Candidate) JobAndCandidate()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Barista", Slug = "barista" };
        return (job, Candidate.Create(organization, "Alex Rivera", "alex@example.com", null, Monday));
    }
}
