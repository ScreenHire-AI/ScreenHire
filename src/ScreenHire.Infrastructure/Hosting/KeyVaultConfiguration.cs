using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ScreenHire.Infrastructure.Hosting;

public static class KeyVaultConfiguration
{
    /// <summary>The connection name the AppHost gives the vault (<c>ConnectionStrings:vault</c>, the vault's address).</summary>
    public const string ConnectionName = "vault";

    /// <summary>
    /// Loads every secret in the Key Vault into configuration, so <c>ScreenHire--Slack--ClientSecret</c> in the vault is
    /// <c>ScreenHire:Slack:ClientSecret</c> in the app. The owner sets a provider's secret there (docs/operations/deploy.md),
    /// and a provider whose secret is absent stays hidden. Does nothing without the vault's address, which is every local
    /// and test run: those take their secrets from the AppHost's parameters. Call it before anything reads configuration.
    /// </summary>
    public static TBuilder AddScreenHireKeyVaultConfiguration<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetConnectionString(ConnectionName) is { Length: > 0 })
        {
            // Authenticates as the app's managed identity (Aspire sets AZURE_CLIENT_ID and the credential type).
            builder.Configuration.AddAzureKeyVaultSecrets(ConnectionName);
        }

        return builder;
    }
}
