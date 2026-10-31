using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// The form builder on a job's own workflow (design system section 11, A7, A8): adding, duplicating, requiring and
/// reordering questions, the preview at phone width and in its own window, and a File question a candidate then answers
/// on the careers apply page. Each test signs in as a new person with a new organization and a published job.
/// </summary>
public sealed class FormBuilderTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Notice = "What is your notice period?";
    private const string Start = "When could you start?";
    private const string RightToWork = "Do you have the right to work here?";

    /// <summary>The id the builder gives the first question it adds to an empty questionnaire.</summary>
    private const string FirstQuestionId = "question";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recruiter_adds_duplicates_requires_and_reorders_questions_and_previews_them_at_phone_width_and_in_a_window()
    {
        var job = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(job.Email);
        await OpenQuestionnaireAsync(job.JobId);
        var questions = Region("Screening questions");

        // Add a question, make it required, then duplicate it: the copy follows the original and opens.
        await AddQuestionAsync("Short text");
        await Expect(OpenText(1)).ToBeFocusedAsync();
        await OpenText(1).FillAsync(Notice);
        var required = questions.GetByRole(AriaRole.Switch, new() { Name = "Required" });
        await required.ClickAsync();
        await Expect(required).ToHaveAttributeAsync("aria-checked", "true");
        await questions.GetByRole(AriaRole.Button, new() { Name = $"Duplicate {Notice}" }).ClickAsync();
        await Expect(OpenText(2)).ToBeFocusedAsync();
        await Expect(OpenText(2)).ToHaveValueAsync(Notice);
        await Expect(questions.GetByRole(AriaRole.Switch, new() { Name = "Required" })).ToHaveAttributeAsync("aria-checked", "true");
        await OpenText(2).FillAsync(Start);
        await AddQuestionAsync("Yes or no");
        await OpenText(3).FillAsync(RightToWork);
        await Expect(questions.GetByRole(AriaRole.Button, new() { Name = $"Edit question 1: {Notice}" })).ToContainTextAsync("*");
        await ExpectOrderAsync(Notice, Start, RightToWork);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-form-builder");

        // Reorder with the grip's menu, then with the keyboard (A7).
        await questions.GetByRole(AriaRole.Button, new() { Name = $"Reorder {RightToWork}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Move to top" }).ClickAsync();
        await ExpectOrderAsync(RightToWork, Notice, Start);
        var grip = questions.GetByRole(AriaRole.Button, new() { Name = $"Reorder {Notice}" });
        await grip.FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Space");
        await ExpectOrderAsync(RightToWork, Start, Notice);

        // Preview: the candidate's form at a phone's width, in place of the builder.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
        await Expect(questions).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Group, new() { Name = "Screen size" }).GetByRole(AriaRole.Button, new() { Name = "Phone" }).ClickAsync();
        var phone = Page.Locator("[data-preview-device=phone]");
        await Expect(phone.GetByRole(AriaRole.Textbox, new() { Name = Start })).ToBeVisibleAsync();
        (await phone.BoundingBoxAsync())!.Width.Should().BeApproximately(390, 2);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-form-builder-preview");

        // The window icon opens the preview in a second window, which follows each edit.
        var popup = await Page.RunAndWaitForPopupAsync(() => Page.GetByRole(AriaRole.Button, new() { Name = "Open in a new window" }).ClickAsync());
        await Expect(popup.GetByRole(AriaRole.Textbox, new() { Name = Start })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        await questions.GetByRole(AriaRole.Button, new() { Name = $"Edit question 2: {Start}" }).ClickAsync();
        await OpenText(2).FillAsync("What date could you start?");
        await Expect(popup.GetByRole(AriaRole.Textbox, new() { Name = "What date could you start?" })).ToBeVisibleAsync();
        await Expect(popup.GetByRole(AriaRole.Textbox, new() { Name = Start })).ToHaveCountAsync(0);
        await popup.CloseAsync();
    }

    [Fact]
    public async Task A_file_question_built_in_the_form_builder_takes_an_upload_on_the_careers_apply_page()
    {
        var job = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(job.Email);
        await OpenQuestionnaireAsync(job.JobId);

        await AddQuestionAsync("File");
        await OpenText(1).FillAsync("A work sample");
        await Region("Screening questions").GetByRole(AriaRole.Switch, new() { Name = "Required" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save workflow" }).ClickAsync();
        await Expect(Page.GetByText("Saved. The job's workflow is now version 2.")).ToBeVisibleAsync();

        await GoToAsync("careers", $"/{job.OrganizationSlug}/jobs/{job.JobSlug}/apply");
        await Page.GetByLabel("First name").FillAsync("Casey");
        await Page.GetByLabel("Last name").FillAsync("Morgan");
        await Page.GetByLabel("Email").FillAsync("casey.morgan@example.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Questions");
        await Page.GetByLabel("A work sample").SetInputFilesAsync(new FilePayload { Name = "Work sample.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Expect(Page.GetByText("Uploaded: Work sample.pdf")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Work sample.pdf");
        await Page.GetByLabel("No, I did not use AI tools").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");

        var tenant = new TenantContext();
        tenant.SetOrganization(job.OrganizationId);
        await using var db = await Fixture.CreateDbContextAsync(tenant, Ct);
        var application = await db.Applications.SingleAsync(a => a.JobId == job.JobId, Ct);
        var file = await db.ApplicationFiles.SingleAsync(f => f.ApplicationId == application.Id, Ct);
        file.QuestionId.Should().Be(FirstQuestionId);
        file.FileName.Should().Be("Work sample.pdf");
    }

    [Fact]
    public async Task A_recruiter_puts_questions_in_sections_and_the_preview_and_the_careers_page_show_one_a_page()
    {
        var job = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(job.Email);
        await OpenQuestionnaireAsync(job.JobId);
        var questions = Region("Screening questions");

        await AddQuestionAsync("Short text");
        await OpenText(1).FillAsync(Notice);
        await AddQuestionAsync("Yes or no");
        await OpenText(2).FillAsync(RightToWork);

        // The first section holds every question; the second opens at its name.
        await questions.GetByRole(AriaRole.Button, new() { Name = "Add a section" }).ClickAsync();
        await Expect(questions.GetByRole(AriaRole.Textbox, new() { Name = "Section 2 name" })).ToBeFocusedAsync();
        await questions.GetByRole(AriaRole.Textbox, new() { Name = "Section 2 name" }).FillAsync("Eligibility");
        await questions.GetByRole(AriaRole.Textbox, new() { Name = "Section 1 name" }).FillAsync("About you");
        await Expect(questions.GetByText("Add a question to this section or delete it.")).ToBeVisibleAsync();

        // Move down past the section row puts the question in that section (A7, no drag needed).
        await questions.GetByRole(AriaRole.Button, new() { Name = $"Reorder {RightToWork}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Move down" }).ClickAsync();
        await Expect(questions.GetByText("Add a question to this section or delete it.")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-form-builder-sections");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
        var pages = Page.Locator("[data-preview-page]");
        await Expect(pages).ToHaveCountAsync(2);
        await Expect(pages.Nth(0).GetByRole(AriaRole.Heading)).ToHaveTextAsync("About you");
        await Expect(pages.Nth(1).GetByRole(AriaRole.Heading)).ToHaveTextAsync("Eligibility");
        await Expect(pages.Nth(1)).ToContainTextAsync("Step 3 of 4");
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save workflow" }).ClickAsync();
        await Expect(Page.GetByText("Saved. The job's workflow is now version 2.")).ToBeVisibleAsync();

        await GoToAsync("careers", $"/{job.OrganizationSlug}/jobs/{job.JobSlug}/apply");
        await Page.GetByLabel("First name").FillAsync("Casey");
        await Page.GetByLabel("Email").FillAsync("casey.morgan@example.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("About you");
        await Expect(Page.Locator("[data-page-progress]")).ToContainTextAsync("Step 2 of 4");
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = RightToWork })).ToHaveCountAsync(0);
    }

    private sealed record ArrangedJob(string Email, Guid OrganizationId, string OrganizationSlug, Guid JobId, string JobSlug);

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // The open question's text box. Only one question is open at a time.
    private ILocator OpenText(int position) =>
        Region("Screening questions").GetByRole(AriaRole.Textbox, new() { Name = $"Question {position}", Exact = true });

    private async Task AddQuestionAsync(string kind)
    {
        await Region("Screening questions").GetByRole(AriaRole.Button, new() { Name = "Add a question" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = kind, Exact = true }).ClickAsync();
    }

    private async Task ExpectOrderAsync(params string[] labels)
    {
        var list = Region("Screening questions").GetByRole(AriaRole.List, new() { Name = "Screening questions", Exact = true });
        await Expect(list.Locator(":scope > li")).ToHaveCountAsync(labels.Length);
        for (var i = 0; i < labels.Length; i++)
        {
            await Expect(list.Locator(":scope > li").Nth(i)).ToHaveAttributeAsync("aria-label", labels[i]);
        }
    }

    private async Task OpenQuestionnaireAsync(Guid jobId)
    {
        await Page.GotoAsync(new Uri(Fixture.GetEndpoint("dashboard"), $"/jobs/{jobId}/workflow").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Workflow:");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Questionnaire" }).ClickAsync();
        await Expect(Region("Screening questions")).ToContainTextAsync("No questions yet.");
    }

    // A new person and organization with a published job on the default workflow, whose questionnaire is empty.
    private async Task<ArrangedJob> ArrangeAsync()
    {
        var email = ApiSession.NewEmail("e2e-form-builder");
        var slug = $"test-{Guid.NewGuid():N}";
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var user = await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane", slug }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = "Support Engineer",
                description = "<p>Help customers get the most from the product.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
        return new ArrangedJob(email, user.GetProperty("organization").GetProperty("id").GetGuid(), slug, jobId, job.GetProperty("slug").GetString()!);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task SignInAsync(string email)
    {
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }
}
