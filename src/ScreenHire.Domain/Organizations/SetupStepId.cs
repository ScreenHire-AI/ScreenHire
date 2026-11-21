namespace ScreenHire.Domain.Organizations;

/// <summary>
/// A step of the setup checklist in the dashboard's notification bell (docs/architecture/notifications.md). The steps tick
/// themselves from the organization's own data; the server keeps only which ones the organization said it does not need.
/// Stored by name, so adding a step never renumbers the ones already skipped.
/// </summary>
public enum SetupStepId
{
    Branding,
    Job,
    Team,
    Email,
}
