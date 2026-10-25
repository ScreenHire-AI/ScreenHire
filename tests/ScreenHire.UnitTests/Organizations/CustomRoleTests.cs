using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

public sealed class CustomRoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_copy_keeps_the_source_s_permissions_in_catalogue_order_without_owner_only_ones()
    {
        var role = CustomRole.Copy(Guid.NewGuid(), "  Hiring lead ", "Owner", RolePermissions.Of(OrganizationRole.Owner), Now);

        role.Name.Should().Be("Hiring lead");
        role.NormalizedName.Should().Be("HIRING LEAD");
        role.CopiedFrom.Should().Be("Owner");
        role.Permissions.Should().NotContain(Permission.ManageOwners)
            .And.Equal(RolePermissions.Permissions.Select(p => p.Permission).Where(RolePermissions.Of(OrganizationRole.Admin).Contains));
        role.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Setting_permissions_always_keeps_seeing_hiring_data_and_drops_owner_only_ones()
    {
        var role = CustomRole.Copy(Guid.NewGuid(), "Interviewer", "Viewer", [Permission.ViewHiring], Now);

        role.SetPermissions([Permission.FillInScorecards, Permission.ManageOwners], Now.AddHours(1));

        role.Permissions.Should().Equal(Permission.ViewHiring, Permission.FillInScorecards);
        role.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void An_unchanged_save_keeps_its_last_updated_time()
    {
        var role = CustomRole.Copy(Guid.NewGuid(), "Interviewer", "Viewer", [Permission.ViewHiring], Now);

        role.Rename(" Interviewer", Now.AddHours(1));
        role.SetPermissions([Permission.ViewHiring], Now.AddHours(1));

        role.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void History_says_what_changed_in_plain_words()
    {
        var before = CustomRole.Copy(Guid.NewGuid(), "Reviewer", "Viewer", [Permission.ViewHiring, Permission.EmailCandidates], Now);
        var after = CustomRole.Copy(before.OrganizationId, "Interviewer", "Viewer", [Permission.ViewHiring, Permission.MoveCandidates], Now);

        RoleChanges.Describe(null, before).Select(l => l.Sentence()).Should().Equal("created the role from Viewer");
        RoleChanges.Describe(before, after).Select(l => l.Sentence()).Should().Equal(
            "renamed the role from Reviewer to Interviewer",
            "allowed Move candidates",
            "took away Email candidates");
        RoleChanges.Describe(before, null).Select(l => l.Sentence()).Should().Equal("deleted the role");
        RoleChanges.Describe(before, before).Should().BeEmpty();
    }

    [Fact]
    public void A_member_holds_exactly_one_role()
    {
        var member = OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Viewer, Now);
        var roleId = Guid.NewGuid();

        member.AssignRole(OrganizationRole.Custom, roleId);
        member.Holds(OrganizationRole.Custom, roleId).Should().BeTrue();

        var custom = () => member.AssignRole(OrganizationRole.Custom);
        var builtIn = () => member.AssignRole(OrganizationRole.Admin, roleId);
        custom.Should().Throw<ArgumentException>();
        builtIn.Should().Throw<ArgumentException>();
    }
}
