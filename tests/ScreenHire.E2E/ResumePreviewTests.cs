using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScreenHire.Application.Integrity;

namespace ScreenHire.E2E;

/// <summary>
/// The resume preview in a real browser (design system section 11): a candidate's PDF is drawn inline from the
/// sandboxed viewer, and nothing in that frame can run script with the dashboard's origin, cookies, DOM or API. Each test
/// signs in as a new person with a new organization.
/// </summary>
public sealed class ResumePreviewTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_resume_is_drawn_inline_and_its_frame_cannot_reach_the_dashboard()
    {
        var applicant = SyntheticApplicants.GenuineHuman;
        var (email, applicationId) = await ApplicationAsync(applicant);
        var console = new List<string>();
        Page.Console += (_, message) => console.Add(message.Text);
        Page.PageError += (_, error) => console.Add(error);

        await Page.SetViewportSizeAsync(1440, 1040);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/applications/{applicationId}"));

        var resume = Page.GetByRole(AriaRole.Region, new() { Name = "Resume" });
        await Expect(resume.GetByRole(AriaRole.Link, new() { Name = $"Download {applicant.ResumeFileName}" })).ToBeVisibleAsync();
        await Expect(resume.GetByText("1 page", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var element = resume.Locator($"iframe[title='Preview of {applicant.ResumeFileName}']");
        (await element.GetAttributeAsync("sandbox")).Should().Be("allow-scripts");
        (await element.BoundingBoxAsync())!.Height.Should().BeGreaterThan(500, "the frame grows to the page it drew");
        var frame = (await (await element.ElementHandleAsync()).ContentFrameAsync())!;

        // The page is drawn, and its text is there for screen readers.
        (await frame.Locator("canvas").CountAsync()).Should().Be(1);
        (await frame.EvaluateAsync<bool>(
            "() => { const c = document.querySelector('canvas'); const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;"
            + " for (let i = 0; i < d.length; i += 4) if (d[i] < 128) return true; return false; }"))
            .Should().BeTrue("the canvas holds dark text, not a blank page");
        var firstWord = applicant.ResumeText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        await Expect(frame.Locator("section[aria-label='Page 1'] p")).ToContainTextAsync(firstWord);
        console.Should().NotContain(line => line.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase),
            "the viewer renders inside its own policy");

        // Script in the frame (the frame's own, or anything a hostile file could get to run) has an opaque origin: no
        // dashboard DOM, no cookies, no API. Script it did not ship with does not run at all.
        var probe = await frame.EvaluateAsync<JsonElement>(
            """
            async () => {
              const attempt = async (action) => { try { return String(await action()); } catch (error) { return error.name; } };
              window.__injected = false;
              const script = document.createElement('script');
              script.textContent = 'window.__injected = true';
              document.head.append(script);
              await new Promise((resolve) => setTimeout(resolve, 100));
              return {
                origin: self.origin,
                parentDocument: await attempt(() => parent.document.title),
                cookie: await attempt(() => document.cookie),
                api: await attempt(() => fetch('/v1/auth/me').then((response) => response.status)),
                storage: await attempt(() => localStorage.length),
                injected: window.__injected,
              };
            }
            """);
        probe.GetProperty("origin").GetString().Should().Be("null");
        probe.GetProperty("parentDocument").GetString().Should().Be("SecurityError");
        probe.GetProperty("cookie").GetString().Should().Be("SecurityError");
        probe.GetProperty("api").GetString().Should().Be("TypeError");
        probe.GetProperty("storage").GetString().Should().Be("SecurityError");
        probe.GetProperty("injected").GetBoolean().Should().BeFalse("the viewer's policy allows only its own script");

        // And the dashboard cannot reach into the frame either: the two do not share an origin.
        (await Page.EvaluateAsync<bool>("() => document.querySelector('iframe[sandbox]').contentDocument === null")).Should().BeTrue();

        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-resume-preview-light");
    }

    // A new person and organization with one published job, and one applicant through the careers submission path,
    // whose resume the worker has finished scanning (NotScanned: no scanner in tests).
    private async Task<(string Email, Guid ApplicationId)> ApplicationAsync(SyntheticApplicant applicant)
    {
        var email = ApiSession.NewEmail("e2e-resume");
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
        var applicationId = await Fixture.ApplyAsync(slug, jobId, applicant, Ct);

        for (var tries = 0; ; tries++)
        {
            var application = await ReadAsync(await session.Client.GetAsync($"/v1/applications/{applicationId}", Ct), HttpStatusCode.OK);
            if (application.GetProperty("files")[0].GetProperty("scanStatus").GetString() != "Pending")
            {
                return (email, applicationId);
            }

            tries.Should().BeLessThan(120, "the worker records each file's scan within a minute");
            await Task.Delay(500, Ct);
        }
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
