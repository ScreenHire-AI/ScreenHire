using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// The candidate's side on a phone and on a wide screen (design system F1, F4, F5, X1): the board, the job, every step of
/// the apply form with the notice and its errors, the confirmation, the privacy page, the pages that ask for an emailed
/// link, and the page every kind of dead link lands on. At 390 px with a touch screen nothing scrolls sideways, nothing is
/// cut off without an ellipsis, every button and block link is at least 44 px to touch, and axe passes, in light and in
/// dark. The walk collects every failure and asserts once. Pictures land in TestResults/screenshots for the review.
/// </summary>
public sealed class CandidatePhoneTests(E2EFixture fixture) : ProofWalk(fixture)
{
    // The pages that need no signed link: the paths under the organization and what each must show.
    private static readonly (string Path, string Heading)[] StaticPages =
    [
        ("", "Open roles"),
        ("/jobs/engineer", "Platform Engineer"),
        ("/privacy", "What we collect and why"),
        ("/my-applications", "Your applications"),
        ("/data-request", "Ask about your data"),
    ];

    // Links that cannot open anything. Each says so in words and never says whether the employer or the person exists.
    private static readonly string[] DeadLinks =
    [
        "/jobs/no-such-job",
        "/my-applications/not-a-link",
        "/interviews/not-a-link",
        "/questionnaires/not-a-link",
        "/talent-pool/not-a-link",
        "/data-request/not-a-link",
    ];

    [Fact]
    public async Task The_candidate_journey_fits_a_phone_and_is_touchable()
    {
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        await UseTouchScreenAsync();
        await Page.SetViewportSizeAsync(390, 844);
        var step = 0;

        async Task Check(string name)
        {
            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            await CollectClippedAsync(name);
            await CollectSmallTargetsAsync(name);
            _ = await ScreenshotAsync("candidate-phone", name);
        }

        foreach (var (path, heading) in StaticPages)
        {
            await GoToAsync("careers", $"/{organization.Slug}{path}");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(heading);
            await SettleAsync();
            await Check($"/{organization.Slug}{path}".TrimEnd('/'));
        }

        // The notice leads with why applications are checked, never how thorough the checks are (an honest applicant reads
        // this first), and it sits above the first field.
        await GoToAsync("careers", $"/{organization.Slug}/jobs/engineer");
        var notice = Page.GetByRole(AriaRole.Region, new() { Name = "How we review applications" });
        await Expect(notice).ToContainTextAsync("so real applicants are not crowded out");
        await Expect(notice).Not.ToContainTextAsync("thorough");

        foreach (var dead in DeadLinks)
        {
            var response = await OpenAsync("careers", $"/{organization.Slug}{dead}");
            response.Status.Should().BeOneOf([404, 410], $"{dead} opens nothing");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            await SettleAsync();
            await Check($"dead {dead}");
        }

        // Every step of the apply form, with its errors, and the confirmation, with JavaScript on (the upload island).
        await GoToAsync("careers", $"/{organization.Slug}/jobs/engineer/apply");
        await ApplySteps.ApplyAsync(
            Page.MainFrame,
            $"phone.{Guid.NewGuid():N}@example.com",
            javaScript: true,
            new Uri(Fixture.GetEndpoint("careers"), $"/{organization.Slug}/jobs/engineer/applied").ToString(),
            () => Check($"apply step {++step}"));

        // The confirmation says what is true: the checks never reject anyone, and no absolute about software.
        var applied = Page.GetByRole(AriaRole.Region, new() { Name = "What happens next" });
        await Expect(applied).ToContainTextAsync("Our checks for bots and impersonation never reject anyone.");
        await Expect(applied).Not.ToContainTextAsync("No software makes a decision");

        ExpectNoProblems("The candidate pages at a phone width");
    }

    [Fact]
    public async Task The_candidate_pages_hold_up_in_dark_mode_on_a_phone()
    {
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        await UseDarkThemeAsync();
        await Page.SetViewportSizeAsync(390, 844);

        foreach (var (path, _) in StaticPages)
        {
            await GoToAsync("careers", $"/{organization.Slug}{path}");
            await SettleAsync();
            var name = $"dark /{organization.Slug}{path}".TrimEnd('/');
            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            _ = await ScreenshotAsync("candidate-dark", name);
        }

        await GoToAsync("careers", $"/{organization.Slug}/jobs/engineer/apply");
        await SettleAsync();
        await CollectAxeAsync("dark apply");
        await CollectFieldEdgesAsync("dark apply");
        _ = await ScreenshotAsync("candidate-dark", "apply");

        ExpectNoProblems("The candidate pages in dark mode");
    }

    [Fact]
    public async Task The_candidate_pages_fit_a_wide_screen()
    {
        var (organization, _) = await OrganizationWithEngineeringJobAsync();
        await Page.SetViewportSizeAsync(1440, 900);

        foreach (var (path, _) in StaticPages)
        {
            await GoToAsync("careers", $"/{organization.Slug}{path}");
            await SettleAsync();
            var name = $"wide /{organization.Slug}{path}".TrimEnd('/');
            await CollectAxeAsync(name);
            await CollectOverflowAsync(name);
            await CollectFieldEdgesAsync(name);
            await CollectClippedAsync(name);
            _ = await ScreenshotAsync("candidate-wide", name);
        }

        ExpectNoProblems("The candidate pages at a wide screen");
    }

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
