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

    private sealed class Host(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
