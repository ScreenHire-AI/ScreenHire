using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Shared steps for the verification tests: open a job, apply as the careers server does (uploads first, then the
/// submission with the candidate's address forwarded), and wait for the worker's assessment.
/// </summary>
internal static class VerificationSupport
{
    /// <summary>Every analyzer the worker registers. An application is settled once each has a run.</summary>
    public static readonly string[] Analyzers = ["network", "identity", "automation", "duplicates", "behavioural", "documents", "answers", "consistency"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A published remote job in the tenant's organization (from <paramref name="templateVersionId"/> if given) and the organization's slug.</summary>
    public static async Task<(string Slug, Guid JobId)> OpenJobAsync(AppHostFixture fixture, Tenant tenant, Guid? templateVersionId = null)
    {
        var body = Samples.RemoteJob("Platform Engineer");
        if (templateVersionId is { } versionId)
        {
            body["templateVersionId"] = versionId;
        }

        var jobId = (await tenant.CreateJobAsync(body)).Id();
        await tenant.PublishJobAsync(jobId);
        await using var db = await TenantDbAsync(fixture, tenant.OrganizationId);
        var slug = await db.Organizations.Where(o => o.Id == tenant.OrganizationId).Select(o => o.Slug).SingleAsync(Ct);
        return (slug, jobId);
    }

    /// <summary>Applies as the careers server does (<see cref="SyntheticApplications.ApplyAsync"/>); the default workflow asks nothing but the resume.</summary>
    public static Task<Guid> ApplyAsync(
        AppHostFixture fixture,
        string slug,
        Guid jobId,
        SyntheticApplicant applicant,
        bool accommodation = false,
        IReadOnlyList<ApplicationAnswer>? answers = null,
        IReadOnlyList<(string QuestionId, string FileName, byte[] Content)>? files = null) =>
        fixture.ApplyAsync(slug, jobId, applicant, Ct, accommodation, answers, files);

    /// <summary>The application's verification once the worker has assessed it at least once.</summary>
    public static async Task<ApiResult> AssessedAsync(Tenant tenant, Guid applicationId)
    {
        ApiResult? result = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                result = (await tenant.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK);
                return result.Text("status") == "Assessed";
            },
            "the worker to assess the application",
            Ct);
        return result!;
    }

    /// <summary>
    /// The application's verification once every analyzer has run, content forensics included (they wait for the files to be
    /// read and the consistency check to finish), and the last reassessment is readable.
    /// </summary>
    public static async Task<ApiResult> SettledAsync(AppHostFixture fixture, Tenant tenant, Guid applicationId)
    {
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await TenantDbAsync(fixture, tenant.OrganizationId);
                var ran = await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == applicationId).Select(r => r.Analyzer).Distinct().CountAsync(Ct);
                return ran == Analyzers.Length;
            },
            "every analyzer to run, content forensics included",
            Ct);
        return await AssessedAsync(tenant, applicationId);
    }

    /// <summary>
    /// Records one call on ScreenHire's key that cost <paramref name="cost"/> dollars at the provider, and the debit from the AI credits
    /// that call makes (the production rule, at the shipped margin), so the balance follows the ledger the way a real check leaves it.
    /// The starting credit is granted first, so the balance is the one the organization would have.
    /// </summary>
    public static async Task SpendAsync(AppHostFixture fixture, Guid organizationId, decimal cost)
    {
        await using var db = await TenantDbAsync(fixture, organizationId);
        await AiCreditLedger.EnsureStartingCreditAsync(db, organizationId, SandboxBilling, DateTimeOffset.UtcNow, Ct);
        var call = new AiUsageCall
        {
            OrganizationId = organizationId,
            OccurredAt = DateTimeOffset.UtcNow,
            CheckName = AiUsageChecks.ConsistencyComparison,
            Provider = "Fake",
            Model = ScreenHire.Infrastructure.Verification.Llm.FakeConsistencyChatClient.ModelId,
            KeySource = AiKeySource.BuiltIn,
            ProviderRequestId = $"seed-{Guid.NewGuid():N}",
            InputTokens = 1000,
            OutputTokens = 0,
            CostBasis = AiCostBasis.Priced,
            Cost = cost,
            Outcome = AiCallOutcome.Ok,
        };
        db.AiUsageCalls.Add(call);
        db.CreditEntries.AddRange(AiCreditLedger.UsageEntries([call], SandboxBilling.AiMarginInForce, Guid.Empty, Guid.Empty));
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Takes the organization's AI credits to exactly zero with one usage entry for what is left (the starting credit is granted first,
    /// or the next check would grant it and run). The AI check then pauses, as it does for an organization that has used its credits.
    /// </summary>
    public static async Task DrainCreditsAsync(AppHostFixture fixture, Guid organizationId)
    {
        await using var db = await TenantDbAsync(fixture, organizationId);
        await AiCreditLedger.EnsureStartingCreditAsync(db, organizationId, SandboxBilling, DateTimeOffset.UtcNow, Ct);
        var balance = await AiCreditLedger.BalanceMicrosAsync(db, Ct);
        if (balance <= 0)
        {
            return;
        }

        db.CreditEntries.Add(new CreditEntry
        {
            OrganizationId = organizationId,
            Kind = CreditEntryKind.Usage,
            Micros = -balance,
            At = DateTimeOffset.UtcNow,
            SourceKey = CreditSourceKeys.Call(Guid.CreateVersion7()),
        });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The billing settings the test stack runs with (the sandbox), for the production rules these helpers call.</summary>
    public static BillingOptions SandboxBilling { get; } = new() { Sandbox = new BillingSandboxOptions { Enabled = true } };

    public static async Task<ScreenHireDbContext> TenantDbAsync(AppHostFixture fixture, Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
