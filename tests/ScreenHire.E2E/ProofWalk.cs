using System.Text;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Walks every dashboard route of a <see cref="ProofScene"/> in the browser and collects what fails, so one run of a proof
/// test reports every page at once instead of stopping at the first. Each derived test chooses the context (dark theme, or a
/// phone with a touch screen) and which checks matter; the walk does the rest.
/// </summary>
public abstract partial class ProofWalk(E2EFixture fixture) : BrowserTest(fixture)
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected ProofScene Scene { get; private set; } = null!;

    protected List<string> Problems { get; } = [];

    protected async Task PrepareAsync()
    {
        Scene = await ProofScene.GetAsync(Fixture, Ct);
    }

    protected string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    /// <summary>
    /// Signs a new person in through Dev SSO in the browser, runs <paramref name="check"/> on the onboarding page they land
    /// on, then sets up an organization with nothing in it there, for the empty states of the lists. Ends on the Jobs page.
    /// </summary>
    protected async Task EmptyOrganizationAsync(Func<string, Task> check)
    {
        var email = ApiSession.NewEmail("e2e-empty");
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Set up your organization");
        await SettleAsync();
        await check("/onboarding");

        await Page.GetByLabel("Organization name").FillAsync("Harbour Lane Studio");
        await Page.GetByLabel("Careers page address").FillAsync($"test-{Guid.NewGuid():N}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create organization" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    /// <summary>
    /// The pages that list records, which an empty organization shows as empty states, and the pages a new Owner meets in the
    /// first hour (New job, Billing on the trial, Branding with nothing set), which only an organization this new shows.
    /// </summary>
    protected static IReadOnlyList<string> EmptyRoutes() =>
    [
        "/",
        "/jobs/new",
        "/settings/billing",
        "/settings/branding",
        "/settings/careers-site",
        "/candidates",
        "/tasks",
        "/reports",
        "/templates",
        "/verification",
        "/verification/activity",
        "/verification/usage",
        "/settings/team",
        "/settings/api-keys",
        "/settings/webhooks",
        "/settings/slack",
        "/settings/data-requests",
    ];

    /// <summary>Signs in through Dev SSO as the scene's Owner and waits for the Jobs page.</summary>
    protected async Task SignInAsync()
    {
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(Scene.OwnerEmail);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    /// <summary>Every dashboard route that has a page, with the records of the scene filled in.</summary>
    protected IReadOnlyList<string> Routes() =>
    [
        "/",
        "/candidates",
        "/tasks",
        "/reports",
        "/jobs/new",
        $"/jobs/{Scene.JobId}",
        $"/jobs/{Scene.JobId}/edit",
        $"/jobs/{Scene.JobId}/workflow",
        $"/applications/{Scene.BotApplicationId}",
        $"/applications/{Scene.GenuineApplicationId}",
        $"/candidates/{Scene.BotCandidateId}",
        "/templates",
        "/templates/new",
        $"/templates/{Scene.TemplateId}",
        "/templates/emails/new",
        $"/templates/emails/{Scene.EmailTemplateId}",
        $"/templates/scorecards/{Scene.ScorecardTemplateId}",
        "/verification",
        "/verification/activity",
        "/verification/checks",
        "/verification/policy",
        "/verification/ai-model",
        "/verification/notifications",
        "/verification/usage",
        "/settings",
        "/settings/organization",
        "/settings/careers-site",
        "/settings/branding",
        "/settings/team",
        "/settings/roles",
        "/settings/roles/compare",
        "/settings/roles/viewer",
        $"/settings/roles/{Scene.RoleId}",
        "/settings/sending-domain",
        "/settings/data-requests",
        "/settings/billing",
        "/settings/api-keys",
        "/settings/webhooks",
        "/settings/webhooks/new",
        $"/settings/webhooks/{Scene.EndpointId}",
        $"/settings/webhooks/{Scene.EndpointId}/edit",
        "/settings/mcp-server",
        "/settings/slack",
        "/account/passkeys",
    ];

    /// <summary>
    /// Opens <paramref name="path"/>, waits for it to load and runs <paramref name="check"/> on it and on each of its tabs.
    /// A page that does not load is a problem of its own, and the walk goes on.
    /// </summary>
    protected async Task WalkAsync(string path, string mode, Func<string, Task> check)
    {
        try
        {
            await Page.GotoAsync(DashboardUrl(path));
            await SettleAsync();
            await check(path);

            var tabs = Page.Locator("main [role=tablist]").First.GetByRole(AriaRole.Tab);
            var count = await tabs.CountAsync();
            for (var index = 0; index < count; index++)
            {
                var tab = Page.Locator("main [role=tablist]").First.GetByRole(AriaRole.Tab).Nth(index);
                var name = Regex.Replace(await tab.InnerTextAsync(), @"\s+", " ").Trim();
                await tab.ClickAsync();
                await SettleAsync();
                await check($"{path} [{name}]");
            }
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            Problems.Add($"{path} ({mode}): the walk could not finish: {FirstLine(exception)}");
        }
    }

    /// <summary>
    /// Opens what the routes do not show on their own and runs <paramref name="check"/> on each: the command palette, the
    /// shortcuts dialog, the notifications bell, the Add candidate sheet, the menu and dialog that close a job, the file
    /// viewer and, on a phone, the navigation sheet. An overlay that cannot be opened is a problem of its own.
    /// </summary>
    protected async Task WalkOverlaysAsync(string mode, Func<string, Task> check)
    {
        async Task Step(string name, Func<Task> open, Func<Task> close)
        {
            try
            {
                await open();
                await check(name);
                await close();
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException or Xunit.Sdk.XunitException)
            {
                Problems.Add($"{name} ({mode}): could not be opened and checked: {FirstLine(exception)}");
                await Page.Keyboard.PressAsync("Escape");
            }
        }

        Task Escape() => Page.Keyboard.PressAsync("Escape");

        await Page.GotoAsync(DashboardUrl("/candidates"));
        await SettleAsync();

        await Step(
            "overlay: command palette",
            async () =>
            {
                await Page.Keyboard.PressAsync("Control+k");
                await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Command palette" })).ToBeVisibleAsync();
            },
            Escape);

        // A phone has no keyboard, so it has no shortcuts and no button to list them.
        if (Page.ViewportSize is not { Width: < 768 })
        {
            await Step(
                "overlay: keyboard shortcuts",
                async () =>
                {
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Keyboard shortcuts" }).ClickAsync();
                    await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Keyboard shortcuts" })).ToBeVisibleAsync();
                },
                Escape);
        }

        await Step(
            "overlay: notifications",
            async () =>
            {
                await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Notifications") }).ClickAsync();
                await Expect(Page.Locator("[data-slot=popover-content], [role=dialog]").First).ToBeVisibleAsync();
            },
            Escape);

        await Step(
            "overlay: add candidate sheet",
            async () =>
            {
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add candidate" }).First.ClickAsync();
                await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Add candidate" })).ToBeVisibleAsync();
            },
            Escape);

        await Step(
            "overlay: close job dialog",
            async () =>
            {
                await Page.GotoAsync(DashboardUrl($"/jobs/{Scene.JobId}"));
                await SettleAsync();
                await Page.Locator("[data-slot='header-band']").GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
                await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Close & remove everywhere" }).ClickAsync();
                await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Close this job and remove it everywhere?" })).ToBeVisibleAsync();
            },
            Escape);

        await Step(
            "overlay: file viewer",
            async () =>
            {
                await Page.GotoAsync(DashboardUrl($"/applications/{Scene.BotApplicationId}?tab=files"));
                await SettleAsync();
                await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new("^Files") }).ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = $"View {Scene.BotResumeFileName}" }).ClickAsync();
                await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = Scene.BotResumeFileName })).ToBeVisibleAsync();
                await Expect(Page.Locator("iframe").First).ToBeVisibleAsync();
            },
            Escape);

        if (Page.ViewportSize is { Width: < 768 })
        {
            await Step(
                "overlay: navigation sheet",
                async () =>
                {
                    await Page.GotoAsync(DashboardUrl("/"));
                    await SettleAsync();
                    await Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation" }).ClickAsync();
                    await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Navigation" })).ToBeVisibleAsync();
                },
                Escape);
        }
    }

    /// <summary>Waits for the page's heading and for its skeletons to be gone, so every panel has loaded.</summary>
    protected async Task SettleAsync()
    {
        await Page.Locator("h1").First.WaitForAsync(new() { Timeout = 20_000 });
        try
        {
            await Page.WaitForFunctionAsync(
                "() => !document.querySelector('main [data-slot=skeleton], main [aria-busy=true]')", null, new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            // A panel that stays a skeleton is reported by the axe and screenshot of the page that follows.
        }

        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    protected async Task CollectAxeAsync(string name)
    {
        // A tooltip fades out after the control that opened it loses the pointer; axe reads it mid-fade at a colour no one sees.
        try
        {
            await Page.WaitForFunctionAsync("() => !document.querySelector('[data-slot=tooltip-content]')", null, new() { Timeout = 3_000 });
        }
        catch (TimeoutException)
        {
            // A tooltip that stays is a real element, and axe judges it.
        }

        var problems = await AccessibilityProblemsAsync();
        if (problems is not null)
        {
            Problems.Add($"{name}: axe{problems}");
        }
    }

    protected async Task CollectOverflowAsync(string name)
    {
        // The stable scrollbar gutter (S8) is in scrollWidth and not in clientWidth: only a positive number is overflow.
        var overflow = await Page.EvaluateAsync<int>("() => Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth)");
        if (overflow > 0)
        {
            Problems.Add($"{name}: the page scrolls sideways by {overflow}px");
        }
    }

    /// <summary>
    /// A text field's edge is at least 3:1 against what surrounds it (WCAG 1.4.11). Measured on the page, with the colours
    /// the browser drew, so a token, a dark variant or a component that sets its own edge all count.
    /// </summary>
    protected async Task CollectFieldEdgesAsync(string name)
    {
        foreach (var weak in await Page.EvaluateAsync<string[]>(FieldEdgesScript))
        {
            Problems.Add($"{name}: field edge {weak}");
        }
    }

    protected async Task CollectClippedAsync(string name)
    {
        foreach (var clipped in await Page.EvaluateAsync<string[]>(ClippedScript))
        {
            Problems.Add($"{name}: clipped {clipped}");
        }
    }

    protected async Task CollectSmallTargetsAsync(string name)
    {
        // Chromium sometimes reports a fine pointer again after the page changes under a touch screen, which would measure
        // the desktop sizes. A reload brings the touch screen back, and the tab is in the address, so the page returns as it was.
        if (!await Page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: pointer was not coarse, reloaded");
            await Page.ReloadAsync();
            await SettleAsync();
        }

        foreach (var target in await Page.EvaluateAsync<string[]>(SmallTargetsScript))
        {
            Problems.Add($"{name}: touch target {target}");
        }
    }

    protected async Task<string> ScreenshotAsync(string mode, string name)
    {
        var slug = SlugPattern().Replace(name.Trim('/'), "-").Trim('-');
        var path = await SaveScreenshotAsync(Page, $"proof-{mode}-{(slug.Length == 0 ? "jobs" : slug)}");
        // A full-page screenshot resizes the window to the page and back, and on a touch screen that can drop the emulated
        // touch. Setting the size again puts it back, so the next page is measured as it was.
        var size = Page.ViewportSize!;
        await Page.SetViewportSizeAsync(size.Width, size.Height);
        return path;
    }

    protected void ExpectNoProblems(string what)
    {
        var report = new StringBuilder();
        foreach (var problem in Problems.Distinct())
        {
            report.AppendLine().Append("- ").Append(problem);
        }

        Problems.Distinct().Should().BeEmpty("{0} failed on these pages:{1}", what, report.ToString());
    }

    private static string FirstLine(Exception exception) => exception.Message.Split('\n')[0].TrimEnd('\r');

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex SlugPattern();

    // The colour a field's own border draws against the colour behind the field, both as the browser painted them. A
    // canvas turns any CSS colour (a hex, an rgb with alpha, color-mix) into red, green, blue and alpha, which is composited
    // over the nearest opaque background above. A colour the canvas cannot read is reported, never taken as black. A field
    // with no border of its own takes the border of the wrapper that hugs it (an input group, a search label); a card or
    // panel around it is not its edge.
    private const string FieldEdgesScript = """
        () => {
          const canvas = document.createElement('canvas');
          canvas.width = canvas.height = 1;
          const context = canvas.getContext('2d', { willReadFrequently: true });
          const found = [];
          const rgba = (css) => {
            context.clearRect(0, 0, 1, 1);
            // A colour the canvas does not parse leaves the previous fill in place, so an unlikely one shows it.
            context.fillStyle = '#010203';
            context.fillStyle = css;
            if (context.fillStyle === '#010203' && !/^(#010203|rgb\(1, 2, 3\))$/.test(css)) found.push(`colour "${css}" could not be read`);
            context.fillRect(0, 0, 1, 1);
            const [r, g, b, a] = context.getImageData(0, 0, 1, 1).data;
            return [r, g, b, a / 255];
          };
          const over = (top, bottom) => top.slice(0, 3).map((c, i) => c * top[3] + bottom[i] * (1 - top[3])).concat(1);
          const backdrop = (el) => {
            const layers = [];
            for (let node = el; node; node = node.parentElement) {
              const color = rgba(getComputedStyle(node).backgroundColor);
              if (color[3] > 0) layers.push(color);
              if (color[3] === 1) break;
            }
            return layers.reduceRight((below, layer) => over(layer, below), [255, 255, 255, 1]);
          };
          const luminance = ([r, g, b]) => {
            const channel = (v) => { const x = v / 255; return x <= 0.03928 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4; };
            return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
          };
          const ratio = (a, b) => { const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x); return (hi + 0.05) / (lo + 0.05); };
          const selector = 'input:not([type=checkbox]):not([type=radio]):not([type=hidden]):not([type=file]):not([type=range]):not([type=submit]):not([type=button]):not([type=color]), textarea, select';
          for (const el of document.querySelectorAll(selector)) {
            if (el.disabled || el.closest('[aria-hidden=true], [inert], .sr-only')) continue;
            const rect = el.getBoundingClientRect();
            if (rect.width <= 1 || rect.height <= 1 || getComputedStyle(el).visibility === 'hidden') continue;
            let edge = el;
            while (edge && edge !== document.body && parseFloat(getComputedStyle(edge).borderTopWidth) < 1) edge = edge.parentElement;
            const label = (el.getAttribute('aria-label') || el.getAttribute('placeholder') || el.id || el.name || el.tagName.toLowerCase()).slice(0, 30);
            // A wrapper's edge counts only when it hugs the field: an addon row may add height, a card's header and body add more.
            if (!edge || edge === document.body || edge.getBoundingClientRect().height > rect.height + 48) { found.push(`<${el.tagName.toLowerCase()}> "${label}" has no edge`); continue; }
            const style = getComputedStyle(edge);
            const line = over(rgba(style.borderTopColor), backdrop(edge.parentElement));
            const around = backdrop(edge.parentElement);
            const value = ratio(line, around);
            if (value < 2.99) found.push(`<${el.tagName.toLowerCase()}> "${label}" is ${value.toFixed(2)}:1`);
          }
          return [...new Set(found)].slice(0, 20);
        }
        """;

    // Elements that reach past the window with no scroll box around them, and text cut off by its own box without an
    // ellipsis (design system F1, F5).
    private const string ClippedScript = """
        () => {
          const found = [];
          const width = document.documentElement.clientWidth;
          const describe = (el) => {
            const label = (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 40);
            return `<${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}${el.dataset.slot ? ' data-slot=' + el.dataset.slot : ''}> "${label}"`;
          };
          for (const el of document.body.querySelectorAll('main *, header *, [role=dialog] *')) {
            const style = getComputedStyle(el);
            if (style.display === 'none' || style.visibility === 'hidden' || style.position === 'fixed') continue;
            const rect = el.getBoundingClientRect();
            if (rect.width <= 1 || rect.height <= 1) continue;
            if (rect.right > width + 1 && rect.left < width) {
              let parent = el.parentElement;
              let boxed = false;
              while (parent && parent !== document.body) {
                const parentStyle = getComputedStyle(parent);
                if (/(auto|scroll|hidden|clip)/.test(parentStyle.overflowX) && parent.getBoundingClientRect().right <= width + 1) { boxed = true; break; }
                parent = parent.parentElement;
              }
              if (!boxed) found.push(`${describe(el)} reaches ${Math.round(rect.right)}px in a ${width}px window`);
            }
            if (/(hidden|clip)/.test(style.overflowX) && el.children.length === 0 && el.clientWidth > 0
                && el.scrollWidth > el.clientWidth + 1 && style.textOverflow !== 'ellipsis' && !el.closest('.sr-only')) {
              found.push(`${describe(el)} is cut off (${el.scrollWidth}px of text in ${el.clientWidth}px)`);
            }
          }
          return [...new Set(found)].slice(0, 12);
        }
        """;

    // Targets under 44px on a touch screen (design system F4). A control may be smaller to the eye when its hit area is
    // 44px: it passes when its label is that size, or when points 21px either side of its centre (just inside a 44px square
    // around it) still land on it. Inline
    // links in a sentence are exempt (WCAG 2.5.8).
    private const string SmallTargetsScript = """
        () => {
          if (!matchMedia('(pointer: coarse)').matches) return ['(pointer: coarse) does not match, so this page was not measured as a phone'];
          const selector = 'a[href], button, input:not([type=hidden]), select, textarea, summary, [role=button], [role=tab], [role=menuitem], [role=menuitemradio], [role=menuitemcheckbox], [role=checkbox], [role=switch], [role=radio], [role=option]';
          const found = [];
          const big = (rect) => rect.width >= 43.5 && rect.height >= 43.5;
          // Under an open dialog or sheet the page behind is inert to a touch: only the overlay's own controls count.
          const overlay = document.querySelector('[role=dialog][data-state=open]');
          for (const el of (overlay ?? document).querySelectorAll(selector)) {
            if (el.disabled || el.getAttribute('aria-disabled') === 'true') continue;
            const style = getComputedStyle(el);
            if (style.visibility === 'hidden' || style.display === 'none' || el.closest('[aria-hidden=true], [inert], .sr-only')) continue;
            let rect = el.getBoundingClientRect();
            if (rect.width <= 1 || rect.height <= 1 || rect.right <= 0 || rect.left >= innerWidth) continue;
            if (big(rect)) continue;
            if (el.tagName === 'A' && style.display === 'inline'
                && [...el.parentElement.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim().length > 0)) continue;
            const labels = [...(el.labels ?? []), el.closest('label')].filter(Boolean);
            if (labels.some((label) => big(label.getBoundingClientRect()))) continue;
            // A hit area drawn by the control's own ::after (a link stretched over its row or card, a checkbox's 44px square).
            const after = getComputedStyle(el, '::after');
            if (after.content !== 'none' && after.position === 'absolute' && parseFloat(after.width) >= 43.5 && parseFloat(after.height) >= 43.5) continue;
            el.scrollIntoView({ block: 'center', inline: 'center' });
            rect = el.getBoundingClientRect();
            const xs = rect.width < 43.5 ? [rect.left + rect.width / 2 - 21, rect.left + rect.width / 2 + 21] : [rect.left + rect.width / 2];
            const ys = rect.height < 43.5 ? [rect.top + rect.height / 2 - 21, rect.top + rect.height / 2 + 21] : [rect.top + rect.height / 2];
            let stolen = null;
            for (const x of xs) for (const y of ys) {
              const hit = document.elementFromPoint(x, y);
              if (!stolen && !(hit && (el.contains(hit) || labels.some((label) => label.contains(hit))))) {
                stolen = hit ? `<${hit.tagName.toLowerCase()}${hit.dataset.slot ? ' data-slot=' + hit.dataset.slot : ''}> "${(hit.getAttribute('aria-label') || hit.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 25)}"` : 'nothing';
              }
            }
            if (!stolen) continue;
            const label = (el.getAttribute('aria-label') || el.textContent || el.getAttribute('placeholder') || '').replace(/\s+/g, ' ').trim().slice(0, 40);
            found.push(`<${el.tagName.toLowerCase()}${el.getAttribute('role') ? ' role=' + el.getAttribute('role') : ''}> "${label}" is ${Math.round(rect.width)}x${Math.round(rect.height)}px; its 44px square reaches ${stolen}; ::after is ${after.content} ${after.position} ${after.width}x${after.height}`);
          }
          return [...new Set(found)].slice(0, 25);
        }
        """;
}
