using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ScreenHire.Infrastructure.Email;

/// <summary>Where <see cref="SmtpEmailSender"/> delivers mail and who it is from.</summary>
public sealed class SmtpEmailOptions
{
    /// <summary>The SMTP server as <c>smtp://host:port</c> (Aspire's Mailpit connection string format).</summary>
    public required Uri Server { get; set; }

    /// <summary>The From address, e.g. <c>ScreenHire &lt;no-reply@example.com&gt;</c>.</summary>
    public required string From { get; set; }
}

/// <summary>
/// Plain SMTP through MailKit (MIT). Locally it delivers to Mailpit, which needs neither TLS nor credentials; a real
/// relay would add both here.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(settings.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Server.Host, settings.Server.Port, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
