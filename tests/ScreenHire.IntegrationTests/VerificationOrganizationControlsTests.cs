using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// What an organization controls on Settings, Verification (verification-engine.md §10): its own AI key and model (the fake
/// model in the test stack, so no test needs a real key), which groups of checks run, the default AI-use policy for new jobs,
/// the device check, trusted domains and networks, and the two alerts. The key is write-only everywhere it could leak.
/// </summary>
public sealed class VerificationOrganizationControlsTests(AppHostFixture fixture)
{
    private const string Settings = "/v1/organization/verification-settings";

    private const string Secret = "own-key-integration-test-0001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_organization_has_every_group_on_ScreenHires_model_and_no_alerts()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        var body = (await tenant.GetAsync(Settings)).Expect(HttpStatusCode.OK).Body;

        body.GetProperty("modelSource").GetString().Should().Be("Included");
        body.GetProperty("apiKeySaved").GetBoolean().Should().BeFalse();
        body.GetProperty("budgetApplies").GetBoolean().Should().BeTrue();
        body.GetProperty("effectiveMonthlyTokenBudget").GetInt64().Should().BeGreaterThan(0);
        foreach (var group in new[] { "network", "contact", "documents", "answers", "duplicates", "formUse" })
        {
            body.GetProperty("checks").GetProperty(group).GetBoolean().Should().BeTrue(group);
        }

        body.GetProperty("defaultAiUse").GetString().Should().Be("AllowedWithDisclosure");
        body.GetProperty("deviceCheck").ValueKind.Should().Be(JsonValueKind.Null, "no choice follows the region");
        body.GetProperty("trustedDomains").GetArrayLength().Should().Be(0);
        body.GetProperty("trustedNetworks").GetArrayLength().Should().Be(0);
        foreach (var alert in new[] { "suggestedEmail", "suggestedSlack", "budgetEmail", "budgetSlack" })
        {
            body.GetProperty("alerts").GetProperty(alert).GetBoolean().Should().BeFalse(alert);
        }
    }

    [Fact]
    public async Task The_whole_page_saves_comes_back_and_is_normalized_and_recorded_in_history()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        var saved = (await tenant.PutAsync(Settings, new JsonObject
        {
            ["llmConsistencyCheck"] = true,
            ["checks"] = new JsonObject { ["documents"] = false, ["formUse"] = false },
            ["defaultAiUse"] = "NotAllowed",
            ["deviceCheck"] = "Consent",
            ["trustedDomains"] = new JsonArray("Partner.Example.COM", "partner.example.com", "@staffing.example.net"),
            ["trustedNetworks"] = new JsonArray("203.0.113.0/24", "198.51.100.7"),
            ["alerts"] = new JsonObject { ["suggestedEmail"] = true, ["budgetSlack"] = true },
        })).Expect(HttpStatusCode.OK).Body;

        saved.GetProperty("checks").GetProperty("documents").GetBoolean().Should().BeFalse();
        saved.GetProperty("checks").GetProperty("formUse").GetBoolean().Should().BeFalse();
        saved.GetProperty("checks").GetProperty("network").GetBoolean().Should().BeTrue("a group the request leaves out stays on");
        saved.GetProperty("defaultAiUse").GetString().Should().Be("NotAllowed");
        saved.GetProperty("deviceCheck").GetString().Should().Be("Consent");
        saved.GetProperty("trustedDomains").EnumerateArray().Select(e => e.GetString()).Should().Equal("partner.example.com", "staffing.example.net");
        saved.GetProperty("trustedNetworks").EnumerateArray().Select(e => e.GetString()).Should().Equal("198.51.100.7/32", "203.0.113.0/24");
        saved.GetProperty("alerts").GetProperty("suggestedEmail").GetBoolean().Should().BeTrue();

        var again = (await tenant.GetAsync(Settings)).Expect(HttpStatusCode.OK).Body;
        again.GetProperty("trustedNetworks").GetRawText().Should().Be(saved.GetProperty("trustedNetworks").GetRawText());

        var history = (await tenant.GetAsync($"{Settings}/history")).Expect(HttpStatusCode.OK).Body.GetRawText();
        history.Should().Contain("documents checks").And.Contain("form use checks").And.Contain("partner.example.com").And.Contain("203.0.113.0/24");
        history.Should().Contain("Verification suggested alert by email");
    }

    [Fact]
    public async Task A_first_save_keeps_every_value_that_differs_from_the_column_defaults()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        (await tenant.PutAsync(Settings, new JsonObject
        {
            ["llmConsistencyCheck"] = false,
            ["defaultAiUse"] = "Allowed",
            ["checks"] = new JsonObject { ["network"] = false, ["contact"] = false, ["documents"] = false, ["answers"] = false, ["duplicates"] = false, ["formUse"] = false },
        })).Expect(HttpStatusCode.OK);

        var body = (await tenant.GetAsync(Settings)).Expect(HttpStatusCode.OK).Body;
        body.GetProperty("defaultAiUse").GetString().Should().Be("Allowed");
        foreach (var group in new[] { "network", "contact", "documents", "answers", "duplicates", "formUse" })
        {
            body.GetProperty("checks").GetProperty(group).GetBoolean().Should().BeFalse(group);
        }
    }

    [Fact]
    public async Task A_member_who_cannot_change_settings_does_not_read_the_saved_model_address()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var put = OwnKey(Secret);
        put["provider"] = "OpenAiCompatible";
        put["baseUrl"] = "https://example-resource.example.com/path-token-0001/v1";
        (await tenant.PutAsync(Settings, put)).Expect(HttpStatusCode.OK).Body.GetProperty("baseUrl").GetString().Should().Contain("path-token-0001");

        using var viewer = await ViewerOfAsync(tenant.OrganizationId);
        var read = (await viewer.GetAsync(Settings)).Expect(HttpStatusCode.OK);

        read.Body.GetRawText().Should().NotContain("path-token-0001");
        read.Body.GetProperty("baseUrl").ValueKind.Should().Be(JsonValueKind.Null);
        read.Body.GetProperty("modelSource").GetString().Should().Be("OwnKey", "the rest of the page still reads");
        (await tenant.GetAsync(Settings)).Body.GetProperty("baseUrl").GetString().Should().Contain("path-token-0001", "the people who edit it see it");
    }

    [Fact]
    public async Task Another_organization_sees_none_of_the_key_the_trusted_entries_or_the_alerts()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        var put = OwnKey(Secret);
        put["trustedDomains"] = new JsonArray("partner.example.com");
        put["trustedNetworks"] = new JsonArray("203.0.113.0/24");
        put["alerts"] = new JsonObject { ["suggestedEmail"] = true };
        put["checks"] = new JsonObject { ["network"] = false };
        (await first.PutAsync(Settings, put)).Expect(HttpStatusCode.OK);

        var other = (await second.GetAsync(Settings)).Expect(HttpStatusCode.OK).Body;

        other.GetProperty("modelSource").GetString().Should().Be("Included");
        other.GetProperty("apiKeySaved").GetBoolean().Should().BeFalse();
        other.GetProperty("trustedDomains").GetArrayLength().Should().Be(0);
        other.GetProperty("trustedNetworks").GetArrayLength().Should().Be(0);
        other.GetProperty("alerts").GetProperty("suggestedEmail").GetBoolean().Should().BeFalse();
        other.GetProperty("checks").GetProperty("network").GetBoolean().Should().BeTrue();
        (await second.GetAsync($"{Settings}/history")).Expect(HttpStatusCode.OK).Body.GetRawText().Should().NotContain("API key");
    }

    [Fact]
    public async Task Entries_that_are_not_domains_or_networks_or_are_too_wide_and_too_many_are_refused_by_field()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["trustedDomains"] = new JsonArray("localhost") }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("trustedDomains");
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["trustedNetworks"] = new JsonArray("10.0.0.0/8") }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("trustedNetworks");
        var tooMany = new JsonArray([.. Enumerable.Range(0, 51).Select(i => (JsonNode)$"d{i}.example.com")]);
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["trustedDomains"] = tooMany }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("trustedDomains");
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["defaultAiUse"] = "Sometimes" }))
            .Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_own_key_is_tested_before_it_is_saved_and_the_key_is_never_returned_or_recorded()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        var refused = (await tenant.PutAsync(Settings, OwnKey(OrganizationModels.FakeInvalidKey))).Expect(HttpStatusCode.BadRequest);
        refused.ProblemKeys().Should().Equal("apiKey");
        (await tenant.GetAsync(Settings)).Body.GetProperty("modelSource").GetString().Should().Be("Included", "a key that fails its test is not saved");

        var saved = (await tenant.PutAsync(Settings, OwnKey(Secret))).Expect(HttpStatusCode.OK);
        saved.Body.GetProperty("modelSource").GetString().Should().Be("OwnKey");
        saved.Body.GetProperty("apiKeySaved").GetBoolean().Should().BeTrue();
        saved.Body.GetProperty("budgetApplies").GetBoolean().Should().BeFalse("the provider bills an own key");
        saved.Body.GetProperty("model").GetString().Should().Be("my-model");

        var read = (await tenant.GetAsync(Settings)).Expect(HttpStatusCode.OK);
        var history = (await tenant.GetAsync($"{Settings}/history")).Expect(HttpStatusCode.OK);
        foreach (var text in new[] { saved.Body.GetRawText(), read.Body.GetRawText(), history.Body.GetRawText() })
        {
            text.Should().NotContain(Secret).And.NotContain("own-key-integration");
        }

        history.Body.GetRawText().Should().Contain("your own AI key").And.Contain("saved an API key").And.Contain("my-model");

        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var row = await db.VerificationSettings.AsNoTracking().SingleAsync(Ct);
        row.ProtectedLlmApiKey.Should().NotBeNullOrEmpty().And.NotContain(Secret, "the key is encrypted at rest");
        row.LlmKeyVersion.Should().Be(1);
    }

    [Fact]
    public async Task Keeping_the_saved_key_needs_no_key_but_a_new_provider_or_address_does_and_the_key_can_be_replaced_or_removed()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, OwnKey(Secret))).Expect(HttpStatusCode.OK);

        // Same target, no key: the saved one is kept.
        var kept = OwnKey(null);
        kept["model"] = "another-model";
        var keptBody = (await tenant.PutAsync(Settings, kept)).Expect(HttpStatusCode.OK).Body;
        keptBody.GetProperty("apiKeySaved").GetBoolean().Should().BeTrue();
        keptBody.GetProperty("model").GetString().Should().Be("another-model");

        // A different provider: the old key is never sent to it.
        var other = OwnKey(null);
        other["provider"] = "OpenAiCompatible";
        other["baseUrl"] = "https://example-resource.example.com/openai/v1";
        (await tenant.PutAsync(Settings, other)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("apiKey");
        other["apiKey"] = "second-key";
        var moved = (await tenant.PutAsync(Settings, other)).Expect(HttpStatusCode.OK).Body;
        moved.GetProperty("baseUrl").GetString().Should().Be("https://example-resource.example.com/openai/v1/", "the address ends with a slash, because the client adds its paths to it");

        // Missing parts of an own-key setup are refused by field.
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("provider").And.Contain("model");
        (await tenant.PutAsync(Settings, new JsonObject
            { ["llmConsistencyCheck"] = true, ["modelSource"] = "OwnKey", ["provider"] = "OpenAiCompatible", ["baseUrl"] = "http://insecure.example.com", ["model"] = "m", ["apiKey"] = "k" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("baseUrl");

        // Going back to ScreenHire's model erases the key.
        var back = (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK).Body;
        back.GetProperty("modelSource").GetString().Should().Be("Included");
        back.GetProperty("apiKeySaved").GetBoolean().Should().BeFalse();
        back.GetProperty("model").ValueKind.Should().Be(JsonValueKind.Null);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        (await db.VerificationSettings.AsNoTracking().SingleAsync(Ct)).ProtectedLlmApiKey.Should().BeNull();
        (await tenant.GetAsync($"{Settings}/history")).Body.GetRawText().Should().Contain("removed the API key").And.Contain("replaced the API key");
    }

    [Fact]
    public async Task The_models_and_test_routes_use_the_saved_key_when_none_is_sent_and_answer_in_words()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var request = new JsonObject { ["provider"] = "Anthropic", ["model"] = "my-model" };

        (await tenant.PostAsync($"{Settings}/llm/test", request)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("apiKey");
        (await tenant.PostAsync($"{Settings}/llm/models", new JsonObject { ["provider"] = "OpenAiCompatible", ["apiKey"] = "k" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("baseUrl");

        request["apiKey"] = Secret;
        var list = (await tenant.PostAsync($"{Settings}/llm/models", request)).Expect(HttpStatusCode.OK).Body;
        list.GetProperty("listed").GetBoolean().Should().BeTrue();
        list.GetProperty("models").EnumerateArray().Select(e => e.GetString()).Should().Equal(FakeConsistencyChatClient.ModelId);
        (await tenant.PostAsync($"{Settings}/llm/test", request)).Expect(HttpStatusCode.OK).Body.GetProperty("ok").GetBoolean().Should().BeTrue();

        request["apiKey"] = OrganizationModels.FakeInvalidKey;
        var failed = (await tenant.PostAsync($"{Settings}/llm/test", request)).Expect(HttpStatusCode.OK).Body;
        failed.GetProperty("ok").GetBoolean().Should().BeFalse();
        failed.GetProperty("problem").GetString().Should().Be("InvalidKey");

        (await tenant.PutAsync(Settings, OwnKey(Secret))).Expect(HttpStatusCode.OK);
        var withSaved = new JsonObject { ["provider"] = "Anthropic", ["model"] = "my-model" };
        (await tenant.PostAsync($"{Settings}/llm/test", withSaved)).Expect(HttpStatusCode.OK).Body.GetProperty("ok").GetBoolean().Should().BeTrue("the page never needs the key back");
    }

    [Fact]
    public async Task With_an_own_key_the_token_budget_does_not_apply_and_usage_still_counts()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var put = OwnKey(Secret);
        put["monthlyTokenBudget"] = 1;
        (await tenant.PutAsync(Settings, put)).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var first = SyntheticApplicants.GenuineHuman;
        var second = SyntheticApplicants.HonestAiAssisted;

        var firstId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, first, answers: first.Answers);
        (await VerificationSupport.SettledAsync(fixture, tenant, firstId)).Text("consistencyCheck.status").Should().Be("Completed");
        var secondId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, second, answers: second.Answers);
        var verification = await VerificationSupport.SettledAsync(fixture, tenant, secondId);

        verification.Text("consistencyCheck.status").Should().Be("Completed", "a budget of one token would have stopped ScreenHire's model after the first check");
        var settings = (await tenant.GetAsync(Settings)).Body;
        settings.GetProperty("tokensUsedThisMonth").GetInt64().Should().BeGreaterThan(1);
        settings.GetProperty("budgetApplies").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_group_that_is_off_is_not_run_and_each_result_it_would_have_fed_says_so()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject
        {
            ["llmConsistencyCheck"] = true,
            ["checks"] = new JsonObject { ["network"] = false, ["answers"] = false },
        })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var bot = SyntheticApplicants.Bot;

        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, bot, answers: bot.Answers);
        var verification = await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        var codes = verification.At("authenticity.signals").EnumerateArray().Select(s => s.GetProperty("code").GetString()).ToList();
        codes.Should().NotContain(SignalCatalogue.DatacenterIp, "the network group is off");
        codes.Should().Contain(SignalCatalogue.Honeypot, "the groups that are on still run");
        var notRun = verification.At("checksNotRun").EnumerateArray().ToList();
        notRun.Select(n => n.GetProperty("label").GetString()).Should().Equal("Network", "Answers");
        notRun.Should().OnlyContain(n => n.GetProperty("kind").GetString() == "TurnedOff");
        notRun[0].GetProperty("note").GetString().Should().Be("Network checks are turned off in this organization's settings.");
        verification.Text("consistencyCheck.status").Should().Be("Disabled", "no source is sent to a model with the answers checks off");

        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var run = await db.VerificationAnalyzerRuns.AsNoTracking().SingleAsync(r => r.ApplicationId == applicationId && r.Analyzer == "network", Ct);
        run.SignalCount.Should().Be(0);
        run.Note.Should().Be("Network checks are turned off in this organization's settings.");
    }

    [Fact]
    public async Task With_form_use_checks_off_the_duplicates_result_says_the_device_was_not_compared()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["checks"] = new JsonObject { ["formUse"] = false } })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);

        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot, answers: SyntheticApplicants.Bot.Answers);
        var verification = await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        var duplicates = verification.At("checksNotRun").EnumerateArray().Single(n => n.GetProperty("label").GetString() == "Duplicates");
        duplicates.GetProperty("kind").GetString().Should().Be("TurnedOff");
        duplicates.GetProperty("note").GetString().Should().Be(RunNotes.DeviceNotCompared);
    }

    [Fact]
    public async Task Nothing_in_the_result_is_a_score_and_turning_groups_off_adds_no_way_to_reject()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["checks"] = new JsonObject { ["network"] = false } })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot, answers: SyntheticApplicants.Bot.Answers);

        var verification = await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        var text = verification.Body.GetRawText();
        text.Should().NotContainEquivalentOf("score").And.NotContainEquivalentOf("percent");
        verification.Text("authenticity.level").Should().Be("VerificationRecommended", "other groups still corroborate, and the level is a category");
    }

    [Fact]
    public async Task A_form_use_group_that_is_off_issues_no_telemetry_session_and_the_device_check_follows_the_more_protective_basis()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var job = await tenant.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await tenant.PublishJobAsync(job.Id());
        var jobSlug = job.Text("slug");
        string organizationSlug;
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId))
        {
            organizationSlug = await db.Organizations.Where(o => o.Id == tenant.OrganizationId).Select(o => o.Slug).SingleAsync(Ct);
        }

        using var client = fixture.CreateHttpClient("api");
        var form = $"/v1/public/organizations/{organizationSlug}/jobs/{jobSlug}/application-form";
        var open = await client.GetFromJsonAsync<JsonElement>(form, Ct);
        open.GetProperty("telemetry").GetProperty("sessionToken").GetString().Should().NotBeNullOrEmpty();
        open.GetProperty("telemetry").GetProperty("deviceCheck").GetString().Should().Be("Notice");

        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["deviceCheck"] = "Consent", ["checks"] = new JsonObject { ["formUse"] = false } }))
            .Expect(HttpStatusCode.OK);
        var closed = await client.GetFromJsonAsync<JsonElement>(form, Ct);
        closed.GetProperty("telemetry").GetProperty("sessionToken").GetString().Should().BeEmpty("with form use checks off, the careers page collects no form telemetry");
        closed.GetProperty("telemetry").GetProperty("deviceCheck").GetString().Should().Be("Off", "the page module that makes the device code does not run, so no consent box is offered");

        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["deviceCheck"] = "Consent" })).Expect(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<JsonElement>(form, Ct)).GetProperty("telemetry").GetProperty("deviceCheck").GetString()
            .Should().Be("Consent", "the organization asked for more protection than a Notice region");

        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["deviceCheck"] = "Notice", ["checks"] = new JsonObject { ["duplicates"] = false } }))
            .Expect(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<JsonElement>(form, Ct)).GetProperty("telemetry").GetProperty("deviceCheck").GetString()
            .Should().Be("Off", "the fingerprint feeds only the duplicates check");
    }

    [Fact]
    public async Task A_new_job_starts_with_the_organizations_default_AI_use_unless_it_starts_from_a_template()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["defaultAiUse"] = "NotAllowed" })).Expect(HttpStatusCode.OK);

        var plain = await tenant.CreateJobAsync(Samples.RemoteJob("Plain job"));
        var (_, versionId) = await tenant.CreatePublishedTemplateAsync("With its own policy");
        var body = Samples.RemoteJob("From a template");
        body["templateVersionId"] = versionId;
        var fromTemplate = await tenant.CreateJobAsync(body);

        plain.At("workflow.verificationPolicy.aiUse").GetString().Should().Be("NotAllowed");
        plain.At("workflowVersion").GetInt32().Should().Be(1, "the default is the job's first workflow, not an edit of one");
        fromTemplate.At("workflow.verificationPolicy.aiUse").GetString().Should().NotBe("NotAllowed", "a template carries its own policy");
    }

    [Fact]
    public async Task Verification_suggested_emails_the_reviewers_who_see_the_job_once_and_nobody_when_the_alert_is_off()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var quiet = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync(Settings, new JsonObject { ["llmConsistencyCheck"] = false, ["alerts"] = new JsonObject { ["suggestedEmail"] = true } })).Expect(HttpStatusCode.OK);
        var ownerEmail = await EmailOfAsync(tenant);
        var quietEmail = await EmailOfAsync(quiet);

        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot, answers: SyntheticApplicants.Bot.Answers);
        (await VerificationSupport.SettledAsync(fixture, tenant, applicationId)).Text("authenticity.level").Should().Be("VerificationRecommended");
        var (quietSlug, quietJob) = await VerificationSupport.OpenJobAsync(fixture, quiet, SystemTemplates.SoftwareEngineerVersion2);
        var quietApplication = await VerificationSupport.ApplyAsync(fixture, quietSlug, quietJob, SyntheticApplicants.Bot, answers: SyntheticApplicants.Bot.Answers);
        await VerificationSupport.SettledAsync(fixture, quiet, quietApplication);

        var mails = await WaitForAlertsAsync(ownerEmail);

        mails.Should().ContainSingle("the application first reached the level once, and later stages of evidence do not announce it again");
        mails[0].Text.Should().Contain($"/applications/{applicationId}").And.Contain("needs a closer look").And.NotContain("Verification suggested");
        mails[0].Text.Should().NotContain(SyntheticApplicants.Bot.FullName, "the email names the job and links, never the candidate");
        (await fixture.Mailpit.MessagesAsync(quietEmail, Ct)).Where(m => m.Subject == VerificationAlertWording.SuggestedSubject)
            .Should().BeEmpty("an organization that did not turn the alert on is not told");
    }

    [Fact]
    public async Task The_budget_alert_goes_once_when_the_month_passes_80_percent_and_never_with_an_own_key()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var ownKey = await fixture.SignedInWithOrganizationAsync();
        var put = new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 1, ["alerts"] = new JsonObject { ["budgetEmail"] = true } };
        (await tenant.PutAsync(Settings, put)).Expect(HttpStatusCode.OK);
        var own = OwnKey(Secret);
        own["monthlyTokenBudget"] = 1;
        own["alerts"] = new JsonObject { ["budgetEmail"] = true };
        (await ownKey.PutAsync(Settings, own)).Expect(HttpStatusCode.OK);
        var ownerEmail = await EmailOfAsync(tenant);
        var ownKeyEmail = await EmailOfAsync(ownKey);

        foreach (var (who, email) in new[] { (tenant, ownerEmail), (ownKey, ownKeyEmail) })
        {
            var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, who, SystemTemplates.SoftwareEngineerVersion2);
            var first = SyntheticApplicants.GenuineHuman;
            var second = SyntheticApplicants.HonestAiAssisted;
            await VerificationSupport.SettledAsync(fixture, who, await VerificationSupport.ApplyAsync(fixture, slug, jobId, first, answers: first.Answers));
            await VerificationSupport.SettledAsync(fixture, who, await VerificationSupport.ApplyAsync(fixture, slug, jobId, second, answers: second.Answers));
        }

        IReadOnlyList<MailpitMessage> alerts = [];
        await Messaging.WolverineStore.WaitUntilAsync(
            async () =>
            {
                alerts = [.. (await fixture.Mailpit.MessagesAsync(ownerEmail, Ct)).Where(m => m.Subject == VerificationAlertWording.BudgetSubject)];
                return alerts.Count > 0;
            },
            "the budget alert email",
            Ct);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        (await fixture.Mailpit.MessagesAsync(ownerEmail, Ct)).Count(m => m.Subject == VerificationAlertWording.BudgetSubject)
            .Should().Be(1, "the month passed 80% once, and the second application found the budget already spent");
        (await fixture.Mailpit.MessagesAsync(ownKeyEmail, Ct)).Where(m => m.Subject == VerificationAlertWording.BudgetSubject)
            .Should().BeEmpty("an own key has no ScreenHire budget to pass");
    }

    private async Task<IReadOnlyList<MailpitMessage>> WaitForAlertsAsync(string address)
    {
        IReadOnlyList<MailpitMessage> mails = [];
        await Messaging.WolverineStore.WaitUntilAsync(
            async () =>
            {
                mails = [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject == VerificationAlertWording.SuggestedSubject)];
                return mails.Count > 0;
            },
            "the Verification suggested email",
            Ct);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct); // a second copy, if there were one, would have arrived by now
        return [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject == VerificationAlertWording.SuggestedSubject)];
    }

    // A second person, signed in as a Viewer of the organization.
    private async Task<Tenant> ViewerOfAsync(Guid organizationId)
    {
        var session = new ApiSession(fixture);
        try
        {
            await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("viewer"), Ct);
            var userId = (await session.CurrentUserAsync(Ct))!.Value.GetProperty("id").GetGuid();
            await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
            db.OrganizationMembers.Add(OrganizationMember.Create(organizationId, userId, OrganizationRole.Viewer, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
            return new Tenant(session, organizationId, userId);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task<string> EmailOfAsync(Tenant tenant)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        return await db.Users.AsNoTracking().Where(u => u.Id == tenant.UserId).Select(u => u.Email).SingleAsync(Ct) ?? throw new InvalidOperationException("The signed-in user has no email address.");
    }

    // An own-key request on Anthropic with the fake model; the key is null to keep the saved one.
    private static JsonObject OwnKey(string? apiKey) => new()
    {
        ["llmConsistencyCheck"] = true,
        ["modelSource"] = "OwnKey",
        ["provider"] = "Anthropic",
        ["model"] = "my-model",
        ["apiKey"] = apiKey,
    };
}
