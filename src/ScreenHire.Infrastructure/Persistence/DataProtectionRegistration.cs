using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ScreenHire.Infrastructure.Persistence;

public static class DataProtectionRegistration
{
    /// <summary>
    /// The name both processes protect under. Data Protection isolates key use by application name, which otherwise
    /// defaults to each process's content root, so the API could not read what the worker protected.
    /// </summary>
    public const string ApplicationName = "ScreenHire";

    /// <summary>The Key Vault key that wraps the key ring, e.g. <c>https://{vault}.vault.azure.net/keys/data-protection</c>.</summary>
    public const string KeyVaultKeyUriSetting = "ScreenHire:DataProtection:KeyVaultKeyUri";

    /// <summary>
    /// One ASP.NET Core Data Protection key ring for the API and the worker, kept in Postgres
    /// (<see cref="ScreenHireDbContext.DataProtectionKeys"/>). The worker signs the follow-up questionnaire links it emails
    /// and the API reads them (workflow-engine.md §8); the API's cookies, antiforgery tokens and telemetry sessions use the
    /// same ring, so they survive a restart and work on every API instance.
    /// </summary>
    /// <remarks>
    /// The keys are stored unencrypted unless <c>ScreenHire:DataProtection:KeyVaultKeyUri</c> names a Key Vault key, which
    /// wraps them at rest (docs/operations/deploy.md). The host signs in to Key Vault as its managed identity, which needs
    /// the Key Vault Crypto User role. Production hosts refuse to start without the setting (<c>ProductionSettings</c>).
    /// </remarks>
    public static TBuilder AddScreenHireDataProtection<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName(ApplicationName);

        // Data Protection reads its key ring as the host starts. The build-time OpenAPI document generator starts the API
        // with no connection strings at all, and must not try the database; anything else without one fails on its first
        // query anyway.
        if (builder.Configuration.GetConnectionString(DatabaseRegistration.AppConnectionName) is { Length: > 0 })
        {
            dataProtection.PersistKeysToDbContext<ScreenHireDbContext>();
        }

        if (builder.Configuration[KeyVaultKeyUriSetting] is { Length: > 0 } keyUri)
        {
            dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyUri), new DefaultAzureCredential());
        }

        return builder;
    }
}
