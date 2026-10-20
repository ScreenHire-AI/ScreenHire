namespace ScreenHire.Infrastructure.Integrity;

/// <summary>
/// <c>ScreenHire:Integrity</c>: the integrity engine's data sources and limits (docs/architecture/integrity-engine.md).
/// Every external source is optional; one that is not configured is skipped, and the analyzer run records why it saw
/// nothing (CLAUDE.md, "every external integration has a local adapter").
/// </summary>
public sealed class IntegrityOptions
{
    public const string Section = "ScreenHire:Integrity";

    public NetworkIntelligenceOptions Network { get; set; } = new();

    public IdentityCheckOptions Identity { get; set; } = new();

    public RetentionOptions Retention { get; set; } = new();

    public SignalNetworkOptions SignalNetwork { get; set; } = new();

    public DeviceCheckOptions DeviceCheck { get; set; } = new();
    public LlmOptions Llm { get; set; } = new();

    /// <summary>
    /// The key the API hashes device fingerprints with, per organization, before storing them
    /// (<see cref="KeyedHashing.DeviceHash"/>); at least 32 characters. Unset: device fingerprints are not stored, and
    /// device duplicates are not checked. The AppHost generates one locally.
    /// </summary>
    public string? DeviceHashKey { get; set; }
}

/// <summary>Where IP intelligence comes from. All licence-compatible, none needs an account.</summary>
public sealed class NetworkIntelligenceOptions
{
    /// <summary>
    /// Address ranges with what is known about them. Development uses the documentation ranges (RFC 5737) for the
    /// synthetic applicants; an owner can add ranges of their own, such as a VPN provider's.
    /// </summary>
    public List<IpRangeOptions> Ranges { get; set; } = [];

    /// <summary>
    /// Path to an iptoasn.com <c>ip2asn-combined.tsv</c> (or <c>-v4</c>/<c>-v6</c>) file: ASN, country and operator per
    /// range. The operator downloads it under iptoasn.com's terms (published under the PDDL 1.0); nothing is bundled.
    /// Unset: no ASN or country lookups.
    /// </summary>
    public string? Ip2AsnFile { get; set; }

    /// <summary>
    /// Path to a Tor exit list the operator fetches from the Tor Project, following its terms: one address per line, or
    /// the <c>ExitAddress</c> format. Nothing is bundled or fetched automatically.
    /// </summary>
    public string? TorExitListFile { get; set; }

    /// <summary>ASNs to treat as hosting providers, on top of the built-in list of large cloud providers.</summary>
    public List<int> HostingAsns { get; set; } = [];
}

/// <summary>One configured address range.</summary>
public sealed class IpRangeOptions
{
    /// <summary>The range in CIDR form, such as <c>198.51.100.0/24</c>.</summary>
    public string Cidr { get; set; } = "";

    public int? Asn { get; set; }

    /// <summary>The network operator's name, as shown to reviewers.</summary>
    public string? Organization { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? Country { get; set; }

    /// <summary>The range belongs to a hosting or cloud provider.</summary>
    public bool Hosting { get; set; }

    /// <summary>The range is a VPN, proxy or Tor exit.</summary>
    public AnonymizerKind? Anonymizer { get; set; }
}

public sealed class IdentityCheckOptions
{
    /// <summary>Look up MX records for email domains. Reserved test domains (example.com, .test, ...) are never looked up.</summary>
    public bool MxLookup { get; set; } = true;

    public int MxTimeoutSeconds { get; set; } = 3;

    /// <summary>Domains to treat as disposable on top of the embedded list.</summary>
    public List<string> ExtraDisposableDomains { get; set; } = [];
}

public sealed class RetentionOptions
{
    /// <summary>The longest raw network data may be kept: the careers privacy notice promises deletion within 30 days.</summary>
    public const int MaxRawNetworkDays = 30;

    /// <summary>
    /// Days to keep raw network data (IP address, browser, device hash), 1 to <see cref="MaxRawNetworkDays"/>. A host
    /// refuses to start with a longer period, because the notice candidates saw would then be false.
    /// </summary>
    public int RawNetworkDays { get; set; } = MaxRawNetworkDays;
}

/// <summary>The cross-employer Signal Network (ADR-0005). Off until legal review and DPIA sign-off.</summary>
public sealed class SignalNetworkOptions
{
    /// <summary>The feature flag. Off in every environment; the worker refuses to start with it on (see <c>SignalNetwork</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>The network HMAC key, at least 32 characters. Held in a secret store in production.</summary>
    public string? Key { get; set; }

    /// <summary>Names the key, so rotation can tell old hashes from new ones.</summary>
    public string KeyId { get; set; } = "k1";
}

/// <summary>
/// On which footing the careers page may compute a device fingerprint (docs/research/legal.md, "Device / browser
/// fingerprint"; integrity-engine.md §3). ePrivacy Art. 5(3) needs consent for fingerprinting unless it is strictly
/// necessary, and data protection authorities differ on whether fraud prevention is, so the basis is configuration per
/// country rather than code.
/// </summary>
public enum DeviceCheckBasis
{
    /// <summary>No device fingerprint at all.</summary>
    Off,

    /// <summary>After the notice, with no separate opt-in: legitimate interest, or "strictly necessary" for fraud prevention.</summary>
    Notice,

    /// <summary>Only when the candidate ticks the device-check box on the form.</summary>
    Consent,
}

/// <summary>The device-check basis per country, decided by where the job is (its locations and the countries it hires from).</summary>
public sealed class DeviceCheckOptions
{
    /// <summary>The basis for a job in no configured country, or in none at all (remote, anywhere).</summary>
    public DeviceCheckBasis Default { get; set; } = DeviceCheckBasis.Notice;

    /// <summary>
    /// The basis per ISO 3166-1 alpha-2 country code. A job in several countries takes the most protective of them:
    /// <see cref="DeviceCheckBasis.Off"/>, then <see cref="DeviceCheckBasis.Consent"/>, then
    /// <see cref="DeviceCheckBasis.Notice"/>. The API's appsettings.json lists the EU and EEA countries as
    /// <see cref="DeviceCheckBasis.Consent"/>.
    /// </summary>
    public Dictionary<string, DeviceCheckBasis> Countries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The basis for a job in <paramref name="countries"/>.</summary>
    public DeviceCheckBasis BasisFor(IEnumerable<string?> countries)
    {
        ArgumentNullException.ThrowIfNull(countries);
        var bases = countries
            .Where(c => c is { Length: 2 })
            .Select(c => Countries.TryGetValue(c!, out var basis) ? basis : Default)
            .ToList();
        if (bases.Count == 0)
        {
            return Default;
        }

        return bases.Contains(DeviceCheckBasis.Off) ? DeviceCheckBasis.Off
            : bases.Contains(DeviceCheckBasis.Consent) ? DeviceCheckBasis.Consent
            : DeviceCheckBasis.Notice;
    }
}

/// <summary>
/// The language model behind the consistency check (<c>CNT_INCONSISTENCY</c>, integrity-engine.md §10.10). Model ids
/// come from here, never from code (.claude/rules/cli-features-over-custom-layers.md): a smaller model extracts claims
/// from each source, a larger one compares them.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Which model provider answers. <see cref="LlmProvider.None"/> marks every check unavailable.</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.None;

    /// <summary>The model that lists the claims in each source. Required for <see cref="LlmProvider.Anthropic"/>.</summary>
    public string? ExtractionModel { get; set; }

    /// <summary>The model that compares the claims. Required for <see cref="LlmProvider.Anthropic"/>.</summary>
    public string? ConsistencyModel { get; set; }

    /// <summary>The most tokens one response may use.</summary>
    public int MaxOutputTokens { get; set; } = 8000;

    /// <summary>How long one call may take before the check is recorded as unavailable.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Tokens per organization per calendar month (UTC) when the organization sets no budget of its own.</summary>
    public long DefaultMonthlyTokenBudget { get; set; } = 2_000_000;

    public AnthropicOptions Anthropic { get; set; } = new();
}

public enum LlmProvider
{
    /// <summary>No model: the check is recorded as unavailable, never as failed.</summary>
    None,

    /// <summary>A deterministic local stand-in (<c>FakeConsistencyChatClient</c>). Development and tests only.</summary>
    Fake,

    /// <summary>Anthropic's API, through its official SDK. Used only when <see cref="AnthropicOptions.ApiKey"/> is set.</summary>
    Anthropic,
}

/// <summary>
/// Anthropic's API. The key comes from a secret store (the AppHost's secret parameter), never from committed settings.
/// API inputs and outputs are not used for training under Anthropic's commercial terms.
/// </summary>
public sealed class AnthropicOptions
{
    public string? ApiKey { get; set; }
}
