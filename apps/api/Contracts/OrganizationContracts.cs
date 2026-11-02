using FluentValidation;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's verification settings. <c>llmConsistencyCheck</c> turns on the LLM consistency check, which sends the
/// resume, answers and any LinkedIn export to the deployment's model provider; it is off until an admin turns it on.
/// <c>monthlyTokenBudget</c> caps the tokens it may use per calendar month (UTC); null uses the deployment's default.
/// <c>tokensUsedThisMonth</c> is what the check has used so far this month. <c>lastUpdated</c> is the settings' last
/// change ("by ScreenHire" while they are the defaults); their history is <c>/verification-settings/history</c>.
/// </summary>
public sealed record OrganizationVerificationSettings(bool LlmConsistencyCheck, long? MonthlyTokenBudget, long TokensUsedThisMonth, LastUpdate LastUpdated);

/// <summary>Turns the LLM consistency check on or off and sets its monthly token budget (null for the deployment's default).</summary>
public sealed record UpdateVerificationSettingsRequest(bool LlmConsistencyCheck, long? MonthlyTokenBudget = null);

/// <summary>
/// The signed-in user's organization. <c>slug</c> names its careers pages and public API routes and does not change after
/// onboarding; <c>careersUrl</c> is its careers board on the configured careers domain, where <c>{careersUrl}/embed.js</c>
/// is the embeddable widget's script. <c>questionsPerPage</c> is how many questions candidates see on each page of a
/// questionnaire that has no sections. <c>embedOrigins</c> are the sites allowed to show the careers pages in a frame
/// (origins such as <c>https://www.example.com</c>, in the order they were added). <c>lastUpdated</c> is the last change
/// to the name, the questions per page or the sites, or when the organization was made; its history is
/// <c>/v1/organization/history</c>.
/// </summary>
public sealed record OrganizationProfile(
    Guid Id, string Name, string Slug, string HomeRegion, string CareersUrl, DateTimeOffset CreatedAt, LastUpdate LastUpdated,
    int QuestionsPerPage, IReadOnlyList<string> EmbedOrigins);

/// <summary>
/// Replaces the sites allowed to embed the careers pages, at most 20. Each is an https site's address; a bare name gets
/// https and a page's address keeps only its site, so <c>www.example.com/careers</c> saves as
/// <c>https://www.example.com</c>. Duplicates are dropped and the order is kept. An empty list lets no other site frame
/// the careers pages.
/// </summary>
public sealed record UpdateCareersEmbeddingRequest(IReadOnlyList<string> Origins);

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
