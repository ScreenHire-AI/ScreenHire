namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's careers-page branding. <c>palette</c> is what careers pages use, derived from
/// <c>brandColor</c> and contrast-checked (docs/design/design-system.md §7); both are null without a brand colour.
/// <c>brandAdjusted</c> means the colour was darkened so text on it stays readable.
/// </summary>
public sealed record OrganizationBranding(string? BrandColor, PublicBranding? Palette, bool BrandAdjusted);

/// <summary>Sets the brand colour (<c>#rrggbb</c>), or clears it with null to use ScreenHire's default.</summary>
public sealed record UpdateBrandingRequest(string? BrandColor);

/// <summary>
/// The organization's integrity settings. <c>llmConsistencyCheck</c> turns on the LLM consistency check, which sends the
/// resume, answers and any LinkedIn export to the deployment's model provider; it is off until an admin turns it on.
/// <c>monthlyTokenBudget</c> caps the tokens it may use per calendar month (UTC); null uses the deployment's default.
/// <c>tokensUsedThisMonth</c> is what the check has used so far this month.
/// </summary>
public sealed record OrganizationIntegritySettings(bool LlmConsistencyCheck, long? MonthlyTokenBudget, long TokensUsedThisMonth);

/// <summary>Turns the LLM consistency check on or off and sets its monthly token budget (null for the deployment's default).</summary>
public sealed record UpdateIntegritySettingsRequest(bool LlmConsistencyCheck, long? MonthlyTokenBudget = null);
