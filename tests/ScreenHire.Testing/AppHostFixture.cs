using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;
using Xunit;

namespace ScreenHire.Testing;

/// <summary>
/// Boots the whole AppHost (requires Docker) once per test assembly. Register it with
/// <c>[assembly: AssemblyFixture(typeof(AppHostFixture))]</c> and take it as a constructor parameter.
/// Every run gets fresh infrastructure: no data volumes and random ports, so it never collides with a running dev
/// stack or a sibling worktree, and Aspire removes the containers when the assembly finishes.
/// </summary>
public class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private DistributedApplication? app;

    /// <summary>The AppHost's <c>ScreenHire:Frontends</c> mode. <c>None</c> starts only the API, worker and infrastructure.</summary>
    protected virtual string Frontends => "None";

    /// <summary>Resources that must report healthy before any test runs.</summary>
    protected virtual IReadOnlyList<string> RequiredResources => ["api"];

    public DistributedApplication App => app ?? throw new InvalidOperationException("The AppHost has not started.");

    public virtual async ValueTask InitializeAsync()
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        startup.CancelAfter(StartupTimeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>(
            ["--ScreenHire:PersistData=false", "--ScreenHire:PinPorts=false", $"--ScreenHire:Frontends={Frontends}"],
            startup.Token);
        app = await builder.BuildAsync(startup.Token);
        try
        {
            await app.StartAsync(startup.Token);
            await Task.WhenAll(RequiredResources.Select(resource =>
                app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token)));
        }
        catch
        {
            // A failed start must not leave containers and processes behind, whether or not xUnit disposes us.
            await DisposeAppAsync();
            throw;
        }
    }

    public HttpClient CreateHttpClient(string resource) => App.CreateHttpClient(resource, "http");

    public Uri GetEndpoint(string resource) => App.GetEndpoint(resource, "http");

    /// <summary>
    /// A context on the AppHost's database. Prefer <see cref="TestData"/> over writing through this directly, so the
    /// tenant rules (row-level security) only need adapting in one place.
    /// </summary>
    public async Task<ScreenHireDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var connectionString = await App.GetConnectionStringAsync("screenhire", cancellationToken);
        var options = new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(connectionString).UseScreenHireConventions().Options;
        return new ScreenHireDbContext(options);
    }

    public virtual async ValueTask DisposeAsync()
    {
        await DisposeAppAsync();
        GC.SuppressFinalize(this);
    }

    // Idempotent: runs after a failed start and again if xUnit then disposes the fixture.
    private async ValueTask DisposeAppAsync()
    {
        if (app is not null)
        {
            var started = app;
            app = null;
            await started.DisposeAsync();
        }
    }
}
