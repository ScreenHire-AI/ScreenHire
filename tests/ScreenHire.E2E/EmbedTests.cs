using System.Net.Http.Json;
using System.Text.Json;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.E2E;

/// <summary>
/// The embeddable careers widget (docs/architecture/careers-widget.md): a company's own page pastes the embed code from
/// Settings, Organization, and its jobs and the whole apply flow show in a frame from the careers origin, branded, sized
/// to their content and cookie-free; the application records the site. A site not on the organization's list cannot
/// frame the careers pages at all.
/// </summary>
/// <remarks>
/// The company's pages are a <see cref="HostPage"/> on this machine: <c>http://partner.localhost:{port}</c> is on the
/// list and <c>http://stranger.localhost:{port}</c> is not. The test stack lets an organization list a site on this
/// machine (<c>ScreenHire:CareersEmbedding:AllowLoopback</c>), and both are secure contexts, as https sites are.
/// </remarks>
public sealed class EmbedTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Organization = "Harbour Lane Studio";

    private static readonly string[] Gb = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_listed_site_embeds_the_jobs_and_the_application_records_it()
    {
        await using var host = await HostPage.StartAsync(Ct);
        var partner = host.Origin("partner");
        var scene = await SceneAsync();
        await SignInAsync(scene.Email);

        // The owner lets the company's site embed the careers page, and takes the embed code from the page.
        await GoToAsync("dashboard", "/settings/careers-site");
        await Page.GetByLabel("New site").FillAsync(partner);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add site" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Sites that can embed the careers page" })).ToContainTextAsync(partner);
        var code = Page.GetByLabel("Embed code");
        await Expect(code).ToContainTextAsync("/embed.js");
        host.Snippet = await code.InnerTextAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Everything the careers origin answers inside the frame, to check that none of it sets a cookie.
        var careersResponses = new List<IResponse>();
        Page.Response += (_, response) =>
        {
            if (response.Url.StartsWith(scene.CareersUrl, StringComparison.Ordinal))
            {
                lock (careersResponses)
                {
                    careersResponses.Add(response);
                }
            }
        };
        await ApplySteps.RecordBeaconsAsync(Page);
        await Page.GotoAsync($"{partner}/careers");

        // The frame shows the board in the organization's branding, as tall as the page inside it.
        var frame = await WidgetFrameAsync();
        await Expect(frame.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync($"Open roles at {Organization}");
        var mark = frame.GetByRole(AriaRole.Banner).Locator("span[aria-hidden='true']").First;
        (await mark.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor")).Should().Be("rgb(31, 78, 90)");
        await Expect(Page.Locator("iframe.screenhire-careers")).ToHaveAttributeAsync("title", $"Jobs at {Organization}");
        var height = await frame.EvaluateAsync<int>("() => Math.ceil(document.documentElement.getBoundingClientRect().height)");
        await Page.WaitForFunctionAsync("h => document.querySelector('iframe.screenhire-careers').style.height === `${h}px`", height);

        // The candidate applies without leaving the company's page.
        await frame.GetByRole(AriaRole.Link, new() { Name = scene.Title }).ClickAsync();
        await frame.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" }).ClickAsync();
        await frame.Locator("astro-island:not([ssr])").WaitForAsync();
        await ApplySteps.ApplyAsync(
            frame, "kai.ward@example.com", javaScript: true,
            $"{scene.CareersUrl}/jobs/{scene.JobSlug}/applied?embed={Uri.EscapeDataString(partner)}", () => Task.CompletedTask);
        Page.Url.Should().Be($"{partner}/careers", "the company's page stayed where it was");
        // The candidate switched assistive technology on, inside the frame as on the careers site: what was counted is
        // dropped, and no beacon is ever sent (CLAUDE.md non-negotiable 4).
        (await ApplySteps.TelemetryBeaconsAsync(frame)).Should().BeEmpty();
        IResponse[] answered;
        lock (careersResponses)
        {
            answered = [.. careersResponses];
        }

        foreach (var response in answered)
        {
            (await response.AllHeadersAsync()).Should().NotContainKey("set-cookie", $"the flow is cookie-free inside the frame ({response.Url})");
        }

        answered.Should().Contain(r => r.Request.Method == "POST", "every step posted inside the frame");

        // The application says where it came from.
        await GoToAsync("dashboard", $"/jobs/{scene.JobId}");
        await Page.GetByRole(AriaRole.Table, new() { Name = $"Candidates for {scene.Title}" })
            .GetByRole(AriaRole.Link, new() { Name = "Casey Morgan" }).ClickAsync();
        await Expect(Page.GetByText($"via partner.localhost:{host.Port}")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_site_not_on_the_list_cannot_frame_the_careers_pages()
    {
        await using var host = await HostPage.StartAsync(Ct);
        var partner = host.Origin("partner");
        var scene = await SceneAsync();
        using (var owner = await OwnerSessionAsync(scene.Email))
        {
            await ReadAsync(await owner.PutAsync("/v1/organization/embedding", new { origins = new[] { partner } }, Ct));
        }

        // Outside the widget the board names every listed site, and runs no script but its data.
        var board = await Page.GotoAsync(scene.CareersUrl);
        (await board!.AllHeadersAsync())["content-security-policy"].Should().Contain($"frame-ancestors 'self' {partner};");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0);

        host.Snippet = $"<script src=\"{scene.CareersUrl}/embed.js\" async></script>";
        var refused = Page.WaitForConsoleMessageAsync(new() { Predicate = message => message.Text.Contains("frame-ancestors", StringComparison.Ordinal) });
        await Page.GotoAsync($"{host.Origin("stranger")}/careers");
        await refused;

        var widget = Page.Locator("iframe.screenhire-careers");
        await Expect((await WidgetFrameAsync()).GetByRole(AriaRole.Heading)).ToHaveCountAsync(0);
        await Expect(widget).ToHaveAttributeAsync("title", "Jobs");
        (await widget.EvaluateAsync<string>("e => e.style.height")).Should().Be("600px");
    }

    private async Task<IFrame> WidgetFrameAsync()
    {
        var element = await Page.Locator("iframe.screenhire-careers").ElementHandleAsync();
        return (await element.ContentFrameAsync())!;
    }

    private sealed record Scene(string Email, string CareersUrl, Guid JobId, string JobSlug, string Title);

    // An owner's organization with a brand colour and a published job from the "Software engineer" template.
    private async Task<Scene> SceneAsync()
    {
        var email = ApiSession.NewEmail("e2e-embed");
        var title = $"Platform Engineer {Guid.NewGuid().ToString("N")[..4]}";
        using var session = await OwnerSessionAsync(email, onboard: true);
        await ReadAsync(await session.PutAsync("/v1/organization/branding", new { brandColor = "#1f4e5a" }, Ct));
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Gb,
                templateVersionId = SystemTemplates.SoftwareEngineerVersion1,
            },
            Ct));
        var jobId = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct));
        var organization = await ReadAsync(await session.Client.GetAsync("/v1/organization", Ct));
        return new Scene(email, organization.GetProperty("careersUrl").GetString()!.TrimEnd('/'), jobId, job.GetProperty("slug").GetString()!, title);
    }

    private async Task<ApiSession> OwnerSessionAsync(string email, bool onboard = false)
    {
        var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        if (onboard)
        {
            await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = Organization, slug = $"test-{Guid.NewGuid():N}" }, Ct));
        }

        return session;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
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
