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
    /// <summary>The highest monthly budget an organization may set, in tokens.</summary>
    public const long MaxMonthlyTokenBudget = 1_000_000_000;

    /// <summary>The longest base URL, model id and (encrypted) key the row holds.</summary>
    public const int MaxBaseUrlLength = 2000;

    public const int MaxModelLength = 200;

    public const int MaxApiKeyLength = 2000;

    public const int MaxProtectedKeyLength = 4000;

    /// <summary>The organization; one row per organization.</summary>
    public Guid OrganizationId { get; init; }

    public bool LlmConsistencyCheckEnabled { get; set; }

    /// <summary>Tokens the check may use per calendar month (UTC), or null for the deployment's default. Not used with an own key.</summary>
    public long? MonthlyTokenBudget { get; set; }

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

    /// <summary>The month (<c>yyyy-MM</c>, UTC) the 80% budget alert last went out, so it goes out once a month.</summary>
    public string? BudgetAlertMonth { get; set; }

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
    /// <summary>ScreenHire's model, within the monthly token budget.</summary>
    Included,

    /// <summary>The organization's own key and model. The token budget does not apply.</summary>
    OwnKey,
}

/// <summary>The kinds of provider an organization can bring a key for.</summary>
public enum ModelProvider
{
    Anthropic,

    /// <summary>Any endpoint that speaks the OpenAI chat API, which covers OpenAI and Azure OpenAI.</summary>
    OpenAiCompatible,
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
