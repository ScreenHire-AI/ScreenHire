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

    // The one amount for AI use carries the margin whenever billing is on, because every call is then drawn from a balance: a figure
    // with a margin nobody is charged is not the amount the customer pays (docs/architecture/ai-usage.md).
    [Theory]
    [InlineData("sk", "whsec", false, true)]
    [InlineData(null, null, true, true)]
    [InlineData(null, null, false, false)]
    [InlineData("sk", null, false, false)]
    public void The_margin_is_in_force_and_credits_are_on_whenever_billing_is_on(string? secretKey, string? webhookSecret, bool sandbox, bool on)
    {
        var options = new BillingOptions
        {
            SecretKey = secretKey,
            WebhookSecret = webhookSecret,
            Sandbox = new BillingSandboxOptions { Enabled = sandbox },
            Ai = new BillingAiOptions { Margin = 0.25m },
        };

        options.CreditsEnabled.Should().Be(on);
        options.AiMarginInForce.Should().Be(on ? 0.25m : 0m);
    }

    [Fact]
    public void A_new_organization_gets_a_fourteen_day_trial_on_the_growth_plan_unless_configured()
    {
        var trial = new BillingOptions().Trial;

        trial.Days.Should().Be(14);
        trial.Plan.Should().Be("growth");
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
    public void The_starting_credit_is_five_dollars_unless_configured()
    {
        new BillingAiOptions().TrialAllowance.Should().Be(5.00m);
    }

    [Theory]
    [InlineData(-0.01, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    public void The_starting_credit_may_not_be_negative(double allowance, bool valid)
    {
        var options = new BillingOptions { Ai = new BillingAiOptions { TrialAllowance = (decimal)allowance } };

        new BillingAiGate().Validate(null, options).Succeeded.Should().Be(valid);
    }

    // ---- Credits ----

    [Fact]
    public void The_credit_defaults_are_the_ones_in_the_shared_catalogue_the_marketing_site_reads()
    {
        // packages/plans/credits.json is embedded and is the site's source for what credits cost, so the two cannot differ.
        var credits = new BillingCreditOptions();

        credits.AmountsOnOffer.Should().Equal(10m, 25m, 50m, 100m);
        credits.ExpiryMonths.Should().Be(12);
        credits.LowBalanceAlert.Should().Be(5m);
        credits.AmountsOnOffer.Should().Equal(CreditDefaults.Shipped.Amounts);
    }

    [Fact]
    public void Configured_amounts_replace_the_shipped_ones_and_are_offered_smallest_first()
    {
        var credits = new BillingCreditOptions { Amounts = [40m, 15m] };

        credits.AmountsOnOffer.Should().Equal(15m, 40m);
    }

    [Fact]
    public void A_credit_expires_the_configured_months_after_its_grant_and_never_when_none_is_set()
    {
        var granted = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);

        new BillingCreditOptions { ExpiryMonths = 12 }.ExpiresAt(granted).Should().Be(new DateTimeOffset(2027, 1, 31, 8, 0, 0, TimeSpan.Zero));
        new BillingCreditOptions { ExpiryMonths = 1 }.ExpiresAt(granted).Should().Be(new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero));
        new BillingCreditOptions { ExpiryMonths = null }.ExpiresAt(granted).Should().BeNull();
    }

    public static TheoryData<BillingCreditOptions, bool> CreditSettings => new()
    {
        { new BillingCreditOptions(), true },
        { new BillingCreditOptions { Amounts = [5m, 12.5m] }, true },
        { new BillingCreditOptions { Amounts = [0m] }, false },
        { new BillingCreditOptions { Amounts = [-5m] }, false },
        { new BillingCreditOptions { Amounts = [10_000.01m] }, false },
        { new BillingCreditOptions { Amounts = [10_000m] }, true },
        { new BillingCreditOptions { Amounts = [10.001m] }, false },
        { new BillingCreditOptions { Amounts = [10m, 10m] }, false },
        { new BillingCreditOptions { ExpiryMonths = 0 }, false },
        { new BillingCreditOptions { ExpiryMonths = 121 }, false },
        { new BillingCreditOptions { ExpiryMonths = 120 }, true },
        { new BillingCreditOptions { ExpiryMonths = null }, true },
        { new BillingCreditOptions { LowBalanceAlert = -1m }, false },
        { new BillingCreditOptions { LowBalanceAlert = 0m }, true },
    };

    [Theory]
    [MemberData(nameof(CreditSettings))]
    public void The_gate_refuses_amounts_expiry_and_a_low_balance_alert_that_would_misbehave_at_run_time(BillingCreditOptions credits, bool valid)
    {
        new BillingAiGate().Validate(null, new BillingOptions { Credits = credits }).Succeeded.Should().Be(valid);
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
