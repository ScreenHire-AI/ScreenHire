using System.Text.RegularExpressions;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// The server checks permissions, not role names (ADR-0014). Every route that calls <c>RequirePermission</c> in
/// <c>apps/api/Endpoints</c> names the permission this table expects, so a new route without a permission, or a route
/// that moves to another permission, fails here. The built-in roles hold exactly the sets they had when the checks were
/// role names (Owner, Admin and Recruiter wrote hiring data; Owner and Admin managed the organization; only an Owner
/// managed Owners), so no existing member's access changed.
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
        ["SetJobHiringTeam"] = Permission.EditJobs,
        ["CreateApplication"] = Permission.AddCandidates,
        ["CreateResumeUpload"] = Permission.AddCandidates,
        ["MoveApplication"] = Permission.MoveCandidates,
        ["AddApplicationNote"] = Permission.AddNotesAndTags,
        ["EditApplicationNote"] = Permission.AddNotesAndTags,
        ["DeleteApplicationNote"] = Permission.AddNotesAndTags,
        ["AddApplicationTag"] = Permission.AddNotesAndTags,
        ["RemoveApplicationTag"] = Permission.AddNotesAndTags,
        ["CreateApplicationTask"] = Permission.CreateTasks,
        ["CompleteTask"] = Permission.CompleteTasks,
        ["ReopenTask"] = Permission.CompleteTasks,
        ["SendEmails"] = Permission.EmailCandidates,
        ["KeepInMind"] = Permission.MoveCandidates,
        ["RemoveFromTalentPool"] = Permission.MoveCandidates,
        ["PreviewTalentPoolInvitation"] = Permission.EmailCandidates,
        ["InviteToApply"] = Permission.EmailCandidates,
        ["AddFromTalentPool"] = Permission.AddCandidates,
        ["SubmitScorecard"] = Permission.FillInScorecards,
        ["RequestScorecard"] = Permission.CreateTasks,
        ["DismissVerificationAssessment"] = Permission.ReviewVerification,
        ["ExplainVerificationReason"] = Permission.ReviewVerification,
        ["UndoVerificationReasonExplanation"] = Permission.ReviewVerification,
        ["AddVerificationNote"] = Permission.ReviewVerification,
        ["RequestIdentityVerification"] = Permission.ReviewVerification,
        ["RunManualAutomation"] = Permission.RunAutomations,
        ["CreateHiringTemplate"] = Permission.EditHiringTemplates,
        ["UpdateHiringTemplate"] = Permission.EditHiringTemplates,
        ["SetHiringTemplateTeam"] = Permission.EditHiringTemplates,
        ["CreateHiringTemplateVersion"] = Permission.EditHiringTemplates,
        ["UpdateHiringTemplateVersion"] = Permission.EditHiringTemplates,
        ["PublishHiringTemplateVersion"] = Permission.EditHiringTemplates,
        ["ArchiveHiringTemplate"] = Permission.EditHiringTemplates,
        ["UnarchiveHiringTemplate"] = Permission.EditHiringTemplates,
        ["CreateScorecardTemplate"] = Permission.EditHiringTemplates,
        ["UpdateScorecardTemplate"] = Permission.EditHiringTemplates,
        ["ArchiveScorecardTemplate"] = Permission.EditHiringTemplates,
        ["UnarchiveScorecardTemplate"] = Permission.EditHiringTemplates,
        ["CreateEmailTemplate"] = Permission.EditEmailTemplates,
        ["UpdateEmailTemplate"] = Permission.EditEmailTemplates,
        ["DeleteEmailTemplate"] = Permission.EditEmailTemplates,
        ["ArchiveEmailTemplate"] = Permission.EditEmailTemplates,
        ["UnarchiveEmailTemplate"] = Permission.EditEmailTemplates,
        ["SendTestEmail"] = Permission.EditEmailTemplates,
        ["UpdateOrganization"] = Permission.EditSettings,
        ["UpdateCareersEmbedding"] = Permission.EditSettings,
        ["UpdateOrganizationBranding"] = Permission.EditSettings,
        ["CreateBrandImageUpload"] = Permission.EditSettings,
        ["VerifyBrandImageUpload"] = Permission.EditSettings,
        ["UpdateOrganizationVerificationSettings"] = Permission.EditSettings,
        ["UpdateOrganizationTalentPoolSettings"] = Permission.EditSettings,
        ["ListApiKeys"] = Permission.ManageApiKeys,
        ["CreateApiKey"] = Permission.ManageApiKeys,
        ["GetApiKey"] = Permission.ManageApiKeys,
        ["RevokeApiKey"] = Permission.ManageApiKeys,
        ["GetApiKeysHistory"] = Permission.ManageApiKeys,
        ["GetApiKeysLastUpdate"] = Permission.ManageApiKeys,
        ["ListWebhookEndpoints"] = Permission.ManageWebhooks,
        ["CreateWebhookEndpoint"] = Permission.ManageWebhooks,
        ["GetWebhookEndpoint"] = Permission.ManageWebhooks,
        ["UpdateWebhookEndpoint"] = Permission.ManageWebhooks,
        ["DeleteWebhookEndpoint"] = Permission.ManageWebhooks,
        ["RotateWebhookSecret"] = Permission.ManageWebhooks,
        ["ListWebhookDeliveries"] = Permission.ManageWebhooks,
        ["ReplayWebhookDelivery"] = Permission.ManageWebhooks,
        ["StartSlackInstall"] = Permission.ManageIntegrations,
        ["ListSlackChannels"] = Permission.ManageIntegrations,
        ["UpdateSlackChannel"] = Permission.ManageIntegrations,
        ["SendSlackTestMessage"] = Permission.ManageIntegrations,
        ["DisconnectSlack"] = Permission.ManageIntegrations,
        ["ListInvitations"] = Permission.ManageTeam,
        ["ListMembershipLog"] = Permission.ManageTeam,
        ["GetTeamHistory"] = Permission.ManageTeam,
        ["GetTeamLastUpdate"] = Permission.ManageTeam,
        ["ChangeMemberRole"] = Permission.ManageTeam,
        ["ChangeMemberJobAccess"] = Permission.ManageTeam,
        ["RemoveMember"] = Permission.ManageTeam,
        ["CreateInvitation"] = Permission.ManageTeam,
        ["RevokeInvitation"] = Permission.ManageTeam,
        ["CreateRole"] = Permission.ManageTeam,
        ["UpdateRole"] = Permission.ManageTeam,
        ["DeleteRole"] = Permission.ManageTeam,
    };

    private static readonly OrganizationRole[] BuiltInRoles =
        [OrganizationRole.Owner, OrganizationRole.Admin, OrganizationRole.Recruiter, OrganizationRole.Viewer];

    [Fact]
    public void Every_guarded_route_requires_its_permission()
    {
        GuardedRoutes().Should().BeEquivalentTo(Routes, "every route a permission guards is in the table, with the same permission");
    }

    [Fact]
    public void Built_in_roles_keep_exactly_the_access_they_had()
    {
        // Written out, not derived: these are the sets the role-name checks allowed before custom roles, plus Create tasks,
        // which came with the task route and went to every role that writes hiring data (custom roles do not get it), and Manage
        // integrations, which came with Slack and went to Owners and Admins.
        Permission[] viewer = [Permission.ViewHiring];
        Permission[] recruiter =
        [
            .. viewer, Permission.EditJobs, Permission.AddCandidates, Permission.MoveCandidates, Permission.AddNotesAndTags,
            Permission.CreateTasks, Permission.CompleteTasks, Permission.EmailCandidates, Permission.FillInScorecards, Permission.ReviewVerification,
            Permission.RunAutomations, Permission.EditHiringTemplates,
        ];
        Permission[] admin =
        [
            .. recruiter, Permission.DeleteAnyNote, Permission.EditEmailTemplates, Permission.EditSettings, Permission.ManageApiKeys, Permission.ManageWebhooks,
            Permission.ManageIntegrations, Permission.ManageTeam,
        ];
        Permission[] owner = [.. admin, Permission.ManageOwners];

        RolePermissions.Of(OrganizationRole.Owner).Should().BeEquivalentTo(owner);
        RolePermissions.Of(OrganizationRole.Admin).Should().BeEquivalentTo(admin);
        RolePermissions.Of(OrganizationRole.Recruiter).Should().BeEquivalentTo(recruiter);
        RolePermissions.Of(OrganizationRole.Viewer).Should().BeEquivalentTo(viewer);
    }

    [Fact]
    public void An_api_key_with_the_write_scope_does_the_hiring_writes_and_nothing_more()
    {
        RolePermissions.HiringWrites.Should().BeEquivalentTo(
            RolePermissions.Of(OrganizationRole.Recruiter).Except([Permission.ViewHiring]),
            "a Write key passes exactly where the Recruiter's writes do, as RequireHiringRole let it through before");
    }

    [Fact]
    public void Team_permissions_follow_the_team_policy()
    {
        foreach (var role in BuiltInRoles)
        {
            var permissions = RolePermissions.Of(role);
            RolePermissions.Has(role, Permission.ManageOwners).Should().Be(
                TeamPolicy.MayAssign(permissions, RolePermissions.Of(OrganizationRole.Owner)), role.ToString());
            var managesSomeone = BuiltInRoles.Any(other => other != OrganizationRole.Owner && TeamPolicy.MayAssign(permissions, RolePermissions.Of(other)));
            RolePermissions.Has(role, Permission.ManageTeam).Should().Be(managesSomeone, role.ToString());
        }
    }

    [Fact]
    public void Every_member_sees_hiring_data()
    {
        BuiltInRoles.Should().OnlyContain(role => RolePermissions.Has(role, Permission.ViewHiring));
        RolePermissions.ForCustom([]).Should().Contain(Permission.ViewHiring);
        RolePermissions.Permissions.Where(p => p.EveryRole).Select(p => p.Permission).Should().Equal(Permission.ViewHiring);
    }

    [Fact]
    public void Only_managing_owners_is_owner_only_and_a_custom_role_never_holds_it()
    {
        RolePermissions.Permissions.Where(p => p.OwnerOnly).Select(p => p.Permission).Should().Equal(Permission.ManageOwners);
        RolePermissions.ForCustom(Enum.GetValues<Permission>()).Should().NotContain(Permission.ManageOwners)
            .And.BeEquivalentTo(RolePermissions.Of(OrganizationRole.Admin), "a custom role can hold anything but Owner-only permissions");
        RolePermissions.Assignable.Should().NotContain(Permission.ManageOwners);
    }

    [Fact]
    public void Every_permission_is_listed_once_and_backed_by_a_route_or_a_rule()
    {
        RolePermissions.Permissions.Select(p => p.Permission).Should().BeEquivalentTo(Enum.GetValues<Permission>())
            .And.OnlyHaveUniqueItems();
        // Seeing hiring data has no write route, Manage Owners is TeamPolicy's, and deleting anyone's note is NotePolicy's.
        Permission[] byRule = [Permission.ViewHiring, Permission.ManageOwners, Permission.DeleteAnyNote];
        Enum.GetValues<Permission>().Except(byRule).Should().OnlyContain(permission => Routes.ContainsValue(permission));
    }

    [Fact]
    public void Each_built_in_role_is_described_once_most_access_first()
    {
        RolePermissions.Roles.Select(r => r.Role).Should().Equal(BuiltInRoles);
        RolePermissions.Roles.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Description));
        RolePermissions.Roles.Select(r => r.Permissions.Count).Should().BeInDescendingOrder();
    }

    [Fact]
    public void A_custom_role_has_no_built_in_permissions()
    {
        var read = () => RolePermissions.Of(OrganizationRole.Custom);
        read.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData(" Viewer ", true)]
    [InlineData("ADMIN", true)]
    [InlineData("Interviewer", false)]
    [InlineData("Custom", false)]
    public void Built_in_names_are_recognized_ignoring_case(string name, bool builtIn) =>
        RolePermissions.IsBuiltInName(name).Should().Be(builtIn);

    [Fact]
    public void A_custom_role_is_summarized_from_its_permissions_in_catalogue_order()
    {
        RolePermissions.Summarize([Permission.FillInScorecards, Permission.ViewHiring, Permission.AddNotesAndTags])
            .Should().Be("See jobs and candidates, add notes and tags and fill in scorecards.");
        RolePermissions.Summarize([Permission.ViewHiring]).Should().Be("See jobs and candidates.");
        RolePermissions.Summarize([Permission.ViewHiring, Permission.ManageApiKeys]).Should().Be("See jobs and candidates and manage API keys.");
    }

    // Each fluent route statement in the endpoint sources that names an operation and requires a permission.
    private static Dictionary<string, Permission> GuardedRoutes()
    {
        var routes = new Dictionary<string, Permission>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(RepositoryFiles.PathTo("apps", "api", "Endpoints"), "*.cs"))
        {
            var code = CommentLine().Replace(File.ReadAllText(file), string.Empty);
            foreach (var statement in code.Split(';'))
            {
                var name = OperationName().Match(statement);
                var check = PermissionCheck().Match(statement);
                if (!check.Success)
                {
                    continue;
                }

                // A guarded route without an operation name would slip past the table, so it fails instead.
                name.Success.Should().BeTrue($"every route a permission guards has a WithName, and one in {Path.GetFileName(file)} has none:{statement}");
                routes.Add(name.Groups[1].Value, Enum.Parse<Permission>(check.Groups[1].Value));
            }
        }

        return routes;
    }

    [GeneratedRegex(@"^\s*//.*$", RegexOptions.Multiline)]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"\.WithName\(""(\w+)""\)")]
    private static partial Regex OperationName();

    [GeneratedRegex(@"\.RequirePermission\(Permission\.(\w+)\)")]
    private static partial Regex PermissionCheck();
}
