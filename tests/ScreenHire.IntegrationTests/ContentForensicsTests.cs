using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Content forensics end to end (docs/architecture/verification-engine.md §10.10): the worker reads uploaded files from
/// blob storage, finds hidden instructions and generator metadata, compares free-text answers within the organization,
/// and runs the consistency check (the local fake model in the test stack) when the organization turns it on, within its
/// monthly token budget. Every LLM-derived signal reaches the API with both quoted spans and its provenance.
/// </summary>
public sealed class ContentForensicsTests(AppHostFixture fixture)
{
    private const string Motivation =
        "I want to join because your team ships accessible products for small charities, and I have spent four years " +
        "building donation platforms that had to work on old phones and slow connections. I enjoy measuring what users " +
        "actually do and fixing the slowest pages first.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_resume_with_hidden_instructions_is_read_and_reported_with_quotes()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);

        var verification = await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        var signals = verification.At("aiAssistance.signals").EnumerateArray().ToList();
        var injection = signals.Single(s => s.GetProperty("code").GetString() == SignalCatalogue.PromptInjection);
        injection.GetProperty("weight").GetString().Should().Be("High");
        injection.GetProperty("evidence").EnumerateArray().Should().Contain(e =>
            e.GetProperty("kind").GetString() == "Quote" && e.GetProperty("text").GetString() == SyntheticApplicants.BotHiddenInstruction);
        signals.Should().Contain(s => s.GetProperty("code").GetString() == SignalCatalogue.ResumeGenerator);

        // The organization has not turned the model on: the check is recorded as off, not failed.
        verification.Text("consistencyCheck.status").Should().Be("Disabled");

        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        var document = await db.ApplicationDocuments.SingleAsync(d => d.ApplicationId == applicationId, Ct);
        document.Status.Should().Be(DocumentExtractionStatus.Extracted);
        document.Kind.Should().Be(DocumentKind.Resume);
        document.Text.Should().Contain("Expert in every language").And.NotContain("ignore all previous instructions");
        document.Metadata.Producer.Should().Be("wkhtmltopdf 0.12.6");
    }

    [Fact]
    public async Task With_the_check_on_a_contradiction_across_resume_linkedin_export_and_answers_cites_both_spans()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await OpenJobWithQuestionsAsync(tenant);
        var applicant = SyntheticApplicants.ApplicationFarm[0];
        var linkedIn = SyntheticDocuments.Pdf("Alex Reed\nSoftware engineer. Two years of experience in web development.");

        var applicationId = await VerificationSupport.ApplyAsync(
            fixture, slug, jobId, applicant,
            answers: [.. applicant.Answers.Where(a => a.QuestionId != "notice_period")],
            files: [(ApplicationForm.LinkedInExportQuestionId, "Profile.pdf", linkedIn)]);
        var verification = await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        verification.Text("consistencyCheck.status").Should().Be("Completed");
        verification.Text("consistencyCheck.promptVersion").Should().Be(ConsistencyPrompts.Version);
        verification.Text("consistencyCheck.consistencyModel").Should().Be(FakeConsistencyChatClient.ModelId);
        var inconsistencies = verification.At("authenticity.signals").EnumerateArray()
            .Where(s => s.GetProperty("code").GetString() == SignalCatalogue.Inconsistency)
            .ToList();
        inconsistencies.Should().NotBeEmpty();
        foreach (var signal in inconsistencies)
        {
            signal.GetProperty("llm").GetProperty("promptVersion").GetString().Should().Be(ConsistencyPrompts.Version);
            var comparison = signal.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "TextComparison");
            comparison.GetProperty("left").GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();
            comparison.GetProperty("right").GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();
        }

        inconsistencies.SelectMany(s => s.GetProperty("evidence").EnumerateArray())
            .Where(e => e.GetProperty("kind").GetString() == "TextComparison")
            .Select(e => e.GetProperty("right").GetProperty("label").GetString())
            .Should().Contain("LinkedIn profile export");

        var settings = (await tenant.GetAsync("/v1/organization/verification-settings")).Expect(HttpStatusCode.OK);
        settings.Body.GetProperty("tokensUsedThisMonth").GetInt64().Should().BeGreaterThan(0);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        (await db.ApplicationDocuments.Where(d => d.ApplicationId == applicationId).Select(d => d.Kind).ToListAsync(Ct))
            .Should().BeEquivalentTo([DocumentKind.Resume, DocumentKind.LinkedInExport]);
    }

    [Fact]
    public async Task Reassessing_as_each_content_stage_lands_runs_the_verification_automation_once()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var bot = SyntheticApplicants.Bot;
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, bot, answers: bot.Answers);

        await VerificationSupport.SettledAsync(fixture, tenant, applicationId);
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        await Messaging.WolverineStore.WaitUntilAsync(
            () => db.AutomationRuns.AnyAsync(r => r.ApplicationId == applicationId && r.Trigger == TriggerType.VerificationAssessed, Ct),
            "the verification automation to run",
            Ct);

        (await db.AutomationRuns.CountAsync(r => r.ApplicationId == applicationId && r.Trigger == TriggerType.VerificationAssessed, Ct))
            .Should().Be(1, "new evidence at the same levels is stored, not announced again");
        (await db.ApplicationEvents.CountAsync(e => e.ApplicationId == applicationId && e.Type == ApplicationEventType.VerificationAssessed, Ct))
            .Should().Be(1);
    }

    [Fact]
    public async Task Once_the_monthly_budget_is_spent_the_check_is_skipped_and_says_why()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 1 }))
            .Expect(HttpStatusCode.OK);
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var first = SyntheticApplicants.GenuineHuman;
        var second = SyntheticApplicants.HonestAiAssisted;

        var firstId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, first, answers: first.Answers);
        (await VerificationSupport.SettledAsync(fixture, tenant, firstId)).Text("consistencyCheck.status").Should().Be("Completed");
        var secondId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, second, answers: second.Answers);
        var verification = await VerificationSupport.SettledAsync(fixture, tenant, secondId);

        verification.Text("consistencyCheck.status").Should().Be("OverBudget");
        verification.Text("consistencyCheck.note").Should().Be("The organization's monthly budget for the consistency check is used up.");
        verification.Text("status").Should().Be("Assessed", "a skipped check never holds up the assessment");
    }

    [Fact]
    public async Task A_near_identical_answer_is_reported_within_the_organization_and_never_across()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobWithQuestionsAsync(tenant);
        var (otherSlug, otherJob) = await OpenJobWithQuestionsAsync(other);
        var copied = Motivation.Replace("four years", "five years", StringComparison.Ordinal);

        var originalId = await VerificationSupport.ApplyAsync(
            fixture, slug, jobId, SyntheticApplicants.GenuineHuman, answers: [.. SyntheticApplicants.GenuineHuman.Answers, new("motivation", Motivation)]);
        await VerificationSupport.SettledAsync(fixture, tenant, originalId);
        var copyId = await VerificationSupport.ApplyAsync(
            fixture, slug, jobId, SyntheticApplicants.HonestAiAssisted, answers: [.. SyntheticApplicants.HonestAiAssisted.Answers, new("motivation", copied)]);
        var elsewhereId = await VerificationSupport.ApplyAsync(
            fixture, otherSlug, otherJob, SyntheticApplicants.HonestAiAssisted, answers: [.. SyntheticApplicants.HonestAiAssisted.Answers, new("motivation", copied)]);

        var copy = await VerificationSupport.SettledAsync(fixture, tenant, copyId);
        var duplicate = copy.At("aiAssistance.signals").EnumerateArray().Single(s => s.GetProperty("code").GetString() == SignalCatalogue.NearDuplicateAnswer);
        var comparison = duplicate.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "TextComparison");
        comparison.GetProperty("right").GetProperty("text").GetString().Should().Be(Motivation);
        comparison.GetProperty("right").GetProperty("label").GetString().Should().NotContain(SyntheticApplicants.GenuineHuman.FullName);

        var elsewhere = await VerificationSupport.SettledAsync(fixture, other, elsewhereId);
        elsewhere.At("aiAssistance.signals").EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().NotContain(SignalCatalogue.NearDuplicateAnswer, "answers are compared within one organization only");
    }

    [Fact]
    public async Task Verification_settings_are_off_by_default_validated_and_per_organization()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();

        var initial = (await tenant.GetAsync("/v1/organization/verification-settings")).Expect(HttpStatusCode.OK);
        initial.Body.GetProperty("llmConsistencyCheck").GetBoolean().Should().BeFalse();
        (await tenant.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 0 }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("monthlyTokenBudget");
        var saved = (await tenant.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 50000 }))
            .Expect(HttpStatusCode.OK);

        saved.Body.GetProperty("llmConsistencyCheck").GetBoolean().Should().BeTrue();
        saved.Body.GetProperty("monthlyTokenBudget").GetInt64().Should().Be(50000);
        (await other.GetAsync("/v1/organization/verification-settings")).Expect(HttpStatusCode.OK)
            .Body.GetProperty("llmConsistencyCheck").GetBoolean().Should().BeFalse("another organization's switch is its own");
    }

    [Fact]
    public async Task Row_level_security_hides_and_refuses_another_organizations_content_records_and_settings()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await OpenJobWithQuestionsAsync(tenant);
        var applicationId = await VerificationSupport.ApplyAsync(
            fixture, slug, jobId, SyntheticApplicants.GenuineHuman, answers: [.. SyntheticApplicants.GenuineHuman.Answers, new("motivation", Motivation)]);
        await VerificationSupport.SettledAsync(fixture, tenant, applicationId);

        // Raw SQL as the application role, past the EF query filter: only the policies stand in the way.
        const string fingerprints = "SELECT count(*) FROM answer_fingerprints WHERE application_id = @id";
        const string documents = "SELECT count(*) FROM application_documents WHERE application_id = @id";
        const string checks = "SELECT count(*) FROM consistency_checks WHERE application_id = @id";
        const string settings = "SELECT count(*) FROM verification_settings WHERE organization_id = @id";
        (await ScalarAsync(tenant.OrganizationId, fingerprints, applicationId)).Should().Be(1);
        (await ScalarAsync(other.OrganizationId, fingerprints, applicationId)).Should().Be(0);
        (await ScalarAsync(tenant.OrganizationId, documents, applicationId)).Should().BePositive("the resume was read");
        (await ScalarAsync(other.OrganizationId, documents, applicationId)).Should().Be(0);
        (await ScalarAsync(tenant.OrganizationId, checks, applicationId)).Should().Be(1);
        (await ScalarAsync(other.OrganizationId, checks, applicationId)).Should().Be(0);
        (await ScalarAsync(tenant.OrganizationId, settings, tenant.OrganizationId)).Should().Be(1);
        (await ScalarAsync(other.OrganizationId, settings, tenant.OrganizationId)).Should().Be(0);

        // Another organization can neither switch the check off for this one nor create a row in its name.
        (await ScalarAsync(other.OrganizationId,
            "WITH changed AS (UPDATE verification_settings SET llm_consistency_check_enabled = false WHERE organization_id = @id RETURNING 1) SELECT count(*) FROM changed",
            tenant.OrganizationId)).Should().Be(0);
        var forged = () => ScalarAsync(other.OrganizationId,
            "INSERT INTO verification_settings (organization_id, llm_consistency_check_enabled, updated_at) VALUES (@id, false, now()) RETURNING 1",
            tenant.OrganizationId);
        (await forged.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
    }

    // One scalar statement as the application role with the organization as tenant, in a transaction like the API's.
    private async Task<long> ScalarAsync(Guid organizationId, string sql, Guid id)
    {
        await using var connection = new Npgsql.NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var setTenant = new Npgsql.NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true), set_config('app.job_scope', 'all', true)", connection, transaction))
        {
            setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new Npgsql.NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        var result = Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
        await transaction.RollbackAsync(Ct);
        return result;
    }

    [Fact]
    public async Task Simultaneous_first_saves_of_the_settings_all_succeed()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();

        var saves = await Task.WhenAll(Enumerable.Range(1, 6).Select(budget =>
            tenant.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = budget * 1000 })));

        saves.Should().AllSatisfy(save => save.Expect(HttpStatusCode.OK));
        await using var db = await VerificationSupport.TenantDbAsync(fixture, tenant.OrganizationId);
        (await db.VerificationSettings.CountAsync(Ct)).Should().Be(1);
    }

    // A job whose questions are the "Software engineer" ones plus a LinkedIn export and a free-text motivation.
    private async Task<(string Slug, Guid JobId)> OpenJobWithQuestionsAsync(Tenant tenant)
    {
        var definition = await Samples.DefinitionOfAsync(tenant, SystemTemplates.SoftwareEngineer, 2);
        var questions = definition["workflow"]!["questionnaire"]!["questions"]!.AsArray();
        questions.Add(new JsonObject
        {
            ["id"] = ApplicationForm.LinkedInExportQuestionId,
            ["type"] = "File",
            ["label"] = "Your LinkedIn profile, saved as a PDF",
            ["required"] = false,
            ["acceptedFileTypes"] = new JsonArray(".pdf"),
        });
        questions.Add(new JsonObject { ["id"] = "motivation", ["type"] = "LongText", ["label"] = "Why do you want this job?", ["required"] = false });
        var (_, versionId) = await tenant.CreatePublishedTemplateAsync($"Content forensics {Guid.NewGuid():N}", definition);
        return await VerificationSupport.OpenJobAsync(fixture, tenant, versionId);
    }

}
