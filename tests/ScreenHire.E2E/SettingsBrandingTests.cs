using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Branding in Settings, in a real browser: what is saved here (both colours, a font and three images) reaches the
/// careers pages on their own origin and host, the editor fits one screen empty and with every image set, and a field
/// that takes focus is not hidden under the switch row. Each test signs in as a new owner of a new organization.
/// </summary>
public sealed class SettingsBrandingTests(E2EFixture fixture) : SettingsArea(fixture)
{
    [Fact]
    public async Task Branding_saved_in_settings_reaches_the_careers_pages_on_their_own_origin()
    {
        var (owner, slug) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/branding");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Branding");
        await ExpectNoAccessibilityViolationsAsync();

        // A pale brand colour fails as links on the page; the field offers the nearest shade that passes.
        await Page.GetByLabel("Brand colour", new() { Exact = true }).FillAsync("ffd400");
        await Expect(Page.GetByText("Links in #ffd400 are hard to read on the page")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Use #[0-9a-f]{6}$") }).ClickAsync();
        await Expect(Page.GetByText("Links in #ffd400 are hard to read on the page")).ToHaveCountAsync(0);
        await Page.GetByLabel("Second colour", new() { Exact = true }).FillAsync("E9DCC4");
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Fraunces" }).CheckAsync();

        // Each image goes straight to storage, is checked and scanned, and waits for Save. The file inputs are found by id:
        // each tile is a labelled group with the same name as its input.
        await Page.Locator("input#logo").SetInputFilesAsync(new FilePayload { Name = "logo.png", MimeType = "image/png", Buffer = TestPng.Solid(400, 100) });
        await Page.Locator("input#favicon").SetInputFilesAsync(new FilePayload { Name = "icon.png", MimeType = "image/png", Buffer = TestPng.Solid(64, 64) });
        await Page.Locator("input#sharing-image")
            .SetInputFilesAsync(new FilePayload { Name = "share.png", MimeType = "image/png", Buffer = TestPng.Solid(1200, 630) });
        // Each tile names its file and size as soon as it is checked, and Save waits for the worker's malware scan, a step
        // the worker's queue answers.
        var scanned = new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 };
        await Expect(Page.GetByText("400 by 100 pixels")).ToBeVisibleAsync(scanned);
        await Expect(Page.GetByText("1200 by 630 pixels")).ToBeVisibleAsync(scanned);
        await Expect(Page.GetByText("64 by 64 pixels")).ToBeVisibleAsync(scanned);
        var save = Page.GetByRole(AriaRole.Button, new() { Name = "Save branding" });
        await Expect(save).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 30_000 });
        await save.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved" })).ToHaveTextAsync("Saved.");
        await ExpectNoAccessibilityViolationsAsync();

        var response = await OpenAsync("careers", $"/{slug}");
        await ExpectCareersBrandingAsync(response, $"/{slug}");

        // The organization's own host serves the same images, linked without the organization in the path.
        var careers = Fixture.GetEndpoint("careers");
        var tenantHost = await Page.GotoAsync($"{careers.Scheme}://{slug}.localhost:{careers.Port}/");
        await ExpectCareersBrandingAsync(tenantHost!, "");
    }

    // Heading fonts from the Google Fonts catalogue (ADR-0031), against the test stack's sandbox catalogue: the picker draws
    // each name in its own font through the API, and the careers titles load the saved family from the careers origin
    // alone. Hosting it once, the files and the licence are HeadingFontTests' (integration).
    [Fact]
    public async Task A_font_chosen_from_the_catalogue_draws_the_careers_titles_from_the_careers_origin()
    {
        var (owner, slug) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/branding");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Branding");

        await Page.GetByRole(AriaRole.Button, new() { Name = "More fonts" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Heading fonts" });
        await sheet.GetByRole(AriaRole.Radio, new() { Name = "Slab", Exact = true }).ClickAsync();
        await sheet.GetByLabel("Search fonts").FillAsync("zilla");
        var zilla = sheet.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Zilla Slab") });
        await zilla.CheckAsync();
        // The name is drawn in its own face, which came from the API's preview route.
        await Page.WaitForFunctionAsync("() => [...document.fonts].some(f => f.family.includes('zilla-slab') && f.status === 'loaded')");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-branding-more-fonts-1280");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Use Zilla Slab" }).ClickAsync();

        await Expect(sheet).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Zilla Slab") })).ToBeCheckedAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save branding" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved" })).ToHaveTextAsync("Saved.");

        var fonts = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.ResourceType == "font")
            {
                fonts.Add(request.Url);
            }
        };
        await OpenAsync("careers", $"/{slug}");
        var title = Page.GetByRole(AriaRole.Heading, new() { Level = 1 });
        (await title.EvaluateAsync<string>("e => getComputedStyle(e).fontFamily")).Should().StartWith("\"ScreenHire heading zilla-slab\"");
        await Page.WaitForFunctionAsync("() => [...document.fonts].some(f => f.family.includes('zilla-slab') && f.status === 'loaded')");

        var careers = Fixture.GetEndpoint("careers");
        var origin = $"{careers.Scheme}://{careers.Host}:{careers.Port}/";
        fonts.Should().Contain(url => url.Contains("/brand/fonts/zilla-slab/latin-700.woff2"), "the title's weight is fetched");
        fonts.Should().OnlyContain(url => url.StartsWith(origin, StringComparison.Ordinal), "a candidate's browser asks no font host for anything");
    }

    [Fact]
    public async Task The_branding_editor_fits_one_screen_at_1440_by_900_empty_and_with_all_three_images_set()
    {
        var (owner, _) = await OwnerAsync();
        await Page.SetViewportSizeAsync(1440, 900);
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/branding");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Branding");
        await Expect(Page.Locator("main [data-slot=skeleton]")).ToHaveCountAsync(0);

        // Colours and Heading font share a row, and the three images are one row of tiles.
        await ExpectBrandingEditorFitsAsync("with the image tiles empty");

        // A catalogue font joins the six as a choice of its own; the More fonts button sits in the panel's header.
        await Page.GetByRole(AriaRole.Button, new() { Name = "More fonts" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Heading fonts" });
        await sheet.GetByLabel("Search fonts").FillAsync("bebas");
        await sheet.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Bebas Neue") }).CheckAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Use Bebas Neue" }).ClickAsync();
        await Expect(sheet).ToHaveCountAsync(0);
        await ExpectBrandingEditorFitsAsync("with a catalogue font chosen");

        // A set tile is taller than an empty one (a picture, its size, Replace and Remove), and the editor still fits.
        await Page.Locator("input#logo").SetInputFilesAsync(new FilePayload { Name = "logo.png", MimeType = "image/png", Buffer = TestPng.Solid(400, 100) });
        await Page.Locator("input#favicon").SetInputFilesAsync(new FilePayload { Name = "icon.png", MimeType = "image/png", Buffer = TestPng.Solid(64, 64) });
        await Page.Locator("input#sharing-image")
            .SetInputFilesAsync(new FilePayload { Name = "share.png", MimeType = "image/png", Buffer = TestPng.Solid(1200, 630) });
        var scanned = new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 };
        await Expect(Page.GetByText("400 by 100 pixels")).ToBeVisibleAsync(scanned);
        await Expect(Page.GetByText("64 by 64 pixels")).ToBeVisibleAsync(scanned);
        await Expect(Page.GetByText("1200 by 630 pixels")).ToBeVisibleAsync(scanned);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save branding" })).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 30_000 });
        await ExpectBrandingEditorFitsAsync("with a logo, a favicon and a sharing image set");
        await SaveScreenshotAsync(Page, "dashboard-settings-branding-images-set-1440");
    }

    [Fact]
    public async Task A_branding_field_that_takes_focus_under_the_switch_row_is_not_hidden_by_it()
    {
        var (owner, _) = await OwnerAsync();
        await Page.SetViewportSizeAsync(390, 844);
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/branding");
        await Expect(Page.Locator("#brand-color")).ToBeVisibleAsync();

        // The field is on screen but under the row that stays in view, so a browser left to itself would not scroll to it.
        var tops = await Page.EvaluateAsync<double[]>(
            """
            async () => {
              const field = document.querySelector('#brand-color');
              window.scrollBy(0, field.getBoundingClientRect().top - 14);
              await new Promise(requestAnimationFrame);
              field.focus();
              await new Promise(requestAnimationFrame);
              await new Promise(requestAnimationFrame);
              return [field.getBoundingClientRect().top, document.querySelector('[data-edit-preview-row]').getBoundingClientRect().bottom];
            }
            """);

        tops[0].Should().BeGreaterThanOrEqualTo(tops[1], "a focused control stays clear of the row that stays in view (WCAG 2.4.11)");
    }

    // The editor's rows line up and the page does not scroll: the work area is the window, so nothing is cut off.

    private async Task ExpectBrandingEditorFitsAsync(string state)
    {
        async Task<double> TopAsync(ILocator locator) => (await locator.BoundingBoxAsync())!.Y;

        (await TopAsync(Page.GetByRole(AriaRole.Region, new() { Name = "Colours", Exact = true })))
            .Should().BeApproximately(await TopAsync(Page.GetByRole(AriaRole.Region, new() { Name = "Heading font", Exact = true })), 1, "Colours and Heading font share a row {0}", state);
        var tiles = new[] { "Logo", "Favicon", "Sharing image" };
        var tops = new List<double>();
        foreach (var tile in tiles)
        {
            tops.Add(await TopAsync(Page.GetByRole(AriaRole.Group, new() { Name = tile, Exact = true })));
        }

        tops.Should().OnlyContain(top => Math.Abs(top - tops[0]) <= 1, "the three image tiles share a top edge {0}", state);

        var size = await Page.EvaluateAsync<int[]>("() => [document.documentElement.scrollHeight, document.documentElement.clientHeight]");
        var (scroll, client) = (size[0], size[1]);
        scroll.Should().BeLessThanOrEqualTo(client, "the whole editor fits in 900px without scrolling {0}", state);
        (await TopAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Save branding" }))).Should().BeInRange(0, client, "Save is in view {0}", state);
    }

    private async Task ExpectCareersBrandingAsync(IResponse response, string basePath)
    {
        var banner = Page.GetByRole(AriaRole.Banner);
        var logo = banner.GetByRole(AriaRole.Img);
        (await logo.GetAttributeAsync("src")).Should().MatchRegex($"^{Regex.Escape(basePath)}/brand/images/[0-9a-f-]{{36}}\\.png$");
        await Expect(logo).ToHaveAttributeAsync("alt", new Regex(".+"));
        (await logo.EvaluateAsync<int>("e => e.complete ? e.naturalWidth : 0")).Should().Be(400, "the logo loads from the careers origin");
        (await banner.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor")).Should().Be("rgb(233, 220, 196)");
        (await Page.GetByRole(AriaRole.Heading, new() { Level = 1 }).EvaluateAsync<string>("e => getComputedStyle(e).fontFamily"))
            .Should().StartWith("\"Fraunces Variable\"");
        (await Page.Locator("link[rel='icon']").GetAttributeAsync("href")).Should().MatchRegex($"^{Regex.Escape(basePath)}/brand/images/[0-9a-f-]{{36}}\\.png$");
        (await Page.Locator("meta[property='og:image']").GetAttributeAsync("content")).Should().MatchRegex("^https?://.+/brand/images/[0-9a-f-]{36}\\.png$");

        // Everything in the header band reads in its own text colour, the focus ring included.
        var allJobs = banner.GetByRole(AriaRole.Link, new() { Name = "All jobs" });
        await allJobs.FocusAsync();
        (await allJobs.EvaluateAsync<string>("e => getComputedStyle(e).outlineColor")).Should().Be("rgb(35, 31, 26)");

        var apply = (await response.AllHeadersAsync())["content-security-policy"];
        apply.Should().Contain("default-src 'none'").And.NotContain("unsafe-inline");
        (await Page.Locator("[style]").CountAsync()).Should().Be(0, "no inline styles");
    }
}
