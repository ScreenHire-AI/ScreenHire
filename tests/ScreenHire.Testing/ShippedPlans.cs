using Microsoft.Extensions.Options;
using ScreenHire.Application.Billing;

namespace ScreenHire.Testing;

/// <summary>
/// The plans the test stacks sell. The stacks pin no plan override, so they sell the catalogue's defaults
/// (<c>packages/plans/plans.json</c>), read here through the same <see cref="PlanCatalogue"/> the API uses. A test that needs
/// a price or a limit reads it here, so the owner may change a working number without editing a test.
/// </summary>
public static class ShippedPlans
{
    private static readonly PlanCatalogue Catalogue = new(Options.Create(new BillingOptions()));

    public static Plan Starter => Shipped("starter");

    public static Plan Growth => Shipped("growth");

    public static Plan Unlimited => Shipped("unlimited");

    extension(Plan plan)
    {
        /// <summary>The plan's limit, for a test that must reach it. Throws when the plan has none.</summary>
        public int RequiredLimit => plan.Limit ?? throw new InvalidOperationException($"The {plan.Key} plan has no limit in plans.json.");
    }

    private static Plan Shipped(string key) =>
        Catalogue.Find(key) ?? throw new InvalidOperationException($"plans.json has no {key} plan.");
}
