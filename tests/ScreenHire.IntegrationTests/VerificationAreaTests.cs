using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Billing;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// What the Verification area's Overview and Activity log read (docs/architecture/verification-engine.md 10.12): this month's
/// applications by level, what each of the six groups of checks counted, one newest-first log of runs of the checks holding their
/// check runs and model calls, and who sees what in it. Uses the synthetic applicants and the fake model, so no test needs a key.
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
        var lastChecked = overview.At("lastCheckedAt").GetDateTimeOffset();
        lastChecked.Should().BeOnOrAfter(overview.At("from").GetDateTimeOffset()).And.BeOnOrBefore(overview.At("to").GetDateTimeOffset(), "the newest check finished this month");
        var from = overview.At("from").GetDateTimeOffset();
        (from.Offset == TimeSpan.Zero && from.Day == 1 && from.TimeOfDay == TimeSpan.Zero).Should().BeTrue("the month is the UTC calendar month");
        var groups = overview.At("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("group").GetString()).Should().Equal(
            ["Network", "Contact", "Documents", "Answers", "Duplicates", "FormUse"], "every group is listed, in the groups' order, found something or not");
        groups.Should().OnlyContain(g => g.GetProperty("on").GetBoolean(), "nothing is turned off");
        groups.Single(g => g.GetProperty("group").GetString() == "FormUse").GetProperty("label").GetString().Should().Be("Form activity");
        var counts = groups.ToDictionary(g => g.GetProperty("group").GetString()!, g => g.GetProperty("applications").GetInt32());
        counts["Network"].Should().Be(1, "the bot came from a data-centre address");
        counts.Values.Should().OnlyContain(count => count == 0 || count == 1, "only the bot raised anything, and it is counted once per group");
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
        overview.At("lastCheckedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "nothing has been checked yet");
        overview.At("pending").GetInt32().Should().Be(1);
        overview.At("groups").EnumerateArray().Should().HaveCount(6).And.OnlyContain(g => g.GetProperty("applications").GetInt32() == 0);
    }

    [Fact]
    public async Task The_overview_marks_a_group_the_organization_turned_off()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var checks = new JsonObject { ["network"] = true, ["contact"] = true, ["documents"] = false, ["answers"] = true, ["duplicates"] = true, ["formUse"] = true };
        (await tenant.PutAsync(Settings, new JsonObject { ["checks"] = checks })).Expect(HttpStatusCode.OK);

        var overview = (await tenant.GetAsync($"{Area}/overview")).Expect(HttpStatusCode.OK);

        overview.At("groups").EnumerateArray().Where(g => !g.GetProperty("on").GetBoolean()).Select(g => g.GetProperty("group").GetString())
            .Should().Equal("Documents");
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
        viewerOverview.At("groups").EnumerateArray().Should().OnlyContain(g => g.GetProperty("applications").GetInt32() == 0);
        viewerActivity.At("usageVisible").GetBoolean().Should().BeFalse();
        var runs = viewerActivity.At("runs").EnumerateArray().ToList();
        runs.Should().NotBeEmpty();
        runs.Select(e => e.GetProperty("applicationId").GetGuid()).Should().OnlyContain(id => id == visible, "nothing about the other job reaches them");
        runs.Select(e => e.GetProperty("jobTitle").GetString()).Should().OnlyContain(title => title == "Platform Engineer");
        (await viewer.Session.Client.GetAsync($"{Area}/activity/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_activity_log_lists_one_row_per_run_newest_first_holding_every_check_run_and_model_call_and_filters_by_group_key_and_outcome()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var applicationId = await ApplyWithCheckOnAsync(tenant);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var analyzerRuns = await db.VerificationAnalyzerRuns.AsNoTracking().ToListAsync(Ct);
        var calls = await db.AiUsageCalls.AsNoTracking().ToListAsync(Ct);

        var all = (await tenant.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);

        all.At("usageVisible").GetBoolean().Should().BeTrue();
        all.At("nextBefore").ValueKind.Should().Be(JsonValueKind.Null);
        var runs = all.At("runs").EnumerateArray().ToList();
        // A run is one pass of the checks over the application: the analyzer runs that share its time.
        runs.Should().HaveCount(analyzerRuns.Select(r => r.CompletedAt).Distinct().Count());
        runs.Select(r => r.GetProperty("at").GetDateTimeOffset()).Should().BeInDescendingOrder("newest first");
        runs.Should().OnlyContain(r => r.GetProperty("applicationId").GetGuid() == applicationId && r.GetProperty("jobTitle").GetString() == "Platform Engineer");
        var items = runs.SelectMany(Items).ToList();
        items.Should().HaveCount(analyzerRuns.Count + calls.Count, "every check run and model call is in exactly one run");
        items.Where(i => i.GetProperty("kind").GetString() == "CheckRun").Select(i => i.GetProperty("check").GetString())
            .Should().BeEquivalentTo(analyzerRuns.Select(r => r.Analyzer));
        foreach (var run in runs)
        {
            run.GetProperty("checks").GetInt32().Should().Be(Items(run).Count(i => i.GetProperty("kind").GetString() == "CheckRun"));
        }

        // The model calls belong to the run that read their answers: the one with the consistency check.
        var withCalls = runs.Single(r => Items(r).Any(i => i.GetProperty("kind").GetString() == "ModelCall"));
        Items(withCalls).Select(i => i.GetProperty("check").GetString()).Should().Contain("consistency");
        var call = Items(withCalls).First(i => i.GetProperty("kind").GetString() == "ModelCall");
        call.GetProperty("group").GetString().Should().Be("Answers");
        call.GetProperty("outcome").GetString().Should().Be("Completed");
        call.GetProperty("call").GetProperty("providerRequestId").GetString().Should().StartWith(FakeConsistencyRequestIdPrefix);
        call.GetProperty("call").GetProperty("keySource").GetString().Should().Be("BuiltIn");
        call.GetProperty("call").GetProperty("cost").GetDecimal().Should().BeGreaterThan(0m);
        withCalls.GetProperty("rulesetVersion").GetString().Should().Be(VerificationRuleset.Version, "the run says which ruleset read it");

        Runs(await tenant.GetAsync($"{Area}/activity?group=Network&limit=200")).Should().NotBeEmpty()
            .And.OnlyContain(r => Items(r).Any(i => i.GetProperty("check").GetString() == "network"));
        Runs(await tenant.GetAsync($"{Area}/activity?group=Answers&limit=200")).SelectMany(Items).Where(i => i.GetProperty("kind").GetString() == "ModelCall").Should().HaveCount(calls.Count);
        Runs(await tenant.GetAsync($"{Area}/activity?keySource=BuiltIn&limit=200")).Should().ContainSingle()
            .Which.GetProperty("id").GetString().Should().Be(withCalls.GetProperty("id").GetString());
        Runs(await tenant.GetAsync($"{Area}/activity?keySource=OwnKey&limit=200")).Should().BeEmpty();
        Runs(await tenant.GetAsync($"{Area}/activity?outcome=Failed&limit=200")).Should().BeEmpty();
        Runs(await tenant.GetAsync($"{Area}/activity?outcome=Found&limit=200")).Should().OnlyContain(r => r.GetProperty("outcome").GetString() == "Found");
        Runs(await tenant.GetAsync($"{Area}/activity?outcome=Completed&limit=200")).Should().NotBeEmpty()
            .And.OnlyContain(r => r.GetProperty("outcome").GetString() == "Completed", "the filter's Nothing found is the run's own outcome");
        Runs(await tenant.GetAsync($"{Area}/activity?from=2099-01-01T00:00:00Z&limit=200")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_says_what_each_check_found_in_the_candidate_tabs_words_and_never_a_threshold_or_a_rule()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant);
        var bot = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        var verification = await VerificationSupport.SettledAsync(fixture, tenant, bot);

        var runs = Runs(await tenant.GetAsync($"{Area}/activity?outcome=Found&limit=200"));

        var network = runs.SelectMany(Items).First(i => i.GetProperty("check").GetString() == "network" && i.GetProperty("outcome").GetString() == "Found");
        var tabTitles = verification.At("authenticity.signals").EnumerateArray()
            .Where(signal => signal.GetProperty("group").GetString() == "Network").Select(signal => signal.GetProperty("title").GetString());
        network.GetProperty("findings").EnumerateArray().Select(f => f.GetString()).Should().NotBeEmpty().And.BeSubsetOf(tabTitles);
        var found = runs.First(r => Items(r).Any(i => i.GetProperty("id").GetGuid() == network.GetProperty("id").GetGuid()));
        found.GetProperty("outcome").GetString().Should().Be("Found");
        found.GetProperty("found").GetInt32().Should().BeGreaterThan(0);
        var body = JsonSerializer.Serialize(runs);
        body.Should().NotContainEquivalentOf("weight").And.NotContainEquivalentOf("threshold").And.NotContainEquivalentOf("appliedRules").And.NotContainEquivalentOf("score");
    }

    [Fact]
    public async Task The_activity_log_pages_by_cursor_without_gaps_or_repeats_and_refuses_a_bad_cursor_or_limit()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        await ApplyWithCheckOnAsync(tenant);
        var total = Runs(await tenant.GetAsync($"{Area}/activity?limit=200")).Count;
        total.Should().BeGreaterThan(1);

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var path = $"{Area}/activity?limit=1" + (cursor is null ? "" : $"&before={Uri.EscapeDataString(cursor)}");
            var page = (await tenant.GetAsync(path)).Expect(HttpStatusCode.OK);
            seen.AddRange(page.At("runs").EnumerateArray().Select(e => e.GetProperty("id").GetString()!));
            var next = page.At("nextBefore");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            pages++;
        }
        while (cursor is not null && pages < 50);

        seen.Should().OnlyHaveUniqueItems().And.HaveCount(total);
        pages.Should().Be(total);
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
        var full = Runs(await owner.GetAsync($"{Area}/activity?limit=200"));
        var lines = full.SelectMany(Items).Count();

        foreach (var reader in new[] { owner, admin })
        {
            var read = (await reader.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);
            read.At("usageVisible").GetBoolean().Should().BeTrue();
            read.At("runs").EnumerateArray().SelectMany(Items).Count(e => e.GetProperty("call").ValueKind == JsonValueKind.Object).Should().BeGreaterThan(0);

            using var export = await reader.Session.Client.GetAsync($"{Area}/activity/export", Ct);
            export.StatusCode.Should().Be(HttpStatusCode.OK);
            export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            export.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            var csv = (await export.Content.ReadAsStringAsync(Ct)).TrimStart((char)0xFEFF);
            var rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            // One line per check run or model call, every column, plus which run it belongs to and the ruleset that read it.
            rows[0].Should().Be("id,at,kind,group,check,outcome,application_id,job,note,provider,model,key_source,provider_request_id,input_tokens,output_tokens,cost_usd,duration_ms,error_kind,run,ruleset");
            rows.Length.Should().Be(lines + 1);
            csv.Should().Contain(FakeConsistencyRequestIdPrefix);
            var runIds = full.Select(r => r.GetProperty("id").GetString()!).ToHashSet();
            rows.Skip(1).Select(row => row.Split(',')[18]).Should().OnlyContain(run => runIds.Contains(run), "each line names a run of the log");
            csv.Should().Contain(VerificationRuleset.Version);
        }

        foreach (var reader in new[] { recruiter, viewer })
        {
            var read = (await reader.GetAsync($"{Area}/activity?limit=200")).Expect(HttpStatusCode.OK);
            read.At("usageVisible").GetBoolean().Should().BeFalse();
            read.At("runs").GetArrayLength().Should().Be(full.Count, "the same rows: every application is on a job they can open");
            read.At("runs").EnumerateArray().SelectMany(Items).Should().OnlyContain(e => e.GetProperty("call").ValueKind == JsonValueKind.Null);
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

        var notRun = Runs(await tenant.GetAsync($"{Area}/activity?group=Network&outcome=NotRun&limit=200"));
        Runs(await tenant.GetAsync($"{Area}/activity?outcome=NotRun&limit=200")).SelectMany(Items)
            .Where(i => i.GetProperty("outcome").GetString() == "NotRun").Select(e => e.GetProperty("check").GetString())
            .Should().Contain(["network", "consistency"], "the model check is off too, and a check that could not look says so");

        notRun.Should().ContainSingle();
        notRun[0].GetProperty("notRun").GetInt32().Should().BeGreaterThan(0);
        var network = Items(notRun[0]).Single(i => i.GetProperty("check").GetString() == "network");
        network.GetProperty("outcome").GetString().Should().Be("NotRun");
        network.GetProperty("group").GetString().Should().Be("Network");
        network.GetProperty("reason").GetString().Should().Be(CheckGroups.TurnedOffSentence(CheckGroup.Network), "why it did not run, as a sentence");
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
        // The amount a customer sees for ScreenHire's key is what it is billed (the provider's price plus the margin, the shared stack runs
        // the billing sandbox), never the provider's price.
        var billed = calls.Sum(c => AiCharge.Billed(c.Cost, c.KeySource, AiDefaults.Shipped.Margin)!.Value);
        billed.Should().BeGreaterThan(calls.Sum(c => c.Cost!.Value));
        days.Sum(d => d.GetProperty("builtInCost").GetDecimal()).Should().Be(billed);
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

    private static List<JsonElement> Runs(ApiResult result) => [.. result.Expect(HttpStatusCode.OK).At("runs").EnumerateArray()];

    private static List<JsonElement> Items(JsonElement run) => [.. run.GetProperty("items").EnumerateArray()];

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
