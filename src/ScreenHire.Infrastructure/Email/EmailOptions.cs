using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// Email settings, <c>ScreenHire:Email</c> (docs/architecture/email.md). The AppHost gives the API and the worker the
/// same values. Nothing here is on until configured: with no SendGrid key the SMTP adapter (Mailpit locally) sends, and
/// with no reply host and no sandbox a candidate's email carries no reply address.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "ScreenHire:Email";

    /// <summary>The default From address, e.g. <c>ScreenHire &lt;no-reply@example.com&gt;</c>.</summary>
    public string? From { get; set; }

    /// <summary>
    /// The host replies arrive on when an organization has no sending domain of its own, e.g.
    /// <c>reply.example.com</c>. Its MX record points at the provider's inbound service. A subdomain, never a domain
    /// that carries other mail.
    /// </summary>
    public string? ReplyDomain { get; set; }

    /// <summary>
    /// The key that signs a reply address, from the <c>email-reply-key</c> parameter. Never committed. Without it no
    /// reply address is made and none is accepted.
    /// </summary>
    public string? ReplyKey { get; set; }

    public SendGridOptions SendGrid { get; set; } = new();

    /// <summary>The local fakes for inbound mail and sending domains: Development and tests only.</summary>
    public EmailSandboxOptions Sandbox { get; set; } = new();

    /// <summary>Whether a candidate's email gets a reply address, and what the address is built on.</summary>
    public bool RepliesEnabled =>
        !string.IsNullOrWhiteSpace(ReplyKey) && (Sandbox.Enabled || !string.IsNullOrWhiteSpace(ReplyDomain));

    /// <summary>The reply host for an organization without its own domain.</summary>
    public string DefaultReplyHost => string.IsNullOrWhiteSpace(ReplyDomain) ? "reply.localhost" : ReplyDomain.Trim().ToLowerInvariant();
}

/// <summary>SendGrid, bought through the Azure Marketplace (ADR-0021).</summary>
public sealed class SendGridOptions
{
    /// <summary>The API key, from the <c>sendgrid-api-key</c> parameter. Never committed. Setting it turns SendGrid on.</summary>
    public string? ApiKey { get; set; }

    /// <summary>SendGrid's API. Configurable only so a test can point the real client at a stand-in.</summary>
    public Uri BaseUrl { get; set; } = new("https://api.sendgrid.com/");

    /// <summary>
    /// The secret in the inbound webhook's path. Inbound Parse cannot sign its posts, so the path is the first check and
    /// the signed reply address the second.
    /// </summary>
    public string? InboundSecret { get; set; }

    /// <summary>
    /// The public key SendGrid shows for the signed event webhook (base64, DER). Not a secret. Without it the event
    /// webhook refuses every post.
    /// </summary>
    public string? EventWebhookPublicKey { get; set; }

    /// <summary>How long one call to SendGrid may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class EmailSandboxOptions
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Refuses to start a host with the email sandbox outside Development: its inbound route accepts a reply from anyone and
/// its sending domains verify without DNS.
/// </summary>
public sealed class EmailSandboxGate(IHostEnvironment environment) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Sandbox.Enabled && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail("ScreenHire:Email:Sandbox:Enabled is true outside Development. The email sandbox is for local runs and tests only.")
            : ValidateOptionsResult.Success;
    }
}
