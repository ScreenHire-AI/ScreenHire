using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

public sealed class JobAiUseTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AiUsePolicy.NotAllowed, AiUsePolicy.Allowed, AiUsePolicy.NotAllowed)]
    [InlineData(null, AiUsePolicy.Allowed, AiUsePolicy.Allowed)]
    [InlineData(null, null, AiUsePolicy.AllowedWithDisclosure)]
    [InlineData(AiUsePolicy.Allowed, null, AiUsePolicy.Allowed)]
    public void The_jobs_own_policy_wins_then_the_organizations_then_allowed_with_disclosure(
        AiUsePolicy? jobOverride, AiUsePolicy? organization, AiUsePolicy expected) =>
        AiUsePolicies.Effective(jobOverride, organization).Should().Be(expected);

    [Fact]
    public void A_job_follows_the_organization_until_it_sets_its_own_and_it_can_go_back()
    {
        var job = NewJob();
        job.AiUseOverride.Should().BeNull();

        job.SetAiUseOverride(AiUsePolicy.NotAllowed, Monday);
        job.AiUseOverride.Should().Be(AiUsePolicy.NotAllowed);
        job.UpdatedAt.Should().Be(Monday);

        job.SetAiUseOverride(null, Monday.AddDays(1));
        job.AiUseOverride.Should().BeNull();
    }

    [Fact]
    public void Setting_the_same_policy_again_changes_nothing_and_a_closed_job_refuses_a_change()
    {
        var job = NewJob();
        job.SetAiUseOverride(null, Monday);
        job.UpdatedAt.Should().BeNull();

        job.Publish(Monday);
        job.Close(Monday.AddDays(1));
        var act = () => job.SetAiUseOverride(AiUsePolicy.Allowed, Monday.AddDays(2));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_override_is_not_posting_content()
    {
        var job = NewJob();
        var before = job.Snapshot();

        job.SetAiUseOverride(AiUsePolicy.Allowed, Monday);

        job.Snapshot().Should().Be(before, "AI use must not mark the posting changed or republish it");
    }

    [Fact]
    public void History_says_when_a_job_sets_its_own_policy_and_when_it_follows_the_organization_again()
    {
        var job = NewJob();
        var own = NewJob();
        own.SetAiUseOverride(AiUsePolicy.NotAllowed, Monday);

        JobChanges.Describe(job, own).Select(l => l.Sentence()).Should().Equal("set AI use for this job to Not allowed");
        JobChanges.Describe(own, job).Select(l => l.Sentence()).Should().Equal("made AI use follow the organization's policy");
    }

    private static Job NewJob() => new()
    {
        OrganizationId = Guid.CreateVersion7(),
        Title = "Backend Engineer",
        Slug = "backend-engineer",
        Description = "<p>Build things.</p>",
        WorkplaceType = WorkplaceType.Remote,
    };
}
