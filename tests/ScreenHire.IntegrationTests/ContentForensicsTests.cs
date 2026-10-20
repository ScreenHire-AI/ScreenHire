using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity.Llm;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Content forensics end to end (docs/architecture/integrity-engine.md §10.10): the worker reads uploaded files from
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
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, tenant);
        var applicationId = await IntegritySupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);

        var integrity = await IntegritySupport.SettledAsync(fixture, tenant, applicationId);

        var signals = integrity.At("aiAssistance.signals").EnumerateArray().ToList();
        var injection = signals.Single(s => s.GetProperty("code").GetString() == SignalCatalogue.PromptInjection);
        injection.GetProperty("weight").GetString().Should().Be("High");
        injection.GetProperty("evidence").EnumerateArray().Should().Contain(e =>
            e.GetProperty("kind").GetString() == "Quote" && e.GetProperty("text").GetString() == SyntheticApplicants.BotHiddenInstruction);
        signals.Should().Contain(s => s.GetProperty("code").GetString() == SignalCatalogue.ResumeGenerator);

        // The organization has not turned the model on: the check is recorded as off, not failed.
        integrity.Text("consistencyCheck.status").Should().Be("Disabled");

        await using var db = await IntegritySupport.TenantDbAsync(fixture, tenant.OrganizationId);
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
        (await tenant.PutAsync("/v1/organization/integrity-settings", new JsonObject { ["llmConsistencyCheck"] = true })).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await OpenJobWithQuestionsAsync(tenant);
        var applicant = SyntheticApplicants.ApplicationFarm[0];
        var linkedIn = SyntheticDocuments.Pdf("Alex Reed\nSoftware engineer. Two years of experience in web development.");

        var applicationId = await IntegritySupport.ApplyAsync(
            fixture, slug, jobId, applicant,
            answers: [.. applicant.Answers.Where(a => a.QuestionId != "notice_period")],
            files: [(ApplicationForm.LinkedInExportQuestionId, "Profile.pdf", linkedIn)]);
        var integrity = await IntegritySupport.SettledAsync(fixture, tenant, applicationId);

        integrity.Text("consistencyCheck.status").Should().Be("Completed");
        integrity.Text("consistencyCheck.promptVersion").Should().Be(ConsistencyPrompts.Version);
        integrity.Text("consistencyCheck.consistencyModel").Should().Be(FakeConsistencyChatClient.ModelId);
        var inconsistencies = integrity.At("authenticity.signals").EnumerateArray()
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

        var settings = (await tenant.GetAsync("/v1/organization/integrity-settings")).Expect(HttpStatusCode.OK);
        settings.Body.GetProperty("tokensUsedThisMonth").GetInt64().Should().BeGreaterThan(0);
        await using var db = await IntegritySupport.TenantDbAsync(fixture, tenant.OrganizationId);
        (await db.ApplicationDocuments.Where(d => d.ApplicationId == applicationId).Select(d => d.Kind).ToListAsync(Ct))
            .Should().BeEquivalentTo([DocumentKind.Resume, DocumentKind.LinkedInExport]);
    }

    [Fact]
    public async Task Once_the_monthly_budget_is_spent_the_check_is_skipped_and_says_why()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        (await tenant.PutAsync("/v1/organization/integrity-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 1 }))
            .Expect(HttpStatusCode.OK);
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, tenant, SystemTemplates.SoftwareEngineerVersion2);
        var first = SyntheticApplicants.GenuineHuman;
        var second = SyntheticApplicants.HonestAiAssisted;

        var firstId = await IntegritySupport.ApplyAsync(fixture, slug, jobId, first, answers: first.Answers);
        (await IntegritySupport.SettledAsync(fixture, tenant, firstId)).Text("consistencyCheck.status").Should().Be("Completed");
        var secondId = await IntegritySupport.ApplyAsync(fixture, slug, jobId, second, answers: second.Answers);
        var integrity = await IntegritySupport.SettledAsync(fixture, tenant, secondId);

        integrity.Text("consistencyCheck.status").Should().Be("OverBudget");
        integrity.Text("consistencyCheck.note").Should().Be("The organization's monthly budget for the consistency check is used up.");
        integrity.Text("status").Should().Be("Assessed", "a skipped check never holds up the assessment");
    }

    [Fact]
    public async Task A_near_identical_answer_is_reported_within_the_organization_and_never_across()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobWithQuestionsAsync(tenant);
        var (otherSlug, otherJob) = await OpenJobWithQuestionsAsync(other);
        var copied = Motivation.Replace("four years", "five years", StringComparison.Ordinal);

        var originalId = await IntegritySupport.ApplyAsync(
            fixture, slug, jobId, SyntheticApplicants.GenuineHuman, answers: [.. SyntheticApplicants.GenuineHuman.Answers, new("motivation", Motivation)]);
        await IntegritySupport.SettledAsync(fixture, tenant, originalId);
        var copyId = await IntegritySupport.ApplyAsync(
            fixture, slug, jobId, SyntheticApplicants.HonestAiAssisted, answers: [.. SyntheticApplicants.HonestAiAssisted.Answers, new("motivation", copied)]);
        var elsewhereId = await IntegritySupport.ApplyAsync(
            fixture, otherSlug, otherJob, SyntheticApplicants.HonestAiAssisted, answers: [.. SyntheticApplicants.HonestAiAssisted.Answers, new("motivation", copied)]);

        var copy = await IntegritySupport.SettledAsync(fixture, tenant, copyId);
        var duplicate = copy.At("aiAssistance.signals").EnumerateArray().Single(s => s.GetProperty("code").GetString() == SignalCatalogue.NearDuplicateAnswer);
        var comparison = duplicate.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "TextComparison");
        comparison.GetProperty("right").GetProperty("text").GetString().Should().Be(Motivation);
        comparison.GetProperty("right").GetProperty("label").GetString().Should().NotContain(SyntheticApplicants.GenuineHuman.FullName);

        var elsewhere = await IntegritySupport.SettledAsync(fixture, other, elsewhereId);
        elsewhere.At("aiAssistance.signals").EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().NotContain(SignalCatalogue.NearDuplicateAnswer, "answers are compared within one organization only");
    }

    [Fact]
    public async Task Integrity_settings_are_off_by_default_validated_and_per_organization()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();

        var initial = (await tenant.GetAsync("/v1/organization/integrity-settings")).Expect(HttpStatusCode.OK);
        initial.Body.GetProperty("llmConsistencyCheck").GetBoolean().Should().BeFalse();
        (await tenant.PutAsync("/v1/organization/integrity-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 0 }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("monthlyTokenBudget");
        var saved = (await tenant.PutAsync("/v1/organization/integrity-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 50000 }))
            .Expect(HttpStatusCode.OK);

        saved.Body.GetProperty("llmConsistencyCheck").GetBoolean().Should().BeTrue();
        saved.Body.GetProperty("monthlyTokenBudget").GetInt64().Should().Be(50000);
        (await other.GetAsync("/v1/organization/integrity-settings")).Expect(HttpStatusCode.OK)
            .Body.GetProperty("llmConsistencyCheck").GetBoolean().Should().BeFalse("another organization's switch is its own");
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
        return await IntegritySupport.OpenJobAsync(fixture, tenant, versionId);
    }

}
