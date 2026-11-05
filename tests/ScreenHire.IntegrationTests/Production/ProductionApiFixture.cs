using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.IntegrationTests.Production;

/// <summary>
/// An AppHost of its own whose API runs in the Production environment, for what must hold outside Development.
/// Production skips the API's Development startup work, so the fixture does what a deploy does first (sign-in-and-tenancy.md,
/// "Deploy prerequisites"): it migrates, gives the application role its login and provisions Wolverine's tables before the
/// API starts. Production has no <c>appsettings.Development.json</c>, so the fixture gives the domains a deployment would
/// (example.com placeholders), and it leaves out Dev SSO's plain-http authority, which the API refuses outside
/// Development. The worker is left out, and nothing waits for the API to report healthy before the tests start.
/// </summary>
public sealed class ProductionApiFixture : AppHostFixture
{
    protected override IReadOnlyList<string> RequiredResources => [];

    // The sandbox workspace and calendar are refused outside Development (SlackSandboxGate, CalendarSandboxGate), as is the email sandbox
    // (EmailSandboxGate), and this stack runs the API as Production.
    // ProductionSettings also refuses the loopback and sandbox switches that appsettings.Development.json turns on for the other stacks.
    protected override IReadOnlyList<string> ExtraArguments =>
    [
        "--ScreenHire:Slack:Sandbox:Enabled=false", "--ScreenHire:Calendar:Sandbox:Enabled=false", "--ScreenHire:Email:Sandbox:Enabled=false",
        "--ScreenHire:Webhooks:AllowLoopback=false", "--ScreenHire:CareersEmbedding:AllowLoopback=false", "--ScreenHire:Distribution:Sandbox:Enabled=false",
    ];

    protected override void Configure(IDistributedApplicationTestingBuilder builder)
    {
        builder.Resources.Remove(builder.Resources.Single(resource => resource.Name == "worker"));
        var api = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api");
        var databases = builder.Resources.OfType<IResourceWithConnectionString>().ToList();

        // Before the API's process starts. The event does not wait for the API's WaitFor(database), so this waits itself.
        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(api, async (started, cancellationToken) =>
        {
            await started.Services.GetRequiredService<ResourceNotificationService>()
                .WaitForResourceHealthyAsync(DatabaseRegistration.OwnerConnectionName, cancellationToken);
            var owner = await ConnectionStringAsync(databases, DatabaseRegistration.OwnerConnectionName, cancellationToken);
            var app = await ConnectionStringAsync(databases, DatabaseRegistration.AppConnectionName, cancellationToken);
            await DevelopmentDataSeeder.MigrateAndSeedAsync(owner, app, DatabasePrecisionTimeProvider.Instance, cancellationToken);
            await ScreenHireMessaging.ProvisionStorageAsync(owner, cancellationToken);
        });

        builder.CreateResourceBuilder(api).WithEnvironment(context =>
        {
            context.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Production";
            context.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Production";
            context.EnvironmentVariables["ScreenHire__Domains__BaseDomain"] = "example.com";
            // What a production host must have (ProductionSettings) and this stack has no Azure to give: a proxy, and the key ring
            // left unwrapped on purpose.
            context.EnvironmentVariables["ScreenHire__Auth__TrustedProxies__0"] = "127.0.0.1";
            context.EnvironmentVariables["ScreenHire__DataProtection__AllowUnprotectedKeys"] = "true";
            context.EnvironmentVariables["ScreenHire__Domains__CareersDomain"] = "careers.example.com";
            context.EnvironmentVariables["ScreenHire__Domains__CareersHostPattern"] = "{tenant}.{CareersDomain}";
            foreach (var key in context.EnvironmentVariables.Keys.Where(key => key.StartsWith("ScreenHire__Auth__Oidc__", StringComparison.Ordinal)).ToList())
            {
                context.EnvironmentVariables.Remove(key);
            }
        });
    }

    private static async Task<string> ConnectionStringAsync(
        IEnumerable<IResourceWithConnectionString> resources, string name, CancellationToken cancellationToken) =>
        await resources.Single(resource => resource.Name == name).GetConnectionStringAsync(cancellationToken)
        ?? throw new InvalidOperationException($"The AppHost has no '{name}' connection string.");
}
