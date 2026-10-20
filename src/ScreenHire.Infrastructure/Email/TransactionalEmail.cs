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

    public Task SendAsync(string templateId, string to, EmailTemplateModel model, CancellationToken cancellationToken) =>
        (sender ?? throw new InvalidOperationException("No email sender is configured."))
            .SendAsync(templates.Render(templateId, to, model), cancellationToken);
}
