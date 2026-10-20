using System.Globalization;
using System.Net;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// Network signals from the address a careers-site application came from (integrity-engine.md §4): a hosting
/// provider's network, a VPN, proxy or Tor exit (context only), and a country that differs from the one the candidate's
/// phone number belongs to. Coarse country only, never the browser's location (docs/research/legal.md). The evidence
/// names the network operator and country, never the address, so it outlives the raw data's retention.
/// </summary>
public sealed class NetworkAnalyzer(IIpIntelligence intelligence) : IIntegrityAnalyzer
{
    public string Name => "network";

    public int Version => 1;

    public Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Observation?.IpAddress is not { } ip || !IPAddress.TryParse(ip, out var address))
        {
            return Task.FromResult(AnalyzerResult.Nothing("No network address was recorded for this application."));
        }

        if (!intelligence.IsConfigured)
        {
            return Task.FromResult(AnalyzerResult.Nothing("No IP intelligence source is configured."));
        }

        if (intelligence.Lookup(address) is not { } lookup)
        {
            return Task.FromResult(AnalyzerResult.Nothing("Nothing is known about the network address."));
        }

        var network = Describe(lookup);
        var source = SignalEvidence.Fact("Source", string.Join(", ", lookup.Sources));
        var claimed = PhoneNumbersCheck.Inspect(context.Candidate.Phone, context.JobCountry) is { Valid: true, Region: { } region } ? region : null;
        var signals = new List<ObservedSignal>();

        if (lookup.Anonymizer is { } anonymizer)
        {
            // A VPN or proxy exit says where the service is, not where the person is, so no location mismatch is
            // reported for it, and a VPN on a hosting provider's network is reported once, as the VPN.
            var service = anonymizer switch
            {
                AnonymizerKind.Tor => "the Tor network",
                AnonymizerKind.Proxy => "a public proxy",
                _ => "a VPN service",
            };
            var counter = new List<CounterEvidence>();
            if (claimed is not null && claimed == lookup.Country)
            {
                counter.Add(new CounterEvidence(
                    $"The exit is in {InSentence(claimed)}, the country the candidate's phone number belongs to.",
                    SignalEvidence.Fact("Phone number country", CountryName(claimed))));
            }

            signals.Add(new ObservedSignal(
                SignalCatalogue.AnonymousProxy,
                SignalConfidence.Medium,
                [SignalEvidence.Fact("Service", Capitalize(service)), .. Country(lookup), source],
                new Dictionary<string, string> { ["service"] = service },
                counter));
            return Task.FromResult(new AnalyzerResult(signals));
        }

        if (lookup.Hosting)
        {
            signals.Add(new ObservedSignal(
                SignalCatalogue.DatacenterIp,
                SignalConfidence.High,
                [SignalEvidence.Fact("Network operator", network), SignalEvidence.Fact("Network type", "Hosting or cloud provider"), .. Country(lookup), source],
                new Dictionary<string, string> { ["network"] = network }));
        }

        if (claimed is not null && lookup.Country is { } observed && observed != claimed)
        {
            signals.Add(new ObservedSignal(
                SignalCatalogue.GeoClaimMismatch,
                SignalConfidence.Medium,
                [SignalEvidence.Fact("Phone number country", CountryName(claimed)), SignalEvidence.Fact("Network country", CountryName(observed)), source],
                new Dictionary<string, string> { ["claimed"] = InSentence(claimed), ["observed"] = InSentence(observed) }));
        }

        return Task.FromResult(new AnalyzerResult(signals));
    }

    /// <summary>A country's English name from its ISO code, or the code itself when .NET does not know it.</summary>
    public static string CountryName(string code)
    {
        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    /// <summary>A country's name as it reads inside a sentence: "the United Kingdom", "Ireland".</summary>
    public static string InSentence(string code)
    {
        var name = CountryName(code);
        return name.StartsWith("United ", StringComparison.Ordinal) || name.EndsWith(" Republic", StringComparison.Ordinal)
            || name is "Netherlands" or "Philippines" or "Bahamas" or "Gambia" or "Maldives"
            ? $"the {name}"
            : name;
    }

    private static IEnumerable<SignalEvidence> Country(IpLookup lookup) =>
        lookup.Country is { } country ? [SignalEvidence.Fact("Network country", CountryName(country))] : [];

    private static string Describe(IpLookup lookup) => (lookup.Organization, lookup.Asn) switch
    {
        ({ } name, { } asn) => $"{name} (AS{asn.ToString(CultureInfo.InvariantCulture)})",
        ({ } name, null) => name,
        (null, { } asn) => $"AS{asn.ToString(CultureInfo.InvariantCulture)}",
        _ => "an unnamed network",
    };

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
