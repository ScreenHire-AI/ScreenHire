using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Infrastructure.Email;

public static class EmailRegistration
{
    /// <summary>The SMTP connection string the AppHost passes from its Mailpit container.</summary>
    public const string SmtpConnectionName = "mailpit";

    /// <summary>
    /// Registers the email sender this host is configured for: <see cref="SendGridEmailSender"/> when
    /// <c>ScreenHire:Email:SendGrid:ApiKey</c> is set, otherwise <see cref="SmtpEmailSender"/> when an SMTP server is
    /// configured. With neither, no <see cref="IEmailSender"/> is registered and features that send email (the magic
    /// link) are hidden, not broken. <paramref name="from"/> is the default From address; <c>ScreenHire:Email:From</c>
    /// overrides it.
    /// </summary>
    public static TBuilder AddScreenHireEmail<TBuilder>(this TBuilder builder, string from)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddScreenHireEmailOptions();
        var configured = builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new();
        var defaultFrom = configured.From is { Length: > 0 } overridden ? overridden : from;

        if (configured.SendGrid.Enabled)
        {
            builder.Services.AddSingleton<IEmailSender>(services =>
                new SendGridEmailSender(services.GetRequiredService<IOptions<EmailOptions>>(), defaultFrom));
        }
        else if (builder.Configuration.GetConnectionString(SmtpConnectionName) is { Length: > 0 } server)
        {
            builder.Services.AddOptions<SmtpEmailOptions>().Configure(options =>
            {
                options.Server = ParseServer(server);
                options.From = defaultFrom;
            });
            builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return builder;
    }

    /// <summary>
    /// The email settings and the reply addresses, in every host (the API, the worker, a test): reply addresses and the
    /// sandbox gate do not depend on which sender is registered.
    /// </summary>
    public static IServiceCollection AddScreenHireEmailOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmailOptions>, EmailSandboxGate>());
        services.TryAddSingleton<ReplyAddresses>();
        return services;
    }

    /// <summary>Accepts <c>smtp://host:port</c> or the <c>Endpoint=smtp://host:port</c> form Aspire integrations emit.</summary>
    internal static Uri ParseServer(string connectionString)
    {
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        var parts = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return parts.TryGetValue("Endpoint", out var endpoint) && Uri.TryCreate(endpoint?.ToString(), UriKind.Absolute, out uri)
            ? uri
            : throw new InvalidOperationException($"ConnectionStrings:{SmtpConnectionName} must be smtp://host:port or Endpoint=smtp://host:port.");
    }
}
