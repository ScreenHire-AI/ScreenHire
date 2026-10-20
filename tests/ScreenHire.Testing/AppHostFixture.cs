using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
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

    /// <summary>The sandbox board's acceptance and removal delay in the test stack.</summary>
    public static readonly TimeSpan SandboxDelay = TimeSpan.FromSeconds(4);

    private DistributedApplication? app;

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
    private static async Task WaitUntilHealthyAsync(DistributedApplication app, string resource, CancellationToken cancellationToken)
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
                "Check its logs in the test output.",
                exception);
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
