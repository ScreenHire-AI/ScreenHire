using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A reviewer's link to an application's files (<c>GET /v1/applications/{id}/files/{fileId}</c>): a redirect to a
/// short-lived read link on storage that downloads the file, and never one for a file the malware scan has not cleared.
/// </summary>
public sealed class ApplicationFilesTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_resume_from_the_careers_page_downloads_through_a_short_lived_read_link()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(EngineeringJob());
        await recruiter.PublishJobAsync(job.Id());
        var slug = (await recruiter.GetAsync("/v1/auth/me")).Text("organization.slug");
        using var careers = fixture.CreateHttpClient("api");
        var uploadId = await CareersUploads.UploadAsync(careers, slug, job.Id(), "resume", "Casey Morgan CV.pdf", CareersUploads.Pdf);
        var submitted = await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{job.Id()}/applications", Application(uploadId), Ct);
        submitted.StatusCode.Should().Be(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync(Ct));
        var applicationId = (await submitted.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
        // With no scanner configured, the worker records NotScanned, which the dashboard may offer.
        var fileId = Guid.Empty;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                var application = await recruiter.GetAsync($"/v1/applications/{applicationId}");
                fileId = application.Id("files.0.id");
                return application.Text("files.0.scanStatus") == "NotScanned";
            },
            "the worker to record the resume's scan",
            Ct);

        using var response = await recruiter.Session.Client.GetAsync($"/v1/applications/{applicationId}/files/{fileId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var link = response.Headers.Location!;
        link.Query.Should().Contain("sp=r", "the link reads and nothing else").And.Contain("se=");
        using var storage = new HttpClient();
        using var download = await storage.GetAsync(link, Ct);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(CareersUploads.Pdf);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Content.Headers.ContentDisposition.FileName.Should().Contain("Casey Morgan CV.pdf");
    }

    [Theory]
    [InlineData(MalwareScanStatus.Infected, "found a threat")]
    [InlineData(MalwareScanStatus.Pending, "still being scanned")]
    public async Task A_file_the_scan_has_not_cleared_is_never_offered(MalwareScanStatus status, string reason)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Scanned"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var fileId = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, application.Id(), status);

        var result = await recruiter.GetAsync($"/v1/applications/{application.Id()}/files/{fileId}");

        result.Expect(HttpStatusCode.Conflict).Text("detail").Should().Contain(reason);
    }

    [Fact]
    public async Task A_file_is_found_only_under_its_own_application()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Two applicants"));
        var owner = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var other = await recruiter.CreateApplicationAsync(job.Id(), "jordan.lee@example.com", "Jordan Lee");
        var fileId = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, owner.Id(), MalwareScanStatus.Clean);

        (await recruiter.GetAsync($"/v1/applications/{other.Id()}/files/{fileId}")).Expect(HttpStatusCode.NotFound);
        (await recruiter.GetAsync($"/v1/applications/{owner.Id()}/files/{Guid.NewGuid()}")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_read_file_previews_as_its_text_and_page_count()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Preview"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var fileId = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, application.Id(), MalwareScanStatus.Clean);
        await fixture.AddApplicationDocumentAsync(
            recruiter.OrganizationId, application.Id(), fileId, DocumentExtractionStatus.Extracted, "Casey Morgan\nPlatform engineer");

        var preview = await recruiter.GetAsync($"/v1/applications/{application.Id()}/files/{fileId}/preview");

        preview.Expect(HttpStatusCode.OK).Text("status").Should().Be("Ready");
        preview.Text("text").Should().Be("Casey Morgan\nPlatform engineer");
        preview.At("pageCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_file_not_read_yet_previews_as_pending()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Preview pending"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var scanning = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, application.Id(), MalwareScanStatus.Pending);
        var unread = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, application.Id(), MalwareScanStatus.Clean, "portfolio");

        foreach (var fileId in new[] { scanning, unread })
        {
            var preview = await recruiter.GetAsync($"/v1/applications/{application.Id()}/files/{fileId}/preview");
            preview.Expect(HttpStatusCode.OK).Text("status").Should().Be("Pending");
            preview.Text("text").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task A_withheld_or_unreadable_file_has_no_preview_and_says_why()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Preview unavailable"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var infected = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, application.Id(), MalwareScanStatus.Infected);
        // Even text read before the scan result arrived is never shown for a file the scan withheld.
        await fixture.AddApplicationDocumentAsync(
            recruiter.OrganizationId, application.Id(), infected, DocumentExtractionStatus.Extracted, "Text that must not be shown");
        var image = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, application.Id(), MalwareScanStatus.Clean, "portfolio");
        await fixture.AddApplicationDocumentAsync(
            recruiter.OrganizationId, application.Id(), image, DocumentExtractionStatus.Unsupported, "", "No text is read from PNG files.");

        var withheld = await recruiter.GetAsync($"/v1/applications/{application.Id()}/files/{infected}/preview");
        var unsupported = await recruiter.GetAsync($"/v1/applications/{application.Id()}/files/{image}/preview");

        withheld.Expect(HttpStatusCode.OK).Text("status").Should().Be("Unavailable");
        withheld.Text("text").Should().BeEmpty();
        withheld.Text("note").Should().Contain("found a threat");
        unsupported.Expect(HttpStatusCode.OK).Text("status").Should().Be("Unavailable");
        unsupported.Text("note").Should().Be("No text is read from PNG files.");
    }

    [Fact]
    public async Task A_preview_is_found_only_under_its_own_application()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Two previews"));
        var owner = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        var other = await recruiter.CreateApplicationAsync(job.Id(), "jordan.lee@example.com", "Jordan Lee");
        var fileId = await fixture.AddApplicationFileAsync(recruiter.OrganizationId, owner.Id(), MalwareScanStatus.Clean);

        (await recruiter.GetAsync($"/v1/applications/{other.Id()}/files/{fileId}/preview")).Expect(HttpStatusCode.NotFound);
        (await recruiter.GetAsync($"/v1/applications/{owner.Id()}/files/{Guid.NewGuid()}/preview")).Expect(HttpStatusCode.NotFound);
    }

    // A job on the "Software engineer" system template, which asks for a resume and wants AI use disclosed.
    private static JsonObject EngineeringJob()
    {
        var job = Samples.RemoteJob("Platform Engineer");
        job["templateVersionId"] = SystemTemplates.SoftwareEngineerVersion1;
        return job;
    }

    private static JsonObject Application(Guid uploadId) => new()
    {
        ["candidate"] = new JsonObject { ["fullName"] = "Casey Morgan", ["email"] = "casey.morgan@example.com" },
        ["privacyNoticeVersion"] = "2026-10-20",
        ["answers"] = new JsonArray(
            new JsonObject { ["questionId"] = "work_authorization", ["value"] = "yes" },
            new JsonObject { ["questionId"] = "experience", ["value"] = "three_to_five" },
            new JsonObject { ["questionId"] = "resume", ["value"] = uploadId.ToString() }),
        ["aiUseDeclaration"] = "DidNotUse",
    };
}
