using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

public sealed class TeamPolicyTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Recruiter, true)]
    [InlineData(OrganizationRole.Recruiter, OrganizationRole.Viewer, false)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Viewer, false)]
    public void Owners_assign_any_role_and_admins_any_but_owner(OrganizationRole actor, OrganizationRole role, bool expected) =>
        TeamPolicy.MayAssign(actor, role).Should().Be(expected);

    [Fact]
    public void An_admin_cannot_change_or_remove_an_owner()
    {
        TeamPolicy.MayChange(OrganizationRole.Admin, OrganizationRole.Owner, OrganizationRole.Viewer).Should().BeFalse();
        TeamPolicy.MayChange(OrganizationRole.Admin, OrganizationRole.Viewer, OrganizationRole.Owner).Should().BeFalse();
        TeamPolicy.MayChange(OrganizationRole.Admin, OrganizationRole.Viewer, OrganizationRole.Admin).Should().BeTrue();
        TeamPolicy.MayRemove(OrganizationRole.Admin, OrganizationRole.Owner).Should().BeFalse();
        TeamPolicy.MayRemove(OrganizationRole.Owner, OrganizationRole.Owner).Should().BeTrue();
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
