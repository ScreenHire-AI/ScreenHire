namespace ScreenHire.Domain.Hiring;

/// <summary>
/// Ids of the system templates the <c>AddHiringModel</c> migration ships, and of later versions of them. Their definitions live only in that migration,
/// because a migration must not change once it has run; a change to a system template is a new version in a new
/// migration.
/// </summary>
public static class SystemTemplates
{
    public static readonly Guid SoftwareEngineer = new("0199e8a0-0000-7000-8000-000000000001");

    public static readonly Guid SoftwareEngineerVersion1 = new("0199e8a0-0000-7000-8000-000000000101");

    /// <summary>Version 1 with its integrity automation's level names corrected (migration <c>AddSoftwareEngineerTemplateVersion2</c>).</summary>
    public static readonly Guid SoftwareEngineerVersion2 = new("0199e8a0-0000-7000-8000-000000000102");

    public static readonly Guid HourlyRole = new("0199e8a0-0000-7000-8000-000000000002");

    public static readonly Guid HourlyRoleVersion1 = new("0199e8a0-0000-7000-8000-000000000201");
}
