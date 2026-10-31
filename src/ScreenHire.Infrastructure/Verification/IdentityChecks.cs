using System.Reflection;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhoneNumbers;

namespace ScreenHire.Infrastructure.Verification;

/// <summary>Whether an email domain can receive mail. A lookup that fails says nothing, so it is <see cref="MailDomainStatus.Unknown"/>.</summary>
public interface IMailDomainChecker
{
    Task<MailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken);
}

public enum MailDomainStatus
{
    /// <summary>Not checked: lookups are off, the domain is reserved for testing, or DNS did not answer.</summary>
    Unknown,
    AcceptsMail,

    /// <summary>The domain does not exist, or publishes a null MX record (RFC 7505) and no address.</summary>
    NoMailServer,
}

/// <summary>
/// MX lookups with DnsClient. No MX record falls back to the domain's own address, as mail servers do (RFC 5321 §5.1).
/// Reserved domains (RFC 2606 and 6761: example.com, .test, .invalid, ...) are never looked up: test and seed data use
/// them, and example.com publishes a null MX that would flag every one.
/// </summary>
public sealed partial class DnsMailDomainChecker(IOptions<VerificationOptions> options, ILogger<DnsMailDomainChecker> logger) : IMailDomainChecker
{
    private static readonly string[] ReservedSuffixes = [".example", ".test", ".invalid", ".localhost", ".local"];
    private static readonly string[] ReservedDomains = ["example.com", "example.net", "example.org"];

    private readonly Lazy<LookupClient> client = new(() => new LookupClient(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.Identity.MxTimeoutSeconds, 1, 10)),
        Retries = 1,
        UseCache = true,
        ThrowDnsErrors = false,
    }));

    public static bool IsReserved(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var name = domain.TrimEnd('.').ToLowerInvariant();
        return ReservedDomains.Any(d => name == d || name.EndsWith("." + d, StringComparison.Ordinal))
            || ReservedSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal) || name == s[1..]);
    }

    public async Task<MailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (!options.Value.Identity.MxLookup || IsReserved(domain))
        {
            return MailDomainStatus.Unknown;
        }

        try
        {
            var mx = await client.Value.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);
            if (mx.HasError)
            {
                return mx.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain ? MailDomainStatus.NoMailServer : MailDomainStatus.Unknown;
            }

            var exchanges = mx.Answers.MxRecords().ToList();
            if (exchanges.Count > 0)
            {
                // A null MX ("0 .") says the domain accepts no mail at all.
                return exchanges.All(r => r.Exchange.Value is "." or "") ? MailDomainStatus.NoMailServer : MailDomainStatus.AcceptsMail;
            }

            var address = await client.Value.QueryAsync(domain, QueryType.A, cancellationToken: cancellationToken);
            return address.HasError ? MailDomainStatus.Unknown
                : address.Answers.OfType<ARecord>().Any() ? MailDomainStatus.AcceptsMail
                : MailDomainStatus.NoMailServer;
        }
        // DNS being slow or unreachable says nothing about the candidate.
        catch (Exception exception) when (exception is DnsResponseException or OperationCanceledException or System.Net.Sockets.SocketException
            && !cancellationToken.IsCancellationRequested)
        {
            LogLookupFailed(logger, exception.GetType().Name);
            return MailDomainStatus.Unknown;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MX lookup failed ({ExceptionType}); the email domain is recorded as not checked")]
    private static partial void LogLookupFailed(ILogger logger, string exceptionType);
}

/// <summary>
/// Disposable (throwaway) email domains: the embedded CC0 list (Verification/Data/README.md) plus
/// <c>ScreenHire:Verification:Identity:ExtraDisposableDomains</c>. A subdomain of a listed domain counts too.
/// </summary>
public sealed class DisposableEmailDomains
{
    private const string ResourceName = "ScreenHire.Verification.disposable-email-domains.txt";

    private static readonly Lazy<HashSet<string>> Embedded = new(LoadEmbedded);

    private readonly HashSet<string> extra;

    public DisposableEmailDomains(IOptions<VerificationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        extra = new(options.Value.Identity.ExtraDisposableDomains.Select(d => d.Trim().ToLowerInvariant()), StringComparer.Ordinal);
    }

    /// <summary>The listed domain <paramref name="domain"/> falls under, or null.</summary>
    public string? Match(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var name = domain.Trim().TrimEnd('.').ToLowerInvariant();
        while (name.Length > 0)
        {
            if (extra.Contains(name) || Embedded.Value.Contains(name))
            {
                return name;
            }

            var dot = name.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                return null;
            }

            name = name[(dot + 1)..];
        }

        return null;
    }

    private static HashSet<string> LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var domains = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var domain = line.Trim().ToLowerInvariant();
            if (domain.Length > 0 && !domain.StartsWith('#'))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }
}

/// <summary>What libphonenumber says about a phone number the candidate gave.</summary>
/// <remarks>
/// <c>parsed</c> is false when the number cannot be read at all (no country code and no region to assume), which
/// says nothing about the candidate. <c>region</c> is the ISO country the number belongs to.
/// </remarks>
public sealed record PhoneInspection(bool Parsed, bool Valid, string? Region, PhoneLineType LineType, string? E164)
{
    public static PhoneInspection Unreadable { get; } = new(false, false, null, PhoneLineType.Unknown, null);
}

public enum PhoneLineType
{
    Unknown,
    Mobile,
    FixedLine,

    /// <summary>An internet (VoIP) number range.</summary>
    Voip,
    Other,
}

/// <summary>Phone checks with libphonenumber-csharp (Apache-2.0).</summary>
public static class PhoneNumbersCheck
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Reads <paramref name="phone"/>, assuming <paramref name="defaultRegion"/> for a number without a country code.
    /// </summary>
    public static PhoneInspection Inspect(string? phone, string? defaultRegion)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return PhoneInspection.Unreadable;
        }

        var trimmed = phone.Trim();
        if (!trimmed.StartsWith('+') && string.IsNullOrEmpty(defaultRegion))
        {
            return PhoneInspection.Unreadable;
        }

        PhoneNumber number;
        try
        {
            number = Util.Parse(trimmed, defaultRegion?.ToUpperInvariant() ?? "ZZ");
        }
        catch (NumberParseException)
        {
            return PhoneInspection.Unreadable;
        }

        var region = Util.GetRegionCodeForNumber(number);
        if (!Util.IsValidNumber(number))
        {
            // An invalid number has no region of its own; its country code still names the main one (1 is the US).
            var main = Util.GetRegionCodeForCountryCode(number.CountryCode);
            return new PhoneInspection(true, false, main is "ZZ" ? null : main, PhoneLineType.Unknown, null);
        }

        var lineType = Util.GetNumberType(number) switch
        {
            PhoneNumberType.MOBILE => PhoneLineType.Mobile,
            PhoneNumberType.FIXED_LINE or PhoneNumberType.FIXED_LINE_OR_MOBILE => PhoneLineType.FixedLine,
            PhoneNumberType.VOIP => PhoneLineType.Voip,
            _ => PhoneLineType.Other,
        };
        return new PhoneInspection(true, true, region, lineType, Util.Format(number, PhoneNumberFormat.E164));
    }
}
