using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace ScreenHire.Application.Billing;

/// <summary>
/// The local payment provider: Development and tests only, so every billing flow runs with no Stripe account (ADR-0029).
/// Its "Checkout" and "portal" are the API's own <see cref="CheckoutPath"/> and <see cref="PortalPath"/>, which apply the
/// change at once and send the browser back to the Billing page. It charges nobody. The URLs it makes are relative, so the
/// dashboard, which proxies <c>/v1</c> to the API, reaches them on its own origin.
/// </summary>
/// <remarks>
/// What the pages do is carried in a Data Protection <c>state</c> the API issued, so the routes, which are anonymous
/// (a redirect is a plain navigation), cannot be pointed at another organization or plan by editing the address.
/// </remarks>
public sealed class SandboxBillingProvider(IDataProtectionProvider dataProtection, TimeProvider time) : IBillingProvider
{
    public const string CheckoutPath = "/v1/billing/sandbox/checkout";

    public const string PortalPath = "/v1/billing/sandbox/portal";

    public const string Purpose = "ScreenHire.Billing.SandboxState.v1";

    /// <summary>How long someone has to follow a sandbox link.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = Issue(new SandboxBillingState(request.OrganizationId, request.Plan.Key, request.SuccessUrl.ToString()));
        return Task.FromResult(new Uri($"{CheckoutPath}?state={Uri.EscapeDataString(state)}", UriKind.Relative));
    }

    public Task<Uri> CreatePortalAsync(PortalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = Issue(new SandboxBillingState(request.OrganizationId, null, request.ReturnUrl.ToString()));
        return Task.FromResult(new Uri($"{PortalPath}?state={Uri.EscapeDataString(state)}", UriKind.Relative));
    }

    /// <summary>
    /// A day-based proration, so tests and the local demo show real-looking numbers: the price difference times the whole days
    /// left in the period over the days in it, rounded to the cent. The period is the month that ends at
    /// <see cref="PlanChangeRequest.CurrentPeriodEnd"/> (the sandbox's checkout sets it a month ahead). A downgrade is a credit,
    /// which shows as nothing due. A plan with no price counts as free.
    /// </summary>
    public Task<PlanChangePreview> PreviewPlanChangeAsync(PlanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var periodEnd = request.CurrentPeriodEnd ?? request.ProrationDate.AddMonths(1);
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

/// <summary>What a sandbox page does: start <c>PlanKey</c> (Checkout; null for the portal) for the organization, then send the browser to <c>ReturnUrl</c>, the Billing page.</summary>
public sealed record SandboxBillingState(Guid OrganizationId, string? PlanKey, string ReturnUrl);
