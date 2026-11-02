using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ScreenHire.Infrastructure.Email;

/// <summary>Why SendGrid did not take an email. Carries the status only: never the recipient or the body.</summary>
public sealed class EmailSendException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>
/// Sends through SendGrid's v3 Mail Send API (https://www.twilio.com/docs/sendgrid/api-reference/mail-send), bought
/// through the Azure Marketplace so email is billed on the Azure subscription (ADR-0022). Click and open tracking are
/// off: ScreenHire adds no tracking to a candidate's email (docs/architecture/email.md).
/// </summary>
/// <remarks>
/// One long-lived <see cref="HttpClient"/> of its own rather than one from the HTTP client factory, as
/// <c>SlackApiClient</c> does: the service defaults add the standard resilience handler to every factory client, and
/// its retries could send one email twice. The worker's retry policy is the one place a failed send is tried again.
/// </remarks>
public sealed class SendGridEmailSender : IEmailSender, IDisposable
{
    private readonly string defaultFrom;
    private readonly HttpClient client;

    public SendGridEmailSender(IOptions<EmailOptions> options, string defaultFrom)
        : this(options, defaultFrom, new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
    }

    /// <summary>With <paramref name="handler"/> in place of the network, for tests.</summary>
    public SendGridEmailSender(IOptions<EmailOptions> options, string defaultFrom, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.defaultFrom = defaultFrom;
        var settings = options.Value.SendGrid;
        client = new HttpClient(handler) { BaseAddress = settings.BaseUrl, Timeout = settings.Timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send")
        {
            Content = new StringContent(Body(message, defaultFrom), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new EmailSendException($"SendGrid refused the email ({(int)response.StatusCode}).", (int)response.StatusCode);
        }
    }

    public void Dispose() => client.Dispose();

    /// <summary>The Mail Send request body for <paramref name="message"/>.</summary>
    internal static string Body(EmailMessage message, string defaultFrom)
    {
        var personalization = new Dictionary<string, object> { ["to"] = new[] { Address(message.To) } };
        if (message.CustomArgs is { Count: > 0 } args)
        {
            personalization["custom_args"] = args;
        }

        var content = new List<object> { new { type = "text/plain", value = message.TextBody } };
        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            content.Add(new { type = "text/html", value = message.HtmlBody });
        }

        var body = new Dictionary<string, object>
        {
            ["personalizations"] = new[] { personalization },
            ["from"] = Address(message.From ?? defaultFrom),
            ["subject"] = message.Subject,
            ["content"] = content,
            ["tracking_settings"] = new
            {
                click_tracking = new { enable = false, enable_text = false },
                open_tracking = new { enable = false },
                subscription_tracking = new { enable = false },
            },
        };
        if (message.ReplyTo is { Length: > 0 } replyTo)
        {
            body["reply_to"] = Address(replyTo);
        }

        return JsonSerializer.Serialize(body);
    }

    private static Dictionary<string, string> Address(string value)
    {
        var mailbox = MailboxAddress.Parse(value);
        var address = new Dictionary<string, string> { ["email"] = mailbox.Address };
        if (!string.IsNullOrWhiteSpace(mailbox.Name))
        {
            address["name"] = mailbox.Name;
        }

        return address;
    }
}
