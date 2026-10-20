using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Api.Demo;
using ScreenHire.Application.Integrity;

namespace ScreenHire.IntegrationTests.Demo;

/// <summary>
/// <c>pnpm dev</c> brings up a working demo: once the API is healthy, Brightwater has its template, the draft and closed
/// jobs, the open jobs on their boards, the candidate emails and the applicants from <see cref="SyntheticApplicants"/> and
/// <see cref="DemoCandidates"/> (docs/operations/local-demo.md). The API reports healthy only after the demo has run, so
/// what the API itself adds is there at once; what the worker does afterwards (emails) is awaited with a deadline.
/// Template, job, board, stage and automation names are written out here from that guide, not read from the seeder.
/// </summary>
public sealed class DevelopmentDemoTests(DevelopmentDemoFixture fixture) : IClassFixture<DevelopmentDemoFixture>
{
    private const string TemplateName = "Brightwater engineering";
    private const string DraftJobSlug = "backend-engineer-payments";
    private const string SeniorDotnetSlug = "senior-dotnet-engineer";
    private const string ProductDesignerSlug = "senior-product-designer";
    private const string FrontendSlug = "frontend-engineer-svelte";
    private const string ClosedJobSlug = "support-engineer";

    // The demo logs one line when a start has finished all its steps, whatever it added.
    private const string DemoFinishedLog = "Development demo:";
    private const string DemoFailedLog = "could not be seeded";

    private static readonly string[] CandidateEmailAutomations = ["demo_application_received", "demo_screening_email", "demo_interview_email"];

    // The stages of the "Software engineer" pipeline and of the default one, together.
    private static readonly string[] EveryStage = ["applied", "screening", "technical", "interview", "offer", "hired", "rejected", "withdrawn"];

    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_demo_data_is_in_as_soon_as_the_api_is_healthy()
    {
        // No polling: the fixture waited for a healthy API, which the demo holds back until its last step has run.
        using var owner = await SignedInOwnerAsync();

        string?[] unmet =
        [
            await TemplateUnmetAsync(owner),
            await DraftJobUnmetAsync(owner),
            await ClosedJobUnmetAsync(owner),
            await SyntheticApplicantsUnmetAsync(owner),
            await DemoCandidatesUnmetAsync(owner),
        ];
        unmet.Should().AllSatisfy(problem => problem.Should().BeNull());
    }

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
    public async Task Each_open_job_has_the_three_candidate_email_automations()
    {
        using var owner = await SignedInOwnerAsync();

        foreach (var slug in new[] { SeniorDotnetSlug, ProductDesignerSlug, FrontendSlug })
        {
            await EventuallyAsync(() => AutomationsUnmetAsync(owner, slug));
        }
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
    public async Task Every_stage_of_both_pipelines_holds_an_application()
    {
        using var owner = await SignedInOwnerAsync();

        var stages = (await GetAsync(owner, "/v1/applications")).EnumerateArray()
            .Select(a => a.GetProperty("stage").GetProperty("id").GetString())
            .ToHashSet();
        stages.Should().Contain(EveryStage);
    }

    [Fact]
    public async Task Each_note_a_recruiter_left_is_on_the_candidates_timeline()
    {
        using var owner = await SignedInOwnerAsync();

        foreach (var candidate in DemoCandidates.All.Where(c => c.Moves.Any(m => m.Note is not null)))
        {
            var application = (await ApplicantsAsync(owner, candidate.JobSlug))
                .Single(a => a.GetProperty("candidateName").GetString() == candidate.Applicant.FullName);
            var notes = (await GetAsync(owner, $"/v1/applications/{application.GetProperty("id").GetGuid()}/timeline")).EnumerateArray()
                .Where(e => e.GetProperty("type").GetString() == "StageChanged")
                .Select(e => (To: e.GetProperty("toStageId").GetString(), Note: e.GetProperty("note").GetString()))
                .ToList();

            notes.Should().Equal(candidate.Moves.Select(m => (To: (string?)m.StageId, m.Note)), candidate.Applicant.FullName);
        }
    }

    [Fact]
    public async Task The_open_jobs_are_on_their_boards()
    {
        using var owner = await SignedInOwnerAsync();
        var everyBoard = (await GetAsync(owner, "/v1/boards")).EnumerateArray().Select(b => b.GetProperty("id").GetString()).Order().ToList();

        (await BoardsOfAsync(owner, SeniorDotnetSlug)).Should().Equal(everyBoard);
        (await BoardsOfAsync(owner, ProductDesignerSlug)).Should().Equal(everyBoard);
        (await BoardsOfAsync(owner, FrontendSlug)).Should().Equal("careers", "sandbox");
        (await BoardsOfAsync(owner, DraftJobSlug)).Should().BeEmpty("a draft is on no board");
    }

    [Fact]
    public async Task The_support_job_is_closed_and_taken_down_everywhere()
    {
        using var owner = await SignedInOwnerAsync();

        (await ClosedJobUnmetAsync(owner)).Should().BeNull();
        var job = (await FindJobAsync(owner, ClosedJobSlug))!.Value;
        var postings = (await GetAsync(owner, $"/v1/jobs/{job.GetProperty("id").GetGuid()}")).GetProperty("postings").EnumerateArray().ToList();
        postings.Should().NotBeEmpty("it was published to every board before it closed");
        postings.Should().AllSatisfy(p => p.GetProperty("state").GetString().Should().BeOneOf("Closed", "Closing"));

        using var anonymous = fixture.CreateHttpClient("api");
        using var response = await anonymous.GetAsync($"/v1/public/organizations/brightwater/jobs/{ClosedJobSlug}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Gone, "a published job that closed is gone for good");
    }

    [Fact]
    public async Task Each_candidate_email_automation_sent_an_email()
    {
        // Who gets which email, from the demo guide: everyone on an open job is told their application arrived, Tom
        // Whitfield reached Screening and Priya Natarajan reached Interview on the designer job.
        var mailpit = fixture.Mailpit;

        var robin = await mailpit.WaitForMessagesAsync(SyntheticApplicants.GenuineHuman.Email, 1, Ct);
        robin.Should().Contain(m => m.Subject == "We received your application for Senior .NET Engineer");

        foreach (var (email, stage) in new[] { ("tom.whitfield@example.org", "Screening"), ("priya.natarajan@example.com", "Interview") })
        {
            var sent = await mailpit.WaitForMessagesAsync(email, 2, Ct);
            sent.Should().Contain(m => m.Subject == "We received your application for Senior Product Designer", email);
            sent.Should().Contain(
                m => m.Subject == "An update on your application for Senior Product Designer" && m.Text.Contains(stage, StringComparison.Ordinal),
                "{0} was moved to {1}", email, stage);
        }
    }

    [Fact]
    public async Task Starting_the_api_again_adds_nothing()
    {
        using var first = await SignedInOwnerAsync();
        await EventuallyAsync(async () =>
            await TemplateUnmetAsync(first) ?? await DraftJobUnmetAsync(first) ?? await AutomationsUnmetAsync(first, SeniorDotnetSlug)
            ?? await SyntheticApplicantsUnmetAsync(first) ?? await DemoCandidatesUnmetAsync(first) ?? await ClosedJobUnmetAsync(first));
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

    // What the demo adds, counted: templates, jobs, applications, postings, and the timeline entries the API writes
    // (submissions and moves; the worker's tags and assessments arrive on their own schedule, so they are left out).
    private static async Task<(int Templates, int Jobs, int Applications, int Postings, int Moves)> CountsAsync(ApiSession owner)
    {
        var jobs = (await GetAsync(owner, "/v1/jobs")).EnumerateArray().ToList();
        var applications = (await GetAsync(owner, "/v1/applications")).EnumerateArray().ToList();
        var postings = 0;
        foreach (var job in jobs)
        {
            postings += (await GetAsync(owner, $"/v1/jobs/{job.GetProperty("id").GetGuid()}")).GetProperty("postings").GetArrayLength();
        }

        var moves = 0;
        foreach (var application in applications)
        {
            moves += (await GetAsync(owner, $"/v1/applications/{application.GetProperty("id").GetGuid()}/timeline")).EnumerateArray()
                .Count(e => e.GetProperty("type").GetString() is "Submitted" or "StageChanged");
        }

        return ((await GetAsync(owner, "/v1/templates")).GetArrayLength(), jobs.Count, applications.Count, postings, moves);
    }

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

    private static async Task<string?> DraftJobUnmetAsync(ApiSession owner) => await JobStatusUnmetAsync(owner, DraftJobSlug, "Draft");

    private static async Task<string?> ClosedJobUnmetAsync(ApiSession owner) => await JobStatusUnmetAsync(owner, ClosedJobSlug, "Closed");

    private static async Task<string?> JobStatusUnmetAsync(ApiSession owner, string slug, string status)
    {
        var job = await FindJobAsync(owner, slug);
        return job is null ? $"no job '{slug}'"
            : job.Value.GetProperty("status").GetString() == status ? null
            : $"'{slug}' is {job.Value.GetProperty("status").GetString()}, not {status}";
    }

    private static async Task<string?> AutomationsUnmetAsync(ApiSession owner, string slug)
    {
        var job = await FindJobAsync(owner, slug);
        if (job is null)
        {
            return $"no job '{slug}'";
        }

        var detail = await GetAsync(owner, $"/v1/jobs/{job.Value.GetProperty("id").GetGuid()}");
        var present = detail.GetProperty("workflow").GetProperty("automations").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()).ToList();
        var missing = CandidateEmailAutomations.Except(present).ToList();
        return missing.Count == 0 ? null : $"the workflow of '{slug}' lacks automations {string.Join(", ", missing)}";
    }

    // Every synthetic applicant once; the .NET job also has the demo candidates who applied to it.
    private static async Task<string?> SyntheticApplicantsUnmetAsync(ApiSession owner)
    {
        var names = (await ApplicantsAsync(owner, SeniorDotnetSlug)).Select(a => a.GetProperty("candidateName").GetString()!).ToList();
        var missing = SyntheticApplicants.All.Where(a => names.Count(n => n == a.FullName) != 1).Select(a => a.FullName).ToList();
        return missing.Count == 0
            ? null
            : $"applicants on '{SeniorDotnetSlug}' are [{string.Join("; ", names)}]; not there exactly once: [{string.Join("; ", missing)}]";
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

    private static async Task<List<string?>> BoardsOfAsync(ApiSession owner, string slug)
    {
        var job = (await FindJobAsync(owner, slug))!.Value;
        return [.. (await GetAsync(owner, $"/v1/jobs/{job.GetProperty("id").GetGuid()}")).GetProperty("postings").EnumerateArray()
            .Select(p => p.GetProperty("boardId").GetString())
            .Order()];
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

    // Polls until `unmet` has nothing to report: what the API adds is there once it is healthy, but the restart test
    // asks while a start may be under way.
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
