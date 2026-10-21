namespace ScreenHire.E2E;

/// <summary>
/// The AppHost with every frontend serving its production build (<c>ScreenHire:Frontends=Built</c>), so the tests see
/// what ships rather than dev servers with injected scripts, plus one headless Chromium shared by the assembly.
/// URLs come from Aspire (random ports) and Aspire stops everything it started when the assembly finishes.
/// </summary>
public sealed class E2EFixture : AppHostFixture
{
    // What `pnpm -r build` leaves behind for the AppHost to serve in Built mode.
    private static readonly string[] BuildOutputs =
    [
        "apps/marketing/dist/index.html",
        "apps/careers/dist/server/entry.mjs",
        "apps/dashboard/build/200.html",
    ];

    private IPlaywright? playwright;
    private IBrowser? browser;

    protected override string Frontends => "Built";

    protected override IReadOnlyList<string> RequiredResources => ["api", "careers", "marketing", "dashboard"];

    public IBrowser Browser => browser ?? throw new InvalidOperationException("The browser has not started.");

    public override async ValueTask InitializeAsync()
    {
        // Cheap checks first, so a missing prerequisite fails in seconds with a fix rather than after booting Aspire.
        EnsureBuildOutputExists();
        playwright = await Playwright.CreateAsync();
        try
        {
            browser = await LaunchChromiumAsync(playwright);
            await base.InitializeAsync();
        }
        catch
        {
            // Stop the Playwright driver and browser even if xUnit does not dispose a fixture that failed to start.
            await DisposeBrowserAsync();
            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await DisposeBrowserAsync();
        }
        finally
        {
            // Stop the AppHost and its containers even if the browser fails to close.
            await base.DisposeAsync();
        }
    }

    // Idempotent: runs after a failed start and again if xUnit then disposes the fixture.
    private async ValueTask DisposeBrowserAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
            browser = null;
        }

        playwright?.Dispose();
        playwright = null;
    }

    private static void EnsureBuildOutputExists()
    {
        var root = RepositoryRoot();
        var missing = BuildOutputs.Where(path => !Path.Exists(Path.Combine(root, path))).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The end-to-end tests serve the frontends' production builds, and these are missing: {string.Join(", ", missing)}. " +
                "Run `pnpm -r build` first (`pnpm verify` does). Rebuild after changing a frontend, or the tests see the old build.");
        }
    }

    private static async Task<IBrowser> LaunchChromiumAsync(IPlaywright playwright)
    {
        try
        {
            // HEADED=1 shows the browser while debugging a test locally.
            return await playwright.Chromium.LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("HEADED") != "1" });
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Playwright's Chromium is not installed for this Microsoft.Playwright version. Run `pnpm e2e:install` once " +
                "per machine (and again after upgrading Microsoft.Playwright); see docs/process/testing.md.",
                exception);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ScreenHire.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find ScreenHire.slnx above {AppContext.BaseDirectory}.");
    }
}
