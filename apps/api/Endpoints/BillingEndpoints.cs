using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.Infrastructure.Verification.Llm;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Billing (docs/architecture/billing.md, ADR-0029): the Owner's page of plan, next payment and the way to change it, the
/// Checkout and portal redirects, the price and confirmation of a plan change, and the local sandbox's stand-ins for both.
/// Signed-in Owners only: an API key gets nothing here. With no payment provider configured, every route but <c>GET</c> is
/// a 404 and that one says <c>available: false</c>, so the dashboard hides the page.
/// </summary>
internal static class BillingEndpoints
{
    public const string SettingsPath = "/settings/billing";

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/billing").WithTags("Billing").RequireAuthorization();
        group.MapGet("", GetBilling).WithName("GetBilling")
            .RequirePermission(Permission.ManageBilling)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/checkout", StartBillingCheckout).WithName("StartBillingCheckout")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/plan/preview", PreviewBillingPlanChange).WithName("PreviewBillingPlanChange")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/plan", ChangeBillingPlan).WithName("ChangeBillingPlan")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/portal", OpenBillingPortal).WithName("OpenBillingPortal")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/invoices", ListBillingInvoices).WithName("ListBillingInvoices")
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/payment-method", GetBillingPaymentMethod).WithName("GetBillingPaymentMethod")
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);

        // The sandbox's own "Checkout" and "portal": mapped only when it is on, so the API's description never lists them.
        // Anonymous because the browser arrives by a plain navigation; the state in the address is signed and expires.
        if (app.ServiceProvider.GetRequiredService<IOptions<BillingOptions>>().Value.Mode == BillingMode.Sandbox)
        {
            app.MapGet(SandboxBillingProvider.CheckoutPath, CompleteSandboxCheckout).AllowAnonymous().ExcludeFromDescription();
            app.MapGet(SandboxBillingProvider.PortalPath, CompleteSandboxPortal).AllowAnonymous().ExcludeFromDescription();
            // A sample invoice's PDF: for the Owner who may see the invoices, as the provider's own link is.
            app.MapGet($"{SandboxBillingProvider.InvoicePdfPath}/{{id}}", SandboxInvoicePdf).WithName("GetSandboxInvoicePdf").ExcludeFromDescription()
                .RequireAuthorization()
                .RequirePermission(Permission.ManageBilling);
        }

        return app;
    }

    private static async Task<Ok<BillingOverview>> GetBilling(
        BillingService billing,
        BillingAccess access,
        PlanCatalogue catalogue,
        IOptions<BillingOptions> options,
        ScreenHireDbContext db,
        TimeProvider time,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (!access.IsConfigured || await billing.StatusAsync(tenant.OrganizationId!.Value, cancellationToken) is not { } status)
        {
            return TypedResults.Ok(new BillingOverview(false, false, null, null, null, null, null, false, 0, null, false, false, [], null, null));
        }

        var now = time.GetUtcNow();
        var ai = options.Value.Ai;
        var margin = options.Value.AiMarginInForce;
        var usage = await AiUsageBillingQueries.ThisMonthAsync(db, now, margin, cancellationToken);
        var metered = access.Mode == BillingMode.Sandbox || !string.IsNullOrWhiteSpace(ai.StripePriceId);
        var aiUsage = new BillingAiUsage(
            AiUsageQueries.Month(now),
            usage.BuiltInCalls,
            AiCharge.Dollars(usage.ChargeMicros),
            usage.CallsWithUnknownCost,
            usage.OwnKeyCalls,
            ai.TrialAllowance,
            AiCharge.Dollars(usage.TrialAllowanceUsedMicros),
            metered);

        var renewing = status.State is BillingState.Active or BillingState.PastDue;
        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        var trialCredit = metered
            ? AiCharge.Dollars(await AiUsageBillingQueries.TrialCreditThisMonthAsync(
                db, now, margin, account.TrialEndsAt, AiCharge.ToMicros(ai.TrialAllowance), usage.TrialAllowanceUsedMicros, cancellationToken))
            : 0m;
        var thisMonth = ThisMonth(status, aiUsage, trialCredit, AiUsageQueries.Month(now));
        return TypedResults.Ok(new BillingOverview(
            true,
            access.Mode == BillingMode.Sandbox,
            status.State,
            status.Plan is null ? null : ToContract(status.Plan, access.Mode),
            status.TrialEndsAt,
            renewing ? status.CurrentPeriodEnd : null,
            renewing && !status.CancelsAtPeriodEnd ? status.Plan?.MonthlyPrice : null,
            renewing && status.CancelsAtPeriodEnd,
            status.OpenJobs,
            status.Limit,
            status.HasCustomer && access.Mode == BillingMode.Stripe || renewing && access.Mode == BillingMode.Sandbox,
            options.Value.Stripe.Tax.Enabled,
            [.. catalogue.Plans.Select(p => ToContract(p, access.Mode))],
            aiUsage,
            thisMonth));
    }

    /// <summary>
    /// This month so far, added up: the plan's line (its price while subscribed, nothing on the trial, no line once ended), AI use on
    /// ScreenHire's key where it is billed, less what the trial's allowance covers, and the next invoice while a subscription runs.
    /// The month is the calendar month and the invoice follows the billing period, so the next invoice is an estimate ("about").
    /// The page shows each line to the cent, so the total and the next invoice are the sum of the lines as rounded, and always add up
    /// on screen. The lines themselves keep every digit, so the page can say "Under $0.01" for use that rounds to nothing.
    /// </summary>
    internal static BillingThisMonth ThisMonth(BillingStatus status, BillingAiUsage ai, decimal trialCredit, string month)
    {
        var renewing = status.State is BillingState.Active or BillingState.PastDue;
        decimal? planAmount = status.State switch
        {
            BillingState.Trialing => 0m,
            BillingState.Active or BillingState.PastDue => status.Plan?.MonthlyPrice ?? 0m,
            _ => null,
        };
        var planName = planAmount is null ? null : status.Plan?.Name;
        decimal? aiAmount = ai.Metered ? ai.Charge : null;
        var credit = aiAmount is { } billed ? Math.Min(billed, trialCredit) : 0m;
        var aiNet = Cents(aiAmount ?? 0m) - Cents(credit);
        var total = Cents(planAmount ?? 0m) + aiNet;
        decimal? next = null;
        if (renewing && status.CurrentPeriodEnd is not null)
        {
            var amount = Cents(status.CancelsAtPeriodEnd ? 0m : status.Plan?.MonthlyPrice ?? 0m) + aiNet;
            next = amount > 0m || !status.CancelsAtPeriodEnd ? amount : null;
        }

        return new BillingThisMonth(
            month, status.Plan?.Currency ?? "USD", planName, planAmount, ai.BuiltInCalls, aiAmount, credit, total, next,
            next is null ? null : status.CurrentPeriodEnd);

        // As the page shows money (Intl's default rounds half away from zero too).
        static decimal Cents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static async Task<Results<Ok<BillingInvoiceList>, NotFound, ProblemHttpResult>> ListBillingInvoices(
        BillingService billing, BillingAccess access, PlanCatalogue catalogue, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        try
        {
            var invoices = await access.Provider.ListInvoicesAsync(
                new InvoiceListRequest(account.OrganizationId, account.ProviderCustomerId, catalogue.Find(account.PlanKey), account.Status, account.CurrentPeriodEnd, time.GetUtcNow()),
                cancellationToken);
            return TypedResults.Ok(new BillingInvoiceList([
                .. invoices.Select(i => new BillingInvoice(i.Id, i.CreatedAt, i.Amount, i.Currency, i.Status, i.PdfUrl?.OriginalString)),
            ]));
        }
        catch (BillingProviderException)
        {
            return ProviderDidNotAnswer();
        }
    }

    private static async Task<Results<Ok<BillingPaymentMethodView>, NotFound, ProblemHttpResult>> GetBillingPaymentMethod(
        BillingService billing, BillingAccess access, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        if (account.ProviderCustomerId is null)
        {
            return TypedResults.Ok(new BillingPaymentMethodView(null));
        }

        try
        {
            var method = await access.Provider.GetPaymentMethodAsync(
                new PaymentMethodRequest(account.OrganizationId, account.ProviderCustomerId, account.ProviderSubscriptionId), cancellationToken);
            return TypedResults.Ok(new BillingPaymentMethodView(method is null ? null : new BillingPaymentMethod(method.Type, method.Brand, method.Last4)));
        }
        catch (BillingProviderException)
        {
            return ProviderDidNotAnswer();
        }
    }

    private static async Task<Results<Ok<BillingRedirect>, NotFound, ValidationProblem, ProblemHttpResult>> StartBillingCheckout(
        StartBillingCheckoutRequest request,
        BillingService billing,
        BillingAccess access,
        PlanCatalogue catalogue,
        ITenantContext tenant,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        if (catalogue.Find(request.Plan) is not { } plan || !ToContract(plan, access.Mode).CanSubscribe)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("plan", "Choose one of the plans on offer."));
        }

        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        if (Entitlement.StateOf(account, time.GetUtcNow()) is BillingState.Active or BillingState.PastDue)
        {
            return TypedResults.Problem(
                "This organization already has a subscription. Switch to another plan from the plans on the Billing page.",
                statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            var url = await access.Provider.CreateCheckoutAsync(
                new CheckoutRequest(
                    account.OrganizationId, plan, account.ProviderCustomerId, null,
                    domains.Value.AppUrl($"{SettingsPath}?checkout=success"), domains.Value.AppUrl($"{SettingsPath}?checkout=cancelled")),
                cancellationToken);
            return TypedResults.Ok(new BillingRedirect(url.OriginalString));
        }
        catch (BillingProviderException)
        {
            return ProviderDidNotAnswer();
        }
    }

    // How long a preview can be confirmed. Stripe needs the proration date inside the current period, and a price shown long ago is stale.
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(15);

    private sealed record PlanChangeCheck(PlanChangeRequest? Request, bool NotFound = false, string? PlanProblem = null, string? Conflict = null);

    // The preview and the change share one set of refusals, so a change the preview showed is never refused for a new reason.
    private static async Task<PlanChangeCheck> CheckPlanChangeAsync(
        string? planKey, DateTimeOffset prorationDate, BillingService billing, BillingAccess access, PlanCatalogue catalogue,
        ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return new PlanChangeCheck(null, NotFound: true);
        }

        if (catalogue.Find(planKey) is not { } plan || !ToContract(plan, access.Mode).CanSubscribe)
        {
            return new PlanChangeCheck(null, PlanProblem: "Choose one of the plans on offer.");
        }

        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        var state = Entitlement.StateOf(account, time.GetUtcNow());
        if (state == BillingState.PastDue)
        {
            return new PlanChangeCheck(null, Conflict: "Your last payment failed. Change the card on the Billing page before changing plan.");
        }

        if (state != BillingState.Active || account.ProviderSubscriptionId is null)
        {
            return new PlanChangeCheck(null, Conflict: "There is no subscription to change yet. Choose a plan first.");
        }

        // A subscription set to end has no next payment to price, and a switch would not undo the cancellation.
        if (account.CancelsAtPeriodEnd)
        {
            return new PlanChangeCheck(null, Conflict: "Your subscription is set to end. To keep it on another plan, renew it first: Change card on the Billing page opens the payment page.");
        }

        if (catalogue.Find(account.PlanKey) is not { } current)
        {
            return new PlanChangeCheck(null, Conflict: "Your current plan is not one that is on offer, so a change cannot be priced here. Change card on the Billing page opens the payment page, where it can change.");
        }

        if (string.Equals(current.Key, plan.Key, StringComparison.OrdinalIgnoreCase))
        {
            return new PlanChangeCheck(null, PlanProblem: "You are already on that plan.");
        }

        return new PlanChangeCheck(new PlanChangeRequest(
            account.OrganizationId, account.ProviderCustomerId, account.ProviderSubscriptionId, current, plan, account.CurrentPeriodEnd, prorationDate));
    }

    private static async Task<Results<Ok<BillingPlanChangePreview>, NotFound, ValidationProblem, ProblemHttpResult>> PreviewBillingPlanChange(
        PreviewBillingPlanChangeRequest request,
        BillingService billing,
        BillingAccess access,
        PlanCatalogue catalogue,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // Stripe takes the proration date in whole seconds.
        var prorationDate = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var check = await CheckPlanChangeAsync(request.Plan, prorationDate, billing, access, catalogue, tenant, time, cancellationToken);
        if (check.NotFound)
        {
            return TypedResults.NotFound();
        }

        if (check.PlanProblem is { } planProblem)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("plan", planProblem));
        }

        if (check.Request is not { } change)
        {
            return TypedResults.Problem(check.Conflict, statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            var preview = await access.Provider.PreviewPlanChangeAsync(change, cancellationToken);
            return TypedResults.Ok(new BillingPlanChangePreview(
                change.NewPlan.Key, preview.AmountDueNow, preview.NextPaymentAmount, preview.NextPaymentAt, preview.Currency, preview.ProrationDate));
        }
        catch (BillingProviderException)
        {
            return ProviderDidNotAnswer();
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> ChangeBillingPlan(
        ChangeBillingPlanRequest request,
        BillingService billing,
        BillingAccess access,
        PlanCatalogue catalogue,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var check = await CheckPlanChangeAsync(request.Plan, request.ProrationDate, billing, access, catalogue, tenant, time, cancellationToken);
        if (check.NotFound)
        {
            return TypedResults.NotFound();
        }

        if (check.PlanProblem is { } planProblem)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("plan", planProblem));
        }

        if (check.Request is not { } change)
        {
            return TypedResults.Problem(check.Conflict, statusCode: StatusCodes.Status409Conflict);
        }

        // The prices were worked out at this instant. An old or future one is a stale page or a replay: price it again.
        if (request.ProrationDate > now.AddMinutes(1) || request.ProrationDate < now - PreviewLifetime)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("prorationDate", "The price on screen is out of date. Look at it again before confirming."));
        }

        try
        {
            await access.Provider.ChangePlanAsync(change, cancellationToken);
        }
        catch (BillingProviderException)
        {
            return TypedResults.Problem(
                "The payment provider did not confirm the change. If your plan has not changed in a moment, check your card with Change card before trying again.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (access.Mode == BillingMode.Sandbox)
        {
            // Stripe tells us through its webhook. The sandbox has none, so the route records the plan itself, as its checkout page does.
            await BillingEventApplier.ApplyAsync(
                outbox, tenant, billing,
                new BillingProviderEvent(
                    $"sandbox_{Guid.CreateVersion7():N}", now, change.OrganizationId, null, null, change.NewPlan.Key, SubscriptionStatus.Active, null, null),
                cancellationToken);
        }

        return TypedResults.NoContent();
    }

    // `intent` names the control that opened the portal (Change card, Cancel plan); the payment provider opens the same portal for each.
    private static async Task<Results<Ok<BillingRedirect>, NotFound, ProblemHttpResult>> OpenBillingPortal(
        BillingService billing, BillingAccess access, ITenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken,
        BillingPortalIntent intent = BillingPortalIntent.Manage)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        if (account.ProviderSubscriptionId is null)
        {
            return TypedResults.Problem("There is no subscription to manage yet. Choose a plan first.", statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            var url = await access.Provider.CreatePortalAsync(
                new PortalRequest(account.OrganizationId, account.ProviderCustomerId, domains.Value.AppUrl(SettingsPath), intent), cancellationToken);
            return TypedResults.Ok(new BillingRedirect(url.OriginalString));
        }
        catch (BillingProviderException)
        {
            return ProviderDidNotAnswer();
        }
    }

    // The sandbox's Checkout: "pays" for the plan in the state at once and goes back to the Billing page.
    private static async Task<Results<RedirectHttpResult, BadRequest>> CompleteSandboxCheckout(
        string? state,
        BillingAccess access,
        PlanCatalogue catalogue,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        BillingService billing,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (access.Mode != BillingMode.Sandbox
            || access.Sandbox.Read(state) is not { PlanKey: { } planKey } data
            || catalogue.Find(planKey) is not { } plan)
        {
            return TypedResults.BadRequest();
        }

        var now = time.GetUtcNow();
        var organization = data.OrganizationId.ToString("N");
        await BillingEventApplier.ApplyAsync(
            outbox, tenant, billing,
            new BillingProviderEvent(
                $"sandbox_{Guid.CreateVersion7():N}", now, data.OrganizationId, $"cus_sandbox_{organization}", $"sub_sandbox_{organization}",
                plan.Key, SubscriptionStatus.Active, now.AddMonths(1), false),
            cancellationToken);

        // What the sandbox's Checkout was asked for, as Stripe's page would have asked it: tax, and a tax id.
        var returnUrl = data.ReturnUrl;
        if (data.TaxRequested)
        {
            returnUrl = QueryHelpers.AddQueryString(returnUrl, SandboxBillingProvider.TaxRequestedParameter, "requested");
        }

        if (data.TaxIdsRequested)
        {
            returnUrl = QueryHelpers.AddQueryString(returnUrl, SandboxBillingProvider.TaxIdsRequestedParameter, "requested");
        }

        return TypedResults.Redirect(returnUrl);
    }

    // The sandbox's portal: cancels the subscription at once, which is the one change worth trying locally (the ended state).
    private static async Task<Results<RedirectHttpResult, BadRequest>> CompleteSandboxPortal(
        string? state,
        BillingAccess access,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        BillingService billing,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (access.Mode != BillingMode.Sandbox || access.Sandbox.Read(state) is not { } data)
        {
            return TypedResults.BadRequest();
        }

        // Changing the card changes nothing the sandbox keeps: it goes back to the Billing page as it was.
        if (data.Intent == BillingPortalIntent.ChangeCard)
        {
            return TypedResults.Redirect(data.ReturnUrl);
        }

        await BillingEventApplier.ApplyAsync(
            outbox, tenant, billing,
            new BillingProviderEvent(
                $"sandbox_{Guid.CreateVersion7():N}", time.GetUtcNow(), data.OrganizationId, null, null, null, SubscriptionStatus.Ended, null, false),
            cancellationToken);
        return TypedResults.Redirect(data.ReturnUrl);
    }

    // A sample invoice's PDF in the sandbox. Only ids the sandbox makes are served, and they name no organization.
    private static Results<FileContentHttpResult, NotFound> SandboxInvoicePdf(string id, BillingAccess access) =>
        access.Mode == BillingMode.Sandbox && SandboxBillingProvider.SampleInvoicePdf(id) is { } pdf
            ? TypedResults.File(pdf, "application/pdf", $"{id}.pdf")
            : TypedResults.NotFound();

    private static BillingPlan ToContract(Plan plan, BillingMode mode) => new(
        plan.Key, plan.Name, plan.MonthlyPrice, plan.Currency, plan.ActiveJobs, plan.Unlimited,
        mode == BillingMode.Sandbox || !string.IsNullOrWhiteSpace(plan.StripePriceId));

    private static ProblemHttpResult ProviderDidNotAnswer() =>
        TypedResults.Problem("The payment provider did not answer. Try again in a moment.", statusCode: StatusCodes.Status502BadGateway);
}

/// <summary>
/// The refusal for a job that would exceed the plan (ADR-0029): 402 with a <c>type</c> the dashboard recognises and a
/// <c>detail</c> that says why in plain words. The dashboard adds the upgrade link for an Owner. Candidates never reach it.
/// </summary>
internal static class BillingProblems
{
    public const string PlanLimitType = "urn:screenhire:problem:plan-limit";

    public const string SubscriptionEndedType = "urn:screenhire:problem:subscription-ended";

    public static ProblemHttpResult Refused(PublishDecision decision) => decision.Kind switch
    {
        PublishDecisionKind.LimitReached => TypedResults.Problem(
            $"Your plan allows {decision.Limit?.ToString(CultureInfo.InvariantCulture)} open {(decision.Limit == 1 ? "job" : "jobs")} and " +
            $"{decision.OpenJobs.ToString(CultureInfo.InvariantCulture)} {(decision.OpenJobs == 1 ? "is" : "are")} open. " +
            "Close a job, or ask an owner to upgrade the plan. Jobs that are already open stay open.",
            statusCode: StatusCodes.Status402PaymentRequired,
            title: "Plan limit reached",
            type: PlanLimitType),
        PublishDecisionKind.SubscriptionEnded => TypedResults.Problem(
            "Your subscription has ended, so no new job can be published. Everything else works as before and open jobs stay open. " +
            "Ask an owner to choose a plan.",
            statusCode: StatusCodes.Status402PaymentRequired,
            title: "Subscription ended",
            type: SubscriptionEndedType),
        PublishDecisionKind.TrialEnded => TypedResults.Problem(
            "Your free trial has ended, so no new job can be published. Everything else works as before and open jobs stay open. " +
            "Ask an owner to choose a plan.",
            statusCode: StatusCodes.Status402PaymentRequired,
            title: "Free trial ended",
            type: SubscriptionEndedType),
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.Kind, "An allowed decision has no refusal."),
    };
}
