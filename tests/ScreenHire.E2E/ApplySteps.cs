using System.Text;

namespace ScreenHire.E2E;

/// <summary>
/// The careers apply journey for a job copied from the "Software engineer" system template, in any frame: the page
/// itself (<see cref="IPage.MainFrame"/>) on the careers site, or the careers widget's frame on another site
/// (<c>EmbedTests</c>). The steps are the same either way, which is the point of the widget.
/// </summary>
internal static class ApplySteps
{
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    /// <summary>
    /// The whole journey from the first step: details and the notice, questions, review, confirmation at
    /// <paramref name="appliedUrl"/>. <paramref name="checkAccessibility"/> runs on every step.
    /// </summary>
    public static async Task ApplyAsync(IFrame frame, string email, bool javaScript, string appliedUrl, Func<Task> checkAccessibility)
    {
        // The notice comes before anything is asked for.
        var notice = frame.GetByRole(AriaRole.Region, new() { Name = "How we keep this fair" });
        await Expect(notice).ToBeVisibleAsync();
        (await notice.BoundingBoxAsync())!.Y.Should().BeLessThan((await frame.GetByLabel("First name").BoundingBoxAsync())!.Y);
        await checkAccessibility();

        // A step sent with nothing in it explains itself.
        await frame.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();
        await Expect(ErrorSummary(frame)).ToContainTextAsync("Enter your first name.");
        await Expect(ErrorSummary(frame)).ToContainTextAsync("Add a file.");
        await checkAccessibility();

        await FillDetailsAsync(frame, email);
        await frame.GetByRole(AriaRole.Switch, new() { Name = "I use dictation, a screen reader or other assistive technology" }).CheckAsync();
        await frame.GetByLabel("Your CV or resume").SetInputFilesAsync(new FilePayload { Name = "Casey CV.pdf", MimeType = "application/pdf", Buffer = Pdf });
        if (javaScript)
        {
            await Expect(frame.GetByText("Uploaded: Casey CV.pdf")).ToBeVisibleAsync();
        }

        await frame.GetByRole(AriaRole.Button, new() { Name = "Continue to questions" }).ClickAsync();

        await Expect(frame.Locator("#step-heading")).ToHaveTextAsync("Questions");
        await frame.GetByRole(AriaRole.Group, new() { Name = "Do you have the right to work in the country this job is in?" })
            .GetByLabel("Yes").CheckAsync();
        await frame.GetByRole(AriaRole.Group, new() { Name = "How many years have you worked as a software engineer?" })
            .GetByLabel("3 to 5").CheckAsync();
        await frame.GetByLabel("A link to code or a project you are proud of").FillAsync("https://example.com/portfolio");
        await checkAccessibility();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        await Expect(frame.Locator("#step-heading")).ToHaveTextAsync("Review and send");
        await Expect(frame.GetByRole(AriaRole.Main)).ToContainTextAsync("Casey CV.pdf");
        await Expect(frame.GetByRole(AriaRole.Main)).ToContainTextAsync("3 to 5");
        // The job wants AI use disclosed: sending without an answer asks for one.
        await frame.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(ErrorSummary(frame)).ToContainTextAsync("Tell us whether you used AI tools for this application.");
        await checkAccessibility();
        await frame.GetByLabel("Yes, I used AI tools").CheckAsync();
        await frame.GetByLabel("If you used AI tools, how?").FillAsync("To check my grammar.");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();

        await frame.WaitForURLAsync(appliedUrl);
        await Expect(frame.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");
        await checkAccessibility();
    }

    public static async Task FillDetailsAsync(IFrame frame, string email)
    {
        await frame.GetByLabel("First name").FillAsync("Casey");
        await frame.GetByLabel("Last name").FillAsync("Morgan");
        await frame.GetByLabel("Email").FillAsync(email);
        await frame.GetByLabel("Phone").FillAsync("+44 20 7946 0000");
    }

    public static ILocator ErrorSummary(IFrame frame) => frame.Locator("#error-summary");
}
