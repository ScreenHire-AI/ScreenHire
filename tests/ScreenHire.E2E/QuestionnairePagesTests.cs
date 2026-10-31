using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// The apply form a page at a time (apply-flow.md §7), with JavaScript off: one named section a page with "Step 2 of 4"
/// and how many questions are answered, Change on the review and back to it, and saving to finish later through the
/// link Mailpit receives. Each test applies to a job of its own organization, copied from the "Software engineer" system
/// template and given two sections.
/// </summary>
public sealed partial class QuestionnairePagesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_answers_one_section_a_page_and_changes_an_answer_from_the_review()
    {
        var (organization, job) = await OrganizationWithSectionedJobAsync();
        var email = ApiSession.NewEmail("pages");
        await WithoutJavaScriptAsync();

        await GoToAsync("careers", $"/{organization.Slug}/jobs/{job.Slug}/apply");
        await Expect(Progress()).ToContainTextAsync("Step 1 of 4");
        await FillDetailsAsync(email);
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Eligibility");
        await Expect(Progress()).ToContainTextAsync("Step 2 of 4");
        await Expect(Progress()).ToContainTextAsync("1 of 5 answered");
        await Expect(Page.GetByLabel("What is your notice period?")).ToHaveCountAsync(0, new() { Timeout = 1000 });
        await ExpectNoAccessibilityViolationsAsync();
        // A page checks its own questions before it moves on.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Expect(Page.Locator("#error-summary")).ToContainTextAsync("Answer this question.");
        await RightToWork().GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Your experience");
        await Expect(Progress()).ToContainTextAsync("Step 3 of 4");
        await Experience().GetByLabel("3 to 5").CheckAsync();
        await Page.GetByLabel("What is your notice period?").FillAsync("A month");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Review and send");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Eligibility" })).ToContainTextAsync("Yes");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Your experience" })).ToContainTextAsync("A month");
        await ExpectNoAccessibilityViolationsAsync();

        // Change goes to that page, and its primary goes straight back to the review.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Change Your experience" }).ClickAsync();
        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Your experience");
        await Page.GetByLabel("What is your notice period?").FillAsync("Two months");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Back to review" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Your experience" })).ToContainTextAsync("Two months");

        await Page.GetByLabel("No, I did not use AI tools").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");

        await using var db = await TenantDbAsync(organization.Id);
        var application = await db.Applications.SingleAsync(Ct);
        application.Answers.Should().ContainEquivalentOf(new Domain.Applications.ApplicationAnswer("notice_period", "Two months"));
        var observation = await db.SubmissionObservations.SingleAsync(Ct);
        observation.PageTimes.Select(p => p.Page).Should().BeEquivalentTo(["details", "page-1", "page-2", "review"]);
        observation.ResumedFromDraft.Should().BeFalse();
    }

    [Fact]
    public async Task A_candidate_saves_the_form_follows_the_emailed_link_and_finishes_it()
    {
        var (organization, job) = await OrganizationWithSectionedJobAsync();
        var email = ApiSession.NewEmail("resume");
        await WithoutJavaScriptAsync();

        await GoToAsync("careers", $"/{organization.Slug}/jobs/{job.Slug}/apply");
        await FillDetailsAsync(email);
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await RightToWork().GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and finish later" }).ClickAsync();

        await Expect(Page.GetByText($"Saved. We emailed a link to {email} to finish by")).ToBeVisibleAsync();
        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Eligibility");
        await ExpectNoAccessibilityViolationsAsync();
        var message = (await Fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct)).Single();
        var link = DraftLink().Match(message.Text);
        link.Success.Should().BeTrue($"the email carries the link: {message.Text}");

        // Another day, another browser: the link fills the form in, on the details step, with the notice first.
        await WithoutJavaScriptAsync();
        await GoToAsync("careers", $"/{organization.Slug}/jobs/{job.Slug}/apply?draft={link.Groups["token"].Value}");
        await Expect(Page.GetByText("Welcome back. Your saved answers are filled in.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "How we keep this fair" })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Email")).ToHaveValueAsync(email.ToLowerInvariant());
        await ExpectNoAccessibilityViolationsAsync();
        // Files are not kept: the resume is attached again.
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await Expect(RightToWork().GetByLabel("Yes")).ToBeCheckedAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Experience().GetByLabel("6 to 9").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();
        await Page.GetByLabel("No, I did not use AI tools").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");

        await using var db = await TenantDbAsync(organization.Id);
        (await db.ApplicationDrafts.CountAsync(Ct)).Should().Be(0, "sending the application deletes its draft");
        var observation = await db.SubmissionObservations.SingleAsync(Ct);
        observation.ResumedFromDraft.Should().BeTrue();
        observation.CarriedOverQuestionIds.Should().Equal("work_authorization");
    }

    private ILocator Progress() => Page.Locator("[data-page-progress]");

    private ILocator RightToWork() => Page.GetByRole(AriaRole.Group, new() { Name = "Do you have the right to work in the country this job is in?" });

    private ILocator Experience() => Page.GetByRole(AriaRole.Group, new() { Name = "How many years have you worked as a software engineer?" });

    private async Task FillDetailsAsync(string email)
    {
        await Page.GetByLabel("First name").FillAsync("Casey");
        await Page.GetByLabel("Last name").FillAsync("Morgan");
        await Page.GetByLabel("Email").FillAsync(email);
    }

    [GeneratedRegex(@"https?://\S+/apply\?draft=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex DraftLink();

    private async Task<ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await Fixture.CreateDbContextAsync(tenant, Ct);
    }

    // "Software engineer" version 1's questions, the resume and the right to work in one section and the rest in another.
    private async Task<(Organization Organization, Job Job)> OrganizationWithSectionedJobAsync()
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
            var questions = job.Workflow.Questionnaire.Questions;
            var first = new[] { "resume", "work_authorization" };
            job.ReviseWorkflow(
                job.Workflow with
                {
                    Questionnaire = job.Workflow.Questionnaire with
                    {
                        Sections =
                        [
                            new QuestionSection("eligibility", "Eligibility", [.. questions.Select(q => q.Id).Where(first.Contains)]),
                            new QuestionSection("experience", "Your experience", [.. questions.Select(q => q.Id).Where(id => !first.Contains(id))]),
                        ],
                    },
                },
                DateTimeOffset.UtcNow);
            job.Publish(DateTimeOffset.UtcNow);
            return [job];
        }, Ct);
        return (organization, job);
    }
}
