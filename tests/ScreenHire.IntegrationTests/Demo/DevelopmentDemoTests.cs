using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Api.Demo;
using ScreenHire.Application.Integrity;

namespace ScreenHire.IntegrationTests.Demo;

/// <summary>
/// <c>pnpm dev</c> brings up a working demo: after the API starts, Brightwater has its template, the draft job, the
/// candidate emails on the .NET job and the applicants from <see cref="SyntheticApplicants"/> and <see cref="DemoCandidates"/>
/// (docs/operations/local-demo.md). The demo runs in the background once the API has started, so each test waits, with a
/// deadline, for the state it asserts. Template, job and automation names are written out here from that guide, not read
/// from the seeder.
/// </summary>
public sealed class DevelopmentDemoTests(DevelopmentDemoFixture fixture) : IClassFixture<DevelopmentDemoFixture>
{
    private const string TemplateName = "Brightwater engineering";
    private const string DraftJobSlug = "backend-engineer-payments";
    private const string SeniorDotnetSlug = "senior-dotnet-engineer";

    // The demo logs one line when a start has finished all its steps, whatever it added.
    private const string DemoFinishedLog = "Development demo:";
    private const string DemoFailedLog = "could not be seeded";

    private static readonly string[] CandidateEmailAutomations = ["demo_application_received", "demo_screening_email", "demo_interview_email"];

    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_brightwater_engineering_template_is_published()
    {
        using var owner = await SignedInOwnerAsync();

        await EventuallyAsync(() => TemplateUnmetAsync(owner));
    }

    [Fact]
    public async Task The_payments_job_is_a_draft()
    {
        using var owner = await SignedInOwnerAsync();

        await EventuallyAsync(() => DraftJobUnmetAsync(owner));
    }

    [Fact]
    public async Task The_dotnet_job_has_the_three_candidate_email_automations()
    {
        using var owner = await SignedInOwnerAsync();

        await EventuallyAsync(() => AutomationsUnmetAsync(owner));
    }

    [Fact]
    public async Task Every_synthetic_applicant_applied_to_the_dotnet_job_once()
    {
        using var owner = await SignedInOwnerAsync();

        await EventuallyAsync(() => SyntheticApplicantsUnmetAsync(owner));
    }

    [Fact]
    public async Task Each_demo_candidate_applied_to_their_job_and_sits_in_their_stage()
    {
        using var owner = await SignedInOwnerAsync();

        await EventuallyAsync(() => DemoCandidatesUnmetAsync(owner));
    }

    [Fact]
    public async Task Starting_the_api_again_adds_nothing()
    {
        using var first = await SignedInOwnerAsync();
        await EventuallyAsync(async () =>
            await TemplateUnmetAsync(first) ?? await DraftJobUnmetAsync(first) ?? await AutomationsUnmetAsync(first)
            ?? await SyntheticApplicantsUnmetAsync(first) ?? await DemoCandidatesUnmetAsync(first));
        var before = await CountsAsync(first);

        // Watch the API's log from before the restart, so the line the second start logs is told apart from the first's.
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var finishedStarts = 0;
        var secondStartFinished = new TaskCompletionSource();
        var watcher = Task.Run(
            async () =>
            {
                // By resource, not by name: the name of a running project's log stream carries a suffix.
                var api = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == "api");
                var logs = fixture.App.Services.GetRequiredService<ResourceLoggerService>();
                await foreach (var batch in logs.WatchAsync(api).WithCancellation(watching.Token))
                {
                    if (batch.Any(line => line.Content.Contains(DemoFailedLog, StringComparison.Ordinal)))
                    {
                        secondStartFinished.TrySetException(new InvalidOperationException("The demo logged that it could not be seeded."));
                    }

                    finishedStarts += batch.Count(line => line.Content.Contains(DemoFinishedLog, StringComparison.Ordinal));
                    if (finishedStarts >= 2)
                    {
                        secondStartFinished.TrySetResult();
                    }
                }
            },
            Ct);

        try
        {
            await RunCommandAsync(KnownResourceCommands.RestartCommand);
            await secondStartFinished.Task.WaitAsync(Deadline, Ct);
        }
        finally
        {
            await watching.CancelAsync();
            try
            {
                await watcher;
            }
            catch (OperationCanceledException)
            {
                // The watch ended because we cancelled it.
            }
        }

        using var second = await SignedInOwnerAsync();
        (await CountsAsync(second)).Should().Be(before, "a start that finds the demo already there adds nothing");
    }

    private async Task RunCommandAsync(string command)
    {
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync("api", command, Ct);
        result.Success.Should().BeTrue(result.Message);
    }

    private async Task<ApiSession> SignedInOwnerAsync()
    {
        var session = new ApiSession(fixture);
        await session.SignInWithDevSsoAsync("brightwater-owner", Ct);
        return session;
    }

    // What the demo adds, counted: templates, jobs and applications in the organization.
    private static async Task<(int Templates, int Jobs, int Applications)> CountsAsync(ApiSession owner) =>
        ((await GetAsync(owner, "/v1/templates")).GetArrayLength(),
         (await GetAsync(owner, "/v1/jobs")).GetArrayLength(),
         (await GetAsync(owner, "/v1/applications")).GetArrayLength());

    private static async Task<string?> TemplateUnmetAsync(ApiSession owner)
    {
        var template = (await GetAsync(owner, "/v1/templates")).EnumerateArray()
            .FirstOrDefault(t => t.GetProperty("name").GetString() == TemplateName);
        if (template.ValueKind == JsonValueKind.Undefined)
        {
            return $"no template named '{TemplateName}'";
        }

        if (template.GetProperty("isSystem").GetBoolean())
        {
            return $"'{TemplateName}' is a system template, not Brightwater's own";
        }

        if (template.GetProperty("latestPublishedVersion").ValueKind != JsonValueKind.Number)
        {
            return $"'{TemplateName}' has no published version";
        }

        var detail = await GetAsync(owner, $"/v1/templates/{template.GetProperty("id").GetGuid()}");
        return detail.GetProperty("versions").EnumerateArray().Any(v => v.GetProperty("status").GetString() == "Published")
            ? null
            : $"'{TemplateName}' lists no version with status Published";
    }

    private static async Task<string?> DraftJobUnmetAsync(ApiSession owner)
    {
        var job = await FindJobAsync(owner, DraftJobSlug);
        return job is null ? $"no job '{DraftJobSlug}'"
            : job.Value.GetProperty("status").GetString() == "Draft" ? null
            : $"'{DraftJobSlug}' is {job.Value.GetProperty("status").GetString()}, not Draft";
    }

    private static async Task<string?> AutomationsUnmetAsync(ApiSession owner)
    {
        var job = await FindJobAsync(owner, SeniorDotnetSlug);
        if (job is null)
        {
            return $"no job '{SeniorDotnetSlug}'";
        }

        var detail = await GetAsync(owner, $"/v1/jobs/{job.Value.GetProperty("id").GetGuid()}");
        var present = detail.GetProperty("workflow").GetProperty("automations").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()).ToList();
        var missing = CandidateEmailAutomations.Except(present).ToList();
        return missing.Count == 0 ? null : $"the workflow of '{SeniorDotnetSlug}' lacks automations {string.Join(", ", missing)}";
    }

    private static async Task<string?> SyntheticApplicantsUnmetAsync(ApiSession owner)
    {
        var expected = SyntheticApplicants.All.Select(a => a.FullName).Order().ToList();
        var names = (await ApplicantsAsync(owner, SeniorDotnetSlug)).Select(a => a.GetProperty("candidateName").GetString()!).Order().ToList();
        return names.SequenceEqual(expected)
            ? null
            : $"applicants on '{SeniorDotnetSlug}' are [{string.Join("; ", names)}], expected [{string.Join("; ", expected)}]";
    }

    private static async Task<string?> DemoCandidatesUnmetAsync(ApiSession owner)
    {
        foreach (var group in DemoCandidates.All.GroupBy(c => c.JobSlug))
        {
            var applicants = await ApplicantsAsync(owner, group.Key);
            foreach (var candidate in group)
            {
                var matches = applicants.Where(a => a.GetProperty("candidateName").GetString() == candidate.Applicant.FullName).ToList();
                if (matches.Count != 1)
                {
                    return $"{matches.Count} applications from '{candidate.Applicant.FullName}' on '{group.Key}', expected 1";
                }

                var stage = matches[0].GetProperty("stage").GetProperty("id").GetString();
                if (stage != candidate.StageId)
                {
                    return $"'{candidate.Applicant.FullName}' is in '{stage}' on '{group.Key}', expected '{candidate.StageId}'";
                }
            }
        }

        return null;
    }

    private static async Task<JsonElement> GetAsync(ApiSession session, string path)
    {
        using var response = await session.Client.GetAsync(path, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0}", path);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement?> FindJobAsync(ApiSession session, string slug) =>
        (await GetAsync(session, "/v1/jobs")).EnumerateArray()
            .Select(j => (JsonElement?)j)
            .FirstOrDefault(j => j!.Value.GetProperty("slug").GetString() == slug);

    // Empty while the job is not there yet.
    private static async Task<List<JsonElement>> ApplicantsAsync(ApiSession session, string slug) =>
        await FindJobAsync(session, slug) is { } job
            ? [.. (await GetAsync(session, $"/v1/jobs/{job.GetProperty("id").GetGuid()}/applications")).EnumerateArray()]
            : [];

    // The demo runs after the API starts and finishes in the background, so poll until `unmet` has nothing to report.
    private static async Task EventuallyAsync(Func<Task<string?>> unmet)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(Deadline);
        string? last = null;
        try
        {
            while ((last = await unmet()) is not null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            throw new TimeoutException($"After {Deadline.TotalSeconds:0} seconds the demo was still missing: {last}.");
        }
    }
}
