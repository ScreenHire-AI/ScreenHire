using FluentValidation;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's integrity settings. <c>llmConsistencyCheck</c> turns on the LLM consistency check, which sends the
/// resume, answers and any LinkedIn export to the deployment's model provider; it is off until an admin turns it on.
/// <c>monthlyTokenBudget</c> caps the tokens it may use per calendar month (UTC); null uses the deployment's default.
/// <c>tokensUsedThisMonth</c> is what the check has used so far this month. <c>lastUpdated</c> is the settings' last
/// change ("by ScreenHire" while they are the defaults); their history is <c>/integrity-settings/history</c>.
/// </summary>
public sealed record OrganizationIntegritySettings(bool LlmConsistencyCheck, long? MonthlyTokenBudget, long TokensUsedThisMonth, LastUpdate LastUpdated);

/// <summary>Turns the LLM consistency check on or off and sets its monthly token budget (null for the deployment's default).</summary>
public sealed record UpdateIntegritySettingsRequest(bool LlmConsistencyCheck, long? MonthlyTokenBudget = null);

/// <summary>
/// The signed-in user's organization. <c>slug</c> names its careers pages and public API routes and does not change after
/// onboarding; <c>careersUrl</c> is its careers board on the configured careers domain. <c>lastUpdated</c> is the name's
/// last change, or when the organization was made; its history is <c>/v1/organization/history</c>.
/// </summary>
public sealed record OrganizationProfile(
    Guid Id, string Name, string Slug, string HomeRegion, string CareersUrl, DateTimeOffset CreatedAt, LastUpdate LastUpdated);

/// <summary>Renames the organization. Its slug, and so its careers address, stays as it is.</summary>
public sealed record UpdateOrganizationRequest(string Name);

internal sealed class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator() =>
        RuleFor(r => r.Name).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter your organization's name.")
            .MaximumLength(200);
}
