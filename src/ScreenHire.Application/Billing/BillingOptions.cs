using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Billing settings, <c>ScreenHire:Billing</c> (docs/architecture/billing.md). The AppHost gives the API and the worker the
/// same values. Nothing here is on until configured: with no Stripe keys and no sandbox, billing is hidden, there is no
/// trial and no plan limits anything.
/// </summary>
public sealed class BillingOptions
{
    public const string Section = "ScreenHire:Billing";

    /// <summary>Stripe's secret API key, from the <c>stripe-secret-key</c> parameter. Never committed.</summary>
    public string? SecretKey { get; set; }

    /// <summary>The signing secret of the Stripe webhook endpoint, from the <c>stripe-webhook-secret</c> parameter.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>The local fake provider (<see cref="SandboxBillingProvider"/>): Development and tests only.</summary>
    public BillingSandboxOptions Sandbox { get; set; } = new();

    /// <summary>The free trial every new organization starts with.</summary>
    public BillingTrialOptions Trial { get; set; } = new();

    /// <summary>
    /// Per-plan overrides of <c>packages/plans/plans.json</c>, by plan key. A field left out keeps the catalogue's value,
    /// so setting one price needs one setting (<c>ScreenHire__Billing__Plans__starter__MonthlyPrice</c>).
    /// </summary>
    public Dictionary<string, PlanOptions> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stripe's API. Configurable only so a test can point the real client at a stand-in.</summary>
    public Uri ApiBaseUrl { get; set; } = new("https://api.stripe.com/v1/");

    /// <summary>How long one call to Stripe may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Stripe when both its keys are set; the sandbox when it is enabled; otherwise off. The one rule both hosts apply.
    /// </summary>
    public BillingMode Mode =>
        !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(WebhookSecret) ? BillingMode.Stripe
        : Sandbox.Enabled ? BillingMode.Sandbox
        : BillingMode.Off;
}

public sealed class BillingSandboxOptions
{
    public bool Enabled { get; set; }
}

public sealed class BillingTrialOptions
{
    /// <summary>How long a new organization's trial lasts.</summary>
    public int Days { get; set; } = 14;

    /// <summary>The plan whose limit applies during the trial.</summary>
    public string Plan { get; set; } = "growth";
}

/// <summary>One plan's settings. Every field is optional: a null keeps the catalogue's value.</summary>
public sealed class PlanOptions
{
    public string? Name { get; set; }

    public int? Order { get; set; }

    /// <summary>The monthly price in whole currency units (a decimal such as 49 or 49.5), or null when not set.</summary>
    public decimal? MonthlyPrice { get; set; }

    public string? Currency { get; set; }

    /// <summary>The most jobs open at once, or null for no limit. Ignored when <see cref="Unlimited"/>.</summary>
    public int? ActiveJobs { get; set; }

    public bool? Unlimited { get; set; }

    /// <summary>The Stripe price the plan's Checkout charges. Billing cannot start a subscription on a plan without one.</summary>
    public string? StripePriceId { get; set; }
}

public enum BillingMode
{
    /// <summary>No provider here: the page is hidden, nobody is limited and no trial runs.</summary>
    Off,

    /// <summary>The local fake, with no third party.</summary>
    Sandbox,

    /// <summary>Stripe with the configured keys.</summary>
    Stripe,
}

/// <summary>
/// Refuses to start a host with the sandbox outside Development: its checkout charges nobody, and a real organization
/// must never think it paid.
/// </summary>
public sealed class BillingSandboxGate(IHostEnvironment environment) : IValidateOptions<BillingOptions>
{
    public ValidateOptionsResult Validate(string? name, BillingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Mode == BillingMode.Sandbox && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail("ScreenHire:Billing:Sandbox:Enabled is true outside Development. The sandbox checkout charges nobody and is for local runs and tests only.")
            : ValidateOptionsResult.Success;
    }
}
