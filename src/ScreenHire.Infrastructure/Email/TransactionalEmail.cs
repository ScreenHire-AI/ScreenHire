using Microsoft.Extensions.DependencyInjection;

namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// Sends a templated email (<see cref="EmailTemplates"/>) through <see cref="IEmailSender"/> when one is registered.
/// Without one, <see cref="IsConfigured"/> is false and callers skip the email rather than fail: a provider with no
/// configuration is hidden, never an error. Always registered, so a handler can depend on it in every host.
/// </summary>
public sealed class TransactionalEmail(IServiceProvider services, EmailTemplates templates)
{
    private readonly IEmailSender? sender = services.GetService<IEmailSender>();

    public bool IsConfigured => sender is not null;

    /// <summary>
    /// Renders <paramref name="content"/> as template <paramref name="templateId"/> for <paramref name="to"/> under
    /// <paramref name="header"/>, sends it, and returns what was sent.
    /// </summary>
    public async Task<EmailMessage> SendAsync(
        string templateId,
        EmailTemplateContent content,
        string to,
        EmailTemplateModel model,
        EmailHeader? header,
        CancellationToken cancellationToken)
    {
        var message = templates.Render(content, to, model, templateId, header);
        await (sender ?? throw new InvalidOperationException("No email sender is configured.")).SendAsync(message, cancellationToken);
        return message;
    }

    /// <summary>Sends <paramref name="message"/> as it is: ScreenHire's own wording to a teammate, not a template.</summary>
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        (sender ?? throw new InvalidOperationException("No email sender is configured.")).SendAsync(message, cancellationToken);
}
