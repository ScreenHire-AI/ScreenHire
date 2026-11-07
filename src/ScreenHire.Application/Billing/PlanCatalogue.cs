using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Billing;

/// <summary>A plan as it is on sale: the catalogue's defaults with any configured override applied.</summary>
public sealed record Plan(
    string Key, string Name, int Order, decimal? MonthlyPrice, string Currency, int? ActiveJobs, bool Unlimited, string? StripePriceId)
{
    /// <summary>
    /// The most jobs that may be open at once, or null for no limit: an unlimited plan, or a plan whose limit the owner has
    /// not set yet (nothing is limited by a number nobody chose).
    /// </summary>
    public int? Limit => Unlimited ? null : ActiveJobs;
}

/// <summary>
/// The plans, read at runtime. Defaults come from <c>packages/plans/plans.json</c>, the file the marketing site's pricing
/// page reads too, embedded in this assembly; <see cref="BillingOptions.Plans"/> overrides any field.
/// </summary>
public sealed class PlanCatalogue(IOptions<BillingOptions> options)
{
    private const string ResourceName = "plans.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<Dictionary<string, PlanOptions>> Defaults = new(LoadDefaults);

    public IReadOnlyList<Plan> Plans => Merge(options.Value.Plans);

    /// <summary>Whether <paramref name="key"/> is a plan in the catalogue. Static, for the check of the options this class reads.</summary>
    public static bool IsKnown(string key) => Defaults.Value.ContainsKey(key);

    public Plan? Find(string? key) =>
        key is null ? null : Plans.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The plan whose limit applies during the trial; null when the configured key names no plan.</summary>
    public Plan? TrialPlan => Find(options.Value.Trial.Plan);

    private static List<Plan> Merge(Dictionary<string, PlanOptions> overrides)
    {
        var plans = new List<Plan>();
        foreach (var (key, defaults) in Defaults.Value)
        {
            var set = overrides.TryGetValue(key, out var found) ? found : new PlanOptions();
            plans.Add(new Plan(
                key,
                set.Name ?? defaults.Name ?? key,
                set.Order ?? defaults.Order ?? int.MaxValue,
                set.MonthlyPrice ?? defaults.MonthlyPrice,
                set.Currency ?? defaults.Currency ?? "USD",
                set.ActiveJobs ?? defaults.ActiveJobs,
                set.Unlimited ?? defaults.Unlimited ?? false,
                string.IsNullOrWhiteSpace(set.StripePriceId) ? defaults.StripePriceId : set.StripePriceId));
        }

        return [.. plans.OrderBy(p => p.Order)];
    }

    private static Dictionary<string, PlanOptions> LoadDefaults()
    {
        using var stream = typeof(PlanCatalogue).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} (packages/plans/plans.json) is missing.");
        var plans = JsonSerializer.Deserialize<Dictionary<string, PlanOptions>>(stream, Json)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} is empty.");
        return new Dictionary<string, PlanOptions>(plans, StringComparer.OrdinalIgnoreCase);
    }
}
