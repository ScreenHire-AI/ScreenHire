using System.Collections.Concurrent;
using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Xunit;

namespace ScreenHire.Testing;

/// <summary>
/// Boots the whole AppHost (requires Docker) once per test assembly. Register it with
/// <c>[assembly: AssemblyFixture(typeof(AppHostFixture))]</c> and take it as a constructor parameter.
/// Every run gets fresh infrastructure: no data volumes and random ports, so it never collides with a running dev
/// stack or a sibling worktree, and Aspire removes the containers when the assembly finishes. Every resource's console
/// log goes to <c>TestResults/logs/{resource}.log</c> next to the test assembly, and a resource that stops while the tests
/// run is kept in <see cref="StoppedResources"/>, so a server that dies mid-run names itself.
/// </summary>
public class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The sandbox board's acceptance and removal delay in the test stack.</summary>
    public static readonly TimeSpan SandboxDelay = TimeSpan.FromSeconds(4);

    private readonly CancellationTokenSource watching = new();
    private readonly ConcurrentDictionary<string, string> stopped = new(StringComparer.Ordinal);
    private Task? watcher;
    private DistributedApplication? app;

    /// <summary>
    /// Where each resource's console log is written, one <c>{resource}.log</c> per resource, in a folder per fixture type
    /// (<c>TestResults/logs/AppHostFixture</c>), since a fixture of its own boots a second stack beside the shared one.
    /// </summary>
    public string LogDirectory => Path.Combine(AppContext.BaseDirectory, "TestResults", "logs", GetType().Name);

    /// <summary>
    /// Every resource that stopped with an error after it started (a crash, a non-zero exit), with its exit code and log
    /// file. Empty while the stack is whole.
    /// </summary>
    public IReadOnlyCollection<string> StoppedResources => [.. stopped.Values];

    /// <summary>The AppHost's <c>ScreenHire:Frontends</c> mode. <c>None</c> starts only the API, worker and infrastructure.</summary>
    protected virtual string Frontends => "None";

    /// <summary>
    /// More AppHost arguments (<c>--ScreenHire:Setting=value</c>), after the standard ones, for a fixture that needs a
    /// stack with a setting of its own. The shared fixture adds none.
    /// </summary>
    protected virtual IReadOnlyList<string> ExtraArguments => [];

    /// <summary>Resources that must report healthy before any test runs.</summary>
    protected virtual IReadOnlyList<string> RequiredResources => ["api"];

    public DistributedApplication App => app ?? throw new InvalidOperationException("The AppHost has not started.");

    public virtual async ValueTask InitializeAsync()
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        startup.CancelAfter(StartupTimeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>(
            [
                "--ScreenHire:PersistData=false", "--ScreenHire:PinPorts=false", $"--ScreenHire:Frontends={Frontends}",
                // Job distribution: a quick sandbox board, so its delayed acceptance and removal fit in a test, and the
                // local Indexing API fake, so the Google queue runs with no account.
                "--ScreenHire:Distribution:Sandbox:Enabled=true",
                $"--ScreenHire:Distribution:Sandbox:AcceptanceDelay={SandboxDelay}",
                $"--ScreenHire:Distribution:Sandbox:RemovalDelay={SandboxDelay}",
                "--ScreenHire:Distribution:GoogleIndexing:Mode=Local",
                .. ExtraArguments,
            ],
            startup.Token);
        app = await builder.BuildAsync(startup.Token);
        watcher = WatchResourcesAsync(app, watching.Token);
        try
        {
            await app.StartAsync(startup.Token);
            await Task.WhenAll(RequiredResources.Select(resource => WaitUntilHealthyAsync(app, resource, startup.Token)));
        }
        catch
        {
            // A failed start must not leave containers and processes behind, whether or not xUnit disposes us.
            await DisposeAppAsync();
            throw;
        }
    }

    // A resource that fails to start (a missing build, a pnpm error) fails the run at once instead of after the timeout,
    // and a timeout names the resource that never became healthy.
    private async Task WaitUntilHealthyAsync(DistributedApplication app, string resource, CancellationToken cancellationToken)
    {
        try
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(
                resource, WaitBehavior.StopOnResourceUnavailable, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The AppHost resource '{resource}' was not healthy within {StartupTimeout.TotalMinutes:0} minutes. " +
                $"Check its log in {LogDirectory}.",
                exception);
        }
    }

    // Writes each resource instance's console log to its file as it arrives, and keeps every resource that stops with an
    // error. Uses Aspire's own log and state streams, which carry every line the dashboard shows.
    private async Task WatchResourcesAsync(DistributedApplication started, CancellationToken cancellationToken)
    {
        var directory = LogDirectory;
        Directory.CreateDirectory(directory);
        foreach (var old in Directory.EnumerateFiles(directory, "*.log"))
        {
            File.Delete(old); // the files describe this run only
        }

        var logs = started.Services.GetRequiredService<ResourceLoggerService>();
        var logging = new ConcurrentDictionary<string, Task>(StringComparer.Ordinal);
        try
        {
            await foreach (var update in started.ResourceNotifications.WatchAsync(cancellationToken))
            {
                var path = Path.Combine(directory, $"{update.Resource.Name}.log");
                // By instance id, not by name: the log stream of a running project carries a suffix after its name.
                _ = logging.GetOrAdd(update.ResourceId, id => Task.Run(() => CopyLogAsync(logs, id, path, cancellationToken), cancellationToken));

                var snapshot = update.Snapshot;
                if (snapshot.State?.Text is { } state && KnownResourceStates.TerminalStates.Contains(state)
                    && (snapshot.ExitCode is not (null or 0) || state == KnownResourceStates.FailedToStart))
                {
                    stopped[update.ResourceId] =
                        $"The AppHost resource '{update.Resource.Name}' stopped ({state}, exit code {snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}) " +
                        $"at {DateTimeOffset.UtcNow:HH:mm:ss} UTC. Its log: {path}";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            await Task.WhenAll(logging.Values);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task CopyLogAsync(ResourceLoggerService logs, string resourceId, string path, CancellationToken cancellationToken)
    {
        await using var file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        await foreach (var batch in logs.WatchAsync(resourceId).WithCancellation(cancellationToken))
        {
            foreach (var line in batch)
            {
                await file.WriteLineAsync($"{(line.IsErrorMessage ? "ERR " : "")}{line.Content}");
            }
        }
    }

    public HttpClient CreateHttpClient(string resource) => App.CreateHttpClient(resource, "http");

    public Uri GetEndpoint(string resource) => App.GetEndpoint(resource, "http");

    /// <summary>The email the stack has sent, from the AppHost's Mailpit container.</summary>
    public Mailpit Mailpit => new(GetEndpoint("mailpit"));

    /// <summary>
    /// A context on the AppHost's database as the application role (<c>screenhire_app</c>), so row-level security
    /// applies exactly as it does in the API: it sees and writes only <paramref name="tenant"/>'s organization. Prefer
    /// <see cref="TestData"/> over writing through this directly.
    /// </summary>
    public async Task<ScreenHireDbContext> CreateDbContextAsync(ITenantContext tenant, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ScreenHireDbContext>()
            .UseNpgsql(await GetAppConnectionStringAsync(cancellationToken))
            .UseScreenHireConventions()
            .Options;
        return new ScreenHireDbContext(options, tenant);
    }

    /// <summary>The connection string the API and worker use: the <c>screenhire_app</c> role, bound by row-level security.</summary>
    public async Task<string> GetAppConnectionStringAsync(CancellationToken cancellationToken) =>
        await App.GetConnectionStringAsync(DatabaseRegistration.AppConnectionName, cancellationToken)
        ?? throw new InvalidOperationException("The AppHost has no application-role connection string.");

    /// <summary>
    /// The table owner's connection string (a superuser, which bypasses row-level security). Only for arranging state no
    /// endpoint can reach, such as an expired sign-in link; never for tenant data.
    /// </summary>
    public async Task<string> GetOwnerConnectionStringAsync(CancellationToken cancellationToken) =>
        await App.GetConnectionStringAsync(DatabaseRegistration.OwnerConnectionName, cancellationToken)
        ?? throw new InvalidOperationException("The AppHost has no owner connection string.");

    public virtual async ValueTask DisposeAsync()
    {
        await DisposeAppAsync();
        watching.Dispose();
        GC.SuppressFinalize(this);
    }

    // Idempotent: runs after a failed start and again if xUnit then disposes the fixture.
    private async ValueTask DisposeAppAsync()
    {
        // Stop watching first: shutting the stack down is not a crash, and the streams end with the app's services.
        await watching.CancelAsync();
        if (watcher is not null)
        {
            await watcher;
            watcher = null;
        }

        if (app is not null)
        {
            var started = app;
            app = null;
            await started.DisposeAsync();
        }
    }
}
