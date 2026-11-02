using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using ScreenHire.Api.Email;
using ScreenHire.Application.Applications;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.Candidates;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The email provider's webhooks (docs/architecture/email.md, ADR-0021): SendGrid Inbound Parse posts a candidate's
/// reply, and SendGrid's event webhook reports bounces and spam complaints. Both are anonymous routes with their own
/// check, and neither is in the generated client. Inbound Parse cannot sign its posts, so the check is HTTP Basic
/// credentials in the URL SendGrid is given (they travel in a header, never in a logged path) and, behind it, the signed
/// reply address, which a post must carry for anything to change. The event webhook is signed by SendGrid (ECDSA).
/// </summary>
internal static partial class EmailWebhookEndpoints
{
    public const string RateLimitPolicy = "email-webhook";
    public const string InboundPath = "/v1/webhooks/email/inbound";
    public const string EventsPath = "/v1/webhooks/email/events";

    /// <summary>The user name SendGrid sends with the inbound secret. It carries no meaning of its own.</summary>
    public const string InboundUser = "sendgrid";

    private const long MaxInboundBytes = 25 * 1024 * 1024;
    private const int MaxEventBytes = 4 * 1024 * 1024;
    private const int MaxReasonLength = 300;

    public static IServiceCollection AddScreenHireEmailWebhooks(this IServiceCollection services)
    {
        services.AddScreenHireEmailOptions();
        services.AddScoped<InboundEmailIngest>();
        services.AddSingleton<EmailAttachmentStore>();
        // SendGrid posts from addresses we cannot tell from anyone else's, so the limit is shared by every caller; a flood
        // of bad posts cannot starve the real ones because the credentials are checked before any work.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(RateLimitPolicy, _ =>
            RateLimitPartition.GetFixedWindowLimiter("email-webhook", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
            })));
        return services;
    }

    public static IEndpointRouteBuilder MapEmailWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var webhooks = app.MapGroup("/v1/webhooks/email").AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        webhooks.MapPost("/inbound", ReceiveReply).ExcludeFromDescription()
            .WithMetadata(new RequestSizeLimitAttribute(MaxInboundBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxInboundBytes });
        webhooks.MapPost("/events", ReceiveEvents).ExcludeFromDescription()
            .WithMetadata(new RequestSizeLimitAttribute(MaxEventBytes));

        // The sandbox: a reply delivered without SendGrid, and DNS records published without a domain. Development and
        // tests only (EmailSandboxGate refuses to start a real deployment with it on).
        var sandbox = app.MapGroup("/v1/dev").AllowAnonymous();
        sandbox.MapPost("/inbound-email", DeliverSandboxReply).ExcludeFromDescription();
        sandbox.MapPost("/sending-domains/publish", PublishSandboxDomain).ExcludeFromDescription();
        return app;
    }

    // SendGrid Inbound Parse: multipart/form-data with the headers, the envelope, both bodies and the attachments.
    private static async Task<IResult> ReceiveReply(
        HttpRequest request,
        IOptions<EmailOptions> options,
        InboundEmailIngest ingest,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        CancellationToken cancellationToken)
    {
        if (!BasicCredentialMatches(request.Headers.Authorization.ToString(), options.Value.SendGrid.InboundSecret))
        {
            return Results.Unauthorized();
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return Results.BadRequest();
        }

        var recipients = EnvelopeRecipients(form["envelope"]);
        var from = MailboxAddress.TryParse(form["from"].ToString(), out var sender) ? sender : null;
        if (recipients.Count == 0 || from is null)
        {
            return Results.Ok(); // nothing to deliver, and nothing for SendGrid to retry
        }

        var headers = form["headers"].ToString();
        var attachments = new List<InboundAttachment>();
        foreach (var file in form.Files)
        {
            attachments.Add(new InboundAttachment(file.FileName, await ReadAsync(file, cancellationToken)));
        }

        var mail = new ParsedInboundEmail(
            recipients, from.Address, from.Name, form["subject"].ToString(), form["text"].ToString(), form["html"].ToString(),
            MessageIdOf(headers), IsAutomatic(headers, form["subject"].ToString()), attachments);

        // Whatever the outcome, 200: a reply to nothing of ours, or one stored already, must not be posted again.
        await ingest.IngestAsync(mail, outbox, cancellationToken);
        return Results.Ok();
    }

    // SendGrid's signed event webhook: a JSON array of events. Only bounces and spam complaints on a candidate email act.
    private static async Task<IResult> ReceiveEvents(
        HttpRequest request,
        IOptions<EmailOptions> options,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        TenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SendGrid.EventWebhookPublicKey))
        {
            return Results.NotFound(); // not set up here
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();
        if (!SendGridEventSignature.IsValid(
                options.Value.SendGrid.EventWebhookPublicKey,
                request.Headers[SendGridEventSignature.SignatureHeader].ToString(),
                request.Headers[SendGridEventSignature.TimestampHeader].ToString(),
                body,
                time.GetUtcNow()))
        {
            return Results.Unauthorized();
        }

        List<DeliveryProblem> problems;
        try
        {
            problems = DeliveryProblem.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var db = outbox.DbContext;
        foreach (var byOrganization in problems.GroupBy(p => p.OrganizationId))
        {
            tenant.SetPublicOrganization(byOrganization.Key);
            var now = time.GetUtcNow();
            foreach (var problem in byOrganization)
            {
                // The candidate is the one the email went to, found through the send, and must still have that address.
                var candidate = await (
                    from email in db.ApplicationEmails
                    join application in db.Applications on email.ApplicationId equals application.Id
                    join person in db.Candidates on application.CandidateId equals person.Id
                    where email.Id == problem.EmailId
                    select person).SingleOrDefaultAsync(cancellationToken);
                if (candidate is not null && candidate.NormalizedEmail == Candidate.Normalize(problem.Address)
                    && candidate.FlagEmail(problem.Issue, problem.Reason, now))
                {
                    await outbox.PublishAsync(new CandidateEmailStatusChanged(byOrganization.Key, candidate.Id, problem.Issue.ToString(), now));
                }
            }

            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> DeliverSandboxReply(
        SandboxReply request,
        IOptions<EmailOptions> options,
        InboundEmailIngest ingest,
        IDbContextOutbox<ScreenHireDbContext> outbox,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Sandbox.Enabled)
        {
            return Results.NotFound();
        }

        var mail = new ParsedInboundEmail(
            [request.To], request.From, request.FromName, request.Subject ?? "Re: your email", request.Text, request.Html, request.MessageId,
            request.AutoReply, [.. (request.Attachments ?? []).Select(a => new InboundAttachment(a.FileName, Convert.FromBase64String(a.ContentBase64)))]);
        var outcome = await ingest.IngestAsync(mail, outbox, cancellationToken);
        return Results.Ok(new SandboxReplyResult(outcome.ToString()));
    }

    private static IResult PublishSandboxDomain(SandboxDomain request, IOptions<EmailOptions> options, SandboxDnsZone zone)
    {
        if (!options.Value.Sandbox.Enabled)
        {
            return Results.NotFound();
        }

        zone.Publish(request.Domain.Trim().ToLowerInvariant());
        return Results.Ok();
    }

    /// <summary>Whether <paramref name="authorization"/> is HTTP Basic with the inbound secret as the password.</summary>
    internal static bool BasicCredentialMatches(string authorization, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || !AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !header.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(header.Parameter))
        {
            return false;
        }

        try
        {
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
            var colon = credentials.IndexOf(':', StringComparison.Ordinal);
            return colon >= 0
                && credentials[..colon] == InboundUser
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(credentials[(colon + 1)..]), Encoding.UTF8.GetBytes(secret));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The envelope's recipients (<c>{"to":["a@x"],"from":"b@y"}</c>): the addresses that reached the provider.</summary>
    internal static List<string> EnvelopeRecipients(string? envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(envelope);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Array
                ? [.. to.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Take(20)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string? MessageIdOf(string headers)
    {
        var match = MessageIdHeader().Match(headers);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>An out-of-office or other automatic answer: <c>Auto-Submitted</c> other than <c>no</c>, an auto-reply precedence, or its subject.</summary>
    internal static bool IsAutomatic(string headers, string subject) =>
        AutoSubmittedHeader().IsMatch(headers) || AutoPrecedenceHeader().IsMatch(headers) || headers.Contains("X-Autoreply:", StringComparison.OrdinalIgnoreCase)
        || subject.StartsWith("Automatic reply", StringComparison.OrdinalIgnoreCase) || subject.StartsWith("Out of office", StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > InboundEmailIngest.MaxAttachmentBytes)
        {
            return []; // too large to keep; the ingest counts it as not kept
        }

        using var stream = new MemoryStream();
        await using var content = file.OpenReadStream();
        await content.CopyToAsync(stream, cancellationToken);
        return stream.ToArray();
    }

    [GeneratedRegex(@"^Message-ID:\s*(<[^>\r\n]{1,900}>)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MessageIdHeader();

    [GeneratedRegex(@"^Auto-Submitted:\s*(?!no\b)\S", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AutoSubmittedHeader();

    [GeneratedRegex(@"^Precedence:\s*(auto_reply|auto-reply|bulk|junk)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AutoPrecedenceHeader();

    /// <summary>One event the provider reported that stops us emailing an address.</summary>
    internal sealed record DeliveryProblem(Guid OrganizationId, Guid EmailId, string Address, CandidateEmailIssue Issue, string? Reason)
    {
        /// <summary>
        /// The bounces and complaints in a SendGrid event batch that carry our custom arguments. A <c>bounce</c> of type
        /// <c>blocked</c> is a soft failure and is ignored, as is a <c>dropped</c> email that was dropped for any reason
        /// but a known-bounced address. Everything else (delivered, deferred, opened) is not a problem.
        /// </summary>
        public static List<DeliveryProblem> Parse(ReadOnlySpan<byte> body)
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var problems = new List<DeliveryProblem>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return problems;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var kind = Text(item, "event");
                var reason = Text(item, "reason");
                var issue = kind switch
                {
                    "bounce" when !string.Equals(Text(item, "type"), "blocked", StringComparison.OrdinalIgnoreCase) => CandidateEmailIssue.Bounced,
                    "dropped" when reason?.Contains("Bounced Address", StringComparison.OrdinalIgnoreCase) == true => CandidateEmailIssue.Bounced,
                    "spamreport" => CandidateEmailIssue.SpamComplaint,
                    _ => CandidateEmailIssue.None,
                };
                if (issue != CandidateEmailIssue.None
                    && Guid.TryParse(Text(item, CandidateMailRouting.OrganizationArgument), out var organizationId)
                    && Guid.TryParse(Text(item, CandidateMailRouting.EmailArgument), out var emailId)
                    && Text(item, "email") is { Length: > 0 } address)
                {
                    problems.Add(new DeliveryProblem(organizationId, emailId, address, issue, reason is { Length: > MaxReasonLength } ? reason[..MaxReasonLength] : reason));
                }
            }

            return problems;
        }

        private static string? Text(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

/// <summary>A reply for the sandbox to deliver as if SendGrid had posted it. <c>to</c> is the reply address from the email's Reply-To.</summary>
internal sealed record SandboxReply(
    string To,
    string From,
    string? FromName,
    string? Subject,
    string? Text,
    string? Html,
    string? MessageId,
    bool AutoReply,
    IReadOnlyList<SandboxAttachment>? Attachments);

internal sealed record SandboxAttachment(string FileName, string ContentBase64);

internal sealed record SandboxReplyResult(string Outcome);

internal sealed record SandboxDomain(string Domain);
