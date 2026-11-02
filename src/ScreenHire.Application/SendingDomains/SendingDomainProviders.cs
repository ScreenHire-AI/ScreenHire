using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.Application.SendingDomains;

/// <summary>What the provider needs to set a domain up.</summary>
/// <param name="Domain">The company's domain, as stored.</param>
/// <param name="ReplyHost">The host replies arrive on (<c>reply.</c> then the domain).</param>
/// <param name="InboundUrl">Where the provider posts a reply it receives on <paramref name="ReplyHost"/>.</param>
public sealed record SendingDomainRequest(string Domain, string ReplyHost, Uri InboundUrl);

/// <summary>A domain the provider set up: its id and the DNS records the owner adds.</summary>
public sealed record ProvisionedDomain(string ProviderDomainId, IReadOnlyList<SendingDomainRecord> Records);

/// <summary>Whether the provider accepts the domain's DNS as it stands, and its reason when it does not.</summary>
public sealed record ProviderValidation(bool Valid, string? Message);

/// <summary>The provider refused a request. <see cref="Exception.Message"/> is safe to show: the provider's own short reason.</summary>
public sealed class SendingDomainProviderException(string message) : Exception(message);

/// <summary>
/// The email provider's domain authentication: it returns the DNS records a domain needs, and says when they are in
/// place. Behind an interface so Azure Communication Services Email could take over (ADR-0022), with a local sandbox
/// that needs no provider and no DNS.
/// </summary>
public interface ISendingDomainProvider
{
    /// <summary>Sets <paramref name="request"/> up with the provider: the domain's authentication and its reply host.</summary>
    Task<ProvisionedDomain> CreateAsync(SendingDomainRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the provider to check the domain's DNS now.</summary>
    Task<ProviderValidation> ValidateAsync(string providerDomainId, CancellationToken cancellationToken);

    /// <summary>Removes the domain and its reply host from the provider. A domain the provider no longer has is not an error.</summary>
    Task DeleteAsync(string providerDomainId, string replyHost, CancellationToken cancellationToken);
}

/// <summary>Looks one DNS record up and says whether it is there with the expected value.</summary>
public interface IDnsRecordChecker
{
    Task<SendingDomainRecordState> CheckAsync(SendingDomainRecord record, CancellationToken cancellationToken);
}

/// <summary>
/// SendGrid's Domain Authentication API (https://www.twilio.com/docs/sendgrid/api-reference/domain-authentication) with
/// automated security: DKIM and the return path are CNAMEs to SendGrid, which carry the SPF record on the return path's
/// own subdomain, so the owner never adds a second SPF TXT record at the apex (a domain may have only one). The reply
/// host is registered with Inbound Parse, which needs one MX record on that subdomain.
/// </summary>
/// <remarks>
/// A client of its own rather than one from the HTTP client factory, as <c>SendGridEmailSender</c> does: the service
/// defaults' retries would repeat a create that already succeeded.
/// </remarks>
public sealed class SendGridSendingDomainProvider : ISendingDomainProvider, IDisposable
{
    /// <summary>SendGrid's MX target for Inbound Parse.</summary>
    public const string InboundMx = "mx.sendgrid.net";

    private const int MaxMessageLength = 300;

    private readonly HttpClient client;

    public SendGridSendingDomainProvider(IOptions<EmailOptions> options)
        : this(options, new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
    }

    /// <summary>With <paramref name="handler"/> in place of the network, for tests.</summary>
    public SendGridSendingDomainProvider(IOptions<EmailOptions> options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value.SendGrid;
        client = new HttpClient(handler) { BaseAddress = settings.BaseUrl, Timeout = settings.Timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    public async Task<ProvisionedDomain> CreateAsync(SendingDomainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var created = await SendAsync(
            HttpMethod.Post, "v3/whitelabel/domains", new { domain = request.Domain, automatic_security = true, custom_spf = false, @default = false }, cancellationToken);
        var root = created.RootElement;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetRawText().Trim('"') : throw new SendingDomainProviderException("SendGrid did not return a domain id.");
        var dns = root.GetProperty("dns");
        var records = new List<SendingDomainRecord>();
        AddCname(records, dns, "dkim1", SendingDomainRecordPurpose.Dkim);
        AddCname(records, dns, "dkim2", SendingDomainRecordPurpose.Dkim);
        AddCname(records, dns, "mail_cname", SendingDomainRecordPurpose.ReturnPath);
        records.Add(new SendingDomainRecord(SendingDomainRecordPurpose.Replies, "MX", request.ReplyHost, InboundMx, Priority: 10));

        // Inbound Parse: the provider posts mail it receives for the reply host to our webhook.
        using var parse = await SendAsync(
            HttpMethod.Post, "v3/user/webhooks/parse/settings", new { hostname = request.ReplyHost, url = request.InboundUrl.ToString(), spam_check = false, send_raw = false }, cancellationToken);
        return new ProvisionedDomain(id, records);
    }

    public async Task<ProviderValidation> ValidateAsync(string providerDomainId, CancellationToken cancellationToken)
    {
        using var answer = await SendAsync(HttpMethod.Post, $"v3/whitelabel/domains/{Uri.EscapeDataString(providerDomainId)}/validate", null, cancellationToken);
        var root = answer.RootElement;
        if (root.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True)
        {
            return new ProviderValidation(true, null);
        }

        // The first failing record's reason, in SendGrid's words.
        if (root.TryGetProperty("validation_results", out var results))
        {
            foreach (var result in results.EnumerateObject())
            {
                if (result.Value.TryGetProperty("valid", out var ok) && ok.ValueKind == JsonValueKind.False
                    && result.Value.TryGetProperty("reason", out var reason) && reason.GetString() is { Length: > 0 } text)
                {
                    return new ProviderValidation(false, Cut(text));
                }
            }
        }

        return new ProviderValidation(false, null);
    }

    public async Task DeleteAsync(string providerDomainId, string replyHost, CancellationToken cancellationToken)
    {
        await DeleteIfPresentAsync($"v3/whitelabel/domains/{Uri.EscapeDataString(providerDomainId)}", cancellationToken);
        await DeleteIfPresentAsync($"v3/user/webhooks/parse/settings/{Uri.EscapeDataString(replyHost)}", cancellationToken);
    }

    public void Dispose() => client.Dispose();

    private static void AddCname(List<SendingDomainRecord> records, JsonElement dns, string name, SendingDomainRecordPurpose purpose)
    {
        if (dns.TryGetProperty(name, out var record)
            && record.TryGetProperty("host", out var host) && host.GetString() is { Length: > 0 } hostName
            && record.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } value)
        {
            records.Add(new SendingDomainRecord(purpose, "CNAME", hostName, value));
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SendingDomainProviderException(Reason(text) ?? $"SendGrid refused the request ({(int)response.StatusCode}).");
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task DeleteIfPresentAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await client.DeleteAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new SendingDomainProviderException(Reason(await response.Content.ReadAsStringAsync(cancellationToken)) ?? $"SendGrid refused the request ({(int)response.StatusCode}).");
        }
    }

    /// <summary>SendGrid's <c>{"errors":[{"message":"..."}]}</c> first message, or null.</summary>
    private static string? Reason(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } reason
                    ? Cut(reason)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Cut(string text) => text.Length <= MaxMessageLength ? text : text[..MaxMessageLength];
}

/// <summary>
/// DNS lookups with DnsClient: CNAME and MX, compared with the expected value ignoring case and a trailing dot. No
/// cache, so a record the owner just added shows on the next check.
/// </summary>
public sealed class DnsRecordChecker : IDnsRecordChecker
{
    private readonly Lazy<LookupClient> client = new(() => new LookupClient(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(5),
        Retries = 1,
        UseCache = false,
        ThrowDnsErrors = false,
    }));

    public async Task<SendingDomainRecordState> CheckAsync(SendingDomainRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var type = record.Type.Equals("MX", StringComparison.OrdinalIgnoreCase) ? QueryType.MX : QueryType.CNAME;
            var answer = await client.Value.QueryAsync(record.Host, type, cancellationToken: cancellationToken);
            var found = type == QueryType.MX
                ? answer.Answers.MxRecords().Select(r => Normalize(r.Exchange.Value)).ToList()
                : answer.Answers.CnameRecords().Select(r => Normalize(r.CanonicalName.Value)).ToList();
            return found.Count == 0 ? SendingDomainRecordState.Missing
                : found.Contains(Normalize(record.Value)) ? SendingDomainRecordState.Matches
                : SendingDomainRecordState.Differs;
        }
        // DNS being slow or unreachable says nothing about the record: report it missing and let the owner check again.
        catch (Exception exception) when (exception is DnsResponseException or OperationCanceledException or System.Net.Sockets.SocketException
            && !cancellationToken.IsCancellationRequested)
        {
            return SendingDomainRecordState.Missing;
        }
    }

    private static string Normalize(string name) => name.Trim().TrimEnd('.').ToLowerInvariant();
}

/// <summary>
/// The sandbox's stand-in for DNS: the hosts a developer or a test published, in this process. A sandbox domain's
/// records are "in DNS" once its domain is published here (the dev route <c>/v1/dev/sending-domains/publish</c>).
/// </summary>
public sealed class SandboxDnsZone
{
    private readonly ConcurrentDictionary<string, byte> published = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(string domain) => published[domain] = 0;

    public void Unpublish(string domain) => published.TryRemove(domain, out _);

    public bool IsPublished(string domain) => published.ContainsKey(domain);
}

/// <summary>The sandbox's DNS: a record is there with its value once its domain is published in the <see cref="SandboxDnsZone"/>.</summary>
public sealed class SandboxDnsRecordChecker(SandboxDnsZone zone) : IDnsRecordChecker
{
    public Task<SendingDomainRecordState> CheckAsync(SendingDomainRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var domain = SandboxSendingDomainProvider.DomainOf(record);
        return Task.FromResult(zone.IsPublished(domain) ? SendingDomainRecordState.Matches : SendingDomainRecordState.Missing);
    }
}

/// <summary>
/// The local provider: deterministic records and a validation that passes once the domain is published in the
/// <see cref="SandboxDnsZone"/>. Development and tests only (<see cref="EmailSandboxGate"/>).
/// </summary>
public sealed class SandboxSendingDomainProvider(SandboxDnsZone zone) : ISendingDomainProvider
{
    public const string Suffix = "sandbox.example.test";

    public Task<ProvisionedDomain> CreateAsync(SendingDomainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var domain = request.Domain;
        IReadOnlyList<SendingDomainRecord> records =
        [
            new(SendingDomainRecordPurpose.Dkim, "CNAME", $"s1._domainkey.{domain}", $"s1.domainkey.{Suffix}"),
            new(SendingDomainRecordPurpose.Dkim, "CNAME", $"s2._domainkey.{domain}", $"s2.domainkey.{Suffix}"),
            new(SendingDomainRecordPurpose.ReturnPath, "CNAME", $"em1.{domain}", $"return.{Suffix}"),
            new(SendingDomainRecordPurpose.Replies, "MX", request.ReplyHost, $"mx.{Suffix}", Priority: 10),
        ];
        return Task.FromResult(new ProvisionedDomain($"sandbox:{domain}", records));
    }

    public Task<ProviderValidation> ValidateAsync(string providerDomainId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerDomainId);
        var domain = providerDomainId["sandbox:".Length..];
        return Task.FromResult(zone.IsPublished(domain)
            ? new ProviderValidation(true, null)
            : new ProviderValidation(false, "The sandbox has not seen this domain's records."));
    }

    public Task DeleteAsync(string providerDomainId, string replyHost, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerDomainId);
        zone.Unpublish(providerDomainId["sandbox:".Length..]);
        return Task.CompletedTask;
    }

    /// <summary>The company domain a sandbox record belongs to: the host with its leading label (or two, for the reply host) removed.</summary>
    internal static string DomainOf(SendingDomainRecord record)
    {
        var host = record.Host.ToLowerInvariant();
        return record.Purpose switch
        {
            SendingDomainRecordPurpose.Dkim => host[(host.IndexOf("._domainkey.", StringComparison.Ordinal) + "._domainkey.".Length)..],
            SendingDomainRecordPurpose.Replies => host[(SendingDomain.ReplyLabel.Length + 1)..],
            _ => host[(host.IndexOf('.', StringComparison.Ordinal) + 1)..],
        };
    }
}
