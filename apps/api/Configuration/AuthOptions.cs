using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Configuration;

/// <summary>
/// Sign-in settings, <c>ScreenHire:Auth:*</c>. Client ids and secrets live in user secrets or the host's secret store,
/// never in a committed file. A provider without both is not registered and does not appear in the providers list.
/// </summary>
public sealed class AuthOptions
{
    public const string Section = "ScreenHire:Auth";

    /// <summary>Google (<c>https://accounts.google.com</c>).</summary>
    public OidcClientOptions Google { get; set; } = new();

    /// <summary>Microsoft personal and work accounts (the <c>common</c> endpoint).</summary>
    public OidcClientOptions Microsoft { get; set; } = new();

    /// <summary>Any other OpenID Connect provider, keyed by its id in URLs (<c>ScreenHire:Auth:Oidc:&lt;name&gt;</c>).</summary>
    public Dictionary<string, OidcClientOptions> Oidc { get; set; } = [];

    public MagicLinkOptions MagicLink { get; set; } = new();

    /// <summary>Requests per minute per client IP address across the sign-in endpoints that change state.</summary>
    [Range(1, 10_000)]
    public int PermitsPerMinute { get; set; } = 100;
}

public sealed class OidcClientOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>The issuer URL. Fixed for Google and Microsoft; required for generic providers.</summary>
    public string? Authority { get; set; }

    /// <summary>The button label. Defaults to the provider id for generic providers.</summary>
    public string? DisplayName { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class MagicLinkOptions
{
    /// <summary>How long a sign-in link works.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Links sent to one address per <see cref="Window"/>. Requests beyond it get the same reply and no email.</summary>
    [Range(1, 100)]
    public int PerAddressLimit { get; set; } = 5;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The From address of sign-in emails. Defaults to <c>no-reply@{BaseDomain}</c>.</summary>
    public string? From { get; set; }
}
