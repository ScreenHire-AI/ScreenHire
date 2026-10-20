using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// The careers apply flow, end to end: the notice, the three steps, the resume upload and the confirmation, with
/// JavaScript off (native forms, the careers server uploads the file) and on (the island uploads straight to storage).
/// Each test applies to a job of its own organization, copied from the "Software engineer" system template.
/// </summary>
public sealed class ApplyTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const int JavaScriptBudgetBytes = 20 * 1024;

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Candidate_applies_with_JavaScript_turned_off()
    {
        await WithoutJavaScriptAsync();
        var (organization, _) = await OrganizationWithEngineeringJobAsync();

        var jobPage = await OpenAsync("careers", $"/{organization.Slug}/jobs/engineer");
        jobPage.Status.Should().Be(200);
        (await jobPage.AllHeadersAsync()).Should().NotContainKey("set-cookie", "browsing collects nothing (CLAUDE.md non-negotiable 4)");
        await Expect(Page.Locator("script:not([type='application/ld+json'])")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" }).ClickAsync();

        await ApplyAsync(organization, "casey.morgan@example.com", javaScript: false);

        await AssertApplicationAsync(organization, "casey.morgan@example.com");
    }

    [Fact]
    public async Task Candidate_applies_with_JavaScript_and_the_resume_goes_straight_to_storage()
    {
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        var problems = new List<string>();
        Page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
            {
                problems.Add(message.Text);
            }
        };
        Page.PageError += (_, error) => problems.Add(error);
        var scripts = new List<IResponse>();
        Page.Response += (_, response) =>
        {
            if (response.Request.ResourceType == "script")
            {
                scripts.Add(response);
            }
        };

        var applyPage = await OpenAsync("careers", $"/{organization.Slug}/jobs/engineer/apply");
        applyPage.Status.Should().Be(200);
        (await applyPage.AllHeadersAsync()).Should().NotContainKey("set-cookie", "the form sets no cookies");
        await Page.Locator("astro-island:not([ssr])").WaitForAsync(); // hydrated
        (await JavaScriptBytesAsync(scripts)).Should().BeLessThanOrEqualTo(JavaScriptBudgetBytes, "the apply page ships about 20 KB of JavaScript at most");

        await ApplyAsync(organization, "jo.park@example.com", javaScript: true);

        await AssertApplicationAsync(organization, "jo.park@example.com");
        problems.Should().BeEmpty("the strict content security policy must not block anything the page needs");
    }

    [Fact]
    public async Task Wrong_type_and_oversized_uploads_are_rejected_with_JavaScript()
    {
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        await GoToAsync("careers", $"/{organization.Slug}/jobs/engineer/apply");
        await Page.Locator("astro-island:not([ssr])").WaitForAsync();
        var resume = Page.GetByLabel("Your CV or resume");

        // Refused in the browser before anything is sent.
        await resume.SetInputFilesAsync(new FilePayload { Name = "setup.exe", MimeType = "application/octet-stream", Buffer = Pdf });
        await Expect(Page.GetByText("Upload a PDF or DOCX file.")).ToBeVisibleAsync();
        await resume.SetInputFilesAsync(new FilePayload { Name = "cv.pdf", MimeType = "application/pdf", Buffer = Oversized() });
        await Expect(Page.GetByText("This file is larger than 10 MB.")).ToBeVisibleAsync();

        // Named .pdf but not a PDF: uploaded, then refused by the API's check of its content.
        await resume.SetInputFilesAsync(new FilePayload { Name = "cv.pdf", MimeType = "application/pdf", Buffer = NotAPdf() });
        await Expect(Page.GetByText("This file is not a PDF or DOCX file.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Uploaded:", new() { Exact = false })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Wrong_type_and_oversized_uploads_are_rejected_without_JavaScript()
    {
        await WithoutJavaScriptAsync();
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        await GoToAsync("careers", $"/{organization.Slug}/jobs/engineer/apply");
        await FillDetailsAsync("ari.moss@example.com");

        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "cv.pdf", MimeType = "application/pdf", Buffer = Oversized() });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await Expect(ErrorSummary()).ToContainTextAsync("This file is larger than 10 MB.");

        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "cv.pdf", MimeType = "application/pdf", Buffer = NotAPdf() });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await Expect(ErrorSummary()).ToContainTextAsync("This file is not a PDF or DOCX file.");
        // What the candidate typed is still there.
        await Expect(Page.GetByLabel("Email")).ToHaveValueAsync("ari.moss@example.com");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_closed_job_is_gone_with_a_friendly_page()
    {
        var now = DateTimeOffset.UtcNow;
        var organization = await Fixture.CreateOrganizationAsync(org =>
        {
            var job = TestData.NewJob(org, "closed-role", "Closed Role");
            job.Publish(now);
            job.Close(now);
            return [job];
        }, Ct);

        foreach (var path in new[] { $"/{organization.Slug}/jobs/closed-role", $"/{organization.Slug}/jobs/closed-role/apply" })
        {
            var response = await OpenAsync("careers", path);

            response.Status.Should().Be(410, "{0} is a job that existed and closed", path);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This job has closed");
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"See open roles at {organization.Name}" })).ToBeVisibleAsync();
            await ExpectNoAccessibilityViolationsAsync();
        }

        (await OpenAsync("careers", $"/{organization.Slug}/jobs/never-existed")).Status.Should().Be(404);
    }

    [Fact]
    public async Task The_privacy_notice_page_explains_what_is_collected()
    {
        await GoToAsync("careers", "/brightwater/privacy");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("What we collect and why");
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Nothing we check rejects you automatically");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Employer_branding_comes_from_a_hashed_stylesheet_under_a_strict_policy()
    {
        var response = await OpenAsync("careers", "/brightwater/jobs/senior-dotnet-engineer");

        var stylesheet = Page.Locator("link[rel='stylesheet'][href*='/brand/']");
        await Expect(stylesheet).ToHaveCountAsync(1);
        (await stylesheet.GetAttributeAsync("href")).Should().MatchRegex("^/brightwater/brand/[0-9a-f]{16}\\.css$");
        var apply = Page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" });
        (await apply.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor")).Should().Be("rgb(31, 78, 90)");
        // Astro's policy (hashes for what it emits) and the middleware's (what only a header can carry), both enforced.
        var policy = (await response.AllHeadersAsync())["content-security-policy"];
        policy.Should().Contain("default-src 'none'").And.Contain("frame-ancestors 'self'").And.NotContain("unsafe-inline");
        (await Page.Locator("[style]").CountAsync()).Should().Be(0, "no inline styles");
    }

    [Fact]
    public async Task A_tenant_host_serves_its_board_without_the_organization_in_the_path()
    {
        var careers = Fixture.GetEndpoint("careers");

        var response = await Page.GotoAsync($"{careers.Scheme}://brightwater.localhost:{careers.Port}/");

        response!.Status.Should().Be(200);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Open roles at Brightwater Studio");
        (await Page.GetByRole(AriaRole.Link, new() { Name = "Senior .NET Engineer" }).GetAttributeAsync("href"))
            .Should().Be("/jobs/senior-dotnet-engineer");
    }

    // The whole journey from the first step: details and the notice, questions, review, confirmation. Accessibility is
    // checked on every step.
    private async Task ApplyAsync(Organization organization, string email, bool javaScript)
    {
        // The notice comes before anything is asked for.
        var notice = Page.GetByRole(AriaRole.Region, new() { Name = "How we keep this fair" });
        await Expect(notice).ToBeVisibleAsync();
        (await notice.BoundingBoxAsync())!.Y.Should().BeLessThan((await Page.GetByLabel("Full name").BoundingBoxAsync())!.Y);
        await ExpectNoAccessibilityViolationsAsync();

        // A step sent with nothing in it explains itself.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await Expect(ErrorSummary()).ToContainTextAsync("Enter your full name.");
        await Expect(ErrorSummary()).ToContainTextAsync("Add a file.");
        await ExpectNoAccessibilityViolationsAsync();

        await FillDetailsAsync(email);
        await Page.GetByRole(AriaRole.Switch, new() { Name = "I use dictation, a screen reader or other assistive technology" }).CheckAsync();
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        if (javaScript)
        {
            await Expect(Page.GetByText("Uploaded: Casey CV.pdf")).ToBeVisibleAsync();
        }

        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await Expect(Page.Locator("[aria-current='step']")).ToContainTextAsync("QUESTIONS");
        await Page.GetByRole(AriaRole.Group, new() { Name = "Do you have the right to work in the country this job is in?" })
            .GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "How many years have you worked as a software engineer?" })
            .GetByLabel("3 to 5").CheckAsync();
        await Page.GetByLabel("A link to code or a project you are proud of").FillAsync("https://example.com/portfolio");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        await Expect(Page.Locator("[aria-current='step']")).ToContainTextAsync("REVIEW");
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Casey CV.pdf");
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("3 to 5");
        // The job wants AI use disclosed: sending without an answer asks for one.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(ErrorSummary()).ToContainTextAsync("Tell us whether you used AI tools for this application.");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByLabel("Yes, I used AI tools").CheckAsync();
        await Page.GetByLabel("If you used AI tools, how?").FillAsync("To check my grammar.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Uri(Fixture.GetEndpoint("careers"), $"/{organization.Slug}/jobs/engineer/applied").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");
        await ExpectNoAccessibilityViolationsAsync();
    }

    private async Task FillDetailsAsync(string email)
    {
        await Page.GetByLabel("Full name").FillAsync("Casey Morgan");
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Phone").FillAsync("+44 20 7946 0000");
    }

    private ILocator ErrorSummary() => Page.Locator("#error-summary");

    private async Task AssertApplicationAsync(Organization organization, string email)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organization.Id);
        await using var db = await Fixture.CreateDbContextAsync(tenant, Ct);
        var candidate = await db.Candidates.SingleAsync(c => c.Email == email, Ct);
        var application = await db.Applications.SingleAsync(a => a.CandidateId == candidate.Id, Ct);
        application.Source.Should().Be(ApplicationSource.CareersSite);
        application.AccommodationRequested.Should().BeTrue();
        application.AiUseDeclaration.Should().Be(AiUseDeclaration.Used);
        application.AiUseDetails.Should().Be("To check my grammar.");
        application.HoneypotFilled.Should().BeFalse();
        application.PrivacyNoticeVersion.Should().NotBeNullOrEmpty();
        application.Answers.Should().ContainEquivalentOf(new ApplicationAnswer("experience", "three_to_five"));
        var file = await db.ApplicationFiles.SingleAsync(f => f.ApplicationId == application.Id, Ct);
        file.FileName.Should().Be("Casey CV.pdf");
        file.SizeBytes.Should().Be(Pdf.Length);
    }

    // The JavaScript the page loaded, brotli-compressed as it would be served, plus its inline scripts (JSON-LD is data).
    private async Task<long> JavaScriptBytesAsync(IEnumerable<IResponse> scripts)
    {
        long total = 0;
        foreach (var script in scripts)
        {
            total += Brotli(await script.BodyAsync());
        }

        foreach (var inline in await Page.Locator("script:not([src]):not([type='application/ld+json'])").AllInnerTextsAsync())
        {
            total += Brotli(Encoding.UTF8.GetBytes(inline));
        }

        return total;
    }

    private static long Brotli(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(bytes);
        }

        return output.Length;
    }

    private static byte[] Oversized()
    {
        var bytes = new byte[(10 * 1024 * 1024) + 1];
        Pdf.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] NotAPdf() => Encoding.ASCII.GetBytes("MZ this is a program, not a PDF document at all.");

    private async Task<(Organization Organization, Job Job)> OrganizationWithEngineeringJobAsync()
    {
        HiringTemplateVersion engineering;
        var reader = new TenantContext();
        reader.SetOrganization(Guid.NewGuid()); // system templates belong to no organization, so any tenant reads them
        await using (var db = await Fixture.CreateDbContextAsync(reader, Ct))
        {
            engineering = await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, Ct);
        }

        Job job = null!;
        var organization = await Fixture.CreateOrganizationAsync(org =>
        {
            job = TestData.NewJob(org, "engineer", "Platform Engineer");
            job.UseTemplate(engineering);
            job.Publish(DateTimeOffset.UtcNow);
            return [job];
        }, Ct);
        return (organization, job);
    }
}
