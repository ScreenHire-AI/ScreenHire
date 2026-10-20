using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// An organization's integrity settings: today, whether the LLM consistency check runs and its monthly token budget.
/// Tenant-scoped, unlike the public <c>organizations</c> row. No row means the defaults: the check off.
/// </summary>
/// <remarks>
/// The check sends the resume, answers and LinkedIn export to the configured model provider, a sub-processor, so it is
/// off until the organization turns it on. Providers are used on terms that exclude training on the data
/// (integrity-engine.md §10.10).
/// </remarks>
public sealed class IntegritySettings : ITenantOwned
{
    /// <summary>The highest monthly budget an organization may set, in tokens.</summary>
    public const long MaxMonthlyTokenBudget = 1_000_000_000;

    /// <summary>The organization; one row per organization.</summary>
    public Guid OrganizationId { get; init; }

    public bool LlmConsistencyCheckEnabled { get; set; }

    /// <summary>Tokens the check may use per calendar month (UTC), or null for the deployment's default.</summary>
    public long? MonthlyTokenBudget { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
