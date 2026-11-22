using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
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
/// Prepaid AI credits (ADR-0032, docs/architecture/billing.md "AI credits"): the balance and the deductions for whoever sees AI usage,
/// and buying, the purchase history and auto-reload for an Owner. Signed-in members only: an API key gets nothing here. With no payment
/// provider, every route but <c>GET</c> is a 404 and that one says <c>available: false</c>. Buying returns the provider's page; the
/// credits are granted only when the provider's signed event confirms the payment, never by the browser coming back.
/// </summary>
internal static class AiCreditEndpoints
{
    /// <summary>Where the browser comes back to after buying credits.</summary>
    public const string ReturnPath = BillingEndpoints.SettingsPath;

    private const int DeductionPageSize = 50;

    public static IEndpointRouteBuilder MapAiCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/billing/credits").WithTags("Billing").RequireAuthorization();
        group.MapGet("", GetAiCredits).WithName("GetAiCredits")
            .RequirePermission(Permission.ViewAiUsage)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/deductions", ListAiCreditDeductions).WithName("ListAiCreditDeductions")
            .RequirePermission(Permission.ViewAiUsage)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/purchases", ListAiCreditPurchases).WithName("ListAiCreditPurchases")
            .RequirePermission(Permission.ManageBilling)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/checkout", StartAiCreditCheckout).WithName("StartAiCreditCheckout")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/settings", UpdateAiCreditSettings).WithName("UpdateAiCreditSettings")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ManageBilling)
            .Produces(StatusCodes.Status401Unauthorized);

        // The sandbox's credit Checkout: mapped only when it is on. It asks the worker to grant the credits, as Stripe's signed event does.
        if (app.ServiceProvider.GetRequiredService<IOptions<BillingOptions>>().Value.Mode == BillingMode.Sandbox)
        {
            app.MapGet(SandboxBillingProvider.CreditCheckoutPath, CompleteSandboxCreditCheckout).AllowAnonymous().ExcludeFromDescription();
        }

        return app;
    }

    private static async Task<Ok<AiCreditsView>> GetAiCredits(
        BillingAccess access, IOptions<BillingOptions> options, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        var credits = options.Value.Credits;
        if (!access.IsConfigured)
        {
            return TypedResults.Ok(new AiCreditsView(false, 0m, false, false, [], null, 0m, new AiAutoReload(false, null, null, null, 0m), false));
        }

        var now = time.GetUtcNow();
        await AiCreditLedger.EnsureStartingCreditAsync(db, tenant.OrganizationId!.Value, options.Value, now, cancellationToken);
        var balance = AiCharge.Dollars(await AiCreditLedger.BalanceMicrosAsync(db, cancellationToken));
        var settings = await db.CreditSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var ownKey = await db.VerificationSettings.AsNoTracking().AnyAsync(s => s.LlmSource == Domain.Verification.LlmSource.OwnKey, cancellationToken);
        var reloaded = await AiCreditLedger.ReloadedThisMonthAsync(db, now, cancellationToken);
        return TypedResults.Ok(new AiCreditsView(
            true,
            balance,
            !ownKey && balance <= 0m,
            ownKey,
            credits.AmountsOnOffer,
            credits.ExpiryMonths,
            settings?.LowBalanceAlert ?? credits.LowBalanceAlert,
            new AiAutoReload(settings?.AutoReloadEnabled ?? false, settings?.AutoReloadBelow, settings?.AutoReloadAmount, settings?.AutoReloadMonthlyMax, reloaded),
            settings?.HasSavedCard ?? false));
    }

    // Purchases that settled, newest first, with the starting credit among them. A Checkout left unpaid is not a purchase and is left out.
    private static async Task<Results<Ok<AiCreditPurchaseList>, NotFound>> ListAiCreditPurchases(
        BillingAccess access, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var grants = await db.CreditEntries.AsNoTracking()
            .Where(e => e.Kind == CreditEntryKind.StartingCredit || e.Kind == CreditEntryKind.Purchase || e.Kind == CreditEntryKind.AutoReload)
            .OrderByDescending(e => e.At).Take(200)
            .Select(e => new { e.Id, e.At, e.Micros, e.Kind, e.ExpiresAt })
            .ToListAsync(cancellationToken);
        var failed = await db.CreditPurchases.AsNoTracking()
            .Where(p => p.Status == CreditPurchaseStatus.Failed)
            .OrderByDescending(p => p.CreatedAt).Take(50)
            .Select(p => new { p.Id, p.CreatedAt, p.Amount, p.Source })
            .ToListAsync(cancellationToken);
        var rows = grants.Select(g => new AiCreditPurchase(
                g.Id, g.At, AiCharge.Dollars(g.Micros),
                g.Kind switch
                {
                    CreditEntryKind.StartingCredit => AiCreditSource.StartingCredit,
                    CreditEntryKind.AutoReload => AiCreditSource.AutoReload,
                    _ => AiCreditSource.Checkout,
                },
                g.Kind == CreditEntryKind.StartingCredit ? AiCreditPurchaseStatus.Included : AiCreditPurchaseStatus.Paid,
                g.ExpiresAt))
            .Concat(failed.Select(f => new AiCreditPurchase(
                f.Id, f.CreatedAt, f.Amount, f.Source == CreditPurchaseSource.AutoReload ? AiCreditSource.AutoReload : AiCreditSource.Checkout,
                AiCreditPurchaseStatus.Failed, null)))
            .OrderByDescending(p => p.Date)
            .ToList();
        return TypedResults.Ok(new AiCreditPurchaseList(rows));
    }

    // Every deduction traceable to the check that used it: a check's calls grouped as one row, and a credit's expiry as another.
    private static async Task<Results<Ok<AiCreditDeductionList>, NotFound>> ListAiCreditDeductions(
        BillingAccess access, ScreenHireDbContext db, CancellationToken cancellationToken, DateTimeOffset? before = null)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var grouped = db.CreditEntries.AsNoTracking()
            .Where(e => e.Kind == CreditEntryKind.Usage)
            .GroupBy(e => new { e.ConsistencyCheckId, e.ApplicationId })
            .Select(g => new { g.Key.ConsistencyCheckId, g.Key.ApplicationId, At = g.Max(e => e.At), Micros = g.Sum(e => e.Micros), Calls = g.Count() });
        var expired = db.CreditEntries.AsNoTracking().Where(e => e.Kind == CreditEntryKind.Expiry);
        if (before is { } cursor)
        {
            grouped = grouped.Where(g => g.At < cursor);
            expired = expired.Where(e => e.At < cursor);
        }

        var checks = await grouped.OrderByDescending(g => g.At).Take(DeductionPageSize + 1).ToListAsync(cancellationToken);
        var expiries = await expired
            .OrderByDescending(e => e.At).Take(DeductionPageSize + 1)
            .Select(e => new { e.Id, e.At, e.Micros })
            .ToListAsync(cancellationToken);
        var applicationIds = checks.Where(c => c.ApplicationId is not null).Select(c => c.ApplicationId!.Value).Distinct().ToList();
        // The candidate's name while the application exists: the ledger outlives an erased candidate, whose row then has none.
        var names = await db.Applications.AsNoTracking()
            .Where(a => applicationIds.Contains(a.Id))
            .Join(db.Candidates, a => a.CandidateId, c => c.Id, (a, c) => new { a.Id, c.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
        var rows = checks.Select(c => new AiCreditDeduction(
                (c.ConsistencyCheckId ?? c.ApplicationId ?? Guid.Empty).ToString("N"), c.At, AiCreditDeductionKind.Check, AiCharge.Dollars(-c.Micros), c.Calls,
                c.ApplicationId, c.ApplicationId is { } id ? names.GetValueOrDefault(id) : null))
            .Concat(expiries.Select(e => new AiCreditDeduction(e.Id.ToString("N"), e.At, AiCreditDeductionKind.Expiry, AiCharge.Dollars(-e.Micros), 0, null, null)))
            .OrderByDescending(r => r.Date)
            .ToList();
        var page = rows.Take(DeductionPageSize).ToList();
        return TypedResults.Ok(new AiCreditDeductionList(page, rows.Count > DeductionPageSize ? page[^1].Date : null));
    }

    private static async Task<Results<Ok<BillingRedirect>, NotFound, ValidationProblem, ProblemHttpResult>> StartAiCreditCheckout(
        StartAiCreditCheckoutRequest request,
        BillingService billing,
        BillingAccess access,
        IOptions<BillingOptions> options,
        ScreenHireDbContext db,
        ITenantContext tenant,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        if (!options.Value.Credits.AmountsOnOffer.Contains(request.Amount))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("amount", "Choose one of the amounts on offer."));
        }

        var account = (await billing.AccountAsync(tenant.OrganizationId!.Value, cancellationToken))!;
        // Pending until the provider's signed event says it was paid. One left unpaid stays Pending and grants nothing.
        var purchase = new CreditPurchase
        {
            OrganizationId = account.OrganizationId,
            Source = CreditPurchaseSource.Checkout,
            Amount = request.Amount,
            CreatedAt = time.GetUtcNow(),
        };
        db.CreditPurchases.Add(purchase);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var url = await access.Provider.CreateCreditCheckoutAsync(
                new CreditCheckoutRequest(
                    account.OrganizationId, purchase.Id, purchase.Amount, account.ProviderCustomerId, null,
                    domains.Value.AppUrl($"{ReturnPath}?credits=success#ai-credits"), domains.Value.AppUrl($"{ReturnPath}?credits=cancelled#ai-credits")),
                cancellationToken);
            return TypedResults.Ok(new BillingRedirect(url.OriginalString));
        }
        catch (BillingProviderException)
        {
            return TypedResults.Problem("The payment provider did not answer. Try again in a moment.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> UpdateAiCreditSettings(
        UpdateAiCreditSettingsRequest request,
        BillingAccess access,
        IOptions<BillingOptions> options,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!access.IsConfigured)
        {
            return TypedResults.NotFound();
        }

        var db = outbox.DbContext;
        var now = time.GetUtcNow();
        var organizationId = tenant.OrganizationId!.Value;
        var settings = await AiCreditLedger.SettingsAsync(db, organizationId, options.Value, now, cancellationToken);
        if (SettingsProblems(request, options.Value.Credits, settings.HasSavedCard) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var reload = request.AutoReload;
        settings.LowBalanceAlert = request.LowBalanceAlert;
        settings.SetAutoReload(reload.Enabled, reload.Below, reload.Amount, reload.MonthlyMax, now);

        // Turned on while the balance is already below its threshold: reload now rather than after the next check. The worker decides
        // again (the monthly maximum, a reload already waiting), keyed on the newest grant as a check's request is, so the two are one.
        if (reload is { Enabled: true, Below: { } below }
            && await AiCreditLedger.BalanceMicrosAsync(db, cancellationToken) < AiCharge.ToMicros(below))
        {
            await outbox.PublishAsync(new ReloadAiCredits(organizationId, await AiCreditLedger.LatestGrantAsync(db, cancellationToken)));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// What is wrong with the settings, by field, or null. Dollars to the cent and within range; turning auto-reload on needs all three
    /// amounts, an amount of at least the smallest on offer, a monthly maximum that allows one reload, and a saved card.
    /// </summary>
    internal static Dictionary<string, string[]>? SettingsProblems(UpdateAiCreditSettingsRequest request, BillingCreditOptions credits, bool hasSavedCard)
    {
        var problems = new Dictionary<string, string[]>();
        var smallest = credits.AmountsOnOffer.Count > 0 ? credits.AmountsOnOffer[0] : 1m;
        Money("lowBalanceAlert", request.LowBalanceAlert, 0m);
        var reload = request.AutoReload;
        Money("autoReload.below", reload.Below, 0.01m);
        Money("autoReload.amount", reload.Amount, smallest);
        Money("autoReload.monthlyMax", reload.MonthlyMax, smallest);
        if (reload.Enabled)
        {
            if (reload.Below is null)
            {
                problems.TryAdd("autoReload.below", ["Say when to add credits."]);
            }

            if (reload.Amount is null)
            {
                problems.TryAdd("autoReload.amount", ["Say how much to add."]);
            }

            if (reload.MonthlyMax is null)
            {
                problems.TryAdd("autoReload.monthlyMax", ["Set the most auto-reload may add in a month."]);
            }

            if (!hasSavedCard)
            {
                problems.TryAdd("autoReload.enabled", ["Buy credits once to save a card. Auto-reload charges that card."]);
            }
        }

        if (reload is { Amount: { } amount, MonthlyMax: { } max } && max < amount)
        {
            problems.TryAdd("autoReload.monthlyMax", ["The monthly maximum must allow at least one reload."]);
        }

        return problems.Count == 0 ? null : problems;

        void Money(string field, decimal? value, decimal least)
        {
            if (value is not { } amount)
            {
                return;
            }

            if (amount < least || amount > BillingCreditOptions.MaxAmount || decimal.Round(amount, 2) != amount)
            {
                problems.TryAdd(field, [
                    $"Enter {least.ToString("C2", CultureInfo.GetCultureInfo("en-US"))} to {BillingCreditOptions.MaxAmount.ToString("C0", CultureInfo.GetCultureInfo("en-US"))}, to the cent.",
                ]);
            }
        }
    }

    // The sandbox's credit Checkout: "pays" for the purchase in the state, and asks the worker to grant it through the same message
    // Stripe's signed event sends. The redirect alone grants nothing.
    private static async Task<Results<RedirectHttpResult, BadRequest>> CompleteSandboxCreditCheckout(
        string? state,
        BillingAccess access,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (access.Mode != BillingMode.Sandbox || access.Sandbox.Read(state) is not { PurchaseId: { } purchaseId } data)
        {
            return TypedResults.BadRequest();
        }

        var payment = $"pi_sandbox_{Guid.CreateVersion7():N}";
        await CreditPaymentApplier.ApplyAsync(
            outbox, tenant,
            new CreditPaymentEvent(
                $"sandbox_{Guid.CreateVersion7():N}", time.GetUtcNow(), data.OrganizationId, purchaseId, payment, payment,
                SandboxBillingProvider.CustomerOf(data.OrganizationId)),
            time.GetUtcNow(),
            cancellationToken);

        var returnUrl = data.ReturnUrl;
        if (data.TaxRequested)
        {
            returnUrl = AddQuery(returnUrl, SandboxBillingProvider.TaxRequestedParameter);
        }

        if (data.TaxIdsRequested)
        {
            returnUrl = AddQuery(returnUrl, SandboxBillingProvider.TaxIdsRequestedParameter);
        }

        return TypedResults.Redirect(returnUrl);
    }

    // Adds a query parameter before the address's fragment (#ai-credits), where QueryHelpers would put it after.
    private static string AddQuery(string url, string name)
    {
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        return hash < 0
            ? QueryHelpers.AddQueryString(url, name, "requested")
            : QueryHelpers.AddQueryString(url[..hash], name, "requested") + url[hash..];
    }
}
