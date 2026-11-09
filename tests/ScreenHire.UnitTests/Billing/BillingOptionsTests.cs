using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ScreenHire.Application.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// Which payment provider a host talks to: Stripe once both its keys are set, the sandbox when it is switched on,
/// otherwise none (billing is hidden and nothing is limited). The sandbox charges nobody, so a host outside Development
/// refuses to start with it.
/// </summary>
public sealed class BillingOptionsTests
{
    [Theory]
    [InlineData("sk", "whsec", false, BillingMode.Stripe)]
    [InlineData("sk", "whsec", true, BillingMode.Stripe)]
    [InlineData("sk", null, true, BillingMode.Sandbox)]
    [InlineData("sk", "  ", true, BillingMode.Sandbox)]
    [InlineData(null, "whsec", true, BillingMode.Sandbox)]
    [InlineData(null, null, true, BillingMode.Sandbox)]
    [InlineData("sk", null, false, BillingMode.Off)]
    [InlineData(null, "whsec", false, BillingMode.Off)]
    [InlineData("", "", false, BillingMode.Off)]
    [InlineData(null, null, false, BillingMode.Off)]
    public void The_mode_follows_the_stripe_keys_and_the_sandbox_switch(string? secretKey, string? webhookSecret, bool sandbox, BillingMode expected)
    {
        var options = new BillingOptions { SecretKey = secretKey, WebhookSecret = webhookSecret, Sandbox = new BillingSandboxOptions { Enabled = sandbox } };

        options.Mode.Should().Be(expected);
    }

    [Fact]
    public void A_new_organization_gets_a_fourteen_day_trial_on_the_growth_plan_unless_configured()
    {
        var trial = new BillingOptions().Trial;

        trial.Days.Should().Be(14);
        trial.Plan.Should().Be("growth");
    }

    [Fact]
    public void ScreenHire_adds_twenty_percent_to_the_providers_price_for_its_own_key_unless_configured()
    {
        new BillingOptions().Ai.Margin.Should().Be(0.20m);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("0.20", true)]
    [InlineData("10", true)]
    [InlineData("-0.01", false)]
    [InlineData("10.01", false)]
    [InlineData("20", false)]
    public void The_ai_margin_is_zero_to_ten_so_a_percentage_typed_for_a_fraction_is_refused(string margin, bool valid)
    {
        var options = new BillingOptions { Ai = new BillingAiOptions { Margin = decimal.Parse(margin, System.Globalization.CultureInfo.InvariantCulture) } };

        new BillingAiGate().Validate(null, options).Succeeded.Should().Be(valid);
    }

    [Fact]
    public void The_margin_default_is_the_one_the_marketing_site_reads_from_the_shared_catalogue()
    {
        // packages/plans/ai.json is embedded and is the site's source for "plus 20%", so the two cannot differ.
        AiDefaults.Shipped.Margin.Should().Be(0.20m);
        new BillingAiOptions().Margin.Should().Be(AiDefaults.Shipped.Margin);
    }

    [Fact]
    public void The_trial_includes_five_dollars_of_ai_use_and_a_run_reports_two_hundred_calls_unless_configured()
    {
        var ai = new BillingAiOptions();

        ai.TrialAllowance.Should().Be(5.00m);
        ai.ReportBatchSize.Should().Be(200);
        ai.MeterEventName.Should().Be("screenhire_ai_usage");
        ai.StripePriceId.Should().BeNull();
    }

    [Theory]
    [InlineData(-0.01, 200, "meter", false)]
    [InlineData(0, 200, "meter", true)]
    [InlineData(5, 0, "meter", false)]
    [InlineData(5, 1001, "meter", false)]
    [InlineData(5, 1000, "meter", true)]
    [InlineData(5, 200, " ", false)]
    public void The_allowance_the_batch_and_the_meter_name_are_checked_at_start(double allowance, int batch, string meter, bool valid)
    {
        var options = new BillingOptions { Ai = new BillingAiOptions { TrialAllowance = (decimal)allowance, ReportBatchSize = batch, MeterEventName = meter } };

        new BillingAiGate().Validate(null, options).Succeeded.Should().Be(valid);
    }

    [Fact]
    public void A_metered_price_beside_a_plan_that_is_not_in_dollars_stops_the_host_because_one_subscription_has_one_currency()
    {
        var options = new BillingOptions
        {
            Ai = new BillingAiOptions { StripePriceId = "price_meter" },
            Plans = new Dictionary<string, PlanOptions>(StringComparer.OrdinalIgnoreCase) { ["starter"] = new() { Currency = "EUR" } },
        };

        var result = new BillingAiGate().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("starter").And.Contain("USD");
        // With no metered price there is nothing to mix, so a euro plan is fine.
        new BillingAiGate().Validate(null, new BillingOptions { Plans = options.Plans }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_sandbox_is_refused_outside_development()
    {
        var result = new BillingSandboxGate(new Host(Environments.Production))
            .Validate(null, new BillingOptions { Sandbox = new BillingSandboxOptions { Enabled = true } });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Sandbox:Enabled").And.Contain("Development");
    }

    [Fact]
    public void The_sandbox_is_allowed_in_development()
    {
        new BillingSandboxGate(new Host(Environments.Development))
            .Validate(null, new BillingOptions { Sandbox = new BillingSandboxOptions { Enabled = true } })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Stripe_keys_win_over_the_sandbox_switch_so_production_with_both_set_starts()
    {
        new BillingSandboxGate(new Host(Environments.Production))
            .Validate(null, new BillingOptions { SecretKey = "sk", WebhookSecret = "whsec", Sandbox = new BillingSandboxOptions { Enabled = true } })
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Stripe_and_no_billing_start_in_any_environment(string environment)
    {
        var gate = new BillingSandboxGate(new Host(environment));

        gate.Validate(null, new BillingOptions { SecretKey = "sk", WebhookSecret = "whsec" }).Succeeded.Should().BeTrue();
        gate.Validate(null, new BillingOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_negative_trial_is_refused_because_every_new_organization_would_start_ended()
    {
        var result = new BillingTrialGate().Validate(null, Sandboxed(new BillingTrialOptions { Days = -1 }));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Trial:Days");
    }

    [Fact]
    public void A_trial_plan_the_catalogue_does_not_have_is_refused_because_every_trial_would_be_unlimited()
    {
        var result = new BillingTrialGate().Validate(null, Sandboxed(new BillingTrialOptions { Plan = "grwoth" }));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Trial:Plan").And.Contain("grwoth");
    }

    [Theory]
    [InlineData(0, "starter")]
    [InlineData(14, "GROWTH")]
    public void No_trial_and_any_catalogue_plan_in_any_case_start(int days, string plan)
    {
        new BillingTrialGate().Validate(null, Sandboxed(new BillingTrialOptions { Days = days, Plan = plan })).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Trial_settings_are_not_checked_while_billing_is_off()
    {
        new BillingTrialGate().Validate(null, new BillingOptions { Trial = new BillingTrialOptions { Days = -1, Plan = "grwoth" } }).Succeeded.Should().BeTrue();
    }

    private static BillingOptions Sandboxed(BillingTrialOptions trial) =>
        new() { Sandbox = new BillingSandboxOptions { Enabled = true }, Trial = trial };

    private sealed class Host(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
