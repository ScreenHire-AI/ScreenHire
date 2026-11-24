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
/// The apply form a page at a time (apply-flow.md §7), with JavaScript off: one named section a page with its progress
/// ("40% done"), Change on the review and back to it, and saving to finish later through the link Mailpit receives; and,
/// without sections, four questions a page, the focus on each page's first question, Enter moving on where it is safe,
/// the check line beside a field that was left wrong, and the finish page. With JavaScript on, the progress bar on a
/// phone: it stays at the top without covering a focused field, and it grows from step to step, or stays still with
/// reduced motion. Each test applies to a job of its own organization, copied from the "Software engineer" system
/// template and given two sections or more questions.
/// </summary>
public sealed partial class QuestionnairePagesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    // The questions of the first section: the resume and the right to work.
    private static readonly string[] FirstSection = ["resume", "work_authorization"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_answers_one_section_a_page_and_changes_an_answer_from_the_review()
    {
        var (organization, job) = await OrganizationWithSectionedJobAsync();
        var email = ApiSession.NewEmail("pages");
        await WithoutJavaScriptAsync();

        await GoToAsync("careers", $"/{organization.Slug}/jobs/{job.Slug}/apply");
        await Expect(Progress()).ToHaveTextAsync("20% done");
        await FillDetailsAsync(email);
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Eligibility");
        await Expect(Progress()).ToHaveTextAsync("40% done");
        await Expect(Page.GetByLabel("What is your notice period?")).ToHaveCountAsync(0, new() { Timeout = 1000 });
        await ExpectNoAccessibilityViolationsAsync();
        // A page checks its own questions before it moves on.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Expect(Page.Locator("#error-summary")).ToContainTextAsync("Answer this question.");
        await RightToWork().GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Your experience");
        await Expect(Progress()).ToHaveTextAsync("60% done");
        await Experience().GetByLabel("3 to 5").CheckAsync();
        await Page.GetByLabel("What is your notice period?").FillAsync("A month");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Review and send");
        await Expect(Progress()).ToHaveTextAsync("Ready to send");
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
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "How we review applications" })).ToBeVisibleAsync();
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

    [Fact]
    public async Task Without_sections_a_candidate_answers_four_questions_a_page_and_moves_on_with_Enter()
    {
        var (organization, job) = await OrganizationWithLongJobAsync();
        var email = ApiSession.NewEmail("short");
        await WithoutJavaScriptAsync();

        // The job page says how long applying takes and why applications are checked; the full notice waits on the form.
        await GoToAsync("careers", $"/{organization.Slug}/jobs/{job.Slug}");
        await Expect(Page.Locator("[data-apply-minutes]")).ToHaveTextAsync("Takes about 5 minutes");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "How we review applications" })).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-checks-line]")).ToContainTextAsync("We look out for bots and borrowed identities, so genuine applicants get a fair look.");
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" }).ClickAsync();

        // Details, three pages of questions (4, 4 and 1) and the review: five steps, and the sent form after them. The
        // details step opens on the notice, not on a field.
        await Expect(Progress()).ToHaveTextAsync("17% done");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "How we review applications" })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("First name")).Not.ToBeFocusedAsync();

        // A field left wrong says so beside it, with no script and before anything is posted.
        var emailCheck = Page.Locator(".field").Filter(new() { Has = Page.GetByLabel("Email") }).Locator(".field-check");
        await Page.GetByLabel("Email").FillAsync("casey.example.com");
        await Page.GetByLabel("Phone").FocusAsync();
        await Expect(emailCheck).ToBeVisibleAsync();
        await Expect(emailCheck).ToHaveTextAsync("Enter an email address like name@example.com.");
        await ExpectNoAccessibilityViolationsAsync();
        await FillDetailsAsync(email);
        await Page.GetByLabel("Phone").FocusAsync();
        await Expect(emailCheck).ToBeHiddenAsync();
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        // The first page opens with the focus on its first question, and Enter in a line moves on.
        await Expect(Progress()).ToHaveTextAsync("33% done");
        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Questions");
        await Expect(RightToWork().GetByLabel("Yes")).ToBeFocusedAsync();
        await Expect(Page.GetByLabel("Which team would you like to join?")).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await RightToWork().GetByLabel("Yes").CheckAsync();
        await Experience().GetByLabel("3 to 5").CheckAsync();
        await Page.GetByLabel("What is your notice period?").FillAsync("A month");
        await Page.GetByLabel("What is your notice period?").PressAsync("Enter");

        // In a few sentences Enter starts a new line and stays on the page.
        await Expect(Progress()).ToHaveTextAsync("50% done");
        var team = Page.GetByLabel("Which team would you like to join?");
        await Expect(team).ToBeFocusedAsync();
        await team.FillAsync("Platform");
        var now = Page.GetByLabel("What are you working on now?");
        await now.FillAsync("A payments service");
        await now.PressAsync("Enter");
        await Expect(now).ToHaveValueAsync("A payments service\n");
        await Expect(Progress()).ToHaveTextAsync("50% done");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        await Expect(Progress()).ToHaveTextAsync("67% done");
        await Expect(Page.GetByLabel("Anything else we should know?")).ToBeFocusedAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        // The review never reads 100% done, and Enter there posts the review back to itself, with the choice kept: it
        // neither sends nor opens a Change.
        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Review and send");
        await Expect(Progress()).ToHaveTextAsync("Ready to send");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Questions, page 2" })).ToContainTextAsync("Platform");
        await Page.GetByLabel("No, I did not use AI tools").CheckAsync();
        // Each render stamps the time the step was shown into the state, so a new value is the review drawn again.
        var state = Page.Locator("input[name=state]");
        var shown = await state.InputValueAsync();
        await Page.GetByLabel("No, I did not use AI tools").PressAsync("Enter");
        await Expect(state).Not.ToHaveValueAsync(shown);
        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Review and send");
        await Expect(Page.GetByLabel("No, I did not use AI tools")).ToBeCheckedAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();

        // A warm finish: what happens next, each led by when, and the candidate's own page.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");
        var next = Page.GetByRole(AriaRole.Region, new() { Name = "What happens next" });
        await Expect(next.Locator("li")).ToHaveTextAsync([new Regex("^Next"), new Regex("^Any time")]);
        await Expect(next.GetByRole(AriaRole.Link, new() { Name = "your page" })).ToHaveAttributeAsync("href", $"/{organization.Slug}/my-applications");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task On_a_phone_the_progress_bar_stays_at_the_top_clear_of_the_focused_field_and_grows_from_step_to_step()
    {
        var (organization, job) = await OrganizationWithSectionedJobAsync();
        var problems = new List<string>();
        Page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
            {
                problems.Add(message.Text);
            }
        };
        await Page.SetViewportSizeAsync(390, 844);
        // What the browser animates as a step opens: the widths the fill's view transition goes between, or nothing when the
        // page opened with no transition. pagereveal comes before the page's first frame.
        await Page.AddInitScriptAsync(
            """
            addEventListener('pagereveal', (event) => {
              window.fillWidths = (event.viewTransition?.ready ?? Promise.resolve()).then(() =>
                document.getAnimations()
                  .filter((animation) => animation.effect.pseudoElement === '::view-transition-group(apply-progress-fill)')
                  .flatMap((animation) => animation.effect.getKeyframes().map((frame) => parseFloat(frame.width))));
            });
            """);

        await GoToAsync("careers", $"/{organization.Slug}/jobs/{job.Slug}/apply");
        await Page.Locator("astro-island:not([ssr])").WaitForAsync(); // hydrated
        var next = Page.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" });
        var row = Page.Locator(".apply-progress");
        var first = Page.GetByLabel("First name");

        // The error summary's link puts its field at the edge the browser scrolls to: the bar covers neither the field nor
        // the label and the error above it.
        await next.ClickAsync();
        await Page.GetByRole(AriaRole.Alert).GetByRole(AriaRole.Link, new() { Name = "Enter your first name." }).ClickAsync();
        await Expect(first).ToBeFocusedAsync();
        var stuck = (await row.BoundingBoxAsync())!;
        stuck.Y.Should().Be(0, "the page scrolled to the field, under the bar");
        (await Page.Locator("label[for='" + await first.GetAttributeAsync("id") + "']").BoundingBoxAsync())!.Y
            .Should().BeGreaterThanOrEqualTo(stuck.Y + stuck.Height, "the label of the field the summary opens is not under the bar");

        await FillDetailsAsync(ApiSession.NewEmail("phone"));
        await Page.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Expect(Page.GetByText("Casey CV.pdf")).ToBeVisibleAsync();

        // The end of the form, then back up through it: every field the focus reaches stays below the bar's edge.
        await next.FocusAsync();
        (await row.BoundingBoxAsync())!.Y.Should().Be(0, "the bar stays at the top of the window");
        var covered = new List<string>();
        for (var stops = 0; stops < 20 && !await first.EvaluateAsync<bool>("field => field === document.activeElement"); stops++)
        {
            await Page.Keyboard.PressAsync("Shift+Tab");
            var bar = (await row.BoundingBoxAsync())!;
            var focused = (await Page.Locator(":focus").BoundingBoxAsync())!;
            if (focused.Y < bar.Y + bar.Height)
            {
                covered.Add($"{await Page.Locator(":focus").EvaluateAsync<string>("e => e.name || e.textContent.trim()")} at {focused.Y}, under a bar that ends at {bar.Y + bar.Height}");
            }
        }

        await Expect(first).ToBeFocusedAsync();
        (await row.BoundingBoxAsync())!.Y.Should().Be(0, "the first field is still below the fold, so the bar was over it on the way");
        covered.Should().BeEmpty("a focused field is never under the bar (WCAG 2.4.11)");
        await ExpectNoAccessibilityViolationsAsync();

        // The fill grows from the last step's width to this one's, and the sheen crosses it once.
        var details = await FillWidthAsync();
        await next.ClickAsync();
        await Expect(Progress()).ToHaveTextAsync("40% done");
        var eligibility = await FillWidthAsync();
        (await FillShareAsync()).Should().BeApproximately(0.4, 0.005);
        var grew = await Page.EvaluateAsync<double[]>("() => window.fillWidths");
        grew.Should().HaveCount(2);
        grew[0].Should().BeApproximately(details, 1);
        grew[1].Should().BeApproximately(eligibility, 1);
        var sheen = Page.Locator(".page-progress-sheen");
        await Expect(sheen).ToHaveCSSAsync("animation-name", "page-progress-sheen");
        await Expect(sheen).ToHaveCSSAsync("animation-iteration-count", "1");

        // Reduced motion: the same form, with nothing moving.
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await RightToWork().GetByLabel("Yes").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Progress()).ToHaveTextAsync("60% done");
        (await FillShareAsync()).Should().BeApproximately(0.6, 0.005);
        (await Page.EvaluateAsync<double[]>("() => window.fillWidths")).Should().BeEmpty();
        await Expect(sheen).ToBeHiddenAsync();
        await Expect(Page.Locator(".page-progress-fill")).ToHaveCSSAsync("transition-duration", "0s");
        await Experience().GetByLabel("3 to 5").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();
        await Expect(Progress()).ToHaveTextAsync("Ready to send");
        (await FillShareAsync()).Should().BeApproximately(1, 0.005);
        await ExpectNoAccessibilityViolationsAsync();
        problems.Should().BeEmpty("the content security policy must not block anything the bar needs");
    }

    private async Task<double> FillWidthAsync() => (await Page.Locator(".page-progress-fill").BoundingBoxAsync())!.Width;

    // The track is what the words beside it leave, so the fill is measured against its own track.
    private async Task<double> FillShareAsync() =>
        await FillWidthAsync() / (await Page.Locator(".page-progress-track").BoundingBoxAsync())!.Width;

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

    // "Software engineer" version 1's four questions after the resume, then five more and no sections: three pages of 4, 4
    // and 1 at the default page size. About 5 minutes: 60 s of details, 10 s a pick, 20 s a line or link, 90 s to write.
    private async Task<(Organization Organization, Job Job)> OrganizationWithLongJobAsync()
    {
        var engineering = await SoftwareEngineerAsync();
        Job job = null!;
        var organization = await Fixture.CreateOrganizationAsync(org =>
        {
            job = TestData.NewJob(org, "engineer", "Platform Engineer");
            job.UseTemplate(engineering);
            job.ReviseWorkflow(
                job.Workflow with
                {
                    Questionnaire = job.Workflow.Questionnaire with
                    {
                        Questions =
                        [
                            .. job.Workflow.Questionnaire.Questions,
                            new Question("team", QuestionType.ShortText, "Which team would you like to join?", Required: true),
                            new Question("now", QuestionType.LongText, "What are you working on now?"),
                            new Question("languages", QuestionType.ShortText, "Which languages do you write?"),
                            new Question("travel", QuestionType.YesNo, "Can you travel twice a year?"),
                            new Question("else", QuestionType.ShortText, "Anything else we should know?"),
                        ],
                    },
                },
                DateTimeOffset.UtcNow);
            job.Publish(DateTimeOffset.UtcNow);
            return [job];
        }, Ct);
        return (organization, job);
    }

    private async Task<HiringTemplateVersion> SoftwareEngineerAsync()
    {
        var reader = new TenantContext();
        reader.SetOrganization(Guid.NewGuid()); // system templates belong to no organization, so any tenant reads them
        await using var db = await Fixture.CreateDbContextAsync(reader, Ct);
        return await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, Ct);
    }

    // "Software engineer" version 1's questions, the resume and the right to work in one section and the rest in another.
    private async Task<(Organization Organization, Job Job)> OrganizationWithSectionedJobAsync()
    {
        var engineering = await SoftwareEngineerAsync();
        Job job = null!;
        var organization = await Fixture.CreateOrganizationAsync(org =>
        {
            job = TestData.NewJob(org, "engineer", "Platform Engineer");
            job.UseTemplate(engineering);
            var questions = job.Workflow.Questionnaire.Questions;
            job.ReviseWorkflow(
                job.Workflow with
                {
                    Questionnaire = job.Workflow.Questionnaire with
                    {
                        Sections =
                        [
                            new QuestionSection("eligibility", "Eligibility", [.. questions.Select(q => q.Id).Where(FirstSection.Contains)]),
                            new QuestionSection("experience", "Your experience", [.. questions.Select(q => q.Id).Where(id => !FirstSection.Contains(id))]),
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
