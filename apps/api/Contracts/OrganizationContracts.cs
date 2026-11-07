using FluentValidation;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's verification settings (verification-engine.md §10). <c>llmConsistencyCheck</c> turns on the LLM
/// consistency check, which sends the resume, answers and any LinkedIn export to the model provider; it is off until an admin
/// turns it on. <c>monthlyTokenBudget</c> caps the tokens it may use per calendar month (UTC) with ScreenHire's included
/// model; null uses the deployment's default, which is <c>effectiveMonthlyTokenBudget</c>. <c>budgetApplies</c> is false
/// with the organization's own key, which the provider bills. <c>tokensUsedThisMonth</c> is what the check has used so far
/// this month, either way.
/// </summary>
/// <remarks>
/// <c>modelSource</c> says whose model answers. For <c>OwnKey</c>, <c>provider</c>, <c>baseUrl</c> (OpenAI-compatible only)
/// and <c>model</c> describe it; <c>baseUrl</c> is null for a member who cannot edit settings, because its path can hold a token.
/// <c>apiKeySaved</c> says a key is held; the key itself is write-only and never returned.
/// <c>checks</c> says which groups of checks run, <c>defaultAiUse</c> is the AI-use policy a new job without a template starts
/// with, <c>deviceCheck</c> is the footing the organization asks for (null follows the region, which is always a floor),
/// <c>trustedDomains</c> and <c>trustedNetworks</c> are what stops a shared mailbox, office or partner network repeating a
/// signal, and <c>alerts</c> are the two notifications. <c>emailAvailable</c> and <c>slackConnected</c> say whether this
/// deployment can send each kind. <c>lastUpdated</c> is the settings' last change ("by ScreenHire" while they are the
/// defaults); their history is <c>/verification-settings/history</c>.
/// </remarks>
public sealed record OrganizationVerificationSettings(
    bool LlmConsistencyCheck,
    long? MonthlyTokenBudget,
    long TokensUsedThisMonth,
    LastUpdate LastUpdated,
    LlmSource ModelSource,
    ModelProvider? Provider,
    string? BaseUrl,
    string? Model,
    bool ApiKeySaved,
    bool BudgetApplies,
    long EffectiveMonthlyTokenBudget,
    VerificationChecks Checks,
    AiUsePolicy DefaultAiUse,
    DeviceCheckBasis? DeviceCheck,
    IReadOnlyList<string> TrustedDomains,
    IReadOnlyList<string> TrustedNetworks,
    VerificationAlerts Alerts,
    bool EmailAvailable,
    bool SlackConnected);

/// <summary>Which groups of checks run. A group that is off is recorded on each result it would have contributed to.</summary>
public sealed record VerificationChecks(
    bool Network = true, bool Contact = true, bool Documents = true, bool Answers = true, bool Duplicates = true, bool FormUse = true);

/// <summary>The two notifications and the channels each goes to. Slack messages are direct messages to the people who can act.</summary>
public sealed record VerificationAlerts(bool SuggestedEmail = false, bool SuggestedSlack = false, bool BudgetEmail = false, bool BudgetSlack = false);

/// <summary>
/// Replaces the organization's verification settings. <c>apiKey</c> is the one place a key travels: null keeps the saved key,
/// text replaces it. Changing the provider or the address needs a new key, and a change to the key, provider, address or
/// model is tested before it is saved. <c>modelSource</c> <c>Included</c> erases the saved key and provider fields.
/// </summary>
public sealed record UpdateVerificationSettingsRequest(
    bool LlmConsistencyCheck,
    long? MonthlyTokenBudget = null,
    LlmSource ModelSource = LlmSource.Included,
    ModelProvider? Provider = null,
    string? BaseUrl = null,
    string? Model = null,
    string? ApiKey = null,
    VerificationChecks? Checks = null,
    AiUsePolicy DefaultAiUse = AiUsePolicy.AllowedWithDisclosure,
    DeviceCheckBasis? DeviceCheck = null,
    IReadOnlyList<string>? TrustedDomains = null,
    IReadOnlyList<string>? TrustedNetworks = null,
    VerificationAlerts? Alerts = null);

/// <summary>
/// An own-key endpoint to list models for or test. A null <c>apiKey</c> uses the saved key when <c>provider</c> and
/// <c>baseUrl</c> match what is saved, so the page never needs the key back. <c>model</c> is needed to test.
/// </summary>
public sealed record VerificationModelRequest(ModelProvider Provider, string? BaseUrl = null, string? ApiKey = null, string? Model = null);

/// <summary>
/// The models the provider lists for the key. <c>listed</c> is false when it offers no list or could not be asked; type
/// the model id then.
/// </summary>
public sealed record VerificationModelList(bool Listed, IReadOnlyList<string> Models);

/// <summary>Whether the endpoint answered a tiny request with the key and model. <c>problem</c> is a word, never the provider's text.</summary>
public sealed record VerificationModelTest(bool Ok, ModelProblem? Problem);

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
