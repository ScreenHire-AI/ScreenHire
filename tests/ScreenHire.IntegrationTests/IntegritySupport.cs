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

    /// <summary>
    /// Applies as <paramref name="applicant"/>: their resume, then any <paramref name="files"/> for other file questions,
    /// then the submission with <paramref name="answers"/> (none by default: the default workflow asks nothing else).
    /// </summary>
    public static async Task<Guid> ApplyAsync(
        AppHostFixture fixture,
        string slug,
        Guid jobId,
        SyntheticApplicant applicant,
        bool accommodation = false,
        IReadOnlyList<ApplicationAnswer>? answers = null,
        IReadOnlyList<(string QuestionId, string FileName, byte[] Content)>? files = null)
    {
        using var client = fixture.CreateHttpClient("api");
        var prefix = $"/v1/public/organizations/{slug}/jobs/{jobId}";
        var submitted = new JsonArray(Answer("resume", (await UploadAsync(client, prefix, "resume", applicant.ResumeFileName, applicant.ResumeBytes())).ToString()));
        foreach (var (questionId, fileName, content) in files ?? [])
        {
            submitted.Add(Answer(questionId, (await UploadAsync(client, prefix, questionId, fileName, content)).ToString()));
        }

        foreach (var answer in answers ?? [])
        {
            submitted.Add(Answer(answer.QuestionId, answer.Value!));
        }

        var body = new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = applicant.FullName, ["email"] = applicant.Email, ["phone"] = applicant.Phone },
            ["privacyNoticeVersion"] = "2026-10-20.3",
            ["answers"] = submitted,
            // The default workflow wants AI use disclosed, so an applicant who did not say is sent as "did not use".
            ["aiUseDeclaration"] = (applicant.AiUseDeclaration == AiUseDeclaration.NotProvided ? AiUseDeclaration.DidNotUse : applicant.AiUseDeclaration).ToString(),
            ["aiUseDetails"] = applicant.AiUseDetails,
            ["honeypotFilled"] = applicant.HoneypotFilled,
            ["accommodationRequested"] = accommodation,
            ["userAgent"] = applicant.UserAgent,
            ["formStartedAt"] = DateTimeOffset.UtcNow - applicant.TimeToComplete,
            ["automationMarkers"] = new JsonArray([.. applicant.AutomationMarkers.Select(m => JsonValue.Create(m))]),
            ["deviceFingerprint"] = applicant.DeviceFingerprint,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{prefix}/applications") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", applicant.IpAddress);
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

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

    private static JsonObject Answer(string questionId, string value) => new() { ["questionId"] = questionId, ["value"] = value };

    private static async Task<Guid> UploadAsync(HttpClient client, string prefix, string questionId, string fileName, byte[] content)
    {
        var ticketResponse = await client.PostAsJsonAsync($"{prefix}/questions/{questionId}/uploads", new { fileName, sizeBytes = content.Length }, Ct);
        ticketResponse.StatusCode.Should().Be(HttpStatusCode.OK, await ticketResponse.Content.ReadAsStringAsync(Ct));
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        using (var storage = new HttpClient())
        using (var put = new HttpRequestMessage(HttpMethod.Put, ticket.GetProperty("uploadUrl").GetString()) { Content = new ByteArrayContent(content) })
        {
            foreach (var header in ticket.GetProperty("headers").EnumerateObject())
            {
                put.Headers.Add(header.Name, header.Value.GetString());
            }

            (await storage.SendAsync(put, Ct)).IsSuccessStatusCode.Should().BeTrue();
        }

        var uploadId = ticket.GetProperty("uploadId").GetGuid();
        (await client.PostAsync($"{prefix}/questions/{questionId}/uploads/{uploadId}/verify", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        return uploadId;
    }
}
