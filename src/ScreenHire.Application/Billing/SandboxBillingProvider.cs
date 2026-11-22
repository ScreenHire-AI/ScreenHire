using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Billing;

namespace ScreenHire.Application.Billing;

/// <summary>
/// The local payment provider: Development and tests only, so every billing flow runs with no Stripe account (ADR-0029).
/// Its "Checkout" and "portal" are the API's own <see cref="CheckoutPath"/> and <see cref="PortalPath"/>, which apply the
/// change at once and send the browser back to the Billing page. Its credit Checkout (<see cref="CreditCheckoutPath"/>) asks the
/// worker to grant the credits through the same message Stripe's signed event sends. It charges nobody. The URLs it makes are relative, so the
/// dashboard, which proxies <c>/v1</c> to the API, reaches them on its own origin.
/// </summary>
/// <remarks>
/// What the pages do is carried in a Data Protection <c>state</c> the API issued, so the routes, which are anonymous
/// (a redirect is a plain navigation), cannot be pointed at another organization or plan by editing the address.
/// </remarks>
public sealed class SandboxBillingProvider(IDataProtectionProvider dataProtection, TimeProvider time, IOptions<BillingOptions> options) : IBillingProvider
{
    public const string CheckoutPath = "/v1/billing/sandbox/checkout";

    public const string PortalPath = "/v1/billing/sandbox/portal";

    /// <summary>The query parameters the sandbox checkout adds to its return address when tax, and a tax id, were asked for.</summary>
    public const string TaxRequestedParameter = "tax";

    public const string TaxIdsRequestedParameter = "taxId";

    public const string Purpose = "ScreenHire.Billing.SandboxState.v1";

    /// <summary>How long someone has to follow a sandbox link.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Like Stripe's form, the checkout records that tax and a tax id were asked for, so a test can see it (the page's
        // redirect says so, see TaxRequestedParameter).
        var tax = options.Value.Stripe.Tax.Enabled;
        var state = Issue(new SandboxBillingState(
            request.OrganizationId, request.Plan.Key, request.SuccessUrl.ToString(), TaxRequested: tax, TaxIdsRequested: options.Value.CollectTaxIds));
        return Task.FromResult(new Uri($"{CheckoutPath}?state={Uri.EscapeDataString(state)}", UriKind.Relative));
    }

    public Task<Uri> CreatePortalAsync(PortalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = Issue(new SandboxBillingState(request.OrganizationId, null, request.ReturnUrl.ToString(), Intent: request.Intent));
        return Task.FromResult(new Uri($"{PortalPath}?state={Uri.EscapeDataString(state)}", UriKind.Relative));
    }

    /// <summary>Where a sample invoice's PDF is served, by its id (<c>in_sandbox_yyyyMM</c>).</summary>
    public const string InvoicePdfPath = "/v1/billing/sandbox/invoices";

    /// <summary>How many sample invoices the sandbox lists for an organization that has subscribed.</summary>
    public const int SampleInvoices = 3;

    /// <summary>
    /// Sample invoices, so the local demo and the tests show the Billing page's list: one a month at the plan's price, the newest at
    /// the start of the current period (a month before the period end its checkout set), and the two months before. The newest is Due
    /// while a payment has failed (PastDue), every other one Paid. None before the organization has a customer.
    /// </summary>
    public Task<IReadOnlyList<ProviderInvoice>> ListInvoicesAsync(InvoiceListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.CustomerId))
        {
            return Task.FromResult<IReadOnlyList<ProviderInvoice>>([]);
        }

        var periodStart = (request.CurrentPeriodEnd ?? request.Now.AddMonths(1)).AddMonths(-1);
        var invoices = Enumerable.Range(0, SampleInvoices).Select(months =>
        {
            var at = periodStart.AddMonths(-months);
            var id = SampleInvoiceId(at);
            var status = months == 0 && request.Status == SubscriptionStatus.PastDue ? BillingInvoiceStatus.Due : BillingInvoiceStatus.Paid;
            return new ProviderInvoice(
                id, at, request.Plan?.MonthlyPrice ?? 0m, request.Plan?.Currency ?? "USD", status, new Uri($"{InvoicePdfPath}/{id}", UriKind.Relative));
        });
        return Task.FromResult<IReadOnlyList<ProviderInvoice>>([.. invoices]);
    }

    /// <summary>The sandbox's card, the test number every payment provider documents, once the organization has a customer.</summary>
    public Task<ProviderPaymentMethod?> GetPaymentMethodAsync(PaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(string.IsNullOrEmpty(request.CustomerId) ? null : new ProviderPaymentMethod("card", "visa", "4242"));
    }

    /// <summary>The id of the sample invoice made at <paramref name="at"/>.</summary>
    public static string SampleInvoiceId(DateTimeOffset at) => $"in_sandbox_{at.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A one-page PDF for a sample invoice id, or null for an id the sandbox never made. Written by hand (no PDF package: ADR-0008),
    /// with one line of text in a standard font, so the link on the Billing page opens a real file locally.
    /// </summary>
    public static byte[]? SampleInvoicePdf(string id)
    {
        if (id is not { Length: 17 } || !id.StartsWith("in_sandbox_", StringComparison.Ordinal)
            || !DateTime.TryParseExact(id[11..], "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return null;
        }

        var text = $"Sample invoice {month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}. Local sandbox: nothing was charged.";
        var content = $"BT /F1 14 Tf 72 720 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// A day-based proration, so tests and the local demo show real-looking numbers: the price difference times the whole days
    /// left in the period over the days in it, rounded to the cent. The period is the month that ends at
    /// <see cref="PlanChangeRequest.CurrentPeriodEnd"/> (the sandbox's checkout sets it a month ahead), or at the first whole month
    /// after it that is still to come. A downgrade is a credit,
    /// which shows as nothing due. A plan with no price counts as free.
    /// </summary>
    public Task<PlanChangePreview> PreviewPlanChangeAsync(PlanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lastKnownEnd = request.CurrentPeriodEnd ?? request.ProrationDate.AddMonths(1);
        // The sandbox never renews, so an account older than a month has a period end in the past. Its current period ends a
        // whole number of months on, as a renewing subscription's would.
        var periodEnd = lastKnownEnd;
        for (var months = 1; periodEnd <= request.ProrationDate; months++)
        {
            periodEnd = lastKnownEnd.AddMonths(months);
        }

        var periodDays = Math.Max(1, (periodEnd - periodEnd.AddMonths(-1)).Days);
        var daysLeft = Math.Clamp((periodEnd.UtcDateTime.Date - request.ProrationDate.UtcDateTime.Date).Days, 0, periodDays);
        var newPrice = request.NewPlan.MonthlyPrice ?? 0m;
        var difference = newPrice - (request.CurrentPlan.MonthlyPrice ?? 0m);
        var dueNow = Math.Max(0m, Math.Round(difference * daysLeft / periodDays, 2, MidpointRounding.AwayFromZero));
        return Task.FromResult(new PlanChangePreview(dueNow, newPrice, periodEnd, request.NewPlan.Currency, request.ProrationDate));
    }

    /// <summary>
    /// Nothing to call: the sandbox charges nobody and has no webhook, so the API's route records the new plan itself, as its
    /// checkout page does.
    /// </summary>
    public Task ChangePlanAsync(PlanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.CompletedTask;
    }

    /// <summary>Where the sandbox's credit Checkout is: it asks the worker to grant the purchase, as Stripe's signed event does.</summary>
    public const string CreditCheckoutPath = "/v1/billing/sandbox/credits";

    /// <summary>The card the sandbox's credit Checkout saves for auto-reload.</summary>
    public const string SandboxCard = "pm_sandbox_4242";

    public Task<Uri> CreateCreditCheckoutAsync(CreditCheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = Issue(new SandboxBillingState(
            request.OrganizationId, null, request.SuccessUrl.ToString(), TaxRequested: options.Value.Stripe.Tax.Enabled,
            TaxIdsRequested: options.Value.CollectTaxIds, PurchaseId: request.PurchaseId));
        return Task.FromResult(new Uri($"{CreditCheckoutPath}?state={Uri.EscapeDataString(state)}", UriKind.Relative));
    }

    /// <summary>
    /// Always succeeds: the sandbox charges nobody. It has no webhook, so the auto-reload handler grants a sandbox charge itself. A
    /// refused charge is tested against Stripe's own answers (<see cref="StripeBillingProvider"/> with a stand-in).
    /// </summary>
    public Task<CreditCharge> ChargeCreditsAsync(CreditChargeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new CreditCharge(CreditChargeStatus.Succeeded, $"in_sandbox_reload_{request.PurchaseId:N}"));
    }

    /// <summary>The sandbox's test card, for any payment its credit Checkout made.</summary>
    public Task<string?> SavedPaymentMethodAsync(string paymentIntentId, CancellationToken cancellationToken) => Task.FromResult<string?>(SandboxCard);

    /// <summary>The customer id the sandbox gives an organization, the same at Checkout for a plan and for credits.</summary>
    public static string CustomerOf(Guid organizationId) => $"cus_sandbox_{organizationId:N}";

    /// <summary>The sandbox has no webhook: its pages apply the change themselves.</summary>
    public BillingWebhook ReadWebhook(string body, string? signature, DateTimeOffset now) => new(false, null);

    /// <summary>What <paramref name="state"/> says, or null when it is forged, damaged or expired.</summary>
    public SandboxBillingState? Read(string? state)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 4000)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SandboxBillingState>(Protector.Unprotect(state, out var expiration)) is { } data
                && expiration > time.GetUtcNow()
                ? data
                : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private string Issue(SandboxBillingState state) => Protector.Protect(JsonSerializer.Serialize(state), time.GetUtcNow() + Lifetime);
}

/// <summary>
/// What a sandbox page does: start <c>PlanKey</c> (Checkout; null for the portal) for the organization, then send the browser to
/// <c>ReturnUrl</c>, the Billing page. <c>TaxRequested</c> and <c>TaxIdsRequested</c> say the checkout asked for tax and a tax id.
/// <c>Intent</c> is what the portal was opened for: it cancels the plan, except to change the card, which the sandbox has nothing to do for.
/// <c>PurchaseId</c> is a credit Checkout's purchase, which the credit page asks the worker to grant.
/// </summary>
public sealed record SandboxBillingState(
    Guid OrganizationId, string? PlanKey, string ReturnUrl, bool TaxRequested = false, bool TaxIdsRequested = false,
    BillingPortalIntent Intent = BillingPortalIntent.Manage, Guid? PurchaseId = null);
