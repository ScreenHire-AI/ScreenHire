using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace ScreenHire.E2E;

/// <summary>
/// Base class for browser tests: a fresh browser context (cookies, storage) per test, a screenshot under
/// <c>TestResults/screenshots</c> when a test fails, and an axe accessibility check.
/// </summary>
public abstract class BrowserTest(E2EFixture fixture) : IAsyncLifetime
{
    // WCAG 2.2 level A and AA (CLAUDE.md, Frontend rules); axe's best-practice rules are advisory, so not enforced here.
    private static readonly List<string> WcagTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    private IBrowserContext? context;

    protected E2EFixture Fixture => fixture;

    protected IPage Page { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        context = await fixture.Browser.NewContextAsync();
        Page = await context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (context is not null)
        {
            try
            {
                if (TestContext.Current.TestState?.Result == TestResult.Failed)
                {
                    await SaveScreenshotAsync();
                }
            }
            finally
            {
                await context.DisposeAsync();
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Opens <paramref name="path"/> on an Aspire resource and expects a successful response.</summary>
    protected async Task GoToAsync(string resource, string path)
    {
        var response = await Page.GotoAsync(new Uri(fixture.GetEndpoint(resource), path).ToString());
        response.Should().NotBeNull();
        response!.Ok.Should().BeTrue("GET {0} on {1} returned HTTP {2}", path, resource, response.Status);
    }

    /// <summary>Runs axe on the current page and fails with a readable list of every WCAG 2.2 AA violation.</summary>
    protected async Task ExpectNoAccessibilityViolationsAsync()
    {
        var result = await Page.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = WcagTags } });

        result.Violations.Should().BeEmpty("axe found WCAG 2.2 AA violations on {0}:{1}", Page.Url, Describe(result.Violations));
    }

    private static string Describe(IEnumerable<AxeResultItem> violations)
    {
        var description = new StringBuilder();
        foreach (var violation in violations)
        {
            description.AppendLine().Append("- ").Append(violation.Id).Append(" (").Append(violation.Impact).Append("): ").Append(violation.Help);
            foreach (var node in violation.Nodes)
            {
                description.AppendLine().Append("    ").Append(node.Target).Append("  ").Append(node.Html);
            }
        }

        return description.ToString();
    }

    private async Task SaveScreenshotAsync()
    {
        if (Page is null)
        {
            return; // the test failed before its page opened
        }

        var testName = TestContext.Current.Test?.TestDisplayName ?? "unknown";
        var fileName = string.Concat(testName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(AppContext.BaseDirectory, "TestResults", "screenshots", $"{fileName}.png");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await Page.ScreenshotAsync(new() { Path = path, FullPage = true });
            TestContext.Current.TestOutputHelper?.WriteLine($"Screenshot of the failure: {path}");
        }
        catch (Exception exception) when (exception is PlaywrightException or IOException or UnauthorizedAccessException)
        {
            // A crashed page or an unwritable folder cannot be captured; the test's own failure is the useful error, so
            // do not mask it.
            TestContext.Current.TestOutputHelper?.WriteLine($"Could not capture a screenshot: {exception.Message}");
        }
    }
}
