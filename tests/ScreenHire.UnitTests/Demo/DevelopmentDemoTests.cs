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
    [Fact]
    public void The_candidate_emails_are_a_valid_workflow_edit()
    {
        var workflow = HiringWorkflow.Default with { Automations = [.. DevelopmentDemo.CandidateEmails] };

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
        DevelopmentDemo.CandidateEmails.Should().AllSatisfy(automation =>
            automation.Actions.Should().ContainSingle().Which.Type.Should().Be(ActionType.SendEmail));
    }

    [Fact]
    public void Each_demo_candidate_is_fictional_and_moves_to_a_stage_the_job_has()
    {
        var stages = HiringWorkflow.Default.Stages.Select(s => s.Id).ToList();

        DemoCandidates.All.Should().AllSatisfy(candidate =>
        {
            stages.Should().Contain(candidate.StageId);
            candidate.Applicant.Email.Should().MatchRegex(@"@example\.(com|org)$");
            candidate.Applicant.Phone.Should().MatchRegex(@"^\+44 (20 7946|1\d\d 496) 0\d{3}$", "Ofcom's drama ranges");
            candidate.Applicant.IpAddress.Should().StartWith("192.0.2.", "RFC 5737's documentation range");
        });
        DemoCandidates.All.Select(c => c.Applicant.Email).Should().OnlyHaveUniqueItems();
        DemoCandidates.All.Select(c => c.Applicant.DeviceFingerprint).Should().OnlyHaveUniqueItems();
        DemoCandidates.All.Select(c => c.JobSlug).Should().OnlyContain(slug => slug == DemoCandidates.ProductDesigner || slug == DemoCandidates.FrontendEngineer);
    }

    [Fact]
    public async Task The_demo_candidates_raise_no_concerns()
    {
        // One organization for all of them, in order, so a shared device, address or file would show as a duplicate.
        var harness = new IntegrityHarness(questions: []);

        foreach (var candidate in DemoCandidates.All)
        {
            var result = await harness.SubmitAsync(candidate.Applicant);

            result.Authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns), candidate.Applicant.Key);
            result.AiAssistance.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated), candidate.Applicant.Key);
        }
    }
}
