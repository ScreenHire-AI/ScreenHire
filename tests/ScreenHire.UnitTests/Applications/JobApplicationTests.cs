using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
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
    public void A_careers_application_records_its_answers_notice_and_checks()
    {
        var (job, candidate) = JobAndCandidate();
        var intake = new ApplicationIntake(
            ApplicationSource.CareersSite,
            [new("notice", " 2 weeks "), new("portfolio", "")],
            PrivacyNoticeVersion: "2026-10-20",
            HoneypotFilled: true,
            BotCheck: BotCheckResult.Failed);

        var (application, submitted) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.DidNotUse, null, Monday, null, intake);

        application.Source.Should().Be(ApplicationSource.CareersSite);
        application.Answers.Should().BeEquivalentTo([new ApplicationAnswer("notice", "2 weeks")]);
        application.PrivacyNoticeVersion.Should().Be("2026-10-20");
        application.HoneypotFilled.Should().BeTrue();
        application.BotCheck.Should().Be(BotCheckResult.Failed);
        application.CurrentStageId.Should().Be("applied", "signals are recorded; they never move or reject an application");
        submitted.ActorUserId.Should().BeNull("the candidate submitted it");
    }

    [Fact]
    public void An_application_through_the_widget_keeps_the_site_and_stays_a_careers_page_one()
    {
        var (job, candidate) = JobAndCandidate();
        var intake = new ApplicationIntake(ApplicationSource.CareersSite, [], PrivacyNoticeVersion: "2026-10-31.2", SourceSite: "https://www.example.com");

        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.DidNotUse, null, Monday, null, intake);

        application.Source.Should().Be(ApplicationSource.CareersSite, "the frame shows the same pages, notice and checks");
        application.SourceSite.Should().Be("https://www.example.com");
    }

    [Fact]
    public void A_recruiter_added_application_has_no_answers_or_checks()
    {
        var (job, candidate) = JobAndCandidate();

        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, Recruiter);

        application.Source.Should().Be(ApplicationSource.Recruiter);
        application.Answers.Should().BeEmpty();
        application.PrivacyNoticeVersion.Should().BeNull();
        application.SourceSite.Should().BeNull();
        application.BotCheck.Should().Be(BotCheckResult.NotChecked);
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
        var stranger = Candidate.Create(Guid.CreateVersion7(), "Sam", "Field", "sam@example.com", null, Monday);

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
    public void Removing_a_tag_ignores_case_and_records_the_tag_as_it_was()
    {
        var (application, _) = Submitted();
        application.AddTag("Referral", Monday, Recruiter);
        var apiKey = Guid.CreateVersion7();

        var removed = application.RemoveTag(" referral ", Monday.AddHours(1), actorUserId: null, apiKey);

        application.Tags.Should().BeEmpty();
        removed!.Type.Should().Be(ApplicationEventType.TagRemoved);
        removed.Data.Should().Be(new ApplicationEventData(Tag: "Referral", ApiKeyId: apiKey));
        application.RemoveTag("referral", Monday.AddHours(2), Recruiter).Should().BeNull("a tag it does not have changes nothing");
    }

    [Fact]
    public void A_note_is_trimmed_and_kept_with_its_author_without_changing_the_application()
    {
        var (application, _) = Submitted();

        var (note, entry) = application.AddNote("  Ask about the notice period.  ", Monday.AddHours(1), Recruiter);

        note.Text.Should().Be("Ask about the notice period.");
        note.AuthorUserId.Should().Be(Recruiter);
        note.ApplicationId.Should().Be(application.Id);
        note.EditedAt.Should().BeNull();
        entry.Type.Should().Be(ApplicationEventType.NoteAdded);
        entry.Data.Should().Be(new ApplicationEventData(NoteId: note.Id), "the timeline names the note and never holds its text");
        entry.ActorUserId.Should().Be(Recruiter);
        application.CurrentStageId.Should().Be("applied");
        application.StageEnteredAt.Should().Be(Monday);
    }

    [Fact]
    public void Emails_match_whatever_their_case_and_spacing()
    {
        Candidate.Create(Guid.CreateVersion7(), " Alex ", " Rivera ", " Alex.Rivera@Example.com ", " ", Monday)
            .Should().BeEquivalentTo(new
            {
                FirstName = "Alex",
                LastName = "Rivera",
                FullName = "Alex Rivera",
                Email = "Alex.Rivera@Example.com",
                NormalizedEmail = "alex.rivera@example.com",
                Phone = (string?)null,
            });
    }

    [Fact]
    public void A_candidate_with_no_last_name_goes_by_their_first_name()
    {
        // A candidate from before the name was split keeps their whole name as the first name; a blank last name is none.
        Candidate.Create(Guid.CreateVersion7(), "Alex Rivera", "  ", "alex@example.com", null, Monday)
            .Should().BeEquivalentTo(new { FirstName = "Alex Rivera", LastName = (string?)null, FullName = "Alex Rivera" });
    }

    [Fact]
    public void A_sent_email_is_recorded_by_the_id_of_its_copy_which_holds_the_body()
    {
        var (application, _) = Submitted();
        var sendId = Guid.CreateVersion7();

        var copy = application.PrepareEmail(sendId, null, "alex@example.com", "A quick question", "Hi Alex,\n", "<p>Hi Alex,</p>", Monday);
        var entry = application.EmailSent(copy, Monday, Recruiter);

        copy.Should().BeEquivalentTo(new { Id = sendId, application.OrganizationId, ApplicationId = application.Id, TemplateId = (string?)null });
        entry.Id.Should().Be(sendId);
        entry.Type.Should().Be(ApplicationEventType.EmailSent);
        entry.Data.Should().BeEquivalentTo(new { EmailId = (Guid?)sendId, EmailSubject = "A quick question", EmailTemplateId = (string?)null });
    }

    [Fact]
    public void A_library_scorecard_records_the_stage_the_candidate_was_in_its_key_and_its_name()
    {
        var (application, _) = Submitted();
        var card = new Scorecard("Culture add", [new ScorecardQuestion("values", ScorecardQuestionType.Rating5, "Shares how the team works")]);

        var (scorecard, entry) = application.SubmitScorecard(
            application.CurrentStageId, card, "11111111-2222-3333-4444-555555555555", Recruiter,
            new Dictionary<string, ScorecardAnswerValue> { ["values"] = new(Rating: 4) }, Recommendation.Yes, null, 1, Monday);

        scorecard.Should().BeEquivalentTo(new
        {
            StageId = application.CurrentStageId,
            ScorecardTemplateId = "11111111-2222-3333-4444-555555555555",
            ScorecardName = "Culture add",
            ReviewerUserId = Recruiter,
        });
        entry.Data.StageId.Should().Be(application.CurrentStageId);
    }

    [Fact]
    public void Asking_for_a_scorecard_makes_a_scorecard_task_in_the_current_stage_and_records_who_asked()
    {
        var (application, _) = Submitted();
        var teammate = Guid.CreateVersion7();

        var (task, entry) = application.RequestScorecard("interview", "Interview", teammate, "Focus on the pairing.", Monday, Recruiter);

        task.Should().BeEquivalentTo(new
        {
            Kind = ApplicationTaskKind.Scorecard,
            AssigneeUserId = (Guid?)teammate,
            ScorecardTemplateId = "interview",
            StageId = application.CurrentStageId,
            Note = "Fill in the Interview scorecard. Focus on the pairing.",
        });
        entry.Type.Should().Be(ApplicationEventType.TaskCreated);
        entry.ActorUserId.Should().Be(Recruiter);
        entry.Data.TaskId.Should().Be(task.Id);
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
        return (job, Candidate.Create(organization, "Alex", "Rivera", "alex@example.com", null, Monday));
    }
}
