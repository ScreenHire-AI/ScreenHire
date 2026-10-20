using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ScreenHire.Infrastructure.Email;

public static class EmailRegistration
{
    /// <summary>The SMTP connection string the AppHost passes from its Mailpit container.</summary>
    public const string SmtpConnectionName = "mailpit";

    /// <summary>
    /// Registers <see cref="SmtpEmailSender"/> when an SMTP server is configured. With none, no
    /// <see cref="IEmailSender"/> is registered and features that send email (the magic link) are hidden, not broken.
    /// </summary>
    public static TBuilder AddScreenHireEmail<TBuilder>(this TBuilder builder, string from)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetConnectionString(SmtpConnectionName) is { Length: > 0 } server)
        {
            builder.Services.AddOptions<SmtpEmailOptions>().Configure(options =>
            {
                options.Server = ParseServer(server);
                options.From = from;
            });
            builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return builder;
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
