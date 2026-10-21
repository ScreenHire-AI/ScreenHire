using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScreenHire.Application.Integrity;

namespace ScreenHire.E2E;

/// <summary>
/// The candidate page on the detail template (design system sections 7 and 8) at the sizes F1 names and the review
/// canvas's 820 and 390: main and side columns from a laptop up, one column with Integrity first on a tablet and a
/// phone, and Move to stage in a bar at the bottom on a phone. Signs in as a new person with a new organization.
/// </summary>
public sealed class CandidatePageTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_candidate_page_follows_the_detail_template_at_every_size()
    {
        var applicant = SyntheticApplicants.GenuineHuman;
        var (email, applicationId) = await ApplicationAsync(applicant);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(applicant.FullName);
        var move = Page.GetByRole(AriaRole.Button, new() { Name = $"Move to stage, {applicant.FullName}", Exact = true });
        await Expect(move).ToBeVisibleAsync();
        await Expect(Region("Integrity").GetByRole(AriaRole.Region, new() { Name = "Authenticity" })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Desktop and laptop: two columns. Tablet and phone: one column, Integrity first (design system section 8).
        foreach (var (width, height, name) in new[] { (1920, 1080, "wide"), (1440, 1000, "desktop"), (1024, 900, "laptop"), (820, 1180, "tablet"), (768, 1024, "small-tablet"), (390, 844, "phone"), (360, 780, "small-phone") })
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
            await SettledAsync();

            var integrity = await BoxAsync(Region("Integrity"));
            var resume = await BoxAsync(Region("Resume"));
            var declarations = await BoxAsync(Region("Declarations"));
            var reviewLog = await BoxAsync(Region("Review log"));
            var activity = await BoxAsync(Region("Activity"));
            var action = await BoxAsync(move);
            if (width >= 1024)
            {
                // Integrity heads the side column, level with Resume; Review log and Activity follow it.
                integrity.X.Should().BeGreaterThan(resume.X + resume.Width, $"the side column is right of the main column at {width}px");
                integrity.Y.Should().BeApproximately(resume.Y, 1, "panels in a row share a top edge (S4)");
                integrity.Width.Should().BeApproximately(320, 1);
                reviewLog.X.Should().BeApproximately(integrity.X, 1);
                reviewLog.Y.Should().BeGreaterThan(integrity.Y + integrity.Height);
                activity.Y.Should().BeGreaterThan(reviewLog.Y + reviewLog.Height);
            }
            else
            {
                // One column: Integrity, then the main column's panels, then the rest of the side column.
                resume.Y.Should().BeGreaterThan(integrity.Y + integrity.Height, $"Integrity comes first at {width}px");
                resume.X.Should().BeApproximately(integrity.X, 1);
                resume.Width.Should().BeApproximately(integrity.Width, 1);
                reviewLog.Y.Should().BeGreaterThan(declarations.Y + declarations.Height);
            }

            if (width < 768)
            {
                (action.Y + action.Height).Should().BeGreaterThan(height - 80, "phones keep the primary action in a bar at the bottom");
            }
            else
            {
                action.Y.Should().BeLessThan(Math.Min(integrity.Y, resume.Y), "the primary action is in the header band");
            }

            await ExpectNoSideScrollAsync();
            await ExpectNoAccessibilityViolationsAsync();
            await SaveScreenshotAsync(Page, $"dashboard-candidate-{name}");
        }

        // The bar's menu still moves the candidate.
        await move.ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Screening" }).ClickAsync();
        await Expect(Region("Activity")).ToContainTextAsync("Moved from Applied to Screening by you");
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // The sidebar switches between full, rail and sheet a frame after a resize and then slides; measure once the content
    // has held still for 20 frames.
    private async Task SettledAsync() => await Page.EvaluateAsync(
        """
        () => new Promise((resolve) => {
          let last = '', still = 0;
          const step = () => {
            const box = document.querySelector('[data-slot="header-band"]').getBoundingClientRect();
            const key = `${box.left},${box.width}`;
            still = key === last ? still + 1 : 0;
            last = key;
            if (still >= 20) resolve(); else requestAnimationFrame(step);
          };
          requestAnimationFrame(step);
        })
        """);

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
    {
        await Expect(locator).ToBeVisibleAsync();
        return (await locator.BoundingBoxAsync())!;
    }

    private async Task ExpectNoSideScrollAsync() =>
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page never scrolls sideways (F1)");

    // A new person and organization with one published job, and one applicant through the careers submission path.
    private async Task<(string Email, Guid ApplicationId)> ApplicationAsync(SyntheticApplicant applicant)
    {
        var email = ApiSession.NewEmail("e2e-candidate");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title = "Platform Engineer",
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
            },
            Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct), HttpStatusCode.OK);
        return (email, await Fixture.ApplyAsync(slug, jobId, applicant, Ct));
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

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
