using ScreenHire.Api.Configuration;

namespace ScreenHire.Api.Auth;

internal enum ProviderKind
{
    Google,
    Microsoft,
    Generic,
}

/// <summary>A configured OpenID Connect provider. <see cref="Id"/> is its authentication scheme and its URL segment.</summary>
internal sealed record ExternalProvider(string Id, string DisplayName, ProviderKind Kind, string Authority, OidcClientOptions Client)
{
    /// <summary>The handler's own redirect URI path, on the dashboard origin.</summary>
    public string CallbackPath => $"/v1/auth/signin-{Id}";
}

/// <summary>The providers with a client id and secret, read once at startup.</summary>
internal sealed class ExternalProviders(IReadOnlyList<ExternalProvider> providers)
{
    public const string GoogleAuthority = "https://accounts.google.com";

    // "common" accepts personal and work accounts; each token's issuer names the account's own tenant.
    public const string MicrosoftAuthority = "https://login.microsoftonline.com/common/v2.0";

    public IReadOnlyList<ExternalProvider> All => providers;

    public ExternalProvider? Find(string id) => providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public static ExternalProviders FromOptions(AuthOptions options)
    {
        var providers = new List<ExternalProvider>();
        if (options.Google.IsConfigured)
        {
            providers.Add(new("google", "Google", ProviderKind.Google, GoogleAuthority, options.Google));
        }

        if (options.Microsoft.IsConfigured)
        {
            providers.Add(new("microsoft", "Microsoft", ProviderKind.Microsoft, MicrosoftAuthority, options.Microsoft));
        }

        foreach (var (id, client) in options.Oidc.Where(entry => entry.Value.IsConfigured).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!IsValidId(id) || id is "google" or "microsoft")
            {
                throw new InvalidOperationException(
                    $"ScreenHire:Auth:Oidc:{id}: provider ids are lower-case letters, digits and hyphens, and cannot be 'google' or 'microsoft'.");
            }

            if (!Uri.TryCreate(client.Authority, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException($"ScreenHire:Auth:Oidc:{id}:Authority must be an absolute URL.");
            }

            providers.Add(new(id, client.DisplayName ?? id, ProviderKind.Generic, client.Authority!, client));
        }

        return new ExternalProviders(providers);
    }

    private static bool IsValidId(string id) =>
        id.Length is > 0 and <= 40 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
