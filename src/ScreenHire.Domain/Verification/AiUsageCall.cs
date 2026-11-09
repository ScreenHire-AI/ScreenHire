using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Verification;

/// <summary>
/// One call to a language model, recorded as it happened (ai-usage.md): when, for which check and application, which
/// provider and model, whose key paid, the provider's own id for the response, the tokens, the cost when it can be known,
/// how long it took and how it ended. The ledger an organization compares with its provider's own dashboard, and the
/// one ScreenHire bills the built-in key from.
/// </summary>
/// <remarks>
/// Append-only: no contract changes a row, and the application role cannot update one. The row is organization-level on
/// purpose. <see cref="ApplicationId"/> is a plain id with no foreign key and no job scope, so the record outlives an
/// erased candidate and Owners and Admins see every job's calls. It holds no text that was sent or received.
/// </remarks>
public sealed class AiUsageCall : ITenantOwned
{
    public const int MaxCheckNameLength = 60;

    public const int MaxProviderLength = 30;

    public const int MaxModelLength = 200;

    public const int MaxRequestIdLength = 200;

    public const int MaxErrorKindLength = 100;

    /// <summary>A version 7 id, so ids sort by the time of the call and the call list pages by id.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>When the call started.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Which check made the call: one of <see cref="AiUsageChecks"/>.</summary>
    public required string CheckName { get; init; }

    /// <summary>The application the call was about; null for a call that is about none (testing a connection).</summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>The provider that answered: a <see cref="ModelProvider"/> name, or the deployment's own (for example <c>Anthropic</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>The model that answered, as the provider reported it, else the one asked for.</summary>
    public required string Model { get; init; }

    public AiKeySource KeySource { get; init; }

    /// <summary>The provider's id for the response, which finds the call in the provider's own logs. Null when it sent none.</summary>
    public string? ProviderRequestId { get; init; }

    /// <summary>Tokens the provider reported; null when it reported none (a call that failed, or a provider that does not say).</summary>
    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    /// <summary>The cost in US dollars; null when <see cref="CostBasis"/> is <see cref="AiCostBasis.Unknown"/>.</summary>
    public decimal? Cost { get; init; }

    public AiCostBasis CostBasis { get; init; }

    public int DurationMs { get; init; }

    public AiCallOutcome Outcome { get; init; }

    /// <summary>The kind of failure, as an exception type name. Never the provider's text, which can hold the request.</summary>
    public string? ErrorKind { get; init; }

    /// <summary>Tokens in and out, counting an unreported side as none.</summary>
    public long Tokens => (InputTokens ?? 0) + (OutputTokens ?? 0);
}

/// <summary>Whose key paid for a call.</summary>
public enum AiKeySource
{
    /// <summary>ScreenHire's own key, billed to the organization at the provider's price plus a margin.</summary>
    BuiltIn,

    /// <summary>The organization's own key, billed by its provider. ScreenHire charges nothing.</summary>
    OwnKey,
}

/// <summary>Where a call's cost came from.</summary>
public enum AiCostBasis
{
    /// <summary>The provider said what the call cost.</summary>
    Reported,

    /// <summary>Tokens times a price per million tokens from configuration (ScreenHire's key) or from the organization's own setting.</summary>
    Priced,

    /// <summary>Nothing prices the call. It counts as no cost toward a money budget, and the Usage tab says so.</summary>
    Unknown,
}

public enum AiCallOutcome
{
    Ok,
    Error,
    TimedOut,
}

/// <summary>The checks that call a model, as the ledger names them.</summary>
public static class AiUsageChecks
{
    public const string ConsistencyExtraction = "consistency.extraction";

    public const string ConsistencyComparison = "consistency.comparison";

    /// <summary>The one-word request behind "Test connection" in the settings.</summary>
    public const string ModelTest = "model-test";
}

/// <summary>
/// A claim that an alert went out: one row per organization and key, so a message delivered twice, or a threshold passed
/// twice, tells nobody twice. The key names the month or day and the kind (<see cref="AiUsageAlertKeys"/>).
/// </summary>
public sealed class AiUsageAlert : ITenantOwned
{
    public const int MaxKeyLength = 60;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Key { get; init; }

    public DateTimeOffset RaisedAt { get; init; }
}

/// <summary>The keys of <see cref="AiUsageAlert"/>.</summary>
public static class AiUsageAlertKeys
{
    /// <summary>The budget passed <paramref name="percent"/> percent in <paramref name="month"/> (<c>yyyy-MM</c>).</summary>
    public static string Budget(string month, int percent) => $"{month}:budget-{percent}";

    /// <summary>The cap was reached in <paramref name="month"/> (<c>yyyy-MM</c>).</summary>
    public static string Cap(string month) => $"{month}:cap";

    /// <summary>A day's use was well above the recent average. <paramref name="day"/> is <c>yyyy-MM-dd</c>.</summary>
    public static string Spike(string day) => $"{day}:spike";
}
