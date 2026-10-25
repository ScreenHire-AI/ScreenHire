using System.Text.RegularExpressions;
using ScreenHire.Api.Auth;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// The permission catalogue describes what the server allows today. Every route a role check guards
/// (<c>RequireHiringRole</c>, <c>RequireOrganizationAdmin</c> in <c>apps/api/Endpoints</c>) belongs to one permission, and
/// a role has that permission exactly when the route's check lets the role through. A new guarded route that no permission
/// names fails here, as does a role set that drifts from <see cref="HiringAuthorization"/> or <see cref="TeamPolicy"/>.
/// </summary>
public sealed partial class RolePermissionsTests
{
    // Each guarded route, by its operation name (WithName), and the permission that covers it.
    private static readonly Dictionary<string, Permission> Routes = new(StringComparer.Ordinal)
    {
        ["CreateJob"] = Permission.EditJobs,
        ["UpdateJob"] = Permission.EditJobs,
        ["PublishJob"] = Permission.EditJobs,
        ["CloseJob"] = Permission.EditJobs,
        ["UpdateJobWorkflow"] = Permission.EditJobs,
        ["ApplyJobTemplateUpdate"] = Permission.EditJobs,
        ["CloseJobPosting"] = Permission.EditJobs,
        ["CreateApplication"] = Permission.AddCandidates,
        ["MoveApplication"] = Permission.MoveCandidates,
        ["AddApplicationNote"] = Permission.AddNotesAndTags,
        ["EditApplicationNote"] = Permission.AddNotesAndTags,
        ["DeleteApplicationNote"] = Permission.AddNotesAndTags,
        ["AddApplicationTag"] = Permission.AddNotesAndTags,
        ["RemoveApplicationTag"] = Permission.AddNotesAndTags,
        ["CompleteTask"] = Permission.CompleteTasks,
        ["SendEmails"] = Permission.EmailCandidates,
        ["SubmitScorecard"] = Permission.FillInScorecards,
        ["DismissIntegrityAssessment"] = Permission.ReviewIntegrity,
        ["AddIntegrityNote"] = Permission.ReviewIntegrity,
        ["RequestIdentityVerification"] = Permission.ReviewIntegrity,
        ["RunManualAutomation"] = Permission.RunAutomations,
        ["CreateHiringTemplate"] = Permission.EditHiringTemplates,
        ["UpdateHiringTemplate"] = Permission.EditHiringTemplates,
        ["CreateHiringTemplateVersion"] = Permission.EditHiringTemplates,
        ["UpdateHiringTemplateVersion"] = Permission.EditHiringTemplates,
        ["PublishHiringTemplateVersion"] = Permission.EditHiringTemplates,
        ["CreateScorecardTemplate"] = Permission.EditHiringTemplates,
        ["UpdateScorecardTemplate"] = Permission.EditHiringTemplates,
        ["CreateEmailTemplate"] = Permission.EditEmailTemplates,
        ["UpdateEmailTemplate"] = Permission.EditEmailTemplates,
        ["DeleteEmailTemplate"] = Permission.EditEmailTemplates,
        ["SendTestEmail"] = Permission.EditEmailTemplates,
        ["UpdateOrganization"] = Permission.EditSettings,
        ["UpdateOrganizationBranding"] = Permission.EditSettings,
        ["UpdateOrganizationIntegritySettings"] = Permission.EditSettings,
        ["ListApiKeys"] = Permission.ManageApiKeys,
        ["CreateApiKey"] = Permission.ManageApiKeys,
        ["GetApiKey"] = Permission.ManageApiKeys,
        ["RevokeApiKey"] = Permission.ManageApiKeys,
        ["ListWebhookEndpoints"] = Permission.ManageWebhooks,
        ["CreateWebhookEndpoint"] = Permission.ManageWebhooks,
        ["GetWebhookEndpoint"] = Permission.ManageWebhooks,
        ["UpdateWebhookEndpoint"] = Permission.ManageWebhooks,
        ["DeleteWebhookEndpoint"] = Permission.ManageWebhooks,
        ["RotateWebhookSecret"] = Permission.ManageWebhooks,
        ["ListWebhookDeliveries"] = Permission.ManageWebhooks,
        ["ReplayWebhookDelivery"] = Permission.ManageWebhooks,
        ["ListInvitations"] = Permission.ManageTeam,
        ["ListMembershipLog"] = Permission.ManageTeam,
        ["ChangeMemberRole"] = Permission.ManageTeam,
        ["RemoveMember"] = Permission.ManageTeam,
        ["CreateInvitation"] = Permission.ManageTeam,
        ["RevokeInvitation"] = Permission.ManageTeam,
    };

    private static readonly OrganizationRole[] AllRoles = Enum.GetValues<OrganizationRole>();

    [Fact]
    public void Every_guarded_route_belongs_to_a_permission()
    {
        GuardedRoutes().Keys.Should().BeEquivalentTo(Routes.Keys, "every route a role check guards is in the catalogue, and nothing else");
    }

    [Fact]
    public void A_role_has_a_route_s_permission_exactly_when_the_route_s_check_allows_the_role()
    {
        foreach (var (operation, check) in GuardedRoutes())
        {
            var allowed = check switch
            {
                "RequireHiringRole" => HiringAuthorization.Writers,
                "RequireOrganizationAdmin" => HiringAuthorization.Administrators,
                _ => throw new InvalidOperationException($"Unknown check {check}."),
            };
            foreach (var role in AllRoles)
            {
                RolePermissions.Has(role, Routes[operation]).Should().Be(
                    allowed.Contains(role), $"{operation} is guarded by {check}, and {role} {(allowed.Contains(role) ? "passes" : "does not pass")} it");
            }
        }
    }

    [Fact]
    public void Team_permissions_follow_the_team_policy()
    {
        foreach (var role in AllRoles)
        {
            RolePermissions.Has(role, Permission.ManageOwners).Should().Be(TeamPolicy.MayAssign(role, OrganizationRole.Owner), role.ToString());
            var managesSomeone = AllRoles.Any(other => other != OrganizationRole.Owner && TeamPolicy.MayAssign(role, other));
            RolePermissions.Has(role, Permission.ManageTeam).Should().Be(
                HiringAuthorization.Administrators.Contains(role) && managesSomeone, role.ToString());
        }
    }

    [Fact]
    public void Every_member_sees_hiring_data()
    {
        AllRoles.Should().OnlyContain(role => RolePermissions.Has(role, Permission.ViewHiring));
    }

    [Fact]
    public void Every_permission_is_listed_once_and_backed_by_a_route_or_a_rule()
    {
        RolePermissions.Permissions.Select(p => p.Permission).Should().BeEquivalentTo(Enum.GetValues<Permission>())
            .And.OnlyHaveUniqueItems();
        Permission[] byRule = [Permission.ViewHiring, Permission.ManageOwners];
        Enum.GetValues<Permission>().Except(byRule).Should().OnlyContain(permission => Routes.ContainsValue(permission));
    }

    [Fact]
    public void Each_built_in_role_is_described_once_most_access_first()
    {
        RolePermissions.Roles.Select(r => r.Role).Should().Equal(AllRoles);
        RolePermissions.Roles.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Description));
        RolePermissions.Roles.Select(r => r.Permissions.Count).Should().BeInDescendingOrder();
    }

    // Each fluent route statement in the endpoint sources that names an operation and calls a role check.
    private static Dictionary<string, string> GuardedRoutes()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(RepositoryFiles.PathTo("apps", "api", "Endpoints"), "*.cs"))
        {
            var code = CommentLine().Replace(File.ReadAllText(file), string.Empty);
            foreach (var statement in code.Split(';'))
            {
                var name = OperationName().Match(statement);
                var check = RoleCheck().Match(statement);
                if (!check.Success)
                {
                    continue;
                }

                // A guarded route without an operation name would slip past the catalogue, so it fails instead.
                name.Success.Should().BeTrue($"every route a role check guards has a WithName, and one in {Path.GetFileName(file)} has none:{statement}");
                routes.Add(name.Groups[1].Value, check.Groups[1].Value);
            }
        }

        return routes;
    }

    [GeneratedRegex(@"^\s*//.*$", RegexOptions.Multiline)]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"\.WithName\(""(\w+)""\)")]
    private static partial Regex OperationName();

    [GeneratedRegex(@"\.(RequireHiringRole|RequireOrganizationAdmin)\(\)")]
    private static partial Regex RoleCheck();
}
