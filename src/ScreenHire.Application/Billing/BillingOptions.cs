using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Billing settings, <c>ScreenHire:Billing</c> (docs/architecture/billing.md), which the AppHost forwards to the API. Nothing
/// here is on until configured: with no Stripe keys and no sandbox, billing is hidden, there is no trial and no plan limits
/// anything.
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

    /// <summary>Stripe's settings, <c>Stripe:*</c>, other than its keys. <c>Stripe:Enabled</c> is the AppHost's and never binds here.</summary>
    public BillingStripeOptions Stripe { get; set; } = new();

    /// <summary>Whether Checkout asks a business for its tax id: only with tax on, and unless switched off.</summary>
    public bool CollectTaxIds => Stripe.Tax.Enabled && Stripe.Tax.CollectTaxIds;

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

public sealed class BillingStripeOptions
{
    /// <summary>Stripe Tax at Checkout, <c>Stripe:Tax:*</c>. Off until the owner has registered in Stripe Tax.</summary>
    public BillingTaxOptions Tax { get; set; } = new();
}

/// <summary>
/// <c>ScreenHire:Billing:Stripe:Tax</c>. Stripe collects tax only where the owner has a registration in Stripe Tax, so this
/// is off until the owner has confirmed those (docs/architecture/billing.md, Owner actions). It applies to Stripe Checkout and,
/// as a record that tax was asked for, to the sandbox's.
/// </summary>
public sealed class BillingTaxOptions
{
    /// <summary>Checkout calculates tax automatically and collects the billing address it needs.</summary>
    public bool Enabled { get; set; }

    /// <summary>With <see cref="Enabled"/>, Checkout also collects the customer's business tax id. On unless set to false.</summary>
    public bool CollectTaxIds { get; set; } = true;
}

public sealed class BillingTrialOptions
{
    /// <summary>How long a new organization's trial lasts, in days. Zero means no trial. Defaults to packages/plans/trial.json.</summary>
    public int Days { get; set; } = TrialDefaults.Shipped.Days;

    /// <summary>The plan whose limit applies during the trial: a key of the catalogue. Defaults to packages/plans/trial.json.</summary>
    public string Plan { get; set; } = TrialDefaults.Shipped.Plan;
}

/// <summary>
/// The trial as the catalogue ships it (packages/plans/trial.json, embedded), the file the marketing site reads to say how long
/// the trial is. Configuration (<see cref="BillingTrialOptions"/>) overrides it for the API alone, so a host that changes the
/// length changes the file too, or the site states a different trial from the one an organization gets.
/// </summary>
public sealed record TrialDefaults(int Days, string Plan)
{
    private const string ResourceName = "trial.json";

    // Before Shipped: static fields initialize in the order they are written.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TrialDefaults Shipped { get; } = Load();

    private static TrialDefaults Load()
    {
        using var stream = typeof(TrialDefaults).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} (packages/plans/trial.json) is missing.");
        return JsonSerializer.Deserialize<TrialDefaults>(stream, Json)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} is empty.");
    }
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

/// <summary>
/// Refuses to start a host with a trial setting that would fail silently once billing is on: a negative length ends every
/// new organization's trial before it starts, and a plan key the catalogue does not have makes every trial unlimited.
/// </summary>
public sealed class BillingTrialGate : IValidateOptions<BillingOptions>
{
    public ValidateOptionsResult Validate(string? name, BillingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Mode == BillingMode.Off)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.Trial.Days < 0)
        {
            return ValidateOptionsResult.Fail($"ScreenHire:Billing:Trial:Days is {options.Trial.Days}. It must be 0 or more.");
        }

        return PlanCatalogue.IsKnown(options.Trial.Plan)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"ScreenHire:Billing:Trial:Plan is '{options.Trial.Plan}', which is not a plan in packages/plans/plans.json.");
    }
}
