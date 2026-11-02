using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MimeKit;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The SendGrid sender against a stub standing in for SendGrid (https://www.twilio.com/docs/sendgrid/api-reference/mail-send):
/// the request it builds for a candidate's email, and how it fails. No network, no key.
/// </summary>
public sealed class SendGridEmailSenderTests : IDisposable
{
    private const string DefaultFrom = "ScreenHire <no-reply@example.com>";

    private readonly RecordingHandler sendGrid = new();
    private readonly SendGridEmailSender sender;

    public SendGridEmailSenderTests()
    {
        var options = Options.Create(new EmailOptions
        {
            SendGrid = new SendGridOptions { ApiKey = "SG.fictional-key", BaseUrl = new Uri("https://sendgrid.example.test/") },
        });
        sender = new SendGridEmailSender(options, DefaultFrom, sendGrid);
    }

    public void Dispose() => sender.Dispose();

    private async Task<JsonElement> SentAsync(EmailMessage message)
    {
        sendGrid.Answer(HttpStatusCode.Accepted);
        await sender.SendAsync(message, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(sendGrid.Requests[^1].Body).RootElement;
    }

    [Fact]
    public async Task An_email_is_posted_to_the_mail_send_api_with_the_bearer_key()
    {
        await SentAsync(new EmailMessage("casey@example.com", "Next steps", "Hello"));

        var request = sendGrid.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("POST");
        request.Path.Should().Be("/v3/mail/send");
        request.Authorization.Should().Be("Bearer SG.fictional-key");
    }

    [Fact]
    public async Task Without_a_route_the_email_goes_from_the_default_address_with_no_reply_to_and_no_custom_arguments()
    {
        var body = await SentAsync(new EmailMessage("casey@example.com", "Next steps", "Hello"));

        body.GetProperty("from").GetProperty("email").GetString().Should().Be("no-reply@example.com");
        body.GetProperty("from").GetProperty("name").GetString().Should().Be("ScreenHire");
        body.TryGetProperty("reply_to", out _).Should().BeFalse();
        var personalization = body.GetProperty("personalizations")[0];
        personalization.GetProperty("to")[0].GetProperty("email").GetString().Should().Be("casey@example.com");
        personalization.TryGetProperty("custom_args", out _).Should().BeFalse();
        body.GetProperty("subject").GetString().Should().Be("Next steps");
    }

    [Fact]
    public async Task The_organizations_name_and_address_are_the_from_and_the_reply_address_is_the_reply_to()
    {
        var from = new MailboxAddress("Brightwater, Studio", "hiring@brightwater.example.org").ToString();

        var body = await SentAsync(new EmailMessage(
            "casey@example.com", "Next steps", "Hello", From: from, ReplyTo: "reply+abc@reply.brightwater.example.org"));

        body.GetProperty("from").GetProperty("email").GetString().Should().Be("hiring@brightwater.example.org");
        body.GetProperty("from").GetProperty("name").GetString().Should().Be("Brightwater, Studio");
        body.GetProperty("reply_to").GetProperty("email").GetString().Should().Be("reply+abc@reply.brightwater.example.org");
    }

    [Fact]
    public async Task Custom_arguments_ride_on_the_personalization_so_the_event_webhook_can_name_the_send()
    {
        var body = await SentAsync(new EmailMessage(
            "casey@example.com", "Next steps", "Hello",
            CustomArgs: new Dictionary<string, string> { ["organizationId"] = "org-1", ["emailId"] = "email-1" }));

        var args = body.GetProperty("personalizations")[0].GetProperty("custom_args");
        args.GetProperty("organizationId").GetString().Should().Be("org-1");
        args.GetProperty("emailId").GetString().Should().Be("email-1");
    }

    [Fact]
    public async Task The_plain_text_part_comes_before_the_html_part()
    {
        var body = await SentAsync(new EmailMessage("casey@example.com", "Next steps", "Plain words", "<p>Html words</p>"));

        var content = body.GetProperty("content");
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("type").GetString().Should().Be("text/plain");
        content[0].GetProperty("value").GetString().Should().Be("Plain words");
        content[1].GetProperty("type").GetString().Should().Be("text/html");
        content[1].GetProperty("value").GetString().Should().Be("<p>Html words</p>");
    }

    [Fact]
    public async Task An_email_with_no_html_sends_only_the_plain_text_part()
    {
        var body = await SentAsync(new EmailMessage("casey@example.com", "Next steps", "Plain words"));

        body.GetProperty("content").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Click_open_and_subscription_tracking_are_off_because_ScreenHire_adds_none_to_a_candidates_email()
    {
        var body = await SentAsync(new EmailMessage("casey@example.com", "Next steps", "Hello", "<p>Hello</p>"));

        var tracking = body.GetProperty("tracking_settings");
        tracking.GetProperty("click_tracking").GetProperty("enable").GetBoolean().Should().BeFalse();
        tracking.GetProperty("click_tracking").GetProperty("enable_text").GetBoolean().Should().BeFalse();
        tracking.GetProperty("open_tracking").GetProperty("enable").GetBoolean().Should().BeFalse();
        tracking.GetProperty("subscription_tracking").GetProperty("enable").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_email_throws_with_the_status_and_never_the_recipient_or_the_body(HttpStatusCode status)
    {
        sendGrid.Answer(status, "{\"errors\":[{\"message\":\"Invalid casey@example.com\"}]}");

        var send = () => sender.SendAsync(
            new EmailMessage("casey@example.com", "A private subject", "A private body"), TestContext.Current.CancellationToken);

        var thrown = (await send.Should().ThrowAsync<EmailSendException>()).Which;
        thrown.StatusCode.Should().Be((int)status);
        thrown.Message.Should().Contain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .And.NotContain("casey@example.com").And.NotContain("private");
    }
}
