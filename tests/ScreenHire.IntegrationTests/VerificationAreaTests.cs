using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// What the Verification area's Overview and Activity log read (docs/architecture/verification-engine.md 10.12): this month's
/// applications by level, which groups of checks counted a signal, one newest-first feed of check runs and model calls, and who
/// sees what in it. Uses the synthetic applicants and the fake model, so no test needs a key.
/// </summary>
public sealed class VerificationAreaTests(AppHostFixture fixture)
{
    private const string Settings = "/v1/organization/verification-settings";

    private const string Area = "/v1/verification";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_overview_counts_the_months_applications_by_level_and_the_groups_that_counted_a_signal_and_never_counts_AI_assistance()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant);
        var human = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.GenuineHuman);
        var bot = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await VerificationSupport.SettledAsync(fixture, tenant, human);
        var botVerification = await VerificationSupport.SettledAsync(fixture, tenant, bot);
        botVerification.Text("authenticity.level").Should().Be("VerificationRecommended");

        var overview = (await tenant.GetAsync($"{Area}/overview")).Expect(HttpStatusCode.OK);

        overview.At("checked").GetInt32().Should().Be(2);
        overview.At("pending").GetInt32().Should().Be(0);
        overview.At("levels.clear").GetInt32().Should().Be(1);
        overview.At("levels.review").GetInt32().Should().Be(0);
        overview.At("levels.verify").GetInt32().Should().Be(1);
        var from = overview.At("from").GetDateTimeOffset();
        (from.Offset == TimeSpan.Zero && from.Day == 1 && from.TimeOfDay == TimeSpan.Zero).Should().BeTrue("the month is the UTC calendar month");
        var groups = overview.At("groups").EnumerateArray().ToDictionary(g => g.GetProperty("group").GetString()!, g => g.GetProperty("applications").GetInt32());
        groups.Should().NotBeEmpty().And.ContainKey("Network", "the bot came from a data-centre address");
        groups.Values.Should().OnlyContain(count => count == 1, "only the bot raised anything, and it is counted once per group");
        overview.Body.ToString().Should().NotContainEquivalentOf("aiAssistance", "AI help never counts");
        overview.Body.ToString().Should().NotContainEquivalentOf("score");
    }

    [Fact]
    public async Task The_overview_counts_an_application_whose_checks_have_not_finished_as_pending()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var job = await tenant.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await tenant.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("pending"));

        var overview = (await tenant.GetAsync($"{Area}/overview")).Expect(HttpStatusCode.OK);

        overview.At("checked").GetInt32().Should().Be(0);
        overview.At("pending").GetInt32().Should().Be(1);
        overview.At("groups").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_sees_the_overview_and_the_activity_of_those_jobs_and_none_of_the_organizations_model_calls()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, theirs) = await VerificationSupport.OpenJobAsync(fixture, owner, SystemTemplates.SoftwareEngineerVersion2);
        var hidden = (await owner.CreateJobAsync(Samples.RemoteJob("Quarterly Auditor"))).Id();
        await owner.PublishJobAsync(hidden);
        var visible = await VerificationSupport.ApplyAsync(fixture, slug, theirs, SyntheticApplicants.GenuineHuman, answers: SyntheticApplicants.GenuineHuman.Answers);
        var other = await VerificationSupport.ApplyAsync(fixture, slug, hidden, SyntheticApplicants.Bot);
        await VerificationSupport.SettledAsync(fixture, owner, visible);
        await VerificationSupport.SettledAsync(fixture, owner, other);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        await owner.SetHiringTeamAsync(theirs, viewerMember);

        var ownerOverview = (await owner.GetAsync($"{Area}/overview")).Expect(HttpStatusCode.OK);
        var viewerOverview = (await viewer.GetAsync($"{Area}/overview")).Expect(HttpStatusCode.OK);
        var viewerActivity = (await viewer.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);

        ownerOverview.At("checked").GetInt32().Should().Be(2);
        viewerOverview.At("checked").GetInt32().Should().Be(1, "they see only the job they are on");
        viewerOverview.At("levels.clear").GetInt32().Should().Be(1);
        viewerOverview.At("levels.verify").GetInt32().Should().Be(0);
        viewerOverview.At("groups").GetArrayLength().Should().Be(0);
        viewerActivity.At("usageVisible").GetBoolean().Should().BeFalse();
        var entries = viewerActivity.At("entries").EnumerateArray().ToList();
        entries.Should().NotBeEmpty();
        entries.Select(e => e.GetProperty("applicationId").GetGuid()).Should().OnlyContain(id => id == visible, "nothing about the other job reaches them");
        entries.Select(e => e.GetProperty("jobTitle").GetString()).Should().OnlyContain(title => title == "Platform Engineer");
        (await viewer.Session.Client.GetAsync($"{Area}/activity/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_activity_log_lists_check_runs_and_model_calls_newest_first_and_filters_by_group_key_and_outcome()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var applicationId = await ApplyWithCheckOnAsync(tenant);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var runs = await db.VerificationAnalyzerRuns.AsNoTracking().ToListAsync(Ct);
        var calls = await db.AiUsageCalls.AsNoTracking().ToListAsync(Ct);

        var all = (await tenant.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);

        all.At("usageVisible").GetBoolean().Should().BeTrue();
        all.At("nextBefore").ValueKind.Should().Be(JsonValueKind.Null);
        var entries = all.At("entries").EnumerateArray().ToList();
        entries.Should().HaveCount(runs.Count + calls.Count);
        entries.Select(e => e.GetProperty("at").GetDateTimeOffset()).Should().BeInDescendingOrder("newest first");
        entries.Where(e => e.GetProperty("kind").GetString() == "CheckRun").Select(e => e.GetProperty("check").GetString())
            .Should().BeEquivalentTo(runs.Select(r => r.Analyzer));
        var call = entries.First(e => e.GetProperty("kind").GetString() == "ModelCall");
        call.GetProperty("applicationId").GetGuid().Should().Be(applicationId);
        call.GetProperty("jobTitle").GetString().Should().Be("Platform Engineer");
        call.GetProperty("group").GetString().Should().Be("Answers");
        call.GetProperty("outcome").GetString().Should().Be("Completed");
        call.GetProperty("call").GetProperty("providerRequestId").GetString().Should().StartWith(FakeConsistencyRequestIdPrefix);
        call.GetProperty("call").GetProperty("keySource").GetString().Should().Be("BuiltIn");
        call.GetProperty("call").GetProperty("cost").GetDecimal().Should().BeGreaterThan(0m);

        Entries(await tenant.GetAsync($"{Area}/activity?group=Network&limit=200")).Should().OnlyContain(e => e.GetProperty("check").GetString() == "network").And.HaveCount(runs.Count(r => r.Analyzer == "network"));
        Entries(await tenant.GetAsync($"{Area}/activity?group=Answers&limit=200")).Should().HaveCount(runs.Count(r => r.Analyzer is "answers" or "consistency") + calls.Count);
        Entries(await tenant.GetAsync($"{Area}/activity?keySource=BuiltIn&limit=200")).Should().HaveCount(calls.Count).And.OnlyContain(e => e.GetProperty("kind").GetString() == "ModelCall");
        Entries(await tenant.GetAsync($"{Area}/activity?keySource=OwnKey&limit=200")).Should().BeEmpty();
        Entries(await tenant.GetAsync($"{Area}/activity?outcome=Failed&limit=200")).Should().BeEmpty();
        Entries(await tenant.GetAsync($"{Area}/activity?outcome=Found&limit=200")).Should().OnlyContain(e => e.GetProperty("kind").GetString() == "CheckRun");
        Entries(await tenant.GetAsync($"{Area}/activity?from=2099-01-01T00:00:00Z&limit=200")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_activity_log_pages_by_cursor_without_gaps_or_repeats_and_refuses_a_bad_cursor_or_limit()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        await ApplyWithCheckOnAsync(tenant);
        var total = Entries(await tenant.GetAsync($"{Area}/activity?limit=200")).Count;
        total.Should().BeGreaterThan(6);

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var path = $"{Area}/activity?limit=4" + (cursor is null ? "" : $"&before={Uri.EscapeDataString(cursor)}");
            var page = (await tenant.GetAsync(path)).Expect(HttpStatusCode.OK);
            seen.AddRange(page.At("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
            var next = page.At("nextBefore");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            pages++;
        }
        while (cursor is not null && pages < 50);

        seen.Should().OnlyHaveUniqueItems().And.HaveCount(total);
        pages.Should().Be((int)Math.Ceiling(total / 4.0));
        (await tenant.GetAsync($"{Area}/activity?before=garbage")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("before");
        (await tenant.GetAsync($"{Area}/activity?limit=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("limit");
        (await tenant.GetAsync($"{Area}/activity?from=2026-01-01T00:00:00Z&to=2025-01-01T00:00:00Z")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("to");
    }

    [Fact]
    public async Task Owners_and_Admins_get_the_cost_the_request_id_and_the_csv_and_everyone_else_gets_the_same_rows_without_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await ApplyWithCheckOnAsync(owner);
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var full = Entries(await owner.GetAsync($"{Area}/activity?limit=200"));

        foreach (var reader in new[] { owner, admin })
        {
            var read = (await reader.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);
            read.At("usageVisible").GetBoolean().Should().BeTrue();
            read.At("entries").EnumerateArray().Count(e => e.GetProperty("call").ValueKind == JsonValueKind.Object).Should().BeGreaterThan(0);

            using var export = await reader.Session.Client.GetAsync($"{Area}/activity/export", Ct);
            export.StatusCode.Should().Be(HttpStatusCode.OK);
            export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            export.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            var csv = (await export.Content.ReadAsStringAsync(Ct)).TrimStart((char)0xFEFF);
            var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            lines[0].Should().Be("id,at,kind,group,check,outcome,application_id,job,note,provider,model,key_source,provider_request_id,input_tokens,output_tokens,cost_usd,duration_ms,error_kind");
            lines.Length.Should().Be(full.Count + 1);
            csv.Should().Contain(FakeConsistencyRequestIdPrefix);
        }

        foreach (var reader in new[] { recruiter, viewer })
        {
            var read = (await reader.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);
            read.At("usageVisible").GetBoolean().Should().BeFalse();
            read.At("entries").GetArrayLength().Should().Be(full.Count, "the same rows: every application is on a job they can open");
            read.At("entries").EnumerateArray().Should().OnlyContain(e => e.GetProperty("call").ValueKind == JsonValueKind.Null);
            read.Body.ToString().Should().NotContain(FakeConsistencyRequestIdPrefix).And.NotContain("providerRequestId").And.NotContain("cost");
            (await reader.Session.Client.GetAsync($"{Area}/activity/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_turned_off_group_shows_in_the_log_as_not_run_with_why()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var checks = new JsonObject { ["network"] = false, ["contact"] = true, ["documents"] = true, ["answers"] = true, ["duplicates"] = true, ["formUse"] = true };
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["checks"] = checks })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant);
        await VerificationSupport.SettledAsync(fixture, tenant, await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.GenuineHuman));

        var notRun = Entries(await tenant.GetAsync($"{Area}/activity?group=Network&outcome=NotRun&limit=200"));
        Entries(await tenant.GetAsync($"{Area}/activity?outcome=NotRun&limit=200")).Select(e => e.GetProperty("check").GetString())
            .Should().Contain(["network", "consistency"], "the model check is off too, and a check that could not look says so");

        notRun.Should().ContainSingle();
        notRun[0].GetProperty("check").GetString().Should().Be("network");
        notRun[0].GetProperty("group").GetString().Should().Be("Network");
        notRun[0].GetProperty("note").GetString().Should().Be(CheckGroups.TurnedOffNote(CheckGroup.Network));
    }

    [Fact]
    public async Task The_usage_summary_splits_each_days_tokens_and_cost_between_ScreenHires_key_and_the_own_key()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        await ApplyWithCheckOnAsync(tenant);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var calls = await db.AiUsageCalls.AsNoTracking().ToListAsync(Ct);

        var summary = (await tenant.GetAsync("/v1/organization/ai-usage/summary")).Expect(HttpStatusCode.OK);

        var days = summary.At("byDay").EnumerateArray().ToList();
        days.Sum(d => d.GetProperty("builtInTokens").GetInt64()).Should().Be(calls.Sum(c => c.InputTokens!.Value + c.OutputTokens!.Value));
        days.Sum(d => d.GetProperty("ownKeyTokens").GetInt64()).Should().Be(0);
        days.Sum(d => d.GetProperty("builtInCost").GetDecimal()).Should().Be(calls.Sum(c => c.Cost!.Value));
        days.Sum(d => d.GetProperty("ownKeyCost").GetDecimal()).Should().Be(0m);
    }

    [Fact]
    public async Task Saving_only_the_checks_keeps_an_own_key_and_its_model_and_asks_the_provider_nothing()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var ownKey = new JsonObject { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey", ["provider"] = "OpenRouter", ["model"] = "vendor/model", ["apiKey"] = "own-key-area-test" };
        (await tenant.PutAsync(Settings, ownKey)).Expect(HttpStatusCode.OK);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var callsBefore = await db.AiUsageCalls.CountAsync(Ct);

        // What a Checks tab sends: everything as saved, the key left null, and its own part changed.
        var checksOnly = new JsonObject
        {
            ["llmConsistencyCheck"] = true,
            ["modelSource"] = "OwnKey",
            ["provider"] = "OpenRouter",
            ["baseUrl"] = null,
            ["model"] = "vendor/model",
            ["apiKey"] = null,
            ["checks"] = new JsonObject { ["network"] = true, ["contact"] = true, ["documents"] = false, ["answers"] = true, ["duplicates"] = true, ["formUse"] = true },
            ["defaultAiUse"] = "AllowedWithDisclosure",
        };
        var saved = (await tenant.PutAsync(Settings, checksOnly)).Expect(HttpStatusCode.OK);

        saved.At("checks.documents").GetBoolean().Should().BeFalse();
        saved.Text("modelSource").Should().Be("OwnKey");
        saved.Text("provider").Should().Be("OpenRouter");
        saved.Text("model").Should().Be("vendor/model");
        saved.At("apiKeySaved").GetBoolean().Should().BeTrue();
        (await db.AiUsageCalls.CountAsync(Ct)).Should().Be(callsBefore, "an unchanged connection is not tested again");
    }

    private const string FakeConsistencyRequestIdPrefix = ScreenHire.Infrastructure.Verification.Llm.FakeConsistencyChatClient.RequestIdPrefix;

    private static List<JsonElement> Entries(ApiResult result) => [.. result.Expect(HttpStatusCode.OK).At("entries").EnumerateArray()];

    // Applies once with the consistency check on, and waits for every analyzer, so the runs, the check and its ledger rows are saved.
    private async Task<Guid> ApplyWithCheckOnAsync(Tenant tenant)
    {
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var applicant = SyntheticApplicants.GenuineHuman;
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, applicant, answers: applicant.Answers);
        (await VerificationSupport.SettledAsync(fixture, tenant, applicationId)).Text("consistencyCheck.status").Should().Be("Completed");
        return applicationId;
    }
}
