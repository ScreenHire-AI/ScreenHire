using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Shared steps for the integrity tests: open a job, apply as the careers server does (uploads first, then the
/// submission with the candidate's address forwarded), and wait for the worker's assessment.
/// </summary>
internal static class IntegritySupport
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

    /// <summary>The application's integrity once the worker has assessed it at least once.</summary>
    public static async Task<ApiResult> AssessedAsync(Tenant tenant, Guid applicationId)
    {
        ApiResult? result = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                result = (await tenant.GetAsync($"/v1/applications/{applicationId}/integrity")).Expect(HttpStatusCode.OK);
                return result.Text("status") == "Assessed";
            },
            "the worker to assess the application",
            Ct);
        return result!;
    }

    /// <summary>
    /// The application's integrity once every analyzer has run, content forensics included (they wait for the files to be
    /// read and the consistency check to finish), and the last reassessment is readable.
    /// </summary>
    public static async Task<ApiResult> SettledAsync(AppHostFixture fixture, Tenant tenant, Guid applicationId)
    {
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await TenantDbAsync(fixture, tenant.OrganizationId);
                var ran = await db.IntegrityAnalyzerRuns.Where(r => r.ApplicationId == applicationId).Select(r => r.Analyzer).Distinct().CountAsync(Ct);
                return ran == Analyzers.Length;
            },
            "every analyzer to run, content forensics included",
            Ct);
        return await AssessedAsync(tenant, applicationId);
    }

    public static async Task<ScreenHireDbContext> TenantDbAsync(AppHostFixture fixture, Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
