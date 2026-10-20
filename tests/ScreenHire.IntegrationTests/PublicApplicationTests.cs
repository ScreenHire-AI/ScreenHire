using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The careers apply flow's API: the application form, upload links and checks, and anonymous submission. Each test makes
/// its own organization with a job copied from the "Software engineer" system template, which asks for a resume (PDF or
/// DOCX, required), a right-to-work knockout question and the candidate's experience, and wants AI use disclosed.
/// </summary>
public sealed class PublicApplicationTests(AppHostFixture fixture)
{
    private static readonly byte[] Pdf = CareersUploads.Pdf;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Application_form_puts_the_resume_first_and_keeps_knockouts_on_the_server()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");

        var form = await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{organization.Slug}/jobs/{job.Slug}/application-form", Ct);

        form.At("organization.slug").GetString().Should().Be(organization.Slug);
        form.At("job.id").GetGuid().Should().Be(job.Id);
        form.At("aiUsePolicy").GetString().Should().Be("AllowedWithDisclosure");
        var questions = form.At("questions").EnumerateArray().ToList();
        questions.Select(q => q.GetProperty("id").GetString()).Should().StartWith("resume").And.Contain(["work_authorization", "experience"]);
        questions[0].GetProperty("acceptedFileTypes").EnumerateArray().Select(t => t.GetString()).Should().Equal(".pdf", ".docx");
        form.GetRawText().Should().NotContainEquivalentOf("knockout").And.NotContainEquivalentOf("disqualifying");
        form.At("uploads.maxBytes").GetInt64().Should().Be(10 * 1024 * 1024);
        form.At("uploads.origin").GetString().Should().StartWith("http");
        form.At("turnstileSiteKey").ValueKind.Should().Be(JsonValueKind.Null, "the bot challenge is off unless configured");
    }

    [Fact]
    public async Task Submission_creates_the_candidate_and_application_and_publishes_its_event()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var uploadId = await UploadAsync(client, organization, job, "resume", "Casey Morgan CV.pdf", Pdf);

        // "no" to the right-to-work knockout question is an answer like any other: stored, and decided on by a person.
        var response = await SubmitAsync(client, organization, job.Id, Application("casey.morgan@example.com", uploadId, workAuthorization: "no"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var applicationId = receipt.GetProperty("applicationId").GetGuid();

        await using var db = await TenantDbAsync(organization.Id);
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId, Ct);
        application.Source.Should().Be(ApplicationSource.CareersSite);
        application.JobId.Should().Be(job.Id);
        application.CurrentStageId.Should().Be("applied");
        application.PrivacyNoticeVersion.Should().Be("2026-10-20");
        application.AccommodationRequested.Should().BeTrue();
        application.AiUseDeclaration.Should().Be(AiUseDeclaration.Used);
        application.AiUseDetails.Should().Be("Grammar check");
        application.HoneypotFilled.Should().BeFalse();
        application.BotCheck.Should().Be(BotCheckResult.NotChecked);
        application.Answers.Should().ContainEquivalentOf(new ApplicationAnswer("work_authorization", "no"));
        application.Answers.Should().ContainEquivalentOf(new ApplicationAnswer("resume", uploadId.ToString()));

        var candidate = await db.Candidates.SingleAsync(c => c.Id == application.CandidateId, Ct);
        candidate.FullName.Should().Be("Casey Morgan");
        candidate.Email.Should().Be("casey.morgan@example.com");

        var file = await db.ApplicationFiles.SingleAsync(f => f.ApplicationId == applicationId, Ct);
        file.QuestionId.Should().Be("resume");
        file.FileName.Should().Be("Casey Morgan CV.pdf");
        file.ContentType.Should().Be("application/pdf");
        file.SizeBytes.Should().Be(Pdf.Length);
        file.Sha256.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Pdf)));
        file.BlobName.Should().StartWith($"{organization.Id}/{job.Id}/resume/{uploadId}/");

        // ApplicationSubmitted is found by the timeline entry only it carries; the worker handles it as the tenant.
        var submittedEventId = await db.ApplicationEvents.Where(e => e.ApplicationId == applicationId).Select(e => e.Id).SingleAsync(Ct);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(submittedEventId.ToString(), Ct) == 1, "the worker to handle ApplicationSubmitted", Ct);

        // The malware-scan hook runs once per file; with no scanner configured it records that.
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var reader = await TenantDbAsync(organization.Id);
                return (await reader.ApplicationFiles.SingleAsync(f => f.Id == file.Id, Ct)).ScanStatus == MalwareScanStatus.NotScanned;
            },
            "the worker to record the file's scan",
            Ct);
    }

    [Fact]
    public async Task A_job_id_from_another_organization_is_not_found_and_nothing_is_written()
    {
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        var (otherOrganization, otherJob) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var uploadId = await UploadAsync(client, otherOrganization, otherJob, "resume", "cv.pdf", Pdf);

        var submission = await SubmitAsync(client, organization, otherJob.Id, Application("robin.lake@example.com", uploadId));
        var upload = await client.PostAsJsonAsync(
            $"/v1/public/organizations/{organization.Slug}/jobs/{otherJob.Id}/questions/resume/uploads",
            new { fileName = "cv.pdf", sizeBytes = 100 }, Ct);
        var verify = await client.PostAsync(
            $"/v1/public/organizations/{organization.Slug}/jobs/{otherJob.Id}/questions/resume/uploads/{uploadId}/verify", null, Ct);

        submission.StatusCode.Should().Be(HttpStatusCode.NotFound);
        upload.StatusCode.Should().Be(HttpStatusCode.NotFound);
        verify.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using (var db = await TenantDbAsync(organization.Id))
        {
            (await db.Applications.CountAsync(Ct)).Should().Be(0);
            (await db.Candidates.CountAsync(Ct)).Should().Be(0);
        }

        await using (var db = await TenantDbAsync(otherOrganization.Id))
        {
            (await db.Applications.CountAsync(Ct)).Should().Be(0);
        }
    }

    [Fact]
    public async Task An_upload_from_another_job_of_the_same_organization_is_not_accepted()
    {
        var engineering = await SoftwareEngineerTemplateAsync();
        Job first = null!, second = null!;
        var organization = await fixture.CreateOrganizationAsync(org =>
        {
            first = OpenJob(org, "first", engineering);
            second = OpenJob(org, "second", engineering);
            return [first, second];
        }, Ct);
        using var client = fixture.CreateHttpClient("api");
        var uploadId = await UploadAsync(client, organization, first, "resume", "cv.pdf", Pdf);

        var response = await SubmitAsync(client, organization, second.Id, Application("sam.field@example.com", uploadId));

        (await ProblemKeysAsync(response)).Should().Equal("answers.resume");
    }

    [Fact]
    public async Task A_closed_job_is_gone_and_a_draft_is_not_found()
    {
        var now = DateTimeOffset.UtcNow;
        Job closed = null!, draft = null!;
        var organization = await fixture.CreateOrganizationAsync(org =>
        {
            closed = TestData.NewJob(org, "closed");
            closed.Publish(now);
            closed.Close(now);
            draft = TestData.NewJob(org, "draft");
            return [closed, draft];
        }, Ct);
        using var client = fixture.CreateHttpClient("api");
        var prefix = $"/v1/public/organizations/{organization.Slug}/jobs";

        (await client.GetAsync($"{prefix}/closed", Ct)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.GetAsync($"{prefix}/closed/application-form", Ct)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await SubmitAsync(client, organization, closed.Id, Application("ola.berg@example.com", null))).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsJsonAsync($"{prefix}/{closed.Id}/questions/resume/uploads", new { fileName = "cv.pdf", sizeBytes = 100 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.GetAsync($"{prefix}/draft/application-form", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SubmitAsync(client, organization, draft.Id, Application("ola.berg@example.com", null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("setup.exe", 1000)]
    [InlineData("cv.png", 1000)] // an allowed type in general, but not one this question accepts
    [InlineData("cv.pdf", 10 * 1024 * 1024 + 1)]
    public async Task An_upload_link_is_refused_for_a_wrong_type_or_an_oversized_file(string fileName, long sizeBytes)
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");

        var response = await client.PostAsJsonAsync(
            $"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/questions/resume/uploads", new { fileName, sizeBytes }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_uploaded_file_whose_content_is_not_its_type_is_rejected_and_deleted()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var ticket = await TicketAsync(client, organization, job, "resume", "cv.pdf", 64);
        await PutAsync(ticket, Encoding.ASCII.GetBytes("MZ this is a program, not a PDF document at all......"));
        var verifyUrl = $"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/questions/resume/uploads/{ticket.GetProperty("uploadId").GetGuid()}/verify";

        var first = await client.PostAsync(verifyUrl, null, Ct);
        var second = await client.PostAsync(verifyUrl, null, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await first.Content.ReadAsStringAsync(Ct)).Should().Contain("not a PDF or DOCX file");
        second.StatusCode.Should().Be(HttpStatusCode.NotFound, "a file that fails the check is deleted");
    }

    [Fact]
    public async Task An_upload_link_writes_its_file_once_and_cannot_replace_it_after_the_check()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var ticket = await TicketAsync(client, organization, job, "resume", "cv.pdf", Pdf.Length);
        await PutAsync(ticket, Pdf);
        var verifyUrl = $"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/questions/resume/uploads/{ticket.GetProperty("uploadId").GetGuid()}/verify";
        (await client.PostAsync(verifyUrl, null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var replaced = await SendToStorageAsync(ticket, Encoding.ASCII.GetBytes("MZ this is a program, not the checked PDF"));

        replaced.IsSuccessStatusCode.Should().BeFalse("the link may create the blob but not overwrite it ({0})", replaced.StatusCode);
        var verified = await client.PostAsync(verifyUrl, null, Ct);
        verified.StatusCode.Should().Be(HttpStatusCode.OK, "the checked file is still the one in storage");
    }

    [Fact]
    public async Task An_uploaded_file_larger_than_the_limit_is_rejected_and_deleted()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        // The link is asked for with a small size; the bytes sent are larger. The link cannot stop that, the check does.
        var ticket = await TicketAsync(client, organization, job, "resume", "cv.pdf", 1000);
        var oversized = new byte[(10 * 1024 * 1024) + 1];
        Pdf.CopyTo(oversized, 0);
        await PutAsync(ticket, oversized);
        var uploadId = ticket.GetProperty("uploadId").GetGuid();

        var response = await SubmitAsync(client, organization, job.Id, Application("kim.ito@example.com", uploadId));

        (await ProblemKeysAsync(response)).Should().Equal("answers.resume");
        (await client.PostAsync(
            $"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/questions/resume/uploads/{uploadId}/verify", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Answers_are_checked_against_the_jobs_questions_and_its_AI_policy()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var body = Application("jo.park@example.com", uploadId: null, workAuthorization: "maybe");
        body["aiUseDeclaration"] = "NotProvided";
        body["answers"]!.AsArray().Add(new JsonObject { ["questionId"] = "favourite_colour", ["value"] = "teal" });

        var response = await SubmitAsync(client, organization, job.Id, body);

        (await ProblemKeysAsync(response)).Should().BeEquivalentTo(
            "answers.resume", "answers.work_authorization", "answers.favourite_colour", "aiUseDeclaration");
    }

    [Fact]
    public async Task A_filled_honeypot_is_recorded_and_never_refuses_the_application()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var uploadId = await UploadAsync(client, organization, job, "resume", "cv.pdf", Pdf);
        var body = Application("lee.chan@example.com", uploadId);
        body["honeypotFilled"] = true;
        body["botCheckToken"] = "not-checked-because-turnstile-is-off";

        var response = await SubmitAsync(client, organization, job.Id, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var db = await TenantDbAsync(organization.Id);
        var application = await db.Applications.SingleAsync(Ct);
        application.HoneypotFilled.Should().BeTrue();
        application.BotCheck.Should().Be(BotCheckResult.NotChecked);
        application.CurrentStageId.Should().Be("applied", "integrity signals never move or reject an application");
    }

    [Fact]
    public async Task A_second_application_from_the_same_email_is_a_conflict_and_changes_no_details()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        (await SubmitAsync(client, organization, job.Id, Application("ari.moss@example.com", await UploadAsync(client, organization, job, "resume", "cv.pdf", Pdf))))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var again = Application("ARI.MOSS@example.com", await UploadAsync(client, organization, job, "resume", "cv.pdf", Pdf));
        again["candidate"]!["fullName"] = "Someone Else";

        var response = await SubmitAsync(client, organization, job.Id, again);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = await TenantDbAsync(organization.Id);
        (await db.Applications.CountAsync(Ct)).Should().Be(1);
        (await db.Candidates.SingleAsync(Ct)).FullName.Should().Be("Casey Morgan", "a public form never changes an existing candidate");
    }

    [Fact]
    public async Task Board_and_organization_carry_the_contrast_checked_brand_palette()
    {
        using var client = fixture.CreateHttpClient("api");

        var organization = await client.GetFromJsonAsync<JsonElement>("/v1/public/organizations/brightwater", Ct);
        var board = await client.GetFromJsonAsync<JsonElement>("/v1/public/organizations/brightwater/jobs", Ct);

        var branding = organization.GetProperty("branding");
        branding.GetProperty("brand").GetString().Should().Be("#1f4e5a");
        board.GetProperty("branding").GetRawText().Should().Be(branding.GetRawText());
        BrandPalette.ContrastRatio(branding.GetProperty("brand").GetString()!, branding.GetProperty("onBrand").GetString()!)
            .Should().BeGreaterThanOrEqualTo(4.5);
        BrandPalette.ContrastRatio(branding.GetProperty("brandText").GetString()!, "#fbf6ee").Should().BeGreaterThanOrEqualTo(4.5);
        BrandPalette.ContrastRatio(branding.GetProperty("brandUi").GetString()!, "#fbf6ee").Should().BeGreaterThanOrEqualTo(3);
    }

    // An application as the careers server sends it: the candidate, the notice they saw, and answers to the engineering
    // template's questions. `uploadId` is the resume's upload, if there is one.
    private static JsonObject Application(string email, Guid? uploadId, string workAuthorization = "yes")
    {
        var answers = new JsonArray(
            new JsonObject { ["questionId"] = "work_authorization", ["value"] = workAuthorization },
            new JsonObject { ["questionId"] = "experience", ["value"] = "three_to_five" },
            new JsonObject { ["questionId"] = "work_sample", ["value"] = "https://example.com/portfolio" });
        if (uploadId is { } id)
        {
            answers.Add(new JsonObject { ["questionId"] = "resume", ["value"] = id.ToString() });
        }

        return new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Casey Morgan", ["email"] = email, ["phone"] = "+44 20 7946 0000" },
            ["privacyNoticeVersion"] = "2026-10-20",
            ["answers"] = answers,
            ["accommodationRequested"] = true,
            ["aiUseDeclaration"] = "Used",
            ["aiUseDetails"] = " Grammar check ",
        };
    }

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, Organization organization, Guid jobId, JsonObject body) =>
        await client.PostAsJsonAsync($"/v1/public/organizations/{organization.Slug}/jobs/{jobId}/applications", body, Ct);

    private static async Task<IReadOnlyList<string>> ProblemKeysAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return [.. problem.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }

    private static Task<Guid> UploadAsync(HttpClient client, Organization organization, Job job, string questionId, string fileName, byte[] content) =>
        CareersUploads.UploadAsync(client, organization.Slug, job.Id, questionId, fileName, content);

    private static Task<JsonElement> TicketAsync(HttpClient client, Organization organization, Job job, string questionId, string fileName, long sizeBytes) =>
        CareersUploads.TicketAsync(client, organization.Slug, job.Id, questionId, fileName, sizeBytes);

    private static Task PutAsync(JsonElement ticket, byte[] content) => CareersUploads.PutAsync(ticket, content);

    private static Task<HttpResponseMessage> SendToStorageAsync(JsonElement ticket, byte[] content) => CareersUploads.SendToStorageAsync(ticket, content);

    private async Task<(Organization Organization, Job Job)> OrganizationWithEngineeringJobAsync()
    {
        var engineering = await SoftwareEngineerTemplateAsync();
        Job job = null!;
        var organization = await fixture.CreateOrganizationAsync(org => [job = OpenJob(org, "engineer", engineering)], Ct);
        return (organization, job);
    }

    private static Job OpenJob(Organization organization, string slug, HiringTemplateVersion template)
    {
        var job = TestData.NewJob(organization, slug);
        job.UseTemplate(template);
        job.Publish(DateTimeOffset.UtcNow);
        return job;
    }

    // System templates belong to no organization, so any tenant reads them.
    private async Task<HiringTemplateVersion> SoftwareEngineerTemplateAsync()
    {
        await using var db = await TenantDbAsync(Guid.NewGuid());
        return await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, Ct);
    }

    private async Task<Infrastructure.Persistence.ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
