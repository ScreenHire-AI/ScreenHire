using ScreenHire.Api.Demo;
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
        // duplicate. The "Software engineer" questions, which the candidates on those jobs answer.
        var harness = new IntegrityHarness();

        foreach (var candidate in DemoCandidates.All)
        {
            var result = await harness.SubmitAsync(candidate.Applicant);

            result.Authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns), candidate.Applicant.Key);
            result.AiAssistance.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated), candidate.Applicant.Key);
        }
    }
}
