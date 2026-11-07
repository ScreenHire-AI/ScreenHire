using System.Net;

namespace ScreenHire.Domain.Verification;

/// <summary>
/// The email domains and networks an organization trusts, such as an office or a staffing partner. A trusted domain stops a
/// shared email from raising a duplicate, and a trusted network stops its addresses raising a network signal or a
/// device-only duplicate (verification-engine.md §10). Parsing and matching live here so the settings endpoint and the
/// analyzers agree on what an entry means.
/// </summary>
public sealed class TrustedSources
{
    /// <summary>The most entries of each kind.</summary>
    public const int MaxEntries = 50;

    /// <summary>The narrowest prefix length an IPv4 network may have: a wider one would trust a large part of the internet.</summary>
    public const int MinIPv4Prefix = 16;

    /// <summary>The narrowest prefix length an IPv6 network may have.</summary>
    public const int MinIPv6Prefix = 48;

    public static TrustedSources None { get; } = new([], []);

    private readonly IReadOnlyList<string> domains;
    private readonly IReadOnlyList<IPNetwork> networks;

    private TrustedSources(IReadOnlyList<string> domains, IReadOnlyList<IPNetwork> networks)
    {
        this.domains = domains;
        this.networks = networks;
    }

    public bool IsEmpty => domains.Count == 0 && networks.Count == 0;

    /// <summary>Reads the stored entries. An entry that no longer parses is ignored, never trusted.</summary>
    public static TrustedSources From(IEnumerable<string> storedDomains, IEnumerable<string> storedNetworks)
    {
        ArgumentNullException.ThrowIfNull(storedDomains);
        ArgumentNullException.ThrowIfNull(storedNetworks);
        return new TrustedSources(
            [.. storedDomains.Select(NormalizeDomain).OfType<string>()],
            [.. storedNetworks.Select(n => TryParseNetwork(n, out var network, out _) ? network : (IPNetwork?)null).OfType<IPNetwork>()]);
    }

    /// <summary>Is the domain of <paramref name="email"/> trusted, or a subdomain of a trusted domain?</summary>
    public bool TrustsEmail(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at < 0 || email is null || NormalizeDomain(email[(at + 1)..]) is not { } domain)
        {
            return false;
        }

        return domains.Any(trusted => domain == trusted || domain.EndsWith("." + trusted, StringComparison.Ordinal));
    }

    /// <summary>Is the address inside a trusted network?</summary>
    public bool TrustsAddress(string? address)
    {
        if (address is null || !IPAddress.TryParse(address, out var ip))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return networks.Any(network => network.Contains(ip));
    }

    /// <summary>
    /// A domain as stored: lower case, no leading <c>@</c> or dots, at least one dot, letters, digits and hyphens only. Null
    /// when it is not a domain.
    /// </summary>
    public static string? NormalizeDomain(string? value)
    {
        var domain = value?.Trim().TrimStart('@').Trim('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(domain) || domain.Length > 253 || !domain.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var label in domain.Split('.'))
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-'
                || !label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return null;
            }
        }

        return domain;
    }

    /// <summary>
    /// Parses a network in CIDR form (<c>203.0.113.0/24</c>); a single address is a /32 or /128. Refuses a network wider than
    /// <see cref="MinIPv4Prefix"/> or <see cref="MinIPv6Prefix"/>, and says why in <paramref name="problem"/>.
    /// </summary>
    public static bool TryParseNetwork(string? value, out IPNetwork network, out string? problem)
    {
        network = default;
        problem = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            problem = "Enter a network such as 203.0.113.0/24.";
            return false;
        }

        if (!text.Contains('/', StringComparison.Ordinal))
        {
            if (!IPAddress.TryParse(text, out var single))
            {
                problem = "Enter a network such as 203.0.113.0/24.";
                return false;
            }

            text += single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "/32" : "/128";
        }

        if (!IPNetwork.TryParse(text, out network))
        {
            problem = "Enter a network such as 203.0.113.0/24.";
            return false;
        }

        var minimum = network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? MinIPv4Prefix : MinIPv6Prefix;
        if (network.PrefixLength < minimum)
        {
            problem = $"That network is too wide. Use /{minimum} or narrower.";
            return false;
        }

        return true;
    }

    /// <summary>A network as stored: the canonical <c>address/prefix</c> text, or null when it is not valid.</summary>
    public static string? NormalizeNetwork(string? value) => TryParseNetwork(value, out var network, out _) ? network.ToString() : null;
}
