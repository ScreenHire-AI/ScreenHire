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

    /// <summary>Version 1 with its verification automation's level names corrected (migration <c>AddSoftwareEngineerTemplateVersion2</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion2 = new("0199e8a0-0000-7000-8000-000000000102");

    /// <summary>Version 2 with default scorecards on Screening, Technical exercise and Interviews (migration <c>AddScorecards</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion3 = new("0199e8a0-0000-7000-8000-000000000103");

    /// <summary>Version 3 with its right-to-work automation firing on the application (migration <c>AddFollowUpQuestionnaires</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion4 = new("0199e8a0-0000-7000-8000-000000000104");

    /// <summary>Version 4 with its verification automation's name and tag saying verification (migration <c>RenameIntegrityToVerification</c>, ADR-0019).</summary>
    public static readonly Guid SoftwareEngineerVersion5 = new("0199e8a0-0000-7000-8000-000000000105");

    /// <summary>Version 5 with its automations' short names (migration <c>ShortenSystemAutomationNames</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion6 = new("0199e8a0-0000-7000-8000-000000000106");

    public static readonly Guid HourlyRole = new("0199e8a0-0000-7000-8000-000000000002");

    public static readonly Guid HourlyRoleVersion1 = new("0199e8a0-0000-7000-8000-000000000201");

    /// <summary>Version 1 with default scorecards on Phone screen and Interview (migration <c>AddScorecards</c>).</summary>
    public static readonly Guid HourlyRoleVersion2 = new("0199e8a0-0000-7000-8000-000000000202");

    /// <summary>Version 2 with its right-to-work automation firing on the application (migration <c>AddFollowUpQuestionnaires</c>).</summary>
    public static readonly Guid HourlyRoleVersion3 = new("0199e8a0-0000-7000-8000-000000000203");

    /// <summary>Version 3 with its automations' short names (migration <c>ShortenSystemAutomationNames</c>).</summary>
    public static readonly Guid HourlyRoleVersion4 = new("0199e8a0-0000-7000-8000-000000000204");

    /// <summary>The starting points of the new template screen (migration <c>AddStartingPointTemplates</c>): published version 1 each.</summary>
    public static readonly Guid OfficeRole = new("0199e8a0-0000-7000-8000-000000000003");

    public static readonly Guid OfficeRoleVersion1 = new("0199e8a0-0000-7000-8000-000000000301");

    /// <summary>Version 1 with its automations' short names (migration <c>ShortenSystemAutomationNames</c>).</summary>
    public static readonly Guid OfficeRoleVersion2 = new("0199e8a0-0000-7000-8000-000000000302");

    public static readonly Guid TradeRole = new("0199e8a0-0000-7000-8000-000000000004");

    public static readonly Guid TradeRoleVersion1 = new("0199e8a0-0000-7000-8000-000000000401");

    /// <summary>Version 1 with the Trial shift scorecard on its Trial shift stage (migration <c>AddHospitalityTemplate</c>).</summary>
    public static readonly Guid TradeRoleVersion2 = new("0199e8a0-0000-7000-8000-000000000402");

    /// <summary>Version 2 with the trial shift stage naming its meeting "trial shift" (migration <c>AddTrialShiftMeetingNames</c>).</summary>
    public static readonly Guid TradeRoleVersion3 = new("0199e8a0-0000-7000-8000-000000000403");

    /// <summary>Version 3 with its automations' short names (migration <c>ShortenSystemAutomationNames</c>).</summary>
    public static readonly Guid TradeRoleVersion4 = new("0199e8a0-0000-7000-8000-000000000404");

    public static readonly Guid HighVolume = new("0199e8a0-0000-7000-8000-000000000005");

    public static readonly Guid HighVolumeVersion1 = new("0199e8a0-0000-7000-8000-000000000501");

    /// <summary>Version 1 with its automations' short names (migration <c>ShortenSystemAutomationNames</c>).</summary>
    public static readonly Guid HighVolumeVersion2 = new("0199e8a0-0000-7000-8000-000000000502");

    /// <summary>The fourth starting point, for cafes, restaurants and bars (migration <c>AddHospitalityTemplate</c>): published version 1.</summary>
    public static readonly Guid Hospitality = new("0199e8a0-0000-7000-8000-000000000006");

    public static readonly Guid HospitalityVersion1 = new("0199e8a0-0000-7000-8000-000000000601");

    /// <summary>Version 1 with the Trial shift stage naming its meeting "trial shift" (migration <c>AddTrialShiftMeetingNames</c>).</summary>
    public static readonly Guid HospitalityVersion2 = new("0199e8a0-0000-7000-8000-000000000602");

    /// <summary>Version 2 with its automations' short names (migration <c>ShortenSystemAutomationNames</c>).</summary>
    public static readonly Guid HospitalityVersion3 = new("0199e8a0-0000-7000-8000-000000000603");

    /// <summary>Every system template, the ones each organization sees beside its own.</summary>
    public static IReadOnlyList<Guid> All { get; } = [SoftwareEngineer, HourlyRole, OfficeRole, TradeRole, HighVolume, Hospitality];
}
