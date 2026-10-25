namespace ScreenHire.Domain.Organizations;

/// <summary>
/// Something a role lets a person do. The ids are stable: custom roles store them. Names and descriptions are in
/// <see cref="RolePermissions"/>.
/// </summary>
public enum Permission
{
    ViewHiring,
    EditJobs,
    AddCandidates,
    MoveCandidates,
    AddNotesAndTags,
    CompleteTasks,
    EmailCandidates,
    FillInScorecards,
    ReviewIntegrity,
    RunAutomations,
    EditHiringTemplates,
    EditEmailTemplates,
    EditSettings,
    ManageApiKeys,
    ManageWebhooks,
    ManageTeam,
    ManageOwners,
}

/// <summary>The groups the Roles page shows permissions in.</summary>
public enum PermissionGroup
{
    Hiring,
    Templates,
    Organization,
}

/// <summary>A permission as people read it: its group, a short verb on a thing, and one line on what it covers.</summary>
public sealed record PermissionDefinition(Permission Permission, PermissionGroup Group, string Name, string Description);

/// <summary>A built-in role: one line on what it is for, and the permissions it has.</summary>
public sealed record RoleDefinition(OrganizationRole Role, string Description, IReadOnlySet<Permission> Permissions);

/// <summary>
/// The permission catalogue: every named permission and the set each built-in role has. It describes exactly what the
/// server allows today, where the checks are the API's <c>HiringAuthorization</c> (hiring writes for Owners, Admins and
/// Recruiters; the organization's settings, API keys, webhooks, email templates and team for Owners and Admins) and
/// <see cref="TeamPolicy"/> (only an Owner manages Owners). Every member reads hiring data. The catalogue does not enforce
/// anything yet; <c>RolePermissionsTests</c> holds it to those checks route by route.
/// </summary>
public static class RolePermissions
{
    /// <summary>Every permission, in the order the Roles page lists them, grouped.</summary>
    public static IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(Permission.ViewHiring, PermissionGroup.Hiring, "See jobs and candidates",
            "Jobs, candidates, their files, scorecards and integrity results."),
        new(Permission.EditJobs, PermissionGroup.Hiring, "Edit jobs",
            "Create, edit, post and close jobs, and change their workflows."),
        new(Permission.AddCandidates, PermissionGroup.Hiring, "Add candidates", "Add a candidate to a job by hand."),
        new(Permission.MoveCandidates, PermissionGroup.Hiring, "Move candidates", "Move a candidate to another stage."),
        new(Permission.AddNotesAndTags, PermissionGroup.Hiring, "Add notes and tags", "Note and tag a candidate."),
        new(Permission.CompleteTasks, PermissionGroup.Hiring, "Complete tasks", "Mark a candidate's task done."),
        new(Permission.EmailCandidates, PermissionGroup.Hiring, "Email candidates", "Send an email to one candidate or a selection."),
        new(Permission.FillInScorecards, PermissionGroup.Hiring, "Fill in scorecards", "Score a candidate at a stage."),
        new(Permission.ReviewIntegrity, PermissionGroup.Hiring, "Review integrity results",
            "Dismiss a result, add a review note or ask for identity verification."),
        new(Permission.RunAutomations, PermissionGroup.Hiring, "Run automations", "Run a manual automation on a candidate."),
        new(Permission.EditHiringTemplates, PermissionGroup.Templates, "Edit hiring templates",
            "Create, edit and publish hiring templates."),
        new(Permission.EditEmailTemplates, PermissionGroup.Templates, "Edit email templates",
            "Create, edit and delete email templates, and send a test."),
        new(Permission.EditSettings, PermissionGroup.Organization, "Edit settings",
            "The organization's name, branding and integrity settings."),
        new(Permission.ManageApiKeys, PermissionGroup.Organization, "Manage API keys", "Create, see and revoke API keys."),
        new(Permission.ManageWebhooks, PermissionGroup.Organization, "Manage webhooks",
            "Add, change and remove webhook endpoints, and replay deliveries."),
        new(Permission.ManageTeam, PermissionGroup.Organization, "Manage the team",
            "Invite people, change roles and remove members, except Owners."),
        new(Permission.ManageOwners, PermissionGroup.Organization, "Manage Owners",
            "Make someone an Owner, or change or remove an Owner."),
    ];

    private static readonly Permission[] Reading = [Permission.ViewHiring];

    private static readonly Permission[] HiringWrites =
    [
        Permission.EditJobs, Permission.AddCandidates, Permission.MoveCandidates, Permission.AddNotesAndTags,
        Permission.CompleteTasks, Permission.EmailCandidates, Permission.FillInScorecards, Permission.ReviewIntegrity,
        Permission.RunAutomations, Permission.EditHiringTemplates,
    ];

    private static readonly Permission[] Administration =
    [
        Permission.EditEmailTemplates, Permission.EditSettings, Permission.ManageApiKeys, Permission.ManageWebhooks,
        Permission.ManageTeam,
    ];

    /// <summary>The built-in roles, most access first.</summary>
    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new(OrganizationRole.Owner, "Everything, including making and removing other Owners.",
            new HashSet<Permission>([.. Reading, .. HiringWrites, .. Administration, Permission.ManageOwners])),
        new(OrganizationRole.Admin,
            "Everything a Recruiter does, plus settings, email templates, API keys, webhooks and the team.",
            new HashSet<Permission>([.. Reading, .. HiringWrites, .. Administration])),
        new(OrganizationRole.Recruiter,
            "Works the jobs: moves candidates, writes notes and emails, fills in scorecards, edits jobs and hiring templates.",
            new HashSet<Permission>([.. Reading, .. HiringWrites])),
        new(OrganizationRole.Viewer, "Sees jobs and candidates and changes nothing.", new HashSet<Permission>(Reading)),
    ];

    /// <summary>Whether <paramref name="role"/> has <paramref name="permission"/>.</summary>
    public static bool Has(OrganizationRole role, Permission permission) =>
        Roles.Single(definition => definition.Role == role).Permissions.Contains(permission);
}
