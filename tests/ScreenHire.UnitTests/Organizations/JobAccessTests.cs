using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>Who sees every job, who may give that, and how a hiring team changes (ADR-0015).</summary>
public sealed class JobAccessTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner, JobAccess.HiringTeamOnly, true)]
    [InlineData(OrganizationRole.Admin, JobAccess.HiringTeamOnly, true)]
    [InlineData(OrganizationRole.Recruiter, JobAccess.HiringTeamOnly, false)]
    [InlineData(OrganizationRole.Viewer, JobAccess.All, true)]
    [InlineData(OrganizationRole.Custom, JobAccess.HiringTeamOnly, false)]
    public void Owners_and_admins_see_every_job_and_everyone_else_follows_their_choice(OrganizationRole role, JobAccess access, bool seesAll) =>
        JobAccessRules.SeesAllJobs(role, access).Should().Be(seesAll);

    [Fact]
    public void Someone_who_sees_only_their_jobs_never_leaves_anyone_seeing_them_all()
    {
        TeamPolicy.MayGiveJobAccess(actorSeesAllJobs: false, OrganizationRole.Viewer, JobAccess.HiringTeamOnly).Should().BeTrue();
        TeamPolicy.MayGiveJobAccess(actorSeesAllJobs: false, OrganizationRole.Viewer, JobAccess.All).Should().BeFalse();
        TeamPolicy.MayGiveJobAccess(actorSeesAllJobs: false, OrganizationRole.Admin, JobAccess.HiringTeamOnly)
            .Should().BeFalse("an Admin sees every job");
        TeamPolicy.MayGiveJobAccess(actorSeesAllJobs: true, OrganizationRole.Owner, JobAccess.All).Should().BeTrue();
    }

    [Fact]
    public void Api_keys_and_webhooks_need_all_jobs() =>
        RolePermissions.NeedsAllJobs.Should().BeEquivalentTo([Permission.ManageApiKeys, Permission.ManageWebhooks]);

    [Fact]
    public void A_hiring_team_says_who_joined_and_who_left_and_keeps_each_person_once()
    {
        var job = new Job { Title = "Engineer", Slug = "engineer" };
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        job.SetHiringTeam([a, b, a]).Added.Should().Equal(a, b);
        job.HiringTeam.Should().Equal(a, b);

        var change = job.SetHiringTeam([b, c]);
        change.Added.Should().Equal(c);
        change.Removed.Should().Equal(a);
        job.SetHiringTeam([b, c]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_system_template_has_no_default_hiring_team()
    {
        var system = new HiringTemplate { Name = "System" };
        var own = new HiringTemplate { Name = "Own", OrganizationId = Guid.NewGuid() };
        var member = Guid.NewGuid();

        var setting = () => system.SetDefaultHiringTeam([member]);
        setting.Should().Throw<InvalidOperationException>();
        own.SetDefaultHiringTeam([member]).Added.Should().Equal(member);
        own.DefaultHiringTeam.Should().Equal(member);
    }
}
