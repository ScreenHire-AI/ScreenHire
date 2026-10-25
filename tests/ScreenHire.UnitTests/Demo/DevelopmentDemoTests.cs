using ScreenHire.Api.Demo;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.UnitTests.Integrity;

namespace ScreenHire.UnitTests.Demo;

/// <summary>
/// The local demo's data (<see cref="DevelopmentDemo"/>): what it adds to Brightwater must pass the same rules a person's
/// edit does, and its candidates must be fictional and land where the demo script says.
/// </summary>
public sealed class DevelopmentDemoTests
{
    // The "Software engineer" pipeline (the .NET job and Brightwater's template), written out from the template.
    private static readonly string[] SoftwareEngineerStages = ["applied", "screening", "technical", "interview", "offer", "hired", "rejected", "withdrawn"];

    // The stages each job's candidates can move through: the designer and frontend jobs use the default pipeline.
    private static readonly Dictionary<string, string[]> StagesByJob = new()
    {
        [DemoCandidates.ProductDesigner] = [.. HiringWorkflow.Default.Stages.Select(s => s.Id)],
        [DemoCandidates.FrontendEngineer] = [.. HiringWorkflow.Default.Stages.Select(s => s.Id)],
        [DemoCandidates.SeniorDotnetEngineer] = SoftwareEngineerStages,
        [DemoCandidates.SupportEngineer] = SoftwareEngineerStages,
    };

    // The questions each job's candidates answer: the "Software engineer" template's, written out in the harness, or the
    // questionnaire the demo adds to a job seeded without a template.
    private static readonly Dictionary<string, IReadOnlyList<Question>> QuestionsByJob = new()
    {
        [DemoCandidates.ProductDesigner] = QuestionnaireOf(DemoCandidates.ProductDesigner),
        [DemoCandidates.FrontendEngineer] = QuestionnaireOf(DemoCandidates.FrontendEngineer),
        [DemoCandidates.SeniorDotnetEngineer] = IntegrityHarness.SoftwareEngineerQuestions,
        [DemoCandidates.SupportEngineer] = IntegrityHarness.SoftwareEngineerQuestions,
    };

    [Fact]
    public void Each_job_seeded_without_a_template_gets_a_valid_questionnaire()
    {
        DemoQuestionnaires.All.Select(q => q.Slug).Should().BeEquivalentTo(
            [DemoCandidates.ProductDesigner, DemoCandidates.FrontendEngineer, DemoQuestionnaires.CustomerSuccessLead]);
        DemoQuestionnaires.All.Should().AllSatisfy(entry =>
        {
            entry.Questionnaire.Questions.Should().NotBeEmpty(entry.Slug);
            DefinitionValidator.Validate(HiringWorkflow.Default with { Questionnaire = entry.Questionnaire })
                .Should().BeEmpty(entry.Slug);
        });
    }

    [Fact]
    public void Each_demo_candidate_answers_every_question_of_their_job()
    {
        DemoCandidates.All.Should().AllSatisfy(candidate =>
        {
            var questions = ApplicationForm.QuestionsFor(HiringWorkflow.Default with { Questionnaire = new(QuestionsByJob[candidate.JobSlug]) });
            // The demo uploads the resume itself.
            ApplicationAnswer[] answers = [new(ApplicationForm.ResumeQuestionId, Guid.CreateVersion7().ToString()), .. candidate.Applicant.Answers];

            ApplicationForm.Validate(questions, answers).Should().BeEmpty(candidate.Applicant.Key);
            answers.Select(a => a.QuestionId).Should().BeEquivalentTo(questions.Select(q => q.Id), candidate.Applicant.Key);
            var links = questions.Where(q => q.Type == QuestionType.Url).Select(q => answers.Single(a => a.QuestionId == q.Id).Value);
            links.Should().AllSatisfy(link => new Uri(link!).Host.Should().MatchRegex(@"(^|\.)example\.org$", candidate.Applicant.Key));
        });
    }

    [Fact]
    public void The_candidate_emails_are_a_valid_workflow_edit()
    {
        var workflow = HiringWorkflow.Default with { Automations = [.. DevelopmentDemo.CandidateEmails] };

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
        DevelopmentDemo.CandidateEmails.Should().AllSatisfy(automation =>
            automation.Actions.Should().ContainSingle().Which.Type.Should().Be(ActionType.SendEmail));
    }

    [Fact]
    public void Each_demo_candidate_is_fictional_and_moves_through_stages_the_job_has()
    {
        DemoCandidates.All.Should().AllSatisfy(candidate =>
        {
            StagesByJob.Should().ContainKey(candidate.JobSlug);
            candidate.Moves.Select(m => m.StageId).Should().BeSubsetOf(StagesByJob[candidate.JobSlug]);
            candidate.Moves.Select(m => m.StageId).Should().OnlyHaveUniqueItems().And.NotContain("applied", "everyone starts in Applied");
            candidate.Applicant.Email.Should().MatchRegex(@"@example\.(com|org)$");
            candidate.Applicant.Phone.Should().MatchRegex(@"^\+44 (20 7946|1\d\d 496) 0\d{3}$", "Ofcom's drama ranges");
            candidate.Applicant.IpAddress.Should().StartWith("192.0.2.", "RFC 5737's documentation range");
        });
        DemoCandidates.All.Select(c => c.Applicant.Email).Should().OnlyHaveUniqueItems();
        DemoCandidates.All.Select(c => c.Applicant.DeviceFingerprint).Should().OnlyHaveUniqueItems();
        DemoCandidates.All.Select(c => c.Applicant.IpAddress).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_stage_of_both_pipelines_ends_up_with_a_demo_candidate_or_applicant()
    {
        // The synthetic applicants all stay in Applied on the .NET job.
        var occupied = DemoCandidates.All.Select(c => c.StageId).Append("applied").ToHashSet();

        occupied.Should().Contain(SoftwareEngineerStages).And.Contain(HiringWorkflow.Default.Stages.Select(s => s.Id));
    }

    [Fact]
    public void A_recruiter_ends_a_candidacy_only_with_a_reason()
    {
        var endings = DemoCandidates.All.SelectMany(c => c.Moves).Where(m => m.StageId is "rejected" or "withdrawn").ToList();

        endings.Should().NotBeEmpty();
        endings.Should().AllSatisfy(move => move.Note.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task The_demo_candidates_raise_no_concerns()
    {
        // One organization for all of them, in order, so a shared device, address, file or answer would show as a
        // duplicate. The harness has one job, so it asks every demo job's questions; a shared id (experience, notice
        // period) keeps the "Software engineer" wording, which comes first.
        var harness = new IntegrityHarness(
            [.. IntegrityHarness.SoftwareEngineerQuestions.Concat(QuestionsByJob.Values.SelectMany(q => q)).DistinctBy(q => q.Id)]);

        foreach (var candidate in DemoCandidates.All)
        {
            var result = await harness.SubmitAsync(candidate.Applicant);

            result.Authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns), candidate.Applicant.Key);
            result.AiAssistance.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated), candidate.Applicant.Key);
        }
    }

    // The seeded scorecards answer the default workflow's built-in scorecards, so each must fit its stage's, and the team's
    // own scorecard must be one the library would save.
    [Fact]
    public void The_demo_scorecards_fit_their_stages_and_make_ratings()
    {
        var library = DemoScorecards.TechnicalInterview(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch);
        DefinitionValidator.ValidateScorecardQuestions(library.Questions, "questions").Should().BeEmpty();
        library.Questions.Select(q => q.Type).Distinct().Should().HaveCount(Enum.GetValues<ScorecardQuestionType>().Length, "it shows every answer kind");

        foreach (var seeded in DemoScorecards.All)
        {
            var scorecard = HiringWorkflow.Default.FindStage(seeded.StageId)!.Scorecard!;
            ScorecardAnswers.Check(scorecard, seeded.Answers).Should().BeEmpty(seeded.CandidateEmail);
            RatingRule.Of(ScorecardAnswers.Keep(scorecard, seeded.Answers)).Should().NotBeNull();
        }
    }

    private static IReadOnlyList<Question> QuestionnaireOf(string slug) =>
        DemoQuestionnaires.All.Single(q => q.Slug == slug).Questionnaire.Questions;
}
