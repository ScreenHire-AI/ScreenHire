using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ScreenHire.Infrastructure.Verification;

/// <summary>
/// What is known about an IP address: its network operator, coarse country, and whether it belongs to a hosting
/// provider or an anonymizer. Coarse on purpose (docs/research/legal.md): country only, never a city or coordinates.
/// </summary>
public interface IIpIntelligence
{
    /// <summary>Whether any source is configured. Without one, every lookup is null and the network analyzer says so.</summary>
    bool IsConfigured { get; }

    /// <summary>What the configured sources know about <paramref name="address"/>, or null if nothing (or a private address).</summary>
    IpLookup? Lookup(IPAddress address);
}

public enum AnonymizerKind
{
    Vpn,
    Proxy,
    Tor,
}

/// <summary>A lookup result. <see cref="Sources"/> names where each fact came from, for the evidence.</summary>
public sealed record IpLookup(int? Asn, string? Organization, string? Country, bool Hosting, AnonymizerKind? Anonymizer, IReadOnlyList<string> Sources);

/// <summary>
/// The local sources, merged: configured ranges first, then an operator-supplied iptoasn.com file for ASN,
/// operator and country, then a Tor exit list. Hosting comes from a range's own flag or from the operator's ASN being a
/// known hosting provider. A source that is not configured is skipped.
/// </summary>
public sealed class IpIntelligence : IIpIntelligence
{
    // Large cloud and hosting providers' main ASNs, a fact about who operates them rather than licensed data. Owners add
    // more with ScreenHire:Verification:Network:HostingAsns.
    private static readonly int[] KnownHostingAsns =
    [
        16509, 14618, // Amazon Web Services
        15169, 396982, // Google Cloud
        8075, // Microsoft Azure
        14061, // DigitalOcean
        16276, // OVHcloud
        24940, // Hetzner
        63949, // Akamai Connected Cloud (Linode)
        20473, // Vultr
        31898, // Oracle Cloud
        45102, // Alibaba Cloud
        132203, // Tencent Cloud
        51167, // Contabo
        12876, // Scaleway
        13335, // Cloudflare
    ];

    private readonly List<(IPNetwork Network, IpRangeOptions Range)> ranges;
    private readonly HashSet<int> hostingAsns;
    private readonly Lazy<Ip2AsnTable?> ip2Asn;
    private readonly Lazy<HashSet<IPAddress>?> torExits;

    public IpIntelligence(IOptions<VerificationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var network = options.Value.Network;
        ranges = [.. network.Ranges.Where(r => IPNetwork.TryParse(r.Cidr, out _)).Select(r => (IPNetwork.Parse(r.Cidr), r))];
        hostingAsns = [.. KnownHostingAsns, .. network.HostingAsns];
        // PublicationOnly: a read that fails is tried again on the next lookup instead of being cached. The worker checks
        // the files exist at startup (NetworkSourceFiles in ScreenHire.Application).
        ip2Asn = new(() => string.IsNullOrWhiteSpace(network.Ip2AsnFile) ? null : Ip2AsnTable.Load(network.Ip2AsnFile), LazyThreadSafetyMode.PublicationOnly);
        torExits = new(() => string.IsNullOrWhiteSpace(network.TorExitListFile) ? null : LoadTorExits(network.TorExitListFile), LazyThreadSafetyMode.PublicationOnly);
        IsConfigured = ranges.Count > 0 || !string.IsNullOrWhiteSpace(network.Ip2AsnFile) || !string.IsNullOrWhiteSpace(network.TorExitListFile);
    }

    public bool IsConfigured { get; }

    public IpLookup? Lookup(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (!IsPublic(address))
        {
            return null;
        }

        int? asn = null;
        string? organization = null;
        string? country = null;
        var hosting = false;
        AnonymizerKind? anonymizer = null;
        var sources = new List<string>();

        if (ranges.FirstOrDefault(r => r.Network.Contains(address)).Range is { } range)
        {
            asn = range.Asn;
            organization = range.Organization;
            country = range.Country?.ToUpperInvariant();
            hosting = range.Hosting;
            anonymizer = range.Anonymizer;
            sources.Add("configured ranges");
        }

        if (ip2Asn.Value?.Find(address) is { } row)
        {
            asn ??= row.Asn;
            organization ??= row.Organization;
            country ??= row.Country;
            sources.Add("iptoasn.com");
        }

        if (torExits.Value?.Contains(address) == true)
        {
            anonymizer ??= AnonymizerKind.Tor;
            sources.Add("Tor exit list");
        }

        if (asn is { } known && hostingAsns.Contains(known))
        {
            hosting = true;
        }

        return sources.Count == 0 ? null : new IpLookup(asn, organization, country, hosting, anonymizer, sources);
    }

    /// <summary>
    /// False for loopback, private, link-local, carrier-grade NAT and multicast addresses, which say nothing about a
    /// candidate. The documentation ranges (RFC 5737) count as public, so the synthetic applicants can use them.
    /// </summary>
    internal static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 or 0 or 127 => false,
            172 => bytes[1] is < 16 or > 31,
            192 => bytes[1] != 168,
            169 => bytes[1] != 254,
            100 => bytes[1] is < 64 or > 127, // carrier-grade NAT
            >= 224 => false,
            _ => true,
        };
    }

    private static HashSet<IPAddress> LoadTorExits(string path)
    {
        var addresses = new HashSet<IPAddress>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var candidate = parts.Length >= 2 && parts[0] == "ExitAddress" ? parts[1] : parts.FirstOrDefault();
            if (candidate is not null && IPAddress.TryParse(candidate, out var address))
            {
                addresses.Add(address);
            }
        }

        return addresses;
    }

    /// <summary>
    /// An iptoasn.com table: tab-separated <c>range_start range_end AS_number country_code AS_description</c>, sorted by
    /// range start. Lookups are a binary search over 128-bit keys (IPv4 mapped into IPv6 space).
    /// </summary>
    internal sealed class Ip2AsnTable
    {
        private readonly UInt128[] starts;
        private readonly UInt128[] ends;
        private readonly (int Asn, string? Country, string? Organization)[] rows;

        private Ip2AsnTable(List<(UInt128 Start, UInt128 End, int Asn, string? Country, string? Organization)> entries)
        {
            entries.Sort((a, b) => a.Start.CompareTo(b.Start));
            starts = [.. entries.Select(e => e.Start)];
            ends = [.. entries.Select(e => e.End)];
            rows = [.. entries.Select(e => (e.Asn, e.Country, e.Organization))];
        }

        public static Ip2AsnTable Load(string path) => Parse(File.ReadLines(path));

        public static Ip2AsnTable Parse(IEnumerable<string> lines)
        {
            var entries = new List<(UInt128, UInt128, int, string?, string?)>();
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 5 || !IPAddress.TryParse(parts[0], out var start) || !IPAddress.TryParse(parts[1], out var end)
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var asn) || asn == 0)
                {
                    continue; // "Not routed" rows have ASN 0
                }

                var country = parts[3] is { Length: 2 } code && code != "None" ? code.ToUpperInvariant() : null;
                var organization = parts[4] is { Length: > 0 } name && name != "Not routed" ? name : null;
                entries.Add((Key(start), Key(end), asn, country, organization));
            }

            return new Ip2AsnTable(entries);
        }

        public (int Asn, string? Country, string? Organization)? Find(IPAddress address)
        {
            var key = Key(address);
            int low = 0, high = starts.Length - 1, found = -1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (starts[middle] <= key)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return found >= 0 && key <= ends[found] ? rows[found] : null;
        }

        private static UInt128 Key(IPAddress address)
        {
            var bytes = address.MapToIPv6().GetAddressBytes();
            UInt128 value = 0;
            foreach (var part in bytes)
            {
                value = (value << 8) | part;
            }

            return value;
        }
    }
}
