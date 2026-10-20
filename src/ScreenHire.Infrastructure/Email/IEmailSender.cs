namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// Sends one email. The local adapter is <see cref="SmtpEmailSender"/> against Mailpit; the production provider is a
/// later decision and implements this same interface. Callers never log the recipient or the body.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>An email with a plain-text body and an optional HTML alternative.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);
