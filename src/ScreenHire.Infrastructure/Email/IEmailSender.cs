namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// Sends one email. The local adapter is <see cref="SmtpEmailSender"/> against Mailpit; the production adapter is
/// <see cref="SendGridEmailSender"/> (ADR-0021), and another provider (Azure Communication Services Email) would
/// implement this same interface. Callers never log the recipient or the body.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// An email with a plain-text body and an optional HTML alternative. <paramref name="From"/> overrides the sender's
/// default From address (an organization's verified sending domain), <paramref name="ReplyTo"/> is where a candidate's
/// answer goes, and <paramref name="CustomArgs"/> are ids the provider echoes back in its event webhook, so a bounce can
/// be matched to an organization and an email. All three are optional; the SMTP adapter ignores the custom arguments.
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string? From = null,
    string? ReplyTo = null,
    IReadOnlyDictionary<string, string>? CustomArgs = null);
