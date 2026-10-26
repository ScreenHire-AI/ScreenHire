using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

public sealed class TeamPolicyTests
{
    private static readonly IReadOnlySet<Permission> Owner = RolePermissions.Of(OrganizationRole.Owner);
    private static readonly IReadOnlySet<Permission> Admin = RolePermissions.Of(OrganizationRole.Admin);
    private static readonly IReadOnlySet<Permission> Recruiter = RolePermissions.Of(OrganizationRole.Recruiter);
    private static readonly IReadOnlySet<Permission> Viewer = RolePermissions.Of(OrganizationRole.Viewer);

    // The matrix the role names gave before permissions: Owners assign anything, Admins anything but Owner, others nothing.
    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Recruiter, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Recruiter, OrganizationRole.Viewer, false)]
    [InlineData(OrganizationRole.Recruiter, OrganizationRole.Recruiter, false)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Viewer, false)]
    public void Owners_assign_any_role_and_admins_any_but_owner(OrganizationRole actor, OrganizationRole role, bool expected) =>
        TeamPolicy.MayAssign(RolePermissions.Of(actor), RolePermissions.Of(role)).Should().Be(expected);

    [Fact]
    public void An_admin_cannot_change_or_remove_an_owner()
    {
        TeamPolicy.MayChange(Admin, Owner, Viewer).Should().BeFalse();
        TeamPolicy.MayChange(Admin, Viewer, Owner).Should().BeFalse();
        TeamPolicy.MayChange(Admin, Viewer, Admin).Should().BeTrue();
        TeamPolicy.MayRemove(Admin, Owner).Should().BeFalse();
        TeamPolicy.MayRemove(Owner, Owner).Should().BeTrue();
        TeamPolicy.IsOwnerLevel(Owner).Should().BeTrue();
        TeamPolicy.IsOwnerLevel(Admin).Should().BeFalse();
    }

    [Fact]
    public void An_admin_manages_every_custom_role()
    {
        var everything = RolePermissions.ForCustom(Enum.GetValues<Permission>());
        TeamPolicy.MayAssign(Admin, everything).Should().BeTrue();
        TeamPolicy.MayManageRole(Admin, null, everything).Should().BeTrue();
        TeamPolicy.MayManageRole(Admin, everything, null).Should().BeTrue();
        TeamPolicy.MayManageRole(Recruiter, null, Viewer).Should().BeFalse("a Recruiter cannot manage the team");
    }

    [Fact]
    public void A_custom_role_that_manages_the_team_gives_nothing_it_does_not_have()
    {
        var lead = RolePermissions.ForCustom([Permission.ManageTeam, Permission.AddNotesAndTags]);
        var interviewer = RolePermissions.ForCustom([Permission.AddNotesAndTags, Permission.FillInScorecards]);
        var noteTaker = RolePermissions.ForCustom([Permission.AddNotesAndTags]);

        TeamPolicy.MayAssign(lead, Viewer).Should().BeTrue();
        TeamPolicy.MayAssign(lead, noteTaker).Should().BeTrue();
        TeamPolicy.MayAssign(lead, interviewer).Should().BeFalse("it cannot give Fill in scorecards");
        TeamPolicy.MayAssign(lead, Recruiter).Should().BeFalse();
        TeamPolicy.MayChange(lead, Recruiter, Viewer).Should().BeFalse("nor take a broader role away");
        TeamPolicy.MayManageRole(lead, null, interviewer).Should().BeFalse();
        TeamPolicy.MayManageRole(lead, noteTaker, interviewer).Should().BeFalse();
        TeamPolicy.MayManageRole(lead, interviewer, null).Should().BeFalse("nor delete a broader role");
        TeamPolicy.MayManageRole(lead, interviewer, noteTaker).Should().BeFalse("before and after both count");
        TeamPolicy.MayManageRole(lead, noteTaker, Viewer).Should().BeTrue();
    }

    [Fact]
    public void An_invitation_is_open_until_accepted_revoked_or_expired()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var invitation = new OrganizationInvitation
        {
            Email = "invitee@example.com",
            Role = OrganizationRole.Viewer,
            TokenHash = new string('0', 64),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
        };

        invitation.IsOpen(now).Should().BeTrue();
        invitation.IsOpen(now.AddDays(7)).Should().BeFalse("it stops working at its expiry");
        invitation.Revoke(now.AddDays(8)).Should().BeFalse("an expired invitation cannot be revoked");
        invitation.Revoke(now).Should().BeTrue();
        invitation.IsOpen(now).Should().BeFalse();
        invitation.Revoke(now).Should().BeFalse("it is revoked once");
    }
}
