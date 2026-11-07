using System.Text.Json;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The plans on sale: <c>packages/plans/plans.json</c> ships no price and no limit (the owner sets them), and
/// <c>ScreenHire:Billing:Plans</c> overrides any field of any plan.
/// </summary>
public sealed class PlanCatalogueTests
{
    private static PlanCatalogue Catalogue(BillingOptions? options = null) => new(Options.Create(options ?? new BillingOptions()));

    private static BillingOptions With(string key, PlanOptions plan) =>
        new() { Plans = new Dictionary<string, PlanOptions>(StringComparer.OrdinalIgnoreCase) { [key] = plan } };

    [Fact]
    public void The_plans_file_parses_and_has_exactly_starter_growth_and_unlimited()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.PathTo("packages", "plans", "plans.json")));

        document.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("starter", "growth", "unlimited");
        Catalogue().Plans.Select(p => p.Key).Should().Equal("starter", "growth", "unlimited");
    }

    [Fact]
    public void The_shipped_plans_set_no_price_no_limit_and_no_stripe_price()
    {
        foreach (var plan in Catalogue().Plans)
        {
            plan.MonthlyPrice.Should().BeNull(plan.Key);
            plan.ActiveJobs.Should().BeNull(plan.Key);
            plan.StripePriceId.Should().BeNull(plan.Key);
            plan.Limit.Should().BeNull($"{plan.Key} is not limited by a number nobody chose");
            plan.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public void Only_the_unlimited_plan_is_unlimited_and_the_plans_are_ordered_by_size()
    {
        var plans = Catalogue().Plans;

        plans.Where(p => p.Unlimited).Select(p => p.Key).Should().Equal("unlimited");
        plans.Select(p => p.Order).Should().BeInAscendingOrder();
        plans.Select(p => p.Name).Should().Equal("Starter", "Growth", "Unlimited");
    }

    [Fact]
    public void An_override_changes_only_the_fields_it_sets()
    {
        var starter = Catalogue(With("starter", new PlanOptions { ActiveJobs = 3 })).Find("starter")!;

        starter.ActiveJobs.Should().Be(3);
        starter.Limit.Should().Be(3);
        starter.Name.Should().Be("Starter");
        starter.MonthlyPrice.Should().BeNull();
        starter.Currency.Should().Be("USD");
        starter.Order.Should().Be(1);
        starter.Unlimited.Should().BeFalse();
    }

    [Fact]
    public void An_override_sets_the_price_name_currency_and_stripe_price()
    {
        var growth = Catalogue(With("growth", new PlanOptions { MonthlyPrice = 49.5m, Name = "Team", Currency = "GBP", StripePriceId = "price_fictional" })).Find("growth")!;

        growth.MonthlyPrice.Should().Be(49.5m);
        growth.Name.Should().Be("Team");
        growth.Currency.Should().Be("GBP");
        growth.StripePriceId.Should().Be("price_fictional");
    }

    [Fact]
    public void A_blank_stripe_price_keeps_the_catalogues()
    {
        Catalogue(With("growth", new PlanOptions { StripePriceId = "  " })).Find("growth")!.StripePriceId.Should().BeNull();
    }

    [Fact]
    public void An_override_of_the_order_reorders_the_plans()
    {
        var plans = Catalogue(With("unlimited", new PlanOptions { Order = 0 })).Plans;

        plans.Select(p => p.Key).Should().Equal("unlimited", "starter", "growth");
    }

    [Fact]
    public void An_unlimited_plan_has_no_limit_even_when_a_number_is_set()
    {
        var unlimited = Catalogue(With("unlimited", new PlanOptions { ActiveJobs = 5 })).Find("unlimited")!;

        unlimited.ActiveJobs.Should().Be(5);
        unlimited.Limit.Should().BeNull();
    }

    [Fact]
    public void Switching_a_plan_to_unlimited_or_back_follows_the_override()
    {
        Catalogue(With("starter", new PlanOptions { ActiveJobs = 2, Unlimited = true })).Find("starter")!.Limit.Should().BeNull();
        Catalogue(With("unlimited", new PlanOptions { ActiveJobs = 7, Unlimited = false })).Find("unlimited")!.Limit.Should().Be(7);
    }

    [Fact]
    public void An_override_for_a_plan_that_does_not_exist_adds_no_plan()
    {
        Catalogue(With("platinum", new PlanOptions { ActiveJobs = 9 })).Plans.Select(p => p.Key).Should().Equal("starter", "growth", "unlimited");
    }

    [Fact]
    public void A_plan_is_found_by_its_key_ignoring_case_and_not_by_a_wrong_or_missing_one()
    {
        var catalogue = Catalogue();

        catalogue.Find("GROWTH")!.Key.Should().Be("growth");
        catalogue.Find("platinum").Should().BeNull();
        catalogue.Find(null).Should().BeNull();
        catalogue.Find("").Should().BeNull();
    }

    [Fact]
    public void The_trial_plan_is_the_configured_one_and_none_when_it_names_no_plan()
    {
        Catalogue().TrialPlan!.Key.Should().Be("growth");
        Catalogue(new BillingOptions { Trial = new BillingTrialOptions { Plan = "starter" } }).TrialPlan!.Key.Should().Be("starter");
        Catalogue(new BillingOptions { Trial = new BillingTrialOptions { Plan = "platinum" } }).TrialPlan.Should().BeNull();
    }
}
