using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// One organization with a record of every kind the dashboard shows, built once through the API for the tests that walk the
/// whole dashboard in dark mode and at phone widths (<see cref="DashboardThemeTests"/>, <see cref="DashboardPhoneTests"/>).
/// A published job with three synthetic applicants whose verification has run (a clear one, a bot and an honest
/// AI-assisted one), a hiring, scorecard and email template, a custom role, an API key, a webhook endpoint and a task. The
/// seeded Brightwater is read-only, so this is a new person and a new organization.
/// </summary>
public sealed class ProofScene
{
    public const string JobTitle = "Platform Engineer";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string[] Gb = ["GB"];
    private static readonly string[] ReadScope = ["Read"];
    private static readonly string[] PublishedEvent = ["job.published"];
    private static ProofScene? built;

    private ProofScene()
    {
    }

    public string OwnerEmail { get; private init; } = "";

    public Guid JobId { get; private init; }

    /// <summary>The application of the bot: the one with findings in every verification group it triggers.</summary>
    public Guid BotApplicationId { get; private init; }

    public Guid BotCandidateId { get; private init; }

    public Guid GenuineApplicationId { get; private init; }

    public string BotResumeFileName { get; private init; } = "";

    public string TemplateId { get; private init; } = "";

    public string ScorecardTemplateId { get; private init; } = "";

    public string EmailTemplateId { get; private init; } = "";

    public string RoleId { get; private init; } = "";

    public string EndpointId { get; private init; } = "";

    public static async Task<ProofScene> GetAsync(E2EFixture fixture, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return built ??= await BuildAsync(fixture, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<ProofScene> BuildAsync(E2EFixture fixture, CancellationToken cancellationToken)
    {
        var email = ApiSession.NewEmail("e2e-proof");
        using var owner = new ApiSession(fixture);
        await owner.SignInWithDevSsoAsync(email, cancellationToken);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await owner.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, cancellationToken), HttpStatusCode.OK, cancellationToken);

        var job = await ReadAsync(await owner.PostAsync(
            "/v1/jobs",
            new
            {
                title = JobTitle,
                department = "Engineering",
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
                salary = new { min = 50_000, max = 60_000, currency = "GBP", period = "Year" },
            },
            cancellationToken), HttpStatusCode.Created, cancellationToken);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await owner.PostAsync($"/v1/jobs/{jobId}/publish", new { }, cancellationToken), HttpStatusCode.OK, cancellationToken);

        var genuineId = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.GenuineHuman, cancellationToken);
        var botId = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.Bot, cancellationToken);
        var assistedId = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.HonestAiAssisted, cancellationToken);
        foreach (var id in new[] { genuineId, botId, assistedId })
        {
            await SettledAsync(fixture, slug, id, cancellationToken);
        }

        var bot = await ReadAsync(await owner.Client.GetAsync($"/v1/applications/{botId}", cancellationToken), HttpStatusCode.OK, cancellationToken);

        var template = await ReadAsync(await owner.PostAsync("/v1/templates", new { name = "Engineering hiring" }, cancellationToken), HttpStatusCode.Created, cancellationToken);
        var scorecard = await ReadAsync(await owner.PostAsync("/v1/scorecard-templates", new
        {
            name = "Engineering scorecard",
            questions = new[] { new { id = "q1", type = "Rating5", label = "How well do they fit the role?", required = true } },
        }, cancellationToken), HttpStatusCode.Created, cancellationToken);
        var emailTemplate = await ReadAsync(await owner.PostAsync("/v1/email-templates", new { name = "Thanks for applying", subject = "Thanks, {{ candidateName }}", body = "Hi {{ candidateName }}." }, cancellationToken), HttpStatusCode.Created, cancellationToken);
        var role = await ReadAsync(await owner.PostAsync("/v1/organization/roles", new { name = "Interviewer", copyFrom = "Viewer" }, cancellationToken), HttpStatusCode.Created, cancellationToken);
        await ReadAsync(await owner.PostAsync("/v1/organization/api-keys", new { name = "Payroll sync", scopes = ReadScope }, cancellationToken), HttpStatusCode.Created, cancellationToken);
        var endpoint = await ReadAsync(await owner.PostAsync("/v1/webhooks", new { url = "https://hooks.example.com/in", events = PublishedEvent, description = "Applicant sync" }, cancellationToken), HttpStatusCode.Created, cancellationToken);
        await ReadAsync(await owner.PostAsync($"/v1/applications/{botId}/tasks", new { note = "Check the phone number" }, cancellationToken), HttpStatusCode.Created, cancellationToken);

        return new ProofScene
        {
            OwnerEmail = email,
            JobId = jobId,
            BotApplicationId = botId,
            BotCandidateId = bot.GetProperty("candidate").GetProperty("id").GetGuid(),
            GenuineApplicationId = genuineId,
            BotResumeFileName = SyntheticApplicants.Bot.ResumeFileName,
            TemplateId = template.GetProperty("id").GetGuid().ToString(),
            ScorecardTemplateId = scorecard.GetProperty("id").GetGuid().ToString(),
            EmailTemplateId = emailTemplate.GetProperty("id").GetGuid().ToString(),
            RoleId = role.GetProperty("id").GetString()!,
            EndpointId = endpoint.GetProperty("endpoint").GetProperty("id").GetString()!,
        };
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(cancellationToken));
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
    }

    // The worker assesses after the submission; the walk starts only once every analyzer has run, so no page is read
    // half-assessed. The first run after the stack starts is the slowest.
    private static async Task SettledAsync(E2EFixture fixture, string slug, Guid applicationId, CancellationToken cancellationToken)
    {
        var reader = new TenantContext();
        reader.SetOrganization(Guid.NewGuid()); // organizations are public rows, readable as any tenant
        Guid organizationId;
        await using (var db = await fixture.CreateDbContextAsync(reader, cancellationToken))
        {
            organizationId = await db.Organizations.Where(o => o.Slug == slug).Select(o => o.Id).SingleAsync(cancellationToken);
        }

        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        for (var attempt = 0; attempt < 600; attempt++)
        {
            await using var db = await fixture.CreateDbContextAsync(tenant, cancellationToken);
            if (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == applicationId).Select(r => r.Analyzer).Distinct().CountAsync(cancellationToken) >= 8)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"Expected every verification analyzer to run on application {applicationId}.");
    }
}
