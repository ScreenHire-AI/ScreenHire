using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Billing (docs/architecture/billing.md, ADR-0029): the Owner's page of plan, next payment and the way to change it, the
/// Checkout and portal redirects, the price and confirmation of a plan change, and the local sandbox's stand-ins for both. Signed-in Owners only: an API key gets
/// nothing here. With no payment provider configured, every route but <c>GET</c> is a 404 and that one says
/// <c>available: false</c>, so the dashboard hides the page.
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

        // The sandbox's own "Checkout" and "portal": mapped only when it is on, so the API's description never lists them.
        // Anonymous because the browser arrives by a plain navigation; the state in the address is signed and expires.
        if (app.ServiceProvider.GetRequiredService<IOptions<BillingOptions>>().Value.Mode == BillingMode.Sandbox)
        {
            app.MapGet(SandboxBillingProvider.CheckoutPath, CompleteSandboxCheckout).AllowAnonymous().ExcludeFromDescription();
            app.MapGet(SandboxBillingProvider.PortalPath, CompleteSandboxPortal).AllowAnonymous().ExcludeFromDescription();
        }

        return app;
    }

    private static async Task<Ok<BillingOverview>> GetBilling(
        BillingService billing, BillingAccess access, PlanCatalogue catalogue, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (!access.IsConfigured || await billing.StatusAsync(tenant.OrganizationId!.Value, cancellationToken) is not { } status)
        {
            return TypedResults.Ok(new BillingOverview(false, false, null, null, null, null, null, false, 0, null, false, []));
        }

        var renewing = status.State is BillingState.Active or BillingState.PastDue;
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
            [.. catalogue.Plans.Select(p => ToContract(p, access.Mode))]));
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
                "This organization already has a subscription. Open the payment portal to change the plan.",
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
            return new PlanChangeCheck(null, Conflict: "Your last payment failed. Update your payment details with Manage billing before changing plan.");
        }

        if (state != BillingState.Active || account.ProviderSubscriptionId is null)
        {
            return new PlanChangeCheck(null, Conflict: "There is no subscription to change yet. Choose a plan first.");
        }

        if (catalogue.Find(account.PlanKey) is not { } current)
        {
            return new PlanChangeCheck(null, Conflict: "Your current plan is not one that is on offer, so a change cannot be priced here. Use Manage billing.");
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
                "The plan was not changed and you have not been charged. Check your payment details with Manage billing, or try again in a moment.",
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

    private static async Task<Results<Ok<BillingRedirect>, NotFound, ProblemHttpResult>> OpenBillingPortal(
        BillingService billing, BillingAccess access, ITenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
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
                new PortalRequest(account.OrganizationId, account.ProviderCustomerId, domains.Value.AppUrl(SettingsPath)), cancellationToken);
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
        return TypedResults.Redirect(data.ReturnUrl);
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

        await BillingEventApplier.ApplyAsync(
            outbox, tenant, billing,
            new BillingProviderEvent(
                $"sandbox_{Guid.CreateVersion7():N}", time.GetUtcNow(), data.OrganizationId, null, null, null, SubscriptionStatus.Ended, null, false),
            cancellationToken);
        return TypedResults.Redirect(data.ReturnUrl);
    }

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
