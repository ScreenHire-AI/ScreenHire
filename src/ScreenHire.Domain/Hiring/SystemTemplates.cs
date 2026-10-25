namespace ScreenHire.Domain.Hiring;

/// <summary>
/// Ids of the system templates the <c>AddHiringModel</c> migration ships, and of later versions of them. Each version's
/// definition lives only in the migration that added it, because a migration must not change once it has run; a change
/// to a system template is a new version in a new migration. The one rewrite in place so far is a change of storage, not
/// of content: <c>AddScorecardTemplates</c> made every version's stages name their built-in scorecard
/// (<see cref="BuiltInScorecards"/>) instead of holding v0.2 criteria.
/// </summary>
public static class SystemTemplates
{
    public static readonly Guid SoftwareEngineer = new("0199e8a0-0000-7000-8000-000000000001");

    public static readonly Guid SoftwareEngineerVersion1 = new("0199e8a0-0000-7000-8000-000000000101");

    /// <summary>Version 1 with its integrity automation's level names corrected (migration <c>AddSoftwareEngineerTemplateVersion2</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion2 = new("0199e8a0-0000-7000-8000-000000000102");

    /// <summary>Version 2 with default scorecards on Screening, Technical exercise and Interviews (migration <c>AddScorecards</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion3 = new("0199e8a0-0000-7000-8000-000000000103");

    /// <summary>Version 3 with its right-to-work automation firing on the application (migration <c>AddFollowUpQuestionnaires</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion4 = new("0199e8a0-0000-7000-8000-000000000104");

    public static readonly Guid HourlyRole = new("0199e8a0-0000-7000-8000-000000000002");

    public static readonly Guid HourlyRoleVersion1 = new("0199e8a0-0000-7000-8000-000000000201");

    /// <summary>Version 1 with default scorecards on Phone screen and Interview (migration <c>AddScorecards</c>).</summary>
    public static readonly Guid HourlyRoleVersion2 = new("0199e8a0-0000-7000-8000-000000000202");

    /// <summary>Version 2 with its right-to-work automation firing on the application (migration <c>AddFollowUpQuestionnaires</c>).</summary>
    public static readonly Guid HourlyRoleVersion3 = new("0199e8a0-0000-7000-8000-000000000203");
}
