using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Domain.History;

/// <summary>
/// What History sees of an organization's verification settings. The row holds the encrypted API key; this view does not:
/// it knows only that a key is saved and how many times it was replaced, and the model endpoint as its host alone (a
/// full address can carry a token in its path). <c>TrackedKindsTests</c> checks no property here is secret-like.
/// </summary>
public sealed record VerificationSettingsFacts(
    bool ConsistencyCheck,
    UsageUnit BudgetUnit,
    decimal? MonthlyBudget,
    decimal? MonthlyCap,
    decimal? OwnKeyInputPrice,
    decimal? OwnKeyOutputPrice,
    LlmSource Source,
    ModelProvider? Provider,
    string? Host,
    string? Model,
    bool HasApiKey,
    int ApiKeyVersion,
    bool Network,
    bool Contact,
    bool Documents,
    bool Answers,
    bool Duplicates,
    bool FormUse,
    AiUsePolicy DefaultAiUse,
    DeviceCheckBasis? DeviceCheck,
    IReadOnlyList<string> TrustedDomains,
    IReadOnlyList<string> TrustedNetworks,
    bool SuggestedAlertEmail,
    bool SuggestedAlertSlack,
    bool BudgetAlertEmail,
    bool BudgetAlertSlack)
{
    /// <summary>The settings of an organization that never saved any: the first save compares with these.</summary>
    public static VerificationSettingsFacts Default { get; } = Of(new VerificationSettings());

    public static VerificationSettingsFacts Of(VerificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new VerificationSettingsFacts(
            settings.LlmConsistencyCheckEnabled,
            settings.BudgetUnit,
            settings.MonthlyBudget,
            settings.MonthlyCap,
            settings.OwnKeyInputPrice,
            settings.OwnKeyOutputPrice,
            settings.LlmSource,
            settings.LlmProvider,
            HostOf(settings.LlmBaseUrl),
            settings.LlmModel,
            !string.IsNullOrEmpty(settings.ProtectedLlmApiKey),
            settings.LlmKeyVersion,
            settings.CheckNetwork,
            settings.CheckContact,
            settings.CheckDocuments,
            settings.CheckAnswers,
            settings.CheckDuplicates,
            settings.CheckFormUse,
            settings.DefaultAiUse,
            settings.DeviceCheck,
            [.. settings.TrustedDomains],
            [.. settings.TrustedNetworks],
            settings.SuggestedAlertEmail,
            settings.SuggestedAlertSlack,
            settings.BudgetAlertEmail,
            settings.BudgetAlertSlack);
    }

    public bool Runs(CheckGroup group) => group switch
    {
        CheckGroup.Network => Network,
        CheckGroup.Contact => Contact,
        CheckGroup.Documents => Documents,
        CheckGroup.Answers => Answers,
        CheckGroup.Duplicates => Duplicates,
        _ => FormUse,
    };

    private static string? HostOf(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
}
