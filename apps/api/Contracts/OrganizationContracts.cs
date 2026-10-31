using FluentValidation;
using ScreenHire.Domain.Organizations;

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
/// The organization's talent pool settings (docs/architecture/talent-pool.md). <c>permissionMonths</c> is how long a
/// candidate's permission to keep their details for future roles lasts from when they gave it: 12 by default, from
/// <c>minMonths</c> to <c>maxMonths</c>. A permission ends at the shorter of the period when it was given and this one.
/// <c>lastUpdated</c> is the settings' last change ("by ScreenHire" while they are the default); their history is
/// <c>/talent-pool-settings/history</c>.
/// </summary>
public sealed record OrganizationTalentPoolSettings(int PermissionMonths, int MinMonths, int MaxMonths, LastUpdate LastUpdated);

/// <summary>Sets how long a candidate's permission for future roles lasts, in whole months from 6 to 24.</summary>
public sealed record UpdateTalentPoolSettingsRequest(int PermissionMonths);

/// <summary>
/// The signed-in user's organization. <c>slug</c> names its careers pages and public API routes and does not change after
/// onboarding; <c>careersUrl</c> is its careers board on the configured careers domain. <c>questionsPerPage</c> is how
/// many questions candidates see on each page of a questionnaire that has no sections. <c>lastUpdated</c> is the last
/// change to the name or the questions per page, or when the organization was made; its history is
/// <c>/v1/organization/history</c>.
/// </summary>
public sealed record OrganizationProfile(
    Guid Id, string Name, string Slug, string HomeRegion, string CareersUrl, DateTimeOffset CreatedAt, LastUpdate LastUpdated,
    int QuestionsPerPage);

/// <summary>
/// Renames the organization and, when given, sets its questions per page (1 to 50); null keeps it. Its slug, and so its
/// careers address, stays as it is.
/// </summary>
public sealed record UpdateOrganizationRequest(string Name, int? QuestionsPerPage = null);

internal sealed class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator()
    {
        RuleFor(r => r.Name).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter your organization's name.")
            .MaximumLength(200);
        RuleFor(r => r.QuestionsPerPage)
            .InclusiveBetween(Organization.MinQuestionsPerPage, Organization.MaxQuestionsPerPage)
            .WithMessage($"Choose from {Organization.MinQuestionsPerPage} to {Organization.MaxQuestionsPerPage} questions a page.");
    }
}
