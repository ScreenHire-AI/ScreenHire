using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.E2E;

/// <summary>
/// The product pictures on the marketing site (apps/marketing/src/assets/product), taken from Brightwater Studio and its
/// demo people on a stack of its own (<see cref="MarketingDemoFixture"/>). They are golden files: with
/// <c>UPDATE_MARKETING_SCREENSHOTS=1</c> the test rewrites every image, and without it the test still opens every screen,
/// takes every picture into <c>TestResults/marketing-screenshots</c> and checks that each committed image exists and is
/// under the size budget, so CI keeps the captures working. Run it after changing a screen the marketing site shows:
/// <c>UPDATE_MARKETING_SCREENSHOTS=1 dotnet test --project tests/ScreenHire.E2E --filter-class "*MarketingScreenshotsTests"</c>.
/// </summary>
/// <remarks>
/// The same picture every run: a fixed viewport at twice the pixels, reduced motion, the fonts loaded, a pinned locale and
/// time zone, no hover, focus ring, toast or caret in the frame. Each screen has its full picture (<c>name.png</c>) and a
/// picture at phone width (<c>name-phone.png</c>): the same screen laid out for a phone, for a page to show where the full
/// screen would be unreadable. The verification panels leave out their reason codes, which the public site does not show.
/// Times in the pictures are relative to the run.
/// </remarks>
[Collection<MarketingScreenshotsRun>]
public sealed partial class MarketingScreenshotsTests(MarketingDemoFixture fixture) : BrowserTest(fixture), IClassFixture<MarketingDemoFixture>
{
    private const string UpdateVariable = "UPDATE_MARKETING_SCREENSHOTS";

    private const string Organization = "brightwater";

    private const string OwnerSubject = "brightwater-owner";

    private const int MaxBytes = 600 * 1024;

    // A laptop-sized window. At twice the pixels a busy screen stays under the size budget. The pipeline and the candidates
    // list are wider: the board shows its Offer column and the table its Last activity column only from about 1600 px.
    private const int Width = 1440;
    private const int Height = 900;
    private const int WideWidth = 1600;
    private const int PipelineWidth = 1920; // all six columns of the board, the closed stages' column included

    // Phone width: the pictures a page shows on a phone are the screens laid out for one, not a crop of the wide screen,
    // whose text would be cut mid-word.
    private const int PhoneWidth = 430;
    private const int PhoneHeight = 900;

    // A set image tile names its size ("330 by 70 pixels"); the empty tiles' hints say "at least 32 pixels" and stay out.
    private static readonly Regex UploadedImageSize = new(@"^\d+ by \d+ pixels$");

    private static readonly string[] ReadScope = ["Read"];

    private static readonly string[] JobPublished = ["job.published"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_marketing_screen_opens_and_each_committed_image_exists_within_its_budget()
    {
        var update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";
        var directory = update ? ProductDirectory() : Path.Combine(AppContext.BaseDirectory, "TestResults", "marketing-screenshots");
        Directory.CreateDirectory(directory);
        var written = new List<string>();

        void Save(string name, byte[] png)
        {
            // What is committed is held to the budget below. A fresh capture is held to it only when it is about to be committed,
            // because Chromium's text rendering differs between Windows and the Linux runners by a few percent.
            if (update)
            {
                png.Length.Should().BeLessThan(MaxBytes, "{0}.png must stay under 600 KB; shrink the window", name);
            }

            File.WriteAllBytes(Path.Combine(directory, $"{name}.png"), png);
            written.Add(name);
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}.png {png.Length / 1024} KB");
        }

        using var avery = new ApiSession(Fixture);
        await avery.SignInWithDevSsoAsync(OwnerSubject, Ct);
        await SignInAsync();
        var state = await Page.Context.StorageStateAsync();

        var people = await ReadAsync(await avery.Client.GetAsync("/v1/applications", Ct), HttpStatusCode.OK);
        JsonElement Person(string name) => people.EnumerateArray().Single(a => a.GetProperty("candidateName").GetString() == name);
        Guid IdOf(string name) => Person(name).GetProperty("id").GetGuid();
        var jobId = Person("Joel Mensah").GetProperty("jobId").GetGuid();
        var joel = IdOf("Joel Mensah");

        // Every applicant on the job has been through all eight analyzers before the first picture, so no chip is pending
        // and no panel is half full.
        var organizationId = await OrganizationIdAsync();
        foreach (var person in people.EnumerateArray().Where(a => a.GetProperty("jobId").GetGuid() == jobId))
        {
            await SettledAsync(organizationId, person.GetProperty("id").GetGuid());
        }

        // The pipeline: people across the stages, flagged and clear alike. Moving someone is a recruiter's choice; no check
        // moves or rejects anyone (CLAUDE.md non-negotiable 2).
        await MoveAsync(avery, IdOf("Robin Ashby"), "interview");
        await MoveAsync(avery, IdOf("Sam Okafor"), "screening");
        await MoveAsync(avery, IdOf("Alex Reed"), "screening");

        await JoelsHistoryAsync(avery, joel);

        await using var light = await CaptureContextAsync(state, ColorScheme.Light);
        await using var dark = await CaptureContextAsync(state, ColorScheme.Dark);

        // The studio's brand and a connected setup are in place first, so every public page and the editor show them.
        await BrandAsync(light);
        await ConnectIntegrationsAsync(avery, light);
        var templateUrl = await TemplateUrlAsync(light);
        var portalLink = await PortalLinkAsync(light);
        // The booking page is taken from a second interview offer, left open: the first is booked, for the activity.
        var bookingLink = await InterviewLinkAsync(OtherEmail) + "?tz=Europe%2FLondon";

        // Where the phone pictures stop: between elements, never through the middle of one.
        static ILocator Stage4(IPage page) => page.GetByRole(AriaRole.Group, new() { Name = "Stage 4:" });
        static ILocator Entry(IPage page, string text) => page.Locator("main ol[aria-label] > li").Filter(new() { HasText = text });
        static ILocator Reason(IPage page, string text) => page.Locator("[data-assessment=authenticity] li[data-check]").Filter(new() { HasText = text });

        var screens = new List<Screen>
        {
            // 1. Two applications to the same job, each panel on its own. The flagged one ends its phone picture after the speed
            // reason, whose counter-evidence is the farm's passed bot challenge.
            new("verification-clear", DashboardUrl($"/applications/{IdOf("Robin Ashby")}?tab=verification"), "Robin Ashby",
                page => PrepareVerificationAsync(page, null, [], counterEvidence: false), page => page.Locator("[data-assessment=authenticity]")),
            new("verification-flagged", DashboardUrl($"/applications/{IdOf("Morgan Hale")}?tab=verification"), "Morgan Hale",
                page => PrepareVerificationAsync(page, "ID_DUPLICATE_IN_ORG", ["NET_DATACENTER_IP", "ID_DUPLICATE_IN_ORG", "AUTO_SUBMIT_VELOCITY"], counterEvidence: true),
                page => page.Locator("[data-assessment=authenticity]"),
                Phone: new(Height: 3600, From: page => page.Locator("[data-assessment=authenticity]"), To: page => Reason(page, "Completed faster than a person could"))),

            // 2. The job's pipeline, light and dark. On a phone, one stage and its cards.
            new("pipeline", DashboardUrl($"/jobs/{jobId}"), "Senior .NET Engineer", PreparePipelineAsync, Width: PipelineWidth,
                Phone: new(Only: page => page.Locator("[data-stage-column]").First)),
            new("pipeline-dark", DashboardUrl($"/jobs/{jobId}"), "Senior .NET Engineer", PreparePipelineAsync, Width: PipelineWidth, Scheme: ColorScheme.Dark,
                Phone: new(Only: page => page.Locator("[data-stage-column]").First)),

            // 3. An application's page: its activity, with the reply and the booked interview. The full picture ends on the last entry;
            // the phone picture runs from the reply to the first note.
            new("candidate-activity", DashboardUrl($"/applications/{joel}?tab=activity"), "Joel Mensah", PrepareActivityAsync, Height: 2400,
                WideTo: page => page.Locator("main ol[aria-label] > li").Last,
                Phone: new(Height: 2600, From: page => Entry(page, "Candidate replied"), To: page => Entry(page, "Good detail on the billing ledger"))),

            // 4. The candidates list with stage and last activity.
            new("candidates", DashboardUrl("/candidates"), "Candidates", PrepareNoPendingChipsAsync, Width: WideWidth),

            // 5. The candidate's own page, from the link in the email they ask for.
            new("candidate-portal", portalLink, "Your applications", PreparePortalAsync, Public: true),

            // 6. A workflow's stages and automations, in the pipeline view (the graph's phone layout is read-only and too small to read).
            // On a phone, the stages down to the fourth, with their automations.
            new("workflow", DashboardUrl($"/jobs/{jobId}/workflow"), "workflow",
                page => Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow automations" })).ToBeVisibleAsync(), Height: 1480,
                Phone: new(Height: 1800, Margin: 10, From: page => PanelTitled(page, "Stages"), To: Stage4)),

            // 7. The candidate's interview booking page, before they pick a time.
            new("interview-booking", bookingLink, "Pick a time for your interview", _ => Task.CompletedTask, Public: true),

            // 8. The Integrations directory.
            new("integrations", DashboardUrl("/settings/integrations"), "Integrations",
                page => Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Directory" })).ToBeVisibleAsync()),

            // 9. A hiring template with its own stage names, and the branding editor. On a phone, the template from its title down to
            // the fourth stage, and the editor's colours, font and logo.
            new("template-stages", templateUrl, "Brightwater engineering", _ => Task.CompletedTask, Height: 1300, WideTo: Stage4, WideMargin: 4,
                Phone: new(Height: 1800, To: Stage4)),
            new("branding", DashboardUrl("/settings/branding"), "Branding", page => Expect(page.GetByText(UploadedImageSize)).ToBeVisibleAsync(Slow),
                Phone: new(Height: 2800, Margin: 10, From: page => PanelTitled(page, "Colours").First, To: page => PanelTitled(page, "Images"))),

            // 10. Reports: the headline cards and the trend. On a phone the cards and the trend, with the table of open jobs between them out.
            new("reports", DashboardUrl("/reports"), "Reports", _ => Task.CompletedTask, Height: 1400, WideTo: page => PanelTitled(page, "Applications and hires by week"),
                Phone: new(
                    Height: 3000,
                    Margin: 10,
                    Prepare: page => PanelTitled(page, "Open jobs").EvaluateAsync("panel => { panel.style.display = 'none'; }"),
                    From: page => page.GetByText("Median time to hire").Locator("xpath=ancestor::div[contains(@class,'rounded-panel')][1]/.."),
                    To: page => PanelTitled(page, "Applications and hires by week"))),

            // 11. A branded careers page.
            new("careers", CareersUrl(""), "Open roles at", _ => Task.CompletedTask, Public: true),
        };
        foreach (var screen in screens)
        {
            await CaptureAsync(screen, screen.Scheme == ColorScheme.Dark ? dark : light, Save);
        }

        // Every screen has its full picture and its phone picture, and what is committed is there and within budget.
        var expected = screens.SelectMany(screen => new[] { screen.Name, $"{screen.Name}-phone" }).Order().ToList();
        written.Order().Should().Equal(expected, "every screen takes its picture and its phone picture");
        foreach (var name in expected)
        {
            var committed = new FileInfo(Path.Combine(ProductDirectory(), $"{name}.png"));
            committed.Exists.Should().BeTrue(
                "{0}.png is committed; run {1}=1 dotnet test --project tests/ScreenHire.E2E --filter-class \"*MarketingScreenshotsTests\"", name, UpdateVariable);
            committed.Length.Should().BeLessThan(MaxBytes, "{0}.png must stay under 600 KB", name);
        }
    }

    // One screen: where it is, the heading that says it has loaded, what to do before the picture, and the part of the page
    // to take (the whole window when there is none). The width and height are the window's for the full picture; WideTo ends
    // the full picture at the bottom of an element, after a window tall enough to hold it.
    private sealed record Screen(
        string Name,
        string Url,
        string Heading,
        Func<IPage, Task> Prepare,
        Func<IPage, ILocator>? Target = null,
        bool Public = false,
        int Width = MarketingScreenshotsTests.Width,
        int Height = MarketingScreenshotsTests.Height,
        ColorScheme Scheme = ColorScheme.Light,
        Func<IPage, ILocator>? WideTo = null,
        Phone? Phone = null,
        int WideMargin = 24);

    // The phone picture of a screen when the window as it opens would cut it. Only takes one element alone. Otherwise the picture
    // runs from the top of From (the top of the page when there is none) to the bottom of To, across the window, in a window tall
    // enough to hold it, so it ends between elements. Margin adds room around a panel for its shadow; a list entry has its own.
    private sealed record Phone(
        int Height = MarketingScreenshotsTests.PhoneHeight,
        Func<IPage, Task>? Prepare = null,
        Func<IPage, ILocator>? From = null,
        Func<IPage, ILocator>? To = null,
        Func<IPage, ILocator>? Only = null,
        int Margin = 0);

    // Each screen twice: in the laptop window, and at phone width, where the page lays itself out for a phone.
    private static async Task CaptureAsync(Screen screen, IBrowserContext context, Action<string, byte[]> save)
    {
        foreach (var phone in new[] { false, true })
        {
            var layout = phone ? screen.Phone : null;
            var page = await context.NewPageAsync();
            await page.SetViewportSizeAsync(phone ? PhoneWidth : screen.Width, phone ? layout?.Height ?? PhoneHeight : screen.Height);
            var response = await page.GotoAsync(screen.Url);
            response!.Ok.Should().BeTrue("GET {0} returned HTTP {1}", screen.Name, response.Status);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(screen.Heading);
            await Expect(page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);
            await screen.Prepare(page);
            if (layout?.Prepare is { } prepare)
            {
                await prepare(page);
            }

            if (phone)
            {
                await HideBottomBarAsync(page);
            }

            await HideScrollbarsAsync(page, screen.Public);
            await ReadyAsync(page);
            var png = phone ? await PhonePictureAsync(page, screen, layout) : await WidePictureAsync(page, screen);
            save(phone ? $"{screen.Name}-phone" : screen.Name, png);
            await page.CloseAsync();
        }
    }

    private static async Task<byte[]> WidePictureAsync(IPage page, Screen screen)
    {
        if (screen.WideTo?.Invoke(page) is { } to)
        {
            var box = await BoxAsync(to);
            return await page.ScreenshotAsync(Clipped(0, 0, page.ViewportSize!.Width, Math.Min(box.Y + box.Height + screen.WideMargin, page.ViewportSize.Height)));
        }

        if (screen.Target?.Invoke(page) is { } target)
        {
            await Expect(target).ToBeVisibleAsync();
            return await target.ScreenshotAsync(Options());
        }

        return await page.ScreenshotAsync(PageOptions());
    }

    private static async Task<byte[]> PhonePictureAsync(IPage page, Screen screen, Phone? layout)
    {
        if (layout?.Only?.Invoke(page) is { } only)
        {
            await Expect(only).ToBeVisibleAsync();
            return await only.ScreenshotAsync(Options());
        }

        if (layout?.To?.Invoke(page) is { } to)
        {
            var top = layout.From?.Invoke(page) is { } from ? Math.Max((await BoxAsync(from)).Y - layout.Margin, 0) : 0;
            var box = await BoxAsync(to);
            var end = Math.Min(box.Y + box.Height + layout.Margin, page.ViewportSize!.Height);
            return await page.ScreenshotAsync(Clipped(0, top, page.ViewportSize.Width, end - top));
        }

        if (screen.Target?.Invoke(page) is { } target)
        {
            await Expect(target).ToBeVisibleAsync();
            return await TopOfAsync(page, target);
        }

        return await page.ScreenshotAsync(PageOptions());
    }

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator element)
    {
        await Expect(element).ToBeVisibleAsync();
        return await element.BoundingBoxAsync() ?? throw new InvalidOperationException("The element has no box.");
    }

    private static PageScreenshotOptions Clipped(double x, double y, double width, double height) =>
        new() { Type = ScreenshotType.Png, Animations = ScreenshotAnimations.Disabled, Caret = ScreenshotCaret.Hide, Clip = new() { X = (float)x, Y = (float)y, Width = (float)width, Height = (float)height } };

    // The dashboard keeps room for a scrollbar on the right (scrollbar-gutter), which the screenshot draws as an empty strip
    // beside its header. The pictures show the page without one, and the check says so: no gutter and no sideways scroll. The
    // careers pages' strict content security policy has no inline style, and they keep no gutter.
    private static async Task HideScrollbarsAsync(IPage page, bool isPublic)
    {
        if (!isPublic)
        {
            await page.AddStyleTagAsync(new() { Content = "html, body, * { scrollbar-width: none !important; scrollbar-gutter: auto !important; }" });
        }

        var measured = await page.EvaluateAsync<double[]>(
            "() => [innerWidth - document.documentElement.clientWidth, document.documentElement.scrollWidth - document.documentElement.clientWidth]");
        measured[0].Should().Be(0, "no strip is reserved for a scrollbar at the right of {0}", page.Url);
        measured[1].Should().BeLessThanOrEqualTo(0, "{0} does not scroll sideways", page.Url);
    }

    private static async Task PrepareVerificationAsync(IPage page, string? signal, string[] expand, bool counterEvidence)
    {
        var panel = page.Locator("[data-assessment=authenticity]");
        await Expect(panel.Locator("[data-summary]")).ToBeVisibleAsync(Slow);
        if (signal is not null)
        {
            await Expect(panel.Locator($"[data-signal='{signal}']")).ToBeVisibleAsync(Slow);
        }

        // The public site shows the plain evidence, not the support code under each reason.
        await page.AddStyleTagAsync(new() { Content = "[data-signal] p:has(> span.font-mono) { display: none !important; }" });
        foreach (var code in expand)
        {
            await panel.Locator($"[data-signal='{code}'] summary").ClickAsync();
            await Expect(panel.Locator($"[data-signal='{code}'] details")).ToHaveAttributeAsync("open", "");
        }

        // "Source: configured ranges" is how the local stack finds networks; a real deployment shows nothing like it.
        await panel.EvaluateAsync(
            """
            root => {
              for (const element of root.querySelectorAll('*')) {
                if (element.children.length === 0 && element.textContent.trim() === 'Source') element.parentElement.style.display = 'none';
              }
            }
            """);
        (await panel.InnerTextAsync()).Should().NotContain("Code ", "reason codes stay out of the picture");
        if (counterEvidence)
        {
            (await panel.InnerTextAsync()).Should().Contain("Counter-evidence", "the flagged panel shows the other side of its speed reason");
        }
    }

    private static async Task PreparePipelineAsync(IPage page)
    {
        await HideAsync(page, "Fix on Job postings"); // the demo's job-board notice is about the demo, not the pipeline
        await page.GetByRole(AriaRole.Button, new() { Name = "Board", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Joel Mensah" }).First).ToBeVisibleAsync();
        await Expect(page.Locator("[data-verification-chip][data-level='pending']")).ToHaveCountAsync(0);
    }

    private static async Task PrepareActivityAsync(IPage page)
    {
        await Expect(page.GetByText("Candidate replied").First).ToBeVisibleAsync(Slow);
        await Expect(page.GetByText(new Regex("[Ii]nterview (booked|scheduled)")).First).ToBeVisibleAsync(Slow);
    }

    private static Task PrepareNoPendingChipsAsync(IPage page) =>
        Expect(page.Locator("[data-verification-chip][data-level='pending']")).ToHaveCountAsync(0);

    private static async Task PreparePortalAsync(IPage page) =>
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Where your application is" }).GetByRole(AriaRole.Listitem)).ToHaveCountAsync(4);

    // The address of the template that has its own stage names, from the Templates page's link.
    private async Task<string> TemplateUrlAsync(IBrowserContext context)
    {
        var page = await OpenDashboardAsync(context, "/templates", "Templates");
        var href = await page.GetByRole(AriaRole.Link, new() { Name = "Brightwater engineering", Exact = true }).GetAttributeAsync("href");
        await page.CloseAsync();
        return DashboardUrl(href!);
    }

    // The link in the email the candidate asks for on their own page.
    private async Task<string> PortalLinkAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(CareersUrl("/my-applications"));
        await page.GetByLabel("Email address").FillAsync(JoelsEmail);
        await page.GetByRole(AriaRole.Button, new() { Name = "Email me a link" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Check your email");
        await page.CloseAsync();
        return await PortalLinkAsync();
    }

    private static readonly LocatorAssertionsToBeVisibleOptions Slow = new() { Timeout = 60_000 };

    private const string JoelsEmail = "joel.mensah@example.org";

    private const string OtherEmail = "sam.okafor@example.org";

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    private string CareersUrl(string path) => new Uri(Fixture.GetEndpoint("careers"), $"/{Organization}{path}").ToString();

    private async Task SignInAsync()
    {
        await Page.GotoAsync(DashboardUrl("/sign-in"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.Locator($"form:has(input[value='{OwnerSubject}']) button").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private Task<IBrowserContext> CaptureContextAsync(string storageState, ColorScheme scheme) =>
        Fixture.Browser.NewContextAsync(new()
        {
            StorageState = storageState,
            ViewportSize = new() { Width = Width, Height = Height },
            DeviceScaleFactor = 2,
            ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = scheme,
            Locale = "en-US",
            TimezoneId = "Europe/London",
        });

    // A dashboard page with its heading shown and nothing still loading.
    private async Task<IPage> OpenDashboardAsync(IBrowserContext context, string pathAndQuery, string heading)
    {
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(DashboardUrl(pathAndQuery));
        response!.Ok.Should().BeTrue("GET {0} returned HTTP {1}", pathAndQuery, response.Status);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(heading);
        await Expect(page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);
        return page;
    }

    // The top of a tall target at phone width: as much of it as the window shows.
    private static async Task<byte[]> TopOfAsync(IPage page, ILocator target)
    {
        await target.EvaluateAsync("element => element.scrollIntoView({ block: 'start' })");
        await ReadyAsync(page);
        var box = await target.BoundingBoxAsync() ?? throw new InvalidOperationException("The target has no box.");
        var left = Math.Max(box.X, 0);
        var top = Math.Max(box.Y, 0);
        var clip = new Clip
        {
            X = left,
            Y = top,
            Width = Math.Min(box.Width, page.ViewportSize!.Width - left),
            Height = Math.Min(box.Height, page.ViewportSize.Height - top),
        };
        return await page.ScreenshotAsync(new() { Type = ScreenshotType.Png, Animations = ScreenshotAnimations.Disabled, Caret = ScreenshotCaret.Hide, Clip = clip });
    }

    private static ILocator PanelTitled(IPage page, string title) =>
        page.Locator("section[data-slot=panel]").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true }) });

    // On a phone the page's action bar (Move to stage) sticks to the bottom of the window and covers the last line.
    private static async Task HideBottomBarAsync(IPage page) =>
        await page.EvaluateAsync(
            """
            () => {
              for (const element of document.querySelectorAll('body *')) {
                const box = element.getBoundingClientRect();
                if (getComputedStyle(element).position === 'fixed' && box.top > innerHeight / 2 && box.bottom >= innerHeight - 4) element.style.visibility = 'hidden';
              }
            }
            """);

    // Takes a notice out of the frame: the row that holds the link with this text.
    private static async Task HideAsync(IPage page, string linkText) =>
        await page.EvaluateAsync(
            """
            text => {
              const link = [...document.querySelectorAll('main a')].find(a => a.textContent.includes(text));
              let notice = link;
              while (notice?.parentElement && notice.parentElement.textContent.length < 250) notice = notice.parentElement;
              if (notice) notice.style.display = 'none';
            }
            """,
            linkText);

    // Slack connected, an API key and a webhook endpoint, so the directory shows a working setup. Slack goes through the
    // sandbox workspace, as the dashboard's own Add to Slack does.
    private async Task ConnectIntegrationsAsync(ApiSession avery, IBrowserContext context)
    {
        var page = await OpenDashboardAsync(context, "/settings/slack", "Slack");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to Slack" }).ClickAsync();
        await Expect(page.Locator("[data-slot=header-band]")).ToContainTextAsync("Connected");
        await page.CloseAsync();
        using (var key = await avery.PostAsync("/v1/organization/api-keys", new { name = "Payroll sync", scopes = ReadScope }, Ct))
        {
            key.StatusCode.Should().Be(HttpStatusCode.Created, await key.Content.ReadAsStringAsync(Ct));
        }

        using var endpoint = await avery.PostAsync(
            "/v1/webhooks", new { url = "https://hooks.example.com/in", events = JobPublished, description = "Applicant sync" }, Ct);
        endpoint.StatusCode.Should().Be(HttpStatusCode.Created, await endpoint.Content.ReadAsStringAsync(Ct));
    }

    private static PageScreenshotOptions PageOptions() => new() { Type = ScreenshotType.Png, Animations = ScreenshotAnimations.Disabled, Caret = ScreenshotCaret.Hide };

    private static LocatorScreenshotOptions Options() => new() { Type = ScreenshotType.Png, Animations = ScreenshotAnimations.Disabled, Caret = ScreenshotCaret.Hide };

    // A clean frame: fonts loaded, nothing focused or hovered, no toast, no transition in flight.
    private static async Task ReadyAsync(IPage page)
    {
        await Expect(page.Locator("[data-sonner-toast]")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await page.EvaluateAsync(
            """
            async () => {
              await document.fonts.ready;
              if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
              for (let frame = 0; frame < 2; frame++) await new Promise(resolve => requestAnimationFrame(() => resolve()));
              await Promise.all(document.getAnimations().filter(a => a instanceof CSSTransition).map(a => a.finished.catch(() => undefined)));
            }
            """);
        await page.Mouse.MoveAsync(0, 0);
    }

    private static async Task MoveAsync(ApiSession session, Guid applicationId, string stage)
    {
        using var response = await session.PostAsync($"/v1/applications/{applicationId}/move", new { toStageId = stage }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    // Joel Mensah applied, was moved twice, got automatic and personal emails and two notes (the demo's). Now he answers an
    // email and the team offers him an interview.
    private async Task JoelsHistoryAsync(ApiSession avery, Guid applicationId)
    {
        var emails = await Fixture.Mailpit.WaitForMessagesAsync(JoelsEmail, 3, Ct);
        var asked = emails.First(m => m.Subject.StartsWith("Interview for", StringComparison.Ordinal));
        asked.ReplyTo.Should().NotBeNull("every email about an application carries its reply address");
        using (var delivered = await avery.PostAsync(
                   "/v1/dev/inbound-email",
                   new
                   {
                       to = asked.ReplyTo!.Address,
                       from = JoelsEmail,
                       fromName = "Joel Mensah",
                       subject = $"Re: {asked.Subject}",
                       text = "Thanks Avery. Thursday afternoon suits me, and I will send the take-home back before then.",
                   },
                   Ct))
        {
            delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync(Ct));
        }

        await avery.ConnectSandboxCalendarAsync(Ct);
        await OfferInterviewAsync(avery, applicationId);
        var link = await InterviewLinkAsync(JoelsEmail);
        await using var candidate = await CaptureContextAsync(await Page.Context.StorageStateAsync(), ColorScheme.Light);
        var booking = await candidate.NewPageAsync();
        await booking.GotoAsync(link + "?tz=Europe%2FLondon");
        await booking.GetByRole(AriaRole.Radio).Nth(2).CheckAsync();
        await booking.GetByRole(AriaRole.Button, new() { Name = "Book this time" }).ClickAsync();
        await Expect(booking.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Your interview is booked");
        await booking.CloseAsync();

        // A second offer, left open, for the booking page's own picture.
        var other = (await ReadAsync(await avery.Client.GetAsync("/v1/applications", Ct), HttpStatusCode.OK))
            .EnumerateArray().Single(a => a.GetProperty("candidateName").GetString() == "Sam Okafor").GetProperty("id").GetGuid();
        await OfferInterviewAsync(avery, other);
    }

    private static async Task OfferInterviewAsync(ApiSession avery, Guid applicationId)
    {
        var owner = (await avery.CurrentUserAsync(Ct))!.Value.GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;
        await ReadAsync(
            await avery.PostAsync(
                $"/v1/applications/{applicationId}/interviews",
                new { interviewerUserIds = new[] { owner }, durationMinutes = 45, windowStart = now, windowEnd = now.AddDays(14), timeZone = "Europe/London" },
                Ct),
            HttpStatusCode.Created);
    }

    // The link in a "Pick a time" email to the candidate the interview was offered to.
    private Task<string> InterviewLinkAsync(string address) =>
        LinkInEmailAsync(address, subject => subject.Contains("Pick a time", StringComparison.Ordinal), InterviewLink());

    private Task<string> PortalLinkAsync() =>
        LinkInEmailAsync(JoelsEmail, subject => subject.StartsWith("Your link to your applications at ", StringComparison.Ordinal), PortalLink());

    // Waits up to a minute for an email to this address whose subject matches, and returns the first link in it.
    private async Task<string> LinkInEmailAsync(string address, Func<string, bool> subject, Regex link)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var match = (await Fixture.Mailpit.MessagesAsync(address, Ct))
                .Where(m => subject(m.Subject))
                .Select(m => link.Match(m.Text))
                .FirstOrDefault(m => m.Success);
            if (match is not null)
            {
                return match.Value;
            }

            await Task.Delay(250, Ct);
        }

        throw new TimeoutException($"The email with the link never reached {address}.");
    }

    // Brightwater's brand as a studio would set it: a second colour, a heading font and a logo, saved in the editor.
    private async Task BrandAsync(IBrowserContext context)
    {
        var page = await OpenDashboardAsync(context, "/settings/branding", "Branding");
        await page.GetByLabel("Second colour", new() { Exact = true }).FillAsync("fbf6ee");
        await page.GetByRole(AriaRole.Radio, new() { Name = "Fraunces" }).CheckAsync();
        await page.Locator("input#logo").SetInputFilesAsync(new FilePayload { Name = "brightwater-logo.png", MimeType = "image/png", Buffer = await LogoAsync() });
        await Expect(page.GetByText(UploadedImageSize)).ToBeVisibleAsync(Slow);
        var save = page.GetByRole(AriaRole.Button, new() { Name = "Save branding" });
        await Expect(save).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await save.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved" })).ToHaveTextAsync("Saved.");
        await page.CloseAsync();
    }

    // A made-up wordmark for the fictional studio, drawn by the browser so no image file is committed for it.
    private async Task<byte[]> LogoAsync()
    {
        await using var context = await Fixture.Browser.NewContextAsync(new() { DeviceScaleFactor = 2, ViewportSize = new() { Width = 400, Height = 120 } });
        var page = await context.NewPageAsync();
        await page.SetContentAsync(
            """
            <style>
              body { margin: 0; background: transparent; }
              #logo { display: inline-flex; align-items: center; gap: 12px; padding: 8px; font: 700 40px/1 system-ui, sans-serif; color: #1f4e5a; }
              #mark { width: 44px; height: 44px; border-radius: 50%; background: #1f4e5a; position: relative; overflow: hidden; }
              #mark::after { content: ''; position: absolute; left: -8px; right: -8px; bottom: -14px; height: 28px; border-radius: 50%; background: #fbf6ee; }
            </style>
            <div id="logo"><span id="mark"></span><span>Brightwater</span></div>
            """);
        return await page.Locator("#logo").ScreenshotAsync(new() { OmitBackground = true, Type = ScreenshotType.Png });
    }

    private async Task<Guid> OrganizationIdAsync()
    {
        var reader = new TenantContext();
        reader.SetOrganization(Guid.NewGuid()); // organizations are public rows, readable as any tenant
        await using var db = await Fixture.CreateDbContextAsync(reader, Ct);
        return await db.Organizations.Where(o => o.Slug == Organization).Select(o => o.Id).SingleAsync(Ct);
    }

    // Waits until every analyzer has run on the application, content forensics included (VerificationRegistration).
    private async Task SettledAsync(Guid organizationId, Guid applicationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        for (var attempt = 0; attempt < 1200; attempt++)
        {
            await using var db = await Fixture.CreateDbContextAsync(tenant, Ct);
            if (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == applicationId).Select(r => r.Analyzer).Distinct().CountAsync(Ct) >= 8)
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException($"Expected every verification analyzer to run on application {applicationId}.");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private static string ProductDirectory() =>
        Path.Combine(E2EFixture.RepositoryRoot(), "apps", "marketing", "src", "assets", "product");

    [GeneratedRegex(@"https?://\S+/interviews/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InterviewLink();

    [GeneratedRegex(@"https?://\S+/my-applications/\S+")]
    private static partial Regex PortalLink();
}
