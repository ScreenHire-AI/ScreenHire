using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Billing;

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

    /// <summary>What ScreenHire charges for AI use on its own key (docs/architecture/ai-usage.md, billing.md). Read by the billing of that use.</summary>
    public BillingAiOptions Ai { get; set; } = new();

    /// <summary>Prepaid AI credits (ADR-0032): the amounts on offer, how long a credit lasts and the default low-balance alert.</summary>
    public BillingCreditOptions Credits { get; set; } = new();

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

    /// <summary>
    /// Whether AI use on ScreenHire's key is paid for with prepaid credits: whenever a payment provider is set up (ADR-0032). With
    /// billing off (a self-hosted deployment, where nobody is billed) there is no balance and nothing pauses the AI check.
    /// </summary>
    public bool CreditsEnabled => Mode != BillingMode.Off;

    /// <summary>
    /// The margin on AI use that a customer pays: <see cref="BillingAiOptions.Margin"/> while credits are on (<see cref="CreditsEnabled"/>),
    /// and 0 otherwise, because then nobody is billed for it and the amount shown is the provider's price. Every figure for AI use goes
    /// through it, so a debit from the balance, Billing's AI use section and the Activity log show the one amount
    /// (docs/architecture/ai-usage.md).
    /// </summary>
    public decimal AiMarginInForce => CreditsEnabled ? Ai.Margin : 0m;
}

/// <summary>
/// The charge for AI use on ScreenHire's key: the provider's price plus a margin, drawn from the organization's prepaid credits
/// (ADR-0032). An organization's own key costs it nothing extra. The margin is configuration, so it changes without a release.
/// </summary>
public sealed class BillingAiOptions
{
    /// <summary>
    /// The share added to the provider's price: 0.20 is 20% on top. Between 0 and <see cref="MaxMargin"/>. Defaults to
    /// packages/plans/ai.json, the file the marketing site reads to say what the margin is.
    /// </summary>
    public decimal Margin { get; set; } = AiDefaults.Shipped.Margin;

    /// <summary>The highest margin accepted, 10 for 1,000%: a larger value is a typo for a percentage (20 for 0.20).</summary>
    public const decimal MaxMargin = 10m;

    /// <summary>
    /// The starting credit: US dollars of AI credits every organization gets once, with its billing account (ADR-0032). It was the
    /// trial's AI allowance, and keeps the name. Zero gives none.
    /// </summary>
    public decimal TrialAllowance { get; set; } = 5.00m;
}

/// <summary>
/// Prepaid AI credits, <c>ScreenHire:Billing:Credits</c> (docs/architecture/billing.md, "AI credits"). The defaults are
/// packages/plans/credits.json, which the marketing site reads too.
/// </summary>
public sealed class BillingCreditOptions
{
    /// <summary>The largest amount one purchase may be, in US dollars.</summary>
    public const decimal MaxAmount = 10_000m;

    /// <summary>The longest a credit may last, in months.</summary>
    public const int MaxExpiryMonths = 120;

    /// <summary>
    /// The fixed amounts an Owner can buy, in US dollars. Empty means the shipped list (<see cref="CreditDefaults.Shipped"/>); left empty
    /// here rather than set to it, because the configuration binder adds a configured list to the elements an array already has.
    /// </summary>
    public decimal[] Amounts { get; set; } = [];

    /// <summary>Months a credit lasts from its grant. Null means never. Defaults to packages/plans/credits.json (12).</summary>
    public int? ExpiryMonths { get; set; } = CreditDefaults.Shipped.ExpiryMonths;

    /// <summary>The low-balance alert a new organization starts with, in US dollars. Each Owner can change their organization's.</summary>
    public decimal LowBalanceAlert { get; set; } = CreditDefaults.Shipped.LowBalanceAlert;

    /// <summary>The amounts on offer: the configured list, or the shipped one, smallest first.</summary>
    public IReadOnlyList<decimal> AmountsOnOffer => [.. (Amounts.Length > 0 ? Amounts : CreditDefaults.Shipped.Amounts).Order()];

    /// <summary>When a credit granted at <paramref name="grantedAt"/> expires, or null when credits never do.</summary>
    public DateTimeOffset? ExpiresAt(DateTimeOffset grantedAt) => ExpiryMonths is { } months ? grantedAt.AddMonths(months) : null;
}

/// <summary>
/// The credit defaults as the catalogue ships them (packages/plans/credits.json, embedded), the file the marketing site reads to say
/// what credits cost. Configuration (<see cref="BillingCreditOptions"/>) overrides them for the API and worker alone.
/// </summary>
public sealed record CreditDefaults(decimal[] Amounts, int? ExpiryMonths, decimal LowBalanceAlert)
{
    private const string ResourceName = "credits.json";

    // Before Shipped: static fields initialize in the order they are written.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CreditDefaults Shipped { get; } = Load();

    private static CreditDefaults Load()
    {
        using var stream = typeof(CreditDefaults).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} (packages/plans/credits.json) is missing.");
        return JsonSerializer.Deserialize<CreditDefaults>(stream, Json)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} is empty.");
    }
}

/// <summary>
/// The margin as the catalogue ships it (packages/plans/ai.json, embedded), the file the marketing site reads to say what the margin
/// is. Configuration (<see cref="BillingAiOptions.Margin"/>) overrides it for the API and worker alone, so a host that changes it
/// changes the file too, or the site states a different margin from the one an organization pays.
/// </summary>
public sealed record AiDefaults(decimal Margin)
{
    private const string ResourceName = "ai.json";

    // Before Shipped: static fields initialize in the order they are written.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AiDefaults Shipped { get; } = Load();

    private static AiDefaults Load()
    {
        using var stream = typeof(AiDefaults).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} (packages/plans/ai.json) is missing.");
        return JsonSerializer.Deserialize<AiDefaults>(stream, Json)
            ?? throw new InvalidOperationException($"The embedded {ResourceName} is empty.");
    }
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

    /// <summary>
    /// The days before <c>TrialEndsAt</c> the Owners are emailed, besides the day it ends, which is always reminded. Empty means 7 and 1
    /// (<see cref="TrialReminders.DefaultDaysBefore"/>). Left empty here rather than set to that default: the configuration binder
    /// adds a configured list to the elements an array or list already has, so <c>[3]</c> would become <c>[7, 1, 3]</c>.
    /// </summary>
    public int[] RemindDaysBefore { get; set; } = [];
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
/// Refuses AI billing settings that would fail silently: a margin that would charge less than the provider's price, or a hundredfold
/// more by a typing slip; a negative starting credit; a credit amount that is not dollars to the cent, is out of range or is listed
/// twice; an expiry out of range; and a negative low-balance alert.
/// </summary>
public sealed class BillingAiGate : IValidateOptions<BillingOptions>
{
    public ValidateOptionsResult Validate(string? name, BillingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ai = options.Ai;
        var credits = options.Credits;
        var problems = new List<string>();
        if (ai.Margin is < 0m or > BillingAiOptions.MaxMargin)
        {
            problems.Add($"ScreenHire:Billing:Ai:Margin is {ai.Margin}. It must be 0 to {BillingAiOptions.MaxMargin} (0.20 is 20% on top of the provider's price).");
        }

        if (ai.TrialAllowance < 0m)
        {
            problems.Add($"ScreenHire:Billing:Ai:TrialAllowance is {ai.TrialAllowance}. It must be 0 or more US dollars.");
        }

        var amounts = credits.AmountsOnOffer;
        if (amounts.Any(a => a is <= 0m or > BillingCreditOptions.MaxAmount || decimal.Round(a, 2) != a) || amounts.Distinct().Count() != amounts.Count)
        {
            problems.Add(
                $"ScreenHire:Billing:Credits:Amounts is [{string.Join(", ", amounts)}]. Each must be more than 0 and at most {BillingCreditOptions.MaxAmount} US dollars, to the cent, and listed once.");
        }

        if (credits.ExpiryMonths is < 1 or > BillingCreditOptions.MaxExpiryMonths)
        {
            problems.Add(
                $"ScreenHire:Billing:Credits:ExpiryMonths is {credits.ExpiryMonths}. It must be 1 to {BillingCreditOptions.MaxExpiryMonths}, or empty for credits that never expire.");
        }

        if (credits.LowBalanceAlert < 0m)
        {
            problems.Add($"ScreenHire:Billing:Credits:LowBalanceAlert is {credits.LowBalanceAlert}. It must be 0 or more US dollars.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
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

        // Checked in every mode: the worker reads the days without knowing the mode, and a day of zero or less would be dropped silently.
        if (options.Trial.RemindDaysBefore.Any(d => d < 1))
        {
            return ValidateOptionsResult.Fail(
                $"ScreenHire:Billing:Trial:RemindDaysBefore is [{string.Join(", ", options.Trial.RemindDaysBefore)}]. Each day must be 1 or more; the day the trial ends is always reminded.");
        }

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
