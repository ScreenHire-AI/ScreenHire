using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Privacy;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Notifications;
using Wolverine;
using Wolverine.Runtime.Recurring;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The AI usage ledger and what hangs on it (docs/architecture/ai-usage.md): one row per model call with the provider's id, the
/// tokens and the cost; who may read it; the CSV; row-level security; the cap that pauses the AI-based checks and nothing else, and
/// asks for them again when it is raised; the spike rule and the retention floor. Runs against the fake model, which gives each
/// response an id and a priced model, so no test needs a key.
/// </summary>
public sealed class AiUsageTests(AppHostFixture fixture)
{
    private const string Settings = "/v1/organization/verification-settings";

    private const string Usage = "/v1/organization/ai-usage";

    // Far in the future, so the real worker's daily sweeps never reach rows the in-process handler tests write.
    private static readonly DateTimeOffset Future = new(2099, 6, 15, 6, 7, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_call_of_a_check_is_one_row_with_the_providers_id_tokens_cost_and_key_and_the_rows_add_up_to_the_check()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var applicationId = await ApplyWithCheckOnAsync(tenant);

        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var calls = await db.AiUsageCalls.AsNoTracking().OrderBy(c => c.Id).ToListAsync(Ct);
        var check = await db.ConsistencyChecks.AsNoTracking().SingleAsync(c => c.ApplicationId == applicationId, Ct);

        calls.Should().NotBeEmpty();
        calls.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        calls.Should().AllSatisfy(call =>
        {
            call.OrganizationId.Should().Be(tenant.OrganizationId);
            call.ApplicationId.Should().Be(applicationId);
            call.KeySource.Should().Be(AiKeySource.BuiltIn);
            call.Provider.Should().Be("Fake");
            call.Model.Should().Be(FakeConsistencyChatClient.ModelId);
            call.ProviderRequestId.Should().StartWith(FakeConsistencyChatClient.RequestIdPrefix);
            call.InputTokens.Should().BeGreaterThan(0);
            call.OutputTokens.Should().BeGreaterThan(0);
            call.CostBasis.Should().Be(AiCostBasis.Priced, "the worker's Development settings price the fake model");
            call.Cost.Should().BeGreaterThan(0m);
            call.Outcome.Should().Be(AiCallOutcome.Ok);
            call.CheckName.Should().BeOneOf(AiUsageChecks.ConsistencyExtraction, AiUsageChecks.ConsistencyComparison);
        });
        calls.Select(c => c.ProviderRequestId).Should().OnlyHaveUniqueItems("the provider's id finds one call");
        calls.Sum(c => c.InputTokens!.Value).Should().Be(check.InputTokens, "a call is recorded once: the rows add up to the check's own totals");
        calls.Sum(c => c.OutputTokens!.Value).Should().Be(check.OutputTokens);
    }

    [Fact]
    public async Task The_summary_the_call_list_and_the_csv_agree_with_the_ledger_and_the_list_pages_by_cursor_and_filters()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        await ApplyWithCheckOnAsync(tenant);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var rows = await db.AiUsageCalls.AsNoTracking().OrderByDescending(c => c.Id).ToListAsync(Ct);
        rows.Count.Should().BeGreaterThanOrEqualTo(2, "a check with two sources asks once for each and once to compare");

        var summary = (await tenant.GetAsync($"{Usage}/summary")).Expect(HttpStatusCode.OK);
        summary.At("totals.calls").GetInt32().Should().Be(rows.Count);
        summary.At("totals.inputTokens").GetInt64().Should().Be(rows.Sum(r => r.InputTokens!.Value));
        summary.At("totals.cost").GetDecimal().Should().Be(rows.Sum(r => r.Cost!.Value));
        summary.At("totals.callsWithUnknownCost").GetInt32().Should().Be(0);
        summary.At("byModel").GetArrayLength().Should().Be(1);
        summary.Text("byModel.0.keySource").Should().Be("BuiltIn");
        summary.Text("byKeySource.0.keySource").Should().Be("BuiltIn");
        summary.At("byDay").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        summary.At("limits.effectiveBudget.used").GetDecimal().Should().Be(rows.Sum(r => r.InputTokens!.Value + r.OutputTokens!.Value));

        var first = (await tenant.GetAsync($"{Usage}/calls?limit=1")).Expect(HttpStatusCode.OK);
        first.Count("calls").Should().Be(1);
        first.Id("calls.0.id").Should().Be(rows[0].Id, "newest first");
        first.Text("calls.0.providerRequestId").Should().Be(rows[0].ProviderRequestId);
        var cursor = first.Id("nextBefore");
        var second = (await tenant.GetAsync($"{Usage}/calls?limit=1&before={cursor}")).Expect(HttpStatusCode.OK);
        second.Id("calls.0.id").Should().Be(rows[1].Id);
        (await tenant.GetAsync($"{Usage}/calls?limit=200")).Expect(HttpStatusCode.OK).Count("calls").Should().Be(rows.Count);
        (await tenant.GetAsync($"{Usage}/calls?limit=200")).Expect(HttpStatusCode.OK).At("nextBefore").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await tenant.GetAsync($"{Usage}/calls?keySource=OwnKey")).Expect(HttpStatusCode.OK).Count("calls").Should().Be(0);
        (await tenant.GetAsync($"{Usage}/calls?outcome=Error")).Expect(HttpStatusCode.OK).Count("calls").Should().Be(0);
        (await tenant.GetAsync($"{Usage}/calls?check={AiUsageChecks.ConsistencyComparison}")).Expect(HttpStatusCode.OK)
            .Count("calls").Should().Be(rows.Count(r => r.CheckName == AiUsageChecks.ConsistencyComparison));
        (await tenant.GetAsync($"{Usage}/calls?limit=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("limit");

        using var response = await tenant.Session.Client.GetAsync($"{Usage}/calls/export", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var csv = (await response.Content.ReadAsStringAsync(Ct)).TrimStart((char)0xFEFF);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("id,occurred_at,check,application_id,provider,model,key_source,provider_request_id,input_tokens,output_tokens,cost_usd,cost_basis,duration_ms,outcome,error_kind");
        lines.Length.Should().Be(rows.Count + 1);
        csv.Should().Contain(rows[0].ProviderRequestId!);
    }

    [Fact]
    public async Task Owners_and_Admins_read_usage_and_everyone_else_is_refused_and_only_an_Owner_sets_the_limits()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var limits = new JsonObject { ["unit"] = "Tokens", ["budget"] = 1000 };

        foreach (var reader in new[] { owner, admin })
        {
            (await reader.GetAsync($"{Usage}/summary")).Expect(HttpStatusCode.OK);
            (await reader.GetAsync($"{Usage}/calls")).Expect(HttpStatusCode.OK);
            (await reader.Session.Client.GetAsync($"{Usage}/calls/export", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        foreach (var other in new[] { recruiter, viewer })
        {
            (await other.GetAsync($"{Usage}/summary")).Expect(HttpStatusCode.Forbidden);
            (await other.GetAsync($"{Usage}/calls")).Expect(HttpStatusCode.Forbidden);
            (await other.Session.Client.GetAsync($"{Usage}/calls/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await other.PutAsync($"{Usage}/limits", limits)).Expect(HttpStatusCode.Forbidden);
        }

        (await admin.PutAsync($"{Usage}/limits", limits)).Expect(HttpStatusCode.Forbidden);
        (await owner.PutAsync($"{Usage}/limits", limits)).Expect(HttpStatusCode.OK);

        // Everyone reads the settings, but the usage, the limits and the own key's prices are only for who may see them.
        (await owner.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        (await admin.GetAsync(Settings)).Expect(HttpStatusCode.OK).At("limits").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        foreach (var other in new[] { recruiter, viewer })
        {
            var read = (await other.GetAsync(Settings)).Expect(HttpStatusCode.OK);
            read.At("limits").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "a member who cannot see AI usage sees no usage, cost or limit");
            read.At("ownKeyInputPrice").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            read.Body.GetRawText().Should().NotContain("1000");
        }

        using var signedOut = new ApiSession(fixture);
        (await signedOut.Client.GetAsync($"{Usage}/summary", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_limits_are_validated_in_their_unit_and_dollars_with_an_own_key_need_its_prices()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["budget"] = 12.5m })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("budget");
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["cap"] = 2_000_000_000L })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("cap");
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Money", ["budget"] = 10.005m })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("budget");
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Money", ["budget"] = 50, ["cap"] = 120.5m })).Expect(HttpStatusCode.OK)
            .At("effectiveCap.amount").GetDecimal().Should().Be(120.5m);

        var ownKey = new JsonObject { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey", ["provider"] = "OpenRouter", ["model"] = "vendor/model", ["apiKey"] = "own-key-usage-test" };
        (await tenant.PutAsync(Settings, ownKey)).Expect(HttpStatusCode.OK).Text("provider").Should().Be("OpenRouter");
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Money", ["cap"] = 5 })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("unit");
        ownKey["ownKeyInputPrice"] = 1m;
        ownKey["ownKeyOutputPrice"] = 2m;
        ownKey["apiKey"] = null;
        (await tenant.PutAsync(Settings, ownKey)).Expect(HttpStatusCode.OK);
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Money", ["cap"] = 5 })).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Each_provider_preset_saves_with_its_own_address_rules_and_a_fixed_address_takes_none()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var presets = (await tenant.GetAsync(Settings)).Expect(HttpStatusCode.OK).At("presets");
        presets.GetArrayLength().Should().Be(6);

        foreach (var (provider, address) in new (string, string?)[] { ("OpenAi", null), ("OpenRouter", null), ("Gemini", null), ("Anthropic", null), ("AzureOpenAi", "https://acme.openai.azure.com/"), ("OpenAiCompatible", "https://llm.example.com/v1/") })
        {
            var body = new JsonObject { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey", ["provider"] = provider, ["model"] = "m", ["apiKey"] = "own-key-preset-test", ["baseUrl"] = address };
            var saved = (await tenant.PutAsync(Settings, body)).Expect(HttpStatusCode.OK);
            saved.Text("provider").Should().Be(provider);
        }

        var fixedWithAddress = new JsonObject { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey", ["provider"] = "OpenAi", ["model"] = "m", ["apiKey"] = "k", ["baseUrl"] = "https://elsewhere.example.com/v1" };
        (await tenant.PutAsync(Settings, fixedWithAddress)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("baseUrl");
        var azureWithout = new JsonObject { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey", ["provider"] = "AzureOpenAi", ["model"] = "m", ["apiKey"] = "k" };
        (await tenant.PutAsync(Settings, azureWithout)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("baseUrl");

        var models = (await tenant.PostAsync($"{Settings}/llm/models", new JsonObject { ["provider"] = "OpenRouter", ["apiKey"] = "own-key-preset-test" })).Expect(HttpStatusCode.OK);
        models.At("listed").GetBoolean().Should().BeTrue();
        (await tenant.PostAsync($"{Settings}/llm/test", new JsonObject { ["provider"] = "Gemini", ["apiKey"] = OrganizationModels.FakeInvalidKey, ["model"] = "m" }))
            .Expect(HttpStatusCode.OK).Text("problem").Should().Be("InvalidKey");
    }

    [Fact]
    public async Task At_the_cap_the_AI_check_pauses_the_rule_based_checks_run_and_raising_the_cap_asks_for_the_check_again()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["cap"] = 1 })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        // The cap is read before a check starts, so the first application runs and uses the cap up.
        var first = SyntheticApplicants.GenuineHuman;
        (await VerificationSupport.SettledAsync(fixture, tenant, await VerificationSupport.ApplyAsync(fixture, slug, jobId, first, answers: first.Answers)))
            .Text("consistencyCheck.status").Should().Be("Completed");
        int callsBefore;
        await using (var before = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId))
        {
            callsBefore = await before.AiUsageCalls.CountAsync(Ct);
        }

        var bot = SyntheticApplicants.Bot;
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, bot, answers: bot.Answers);

        var paused = await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        paused.Text("consistencyCheck.status").Should().Be("Paused");
        paused.At("checksNotRun").EnumerateArray().Select(n => n.GetProperty("kind").GetString()).Should().Contain("Paused");
        paused.At("authenticity.signals").EnumerateArray().Should().NotBeEmpty("the rule-based checks kept running and still found the bot");
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId))
        {
            (await db.AiUsageCalls.CountAsync(Ct)).Should().Be(callsBefore, "a paused check calls no model");
        }

        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["cap"] = 100_000_000L })).Expect(HttpStatusCode.OK);

        ApiResult? resumed = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                resumed = (await tenant.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK);
                // While the check is asked again its row is Running, which the read leaves out (null).
                return resumed.Body.TryGetProperty("consistencyCheck", out var check) && check.ValueKind == System.Text.Json.JsonValueKind.Object
                    && check.GetProperty("status").GetString() == "Completed"
                    && !resumed.At("checksNotRun").EnumerateArray().Any(n => n.GetProperty("kind").GetString() == "Paused");
            },
            "the paused AI check to run once the cap was raised, and the candidate's tab to stop saying it was paused",
            Ct);
        await using var after = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        (await after.AiUsageCalls.CountAsync(Ct)).Should().BeGreaterThan(callsBefore, "the resumed check called the model");
    }

    [Fact]
    public async Task Reaching_the_cap_tells_the_Owner_once_and_raising_it_clears_the_notification()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["cap"] = 1 })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        foreach (var applicant in new[] { SyntheticApplicants.GenuineHuman, SyntheticApplicants.HonestAiAssisted })
        {
            await VerificationSupport.SettledAsync(fixture, tenant, await VerificationSupport.ApplyAsync(fixture, slug, jobId, applicant, answers: applicant.Answers));
        }

        var bell = await tenant.WaitForItemsAsync(1);
        bell.Text("items.0.kind").Should().Be("ai-usage-cap");
        bell.Text("items.0.link").Should().Be("/verification/usage");

        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["cap"] = 100_000_000L })).Expect(HttpStatusCode.OK);
        await WolverineStore.WaitUntilAsync(
            async () => (await tenant.NotificationsAsync()).Count("items") == 0, "the cap notification to be cleared when the cap was raised", Ct);
    }

    [Fact]
    public async Task Resuming_more_paused_checks_than_one_batch_holds_sends_the_next_batch_after_the_last_one_asked()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        List<Guid> applications = [];
        foreach (var applicant in new[] { SyntheticApplicants.GenuineHuman, SyntheticApplicants.HonestAiAssisted, SyntheticApplicants.Bot })
        {
            var id = await VerificationSupport.ApplyAsync(fixture, slug, jobId, applicant, answers: applicant.Answers);
            await VerificationSupport.SettledAsync(fixture, tenant, id);
            applications.Add(id);
        }

        // Three paused checks, one second apart, the oldest first.
        var startedAt = applications.Select((_, i) => Future.AddSeconds(i)).ToList();
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId))
        {
            for (var i = 0; i < applications.Count; i++)
            {
                var (application, at) = (applications[i], startedAt[i]);
                if (await db.ConsistencyChecks.AnyAsync(c => c.ApplicationId == application, Ct))
                {
                    await db.ConsistencyChecks.Where(c => c.ApplicationId == application)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ConsistencyCheckStatus.Paused).SetProperty(c => c.StartedAt, at), Ct);
                }
                else
                {
                    var check = ConsistencyCheck.Claim(tenant.OrganizationId, application, ConsistencyPrompts.Version, at);
                    check.Complete(new ConsistencyCheckOutcome(ConsistencyCheckStatus.Paused, RunNotes.AiChecksPaused), at);
                    db.ConsistencyChecks.Add(check);
                }
            }

            await db.SaveChangesAsync(Ct);
        }

        var first = await ResumeAsync(new ResumeOrganizationAiChecks(tenant.OrganizationId, "test"));
        first.OfType<CheckApplicationConsistency>().Select(m => m.ApplicationId).Should().Equal(applications[0], applications[1]);
        var next = first.OfType<ResumeOrganizationAiChecks>().Should().ContainSingle("a full batch may have left more behind").Subject;
        next.After.Should().Be(startedAt[1]);
        next.IdempotencyKey.Should().NotBe(new ResumeOrganizationAiChecks(tenant.OrganizationId, "test").IdempotencyKey, "the follow-up is its own intent");

        var second = await ResumeAsync(next);
        second.OfType<CheckApplicationConsistency>().Select(m => m.ApplicationId).Should().Equal(applications[2]);
        second.OfType<ResumeOrganizationAiChecks>().Should().BeEmpty("a batch that was not full was the last");

        async Task<List<object>> ResumeAsync(ResumeOrganizationAiChecks message)
        {
            await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
            var outgoing = await ResumeOrganizationAiChecksHandler.Handle(
                message, db, Microsoft.Extensions.Options.Options.Create(new VerificationOptions()),
                Microsoft.Extensions.Options.Options.Create(new AiUsageOptions { ResumeBatchSize = 2 }), TimeProvider.System,
                NullLogger<ResumeOrganizationAiChecks>.Instance, Ct);
            return [.. outgoing];
        }
    }

    [Fact]
    public async Task A_new_budget_lets_its_alerts_go_out_again_and_saving_the_same_budget_keeps_the_ones_already_sent()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["budget"] = 1_000 })).Expect(HttpStatusCode.OK);
        var month = AiUsageQueries.Month(TimeProvider.System.GetUtcNow());
        var sent = AiUsageAlertKeys.Budget(month, 50);
        var cap = AiUsageAlertKeys.Cap(month);
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId))
        {
            db.AiUsageAlerts.AddRange(
                new AiUsageAlert { OrganizationId = tenant.OrganizationId, Key = sent, RaisedAt = TimeProvider.System.GetUtcNow() },
                new AiUsageAlert { OrganizationId = tenant.OrganizationId, Key = cap, RaisedAt = TimeProvider.System.GetUtcNow() });
            await db.SaveChangesAsync(Ct);
        }

        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["budget"] = 1_000 })).Expect(HttpStatusCode.OK);
        (await AlertKeysAsync()).Should().BeEquivalentTo([sent, cap], "the same budget and cap are no new decision");

        (await tenant.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["budget"] = 5_000 })).Expect(HttpStatusCode.OK);
        (await AlertKeysAsync()).Should().BeEquivalentTo([cap], "a new budget is measured afresh, and the unchanged cap keeps its alert");

        async Task<List<string>> AlertKeysAsync()
        {
            await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
            return await db.AiUsageAlerts.Select(a => a.Key).ToListAsync(Ct);
        }
    }

    [Fact]
    public async Task A_day_well_above_the_recent_average_is_a_spike_and_a_quiet_history_or_a_small_day_is_not()
    {
        using var busy = await fixture.SignedInWithOrganizationAsync();
        using var quiet = await fixture.SignedInWithOrganizationAsync();
        var today = AiUsageQueries.DayStart(Future);
        await SeedAsync(busy.OrganizationId, today.AddDays(-1), 400_000);
        for (var day = 2; day <= 8; day++)
        {
            await SeedAsync(busy.OrganizationId, today.AddDays(-day), 100_000);
        }

        await SeedAsync(quiet.OrganizationId, today.AddDays(-1), 400_000);
        await SeedAsync(quiet.OrganizationId, today.AddDays(-2), 100_000);

        (await SpikeAsync(busy.OrganizationId)).Should().ContainSingle().Which.Key.Should().Be(AiUsageAlertKeys.Spike(AiUsageQueries.Day(today.AddDays(-1))));
        (await SpikeAsync(quiet.OrganizationId)).Should().BeEmpty("two days of history is too little to call a day a spike");
    }

    [Fact]
    public async Task Retention_deletes_ledger_rows_past_the_organizations_period_but_never_the_last_full_month()
    {
        using var kept = await fixture.SignedInWithOrganizationAsync();
        using var off = await fixture.SignedInWithOrganizationAsync();
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, kept.OrganizationId))
        {
            var settings = new RetentionSettings { OrganizationId = kept.OrganizationId };
            settings.Set(1, Future);
            db.RetentionSettings.Add(settings);
            await db.SaveChangesAsync(Ct);
        }

        foreach (var organizationId in new[] { kept.OrganizationId, off.OrganizationId })
        {
            await SeedAsync(organizationId, new DateTimeOffset(2099, 4, 20, 9, 0, 0, TimeSpan.Zero), 1000); // past the floor
            await SeedAsync(organizationId, new DateTimeOffset(2099, 5, 10, 9, 0, 0, TimeSpan.Zero), 1000); // older than a month, inside last month
            await SeedAsync(organizationId, new DateTimeOffset(2099, 6, 10, 9, 0, 0, TimeSpan.Zero), 1000);
        }

        foreach (var organizationId in new[] { kept.OrganizationId, off.OrganizationId })
        {
            await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            await PurgeOrganizationAiUsageHandler.Handle(new PurgeOrganizationAiUsage(organizationId, Future), db, NullLogger<PurgeOrganizationAiUsage>.Instance, Ct);
            await transaction.CommitAsync(Ct);
        }

        (await MonthsAsync(kept.OrganizationId)).Should().Equal(5, 6);
        (await MonthsAsync(off.OrganizationId)).Should().Equal(4, 5, 6);
    }

    [Fact]
    public async Task The_usage_sweeps_are_tracked_daily_schedules()
    {
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], Ct, withRecurringSchedules: true);
        var schedules = worker.Services.GetRequiredService<IRecurringScheduleControl>();

        await WolverineStore.WaitUntilAsync(
            async () => (await schedules.QueryAsync(Ct)).Count(s => s.Name is nameof(CheckAiUsageSpikes) or nameof(PurgeExpiredAiUsage) or nameof(ResumePausedAiChecks)) == 3,
            "the three AI usage schedules to be tracked", Ct);
        var all = await schedules.QueryAsync(Ct);
        all.Single(s => s.Name == nameof(CheckAiUsageSpikes)).CronExpression.Should().Be("7 6 * * *");
        all.Single(s => s.Name == nameof(PurgeExpiredAiUsage)).CronExpression.Should().Be("42 4 * * *");
        all.Single(s => s.Name == nameof(ResumePausedAiChecks)).CronExpression.Should().Be("12 5 * * *");
    }

    [Fact]
    public async Task Another_organization_reads_none_of_this_ones_usage_over_the_API_whatever_ids_it_passes()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var applicationId = await ApplyWithCheckOnAsync(ours);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, ours.OrganizationId);
        var rows = await db.AiUsageCalls.AsNoTracking().OrderByDescending(c => c.Id).ToListAsync(Ct);
        rows.Should().NotBeEmpty();

        (await theirs.GetAsync($"{Usage}/summary")).Expect(HttpStatusCode.OK).At("totals.calls").GetInt32().Should().Be(0);
        (await theirs.GetAsync($"{Usage}/calls")).Expect(HttpStatusCode.OK).Count("calls").Should().Be(0);
        (await theirs.GetAsync($"{Usage}/calls?applicationId={applicationId}")).Expect(HttpStatusCode.OK).Count("calls").Should().Be(0, "their view of our application id is empty, not forbidden");
        (await theirs.GetAsync($"{Usage}/calls?before={rows[0].Id}&limit=200")).Expect(HttpStatusCode.OK).Count("calls").Should().Be(0, "a cursor taken from our list opens nothing of ours");
        using var export = await theirs.Session.Client.GetAsync($"{Usage}/calls/export?applicationId={applicationId}", Ct);
        var csv = (await export.Content.ReadAsStringAsync(Ct)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        csv.Should().ContainSingle("only the header row").Which.Should().StartWith("id,occurred_at");
        (await theirs.PutAsync($"{Usage}/limits", new JsonObject { ["unit"] = "Tokens", ["cap"] = 1 })).Expect(HttpStatusCode.OK);
        (await ours.GetAsync(Settings)).Expect(HttpStatusCode.OK).At("limits.cap").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "their cap is theirs");
    }

    [Fact]
    public async Task Usage_stays_with_its_organization_even_with_the_query_filters_off_and_the_ledger_cannot_be_edited()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(ours.OrganizationId, Future, 500);

        await using (var own = await VerificationSupport.TenantDbAsync(fixture, ours.OrganizationId))
        {
            (await own.AiUsageCalls.CountAsync(Ct)).Should().Be(1);
        }

        await using (var outside = await VerificationSupport.TenantDbAsync(fixture, theirs.OrganizationId))
        {
            (await outside.AiUsageCalls.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0, "row-level security hides the row even without the query filter");
            outside.AiUsageCalls.Add(Row(ours.OrganizationId, Future, 1));
            await FluentActions.Awaiting(() => outside.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>("the policy refuses a row for another organization");
            outside.ChangeTracker.Clear();
            outside.AiUsageAlerts.Add(new AiUsageAlert { OrganizationId = ours.OrganizationId, Key = "2099-06:cap", RaisedAt = Future });
            await FluentActions.Awaiting(() => outside.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>();
        }

        await using var none = await fixture.CreateDbContextAsync(new TenantContext(), Ct);
        (await none.AiUsageCalls.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0, "with no tenant set no row is visible");

        // The application role has no UPDATE on the ledger.
        await using var edit = await VerificationSupport.TenantDbAsync(fixture, ours.OrganizationId);
        var update = async () => await edit.Database.ExecuteSqlRawAsync("UPDATE ai_usage_calls SET input_tokens = 1", Ct);
        await update.Should().ThrowAsync<Exception>("a recorded call is never edited");
        await using var editAlerts = await VerificationSupport.TenantDbAsync(fixture, ours.OrganizationId);
        var updateAlerts = async () => await editAlerts.Database.ExecuteSqlRawAsync("UPDATE ai_usage_alerts SET key = 'x'", Ct);
        await updateAlerts.Should().ThrowAsync<Exception>("a claim is made once and kept");
    }

    // Applies once with the consistency check on, and waits for every analyzer, so the check and its ledger rows are saved.
    private async Task<Guid> ApplyWithCheckOnAsync(Tenant tenant)
    {
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var applicant = SyntheticApplicants.GenuineHuman;
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, applicant, answers: applicant.Answers);
        (await VerificationSupport.SettledAsync(fixture, tenant, applicationId)).Text("consistencyCheck.status").Should().Be("Completed");
        return applicationId;
    }

    private async Task<IReadOnlyList<NotifyAiUsage>> SpikeAsync(Guid organizationId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var outgoing = await CheckOrganizationAiUsageSpikeHandler.Handle(
            new CheckOrganizationAiUsageSpike(organizationId, AiUsageQueries.DayStart(Future)),
            db, Microsoft.Extensions.Options.Options.Create(new AiUsageOptions()), NullLogger<CheckOrganizationAiUsageSpike>.Instance, Ct);
        return [.. outgoing.OfType<NotifyAiUsage>()];
    }

    private async Task<IReadOnlyList<int>> MonthsAsync(Guid organizationId)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        return [.. (await db.AiUsageCalls.AsNoTracking().Select(c => c.OccurredAt).ToListAsync(Ct)).Select(at => at.Month).Order()];
    }

    private async Task SeedAsync(Guid organizationId, DateTimeOffset at, long tokens)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        db.AiUsageCalls.Add(Row(organizationId, at, tokens));
        await db.SaveChangesAsync(Ct);
    }

    private static AiUsageCall Row(Guid organizationId, DateTimeOffset at, long tokens) => new()
    {
        OrganizationId = organizationId,
        OccurredAt = at,
        CheckName = AiUsageChecks.ConsistencyComparison,
        Provider = "Fake",
        Model = FakeConsistencyChatClient.ModelId,
        KeySource = AiKeySource.BuiltIn,
        ProviderRequestId = $"seed-{Guid.NewGuid():N}",
        InputTokens = tokens,
        OutputTokens = 0,
        CostBasis = AiCostBasis.Unknown,
        Outcome = AiCallOutcome.Ok,
    };
}
