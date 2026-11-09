using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Verification;

/// <summary>
/// An organization's verification settings (verification-engine.md §10): which model answers the consistency check, which
/// groups of checks run, the default AI-use policy for new jobs, the device check basis, the domains and networks it
/// trusts, and two alerts. Tenant-scoped, unlike the public <c>organizations</c> row. No row means the defaults: every
/// group on, the consistency check off, ScreenHire's model, no alerts.
/// </summary>
/// <remarks>
/// The check sends the resume, answers and LinkedIn export to the model provider, a sub-processor, so it is off until the
/// organization turns it on. ScreenHire's providers are used on terms that exclude training on the data
/// (verification-engine.md §10.10); with an organization's own key, the provider handles the data under the organization's
/// own terms. The API key is held encrypted and is never returned, logged or recorded in History.
/// </remarks>
public sealed class VerificationSettings : ITenantOwned
{
    /// <summary>The highest monthly budget or cap an organization may set, in tokens.</summary>
    public const long MaxMonthlyTokens = 1_000_000_000;

    /// <summary>The highest monthly budget or cap an organization may set, in US dollars.</summary>
    public const decimal MaxMonthlyMoney = 1_000_000m;

    /// <summary>The highest price per million tokens an organization may enter for its own key, in US dollars.</summary>
    public const decimal MaxPricePerMillion = 10_000m;

    /// <summary>The longest base URL, model id and (encrypted) key the row holds.</summary>
    public const int MaxBaseUrlLength = 2000;

    public const int MaxModelLength = 200;

    public const int MaxApiKeyLength = 2000;

    public const int MaxProtectedKeyLength = 4000;

    /// <summary>The organization; one row per organization.</summary>
    public Guid OrganizationId { get; init; }

    public bool LlmConsistencyCheckEnabled { get; set; }

    /// <summary>
    /// What <see cref="MonthlyBudget"/> and <see cref="MonthlyCap"/> are counted in. One unit for both, so the two never
    /// compare tokens with dollars.
    /// </summary>
    public UsageUnit BudgetUnit { get; set; } = UsageUnit.Tokens;

    /// <summary>
    /// What the organization expects to use per calendar month (UTC), in <see cref="BudgetUnit"/>. Alerts go out at 50%, 80%
    /// and 100% of it; it stops nothing. Null: the deployment's default budget applies to ScreenHire's key, none to an own key.
    /// </summary>
    public decimal? MonthlyBudget { get; set; }

    /// <summary>
    /// The hard cap per calendar month (UTC), in <see cref="BudgetUnit"/>. At the cap the AI-based checks pause until next
    /// month or until the cap is raised; the rule-based checks keep running. Null: the deployment's default cap applies to
    /// ScreenHire's key, none to an own key. Only an Owner sets it.
    /// </summary>
    public decimal? MonthlyCap { get; set; }

    /// <summary>US dollars per million input tokens the organization's own key costs it, so usage can show a cost. Null: unknown.</summary>
    public decimal? OwnKeyInputPrice { get; set; }

    /// <summary>US dollars per million output tokens the organization's own key costs it. Null: unknown.</summary>
    public decimal? OwnKeyOutputPrice { get; set; }

    /// <summary>Whose model answers the check.</summary>
    public LlmSource LlmSource { get; set; } = LlmSource.Included;

    /// <summary>The organization's own provider; null with <see cref="LlmSource.Included"/>.</summary>
    public ModelProvider? LlmProvider { get; set; }

    /// <summary>The endpoint of an <see cref="ModelProvider.OpenAiCompatible"/> provider; null for Anthropic.</summary>
    public string? LlmBaseUrl { get; set; }

    /// <summary>The model id the organization typed or chose from the provider's own list. One model serves the whole check.</summary>
    public string? LlmModel { get; set; }

    /// <summary>The API key, protected with ASP.NET Core Data Protection (<c>LlmKeys</c>). Write-only: no contract carries it.</summary>
    public string? ProtectedLlmApiKey { get; set; }

    /// <summary>Counts key replacements, so History can say "replaced the API key" without ever holding a key.</summary>
    public int LlmKeyVersion { get; set; }

    // Which groups of checks run (CheckGroup). A group that is off is recorded on each application's run, never skipped silently.
    public bool CheckNetwork { get; set; } = true;

    public bool CheckContact { get; set; } = true;

    public bool CheckDocuments { get; set; } = true;

    public bool CheckAnswers { get; set; } = true;

    public bool CheckDuplicates { get; set; } = true;

    public bool CheckFormUse { get; set; } = true;

    /// <summary>The AI-use policy a job starts with when it does not start from a template.</summary>
    public AiUsePolicy DefaultAiUse { get; set; } = AiUsePolicy.AllowedWithDisclosure;

    /// <summary>The device check basis the organization asks for, or null to follow the region. A region's basis is a floor.</summary>
    public DeviceCheckBasis? DeviceCheck { get; set; }

    /// <summary>Email domains (lower case) whose shared mailbox does not raise a duplicate.</summary>
    public string[] TrustedDomains { get; set; } = [];

    /// <summary>Networks in CIDR form whose addresses raise no network signal and no device-only duplicate.</summary>
    public string[] TrustedNetworks { get; set; } = [];

    public bool SuggestedAlertEmail { get; set; }

    public bool SuggestedAlertSlack { get; set; }

    public bool BudgetAlertEmail { get; set; }

    public bool BudgetAlertSlack { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Does the organization use its own model?</summary>
    public bool UsesOwnKey => LlmSource == LlmSource.OwnKey;

    /// <summary>Does <paramref name="group"/> run?</summary>
    public bool Runs(CheckGroup group) => group switch
    {
        CheckGroup.Network => CheckNetwork,
        CheckGroup.Contact => CheckContact,
        CheckGroup.Documents => CheckDocuments,
        CheckGroup.Answers => CheckAnswers,
        CheckGroup.Duplicates => CheckDuplicates,
        _ => CheckFormUse,
    };

    /// <summary>What the pipeline needs from the row, with no secret in it.</summary>
    public VerificationConfig ToConfig() => VerificationConfig.From(this);
}

/// <summary>Whose model answers the consistency check.</summary>
public enum LlmSource
{
    /// <summary>ScreenHire's model, billed at the provider's price plus a margin.</summary>
    Included,

    /// <summary>The organization's own key and model. ScreenHire charges nothing for it.</summary>
    OwnKey,
}

/// <summary>What a monthly budget or cap is counted in.</summary>
public enum UsageUnit
{
    Tokens,

    /// <summary>US dollars, from each call's cost (the provider's own figure, or tokens times a configured price).</summary>
    Money,
}

/// <summary>
/// The providers an organization can bring a key for. Each is a preset (<c>ModelPresets</c>): the address and the way the key
/// is sent are filled in, and the person enters the key. Stored as text, so a new preset needs no data change.
/// </summary>
public enum ModelProvider
{
    Anthropic,

    /// <summary>Any endpoint that speaks the OpenAI chat API: the person enters the address. Shown as "Other".</summary>
    OpenAiCompatible,

    OpenAi,

    OpenRouter,

    /// <summary>Azure OpenAI: the address is the organization's own resource, and the model is its deployment name.</summary>
    AzureOpenAi,

    /// <summary>Google Gemini, through its OpenAI-compatible endpoint.</summary>
    Gemini,
}

/// <summary>
/// The settings the verification pipeline and its analyzers read: which groups run and what is trusted. A value without the
/// key, so it can sit in the analyzers' context.
/// </summary>
public sealed record VerificationConfig(
    bool Network, bool Contact, bool Documents, bool Answers, bool Duplicates, bool FormUse, TrustedSources Trusted)
{
    /// <summary>The defaults: every group on, nothing trusted.</summary>
    public static VerificationConfig Default { get; } = new(true, true, true, true, true, true, TrustedSources.None);

    public static VerificationConfig From(VerificationSettings? settings) =>
        settings is null
            ? Default
            : new VerificationConfig(
                settings.CheckNetwork, settings.CheckContact, settings.CheckDocuments, settings.CheckAnswers,
                settings.CheckDuplicates, settings.CheckFormUse, TrustedSources.From(settings.TrustedDomains, settings.TrustedNetworks));

    public bool Runs(CheckGroup group) => group switch
    {
        CheckGroup.Network => Network,
        CheckGroup.Contact => Contact,
        CheckGroup.Documents => Documents,
        CheckGroup.Answers => Answers,
        CheckGroup.Duplicates => Duplicates,
        _ => FormUse,
    };
}
