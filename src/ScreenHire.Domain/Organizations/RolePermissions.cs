using System.Diagnostics.CodeAnalysis;

namespace ScreenHire.Domain.Organizations;

/// <summary>
/// Something a role lets a person do. The ids are stable: custom roles store them. Names and descriptions are in
/// <see cref="RolePermissions"/>.
/// </summary>
// CA1711 keeps the "Permission" suffix for .NET's code access security types, which .NET no longer has. This is the
// product's word for it, in the API contract and on the Roles page.
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The product's term; not a code access security type.")]
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

/// <summary>
/// A permission as people read it: its group, a short verb on a thing, and one line on what it covers.
/// <see cref="OwnerOnly"/> permissions never go into a custom role; <see cref="EveryRole"/> ones are in every role.
/// </summary>
public sealed record PermissionDefinition(
    Permission Permission, PermissionGroup Group, string Name, string Description, bool OwnerOnly = false, bool EveryRole = false);

/// <summary>A built-in role: one line on what it is for, and the permissions it has.</summary>
public sealed record RoleDefinition(OrganizationRole Role, string Description, IReadOnlySet<Permission> Permissions);

/// <summary>
/// The permission catalogue: every named permission and the set each built-in role has. The API checks these, never
/// role names (ADR-0014): every route that changes data names one permission (<c>RequirePermission</c>), and
/// <see cref="TeamPolicy"/> compares the permissions of the person acting with those of the role they give. Every member
/// reads hiring data. <c>RolePermissionsTests</c> holds the routes to their permissions and the built-in roles to the
/// exact sets they had before custom roles.
/// </summary>
public static class RolePermissions
{
    /// <summary>Every permission, in the order the Roles page lists them, grouped.</summary>
    public static IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(Permission.ViewHiring, PermissionGroup.Hiring, "See jobs and candidates",
            "Jobs, candidates, their files, scorecards and integrity results.", EveryRole: true),
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
            "Invite people, change roles and remove members, except Owners, and make roles."),
        new(Permission.ManageOwners, PermissionGroup.Organization, "Manage Owners",
            "Make someone an Owner, or change or remove an Owner.", OwnerOnly: true),
    ];

    private static readonly Dictionary<Permission, PermissionDefinition> ByPermission = Permissions.ToDictionary(p => p.Permission);

    private static readonly Permission[] Reading = [Permission.ViewHiring];

    /// <summary>
    /// The hiring writes: what Recruiters do, and what an API key with the Write scope may do (ADR-0011). A person who
    /// creates a Write key needs all of them, so a key never carries more than its creator's role.
    /// </summary>
    public static IReadOnlySet<Permission> HiringWrites { get; } = new HashSet<Permission>(
    [
        Permission.EditJobs, Permission.AddCandidates, Permission.MoveCandidates, Permission.AddNotesAndTags,
        Permission.CompleteTasks, Permission.EmailCandidates, Permission.FillInScorecards, Permission.ReviewIntegrity,
        Permission.RunAutomations, Permission.EditHiringTemplates,
    ]);

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

    /// <summary>The permissions a custom role may hold: all but the Owner-only ones.</summary>
    public static IReadOnlySet<Permission> Assignable { get; } =
        new HashSet<Permission>(Permissions.Where(p => !p.OwnerOnly).Select(p => p.Permission));

    private static readonly Permission[] InEveryRole = [.. Permissions.Where(p => p.EveryRole).Select(p => p.Permission)];

    /// <summary>The definition of <paramref name="permission"/>.</summary>
    public static PermissionDefinition Definition(Permission permission) => ByPermission[permission];

    /// <summary>The permissions of a built-in role. A custom role's are its own (<see cref="ForCustom"/>).</summary>
    public static IReadOnlySet<Permission> Of(OrganizationRole role) =>
        Roles.SingleOrDefault(definition => definition.Role == role)?.Permissions
        ?? throw new ArgumentException("A custom role has no built-in permissions; read its own.", nameof(role));

    /// <summary>
    /// What a custom role with <paramref name="permissions"/> may do: those permissions without any Owner-only one, and
    /// with the ones every role has. Applied on the way in and on the way out, so a stored row can never grant more.
    /// </summary>
    public static IReadOnlySet<Permission> ForCustom(IEnumerable<Permission> permissions) =>
        new HashSet<Permission>(permissions.Where(Assignable.Contains).Concat(InEveryRole));

    /// <summary>Whether <paramref name="role"/> has <paramref name="permission"/>.</summary>
    public static bool Has(OrganizationRole role, Permission permission) => Of(role).Contains(permission);

    /// <summary>Whether <paramref name="name"/> is a built-in role's name, ignoring case and surrounding spaces.</summary>
    public static bool IsBuiltInName(string name) =>
        Roles.Any(definition => string.Equals(definition.Role.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One line on what a custom role can do, from its permission names in catalogue order: "See jobs and candidates,
    /// add notes and tags and fill in scorecards." Generated, so it always says what the role allows.
    /// </summary>
    public static string Summarize(IEnumerable<Permission> permissions)
    {
        var held = permissions.ToHashSet();
        var phrases = Permissions.Where(p => held.Contains(p.Permission))
            .Select((p, index) => index == 0 ? p.Name : char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToList();
        return phrases.Count switch
        {
            0 => "Nothing yet.",
            1 => phrases[0] + ".",
            _ => string.Join(", ", phrases[..^1]) + " and " + phrases[^1] + ".",
        };
    }
}
