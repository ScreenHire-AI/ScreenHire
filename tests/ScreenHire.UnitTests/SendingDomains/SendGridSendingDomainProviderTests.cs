using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.UnitTests.Email;

namespace ScreenHire.UnitTests.SendingDomains;

/// <summary>
/// The SendGrid Domain Authentication client against a stub standing in for SendGrid
/// (https://www.twilio.com/docs/sendgrid/api-reference/domain-authentication): the DNS records it hands the owner, the
/// reply host it registers with Inbound Parse, the reason it surfaces when validation fails, and what a delete tolerates.
/// </summary>
public sealed class SendGridSendingDomainProviderTests : IDisposable
{
    private static readonly Uri InboundUrl = new("https://api.example.test/v1/webhooks/email/inbound");

    private readonly RecordingHandler sendGrid = new();
    private readonly SendGridSendingDomainProvider provider;

    public SendGridSendingDomainProviderTests()
    {
        provider = new SendGridSendingDomainProvider(
            Options.Create(new EmailOptions { SendGrid = new SendGridOptions { ApiKey = "SG.fictional-key", BaseUrl = new Uri("https://sendgrid.example.test/") } }),
            sendGrid);
    }

    public void Dispose() => provider.Dispose();

    private const string CreatedDomain = """
        {
          "id": 1234,
          "domain": "acme.example.org",
          "dns": {
            "mail_cname": { "valid": false, "type": "cname", "host": "em1234.acme.example.org", "data": "u1234.wl.sendgrid.net" },
            "dkim1": { "valid": false, "type": "cname", "host": "s1._domainkey.acme.example.org", "data": "s1.domainkey.u1234.wl.sendgrid.net" },
            "dkim2": { "valid": false, "type": "cname", "host": "s2._domainkey.acme.example.org", "data": "s2.domainkey.u1234.wl.sendgrid.net" }
          }
        }
        """;

    [Fact]
    public async Task A_domain_is_created_with_automated_security_and_lists_two_dkim_cnames_a_return_path_cname_and_the_reply_mx()
    {
        sendGrid.Answer(HttpStatusCode.Created, CreatedDomain).Answer(HttpStatusCode.Created, "{}");

        var provisioned = await provider.CreateAsync(
            new SendingDomainRequest("acme.example.org", "reply.acme.example.org", InboundUrl), TestContext.Current.CancellationToken);

        provisioned.ProviderDomainId.Should().Be("1234");
        provisioned.Records.Should().Equal(
            new SendingDomainRecord(SendingDomainRecordPurpose.Dkim, "CNAME", "s1._domainkey.acme.example.org", "s1.domainkey.u1234.wl.sendgrid.net"),
            new SendingDomainRecord(SendingDomainRecordPurpose.Dkim, "CNAME", "s2._domainkey.acme.example.org", "s2.domainkey.u1234.wl.sendgrid.net"),
            new SendingDomainRecord(SendingDomainRecordPurpose.ReturnPath, "CNAME", "em1234.acme.example.org", "u1234.wl.sendgrid.net"),
            new SendingDomainRecord(SendingDomainRecordPurpose.Replies, "MX", "reply.acme.example.org", "mx.sendgrid.net", Priority: 10));

        var create = sendGrid.Requests[0];
        create.Method.Should().Be("POST");
        create.Path.Should().Be("/v3/whitelabel/domains");
        create.Authorization.Should().Be("Bearer SG.fictional-key");
        var body = JsonDocument.Parse(create.Body).RootElement;
        body.GetProperty("domain").GetString().Should().Be("acme.example.org");
        body.GetProperty("automatic_security").GetBoolean().Should().BeTrue("so the owner adds no SPF TXT record at the apex");
        body.GetProperty("default").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_reply_host_is_registered_with_inbound_parse_to_post_to_our_webhook()
    {
        sendGrid.Answer(HttpStatusCode.Created, CreatedDomain).Answer(HttpStatusCode.Created, "{}");

        await provider.CreateAsync(new SendingDomainRequest("acme.example.org", "reply.acme.example.org", InboundUrl), TestContext.Current.CancellationToken);

        var parse = sendGrid.Requests[1];
        parse.Method.Should().Be("POST");
        parse.Path.Should().Be("/v3/user/webhooks/parse/settings");
        var body = JsonDocument.Parse(parse.Body).RootElement;
        body.GetProperty("hostname").GetString().Should().Be("reply.acme.example.org");
        body.GetProperty("url").GetString().Should().Be(InboundUrl.ToString());
        body.GetProperty("send_raw").GetBoolean().Should().BeFalse("the multipart fields are what the webhook reads");
    }

    [Fact]
    public async Task A_refusal_carries_SendGrids_own_short_reason()
    {
        sendGrid.Answer(HttpStatusCode.BadRequest, """{"errors":[{"message":"The domain is already authenticated by another account."}]}""");

        var create = () => provider.CreateAsync(
            new SendingDomainRequest("acme.example.org", "reply.acme.example.org", InboundUrl), TestContext.Current.CancellationToken);

        (await create.Should().ThrowAsync<SendingDomainProviderException>()).Which.Message
            .Should().Be("The domain is already authenticated by another account.");
    }

    [Fact]
    public async Task A_refusal_without_a_reason_names_only_the_status()
    {
        sendGrid.Answer(HttpStatusCode.BadGateway, "<html>gateway</html>");

        var create = () => provider.CreateAsync(
            new SendingDomainRequest("acme.example.org", "reply.acme.example.org", InboundUrl), TestContext.Current.CancellationToken);

        (await create.Should().ThrowAsync<SendingDomainProviderException>()).Which.Message.Should().Be("SendGrid refused the request (502).");
    }

    [Fact]
    public async Task Validation_posts_to_the_domains_validate_path_and_reports_valid()
    {
        sendGrid.Answer(HttpStatusCode.OK, """{"id":1234,"valid":true,"validation_results":{}}""");

        var result = await provider.ValidateAsync("1234", TestContext.Current.CancellationToken);

        result.Should().Be(new ProviderValidation(true, null));
        sendGrid.Requests[0].Method.Should().Be("POST");
        sendGrid.Requests[0].Path.Should().Be("/v3/whitelabel/domains/1234/validate");
    }

    [Fact]
    public async Task A_failed_validation_surfaces_the_first_failing_records_reason()
    {
        sendGrid.Answer(HttpStatusCode.OK, """
            {
              "id": 1234,
              "valid": false,
              "validation_results": {
                "mail_cname": { "valid": true, "reason": null },
                "dkim1": { "valid": false, "reason": "Expected CNAME for s1._domainkey.acme.example.org to point to s1.domainkey.u1234.wl.sendgrid.net." },
                "dkim2": { "valid": false, "reason": "Could not find DKIM record." }
              }
            }
            """);

        var result = await provider.ValidateAsync("1234", TestContext.Current.CancellationToken);

        result.Valid.Should().BeFalse();
        result.Message.Should().Be("Expected CNAME for s1._domainkey.acme.example.org to point to s1.domainkey.u1234.wl.sendgrid.net.");
    }

    [Fact]
    public async Task A_failed_validation_with_no_reason_has_no_message()
    {
        sendGrid.Answer(HttpStatusCode.OK, """{"valid":false}""");

        (await provider.ValidateAsync("1234", TestContext.Current.CancellationToken)).Should().Be(new ProviderValidation(false, null));
    }

    [Fact]
    public async Task A_long_reason_is_cut_to_300_characters()
    {
        var reason = new string('x', 500);
        sendGrid.Answer(HttpStatusCode.OK, "{\"valid\":false,\"validation_results\":{\"dkim1\":{\"valid\":false,\"reason\":\"" + reason + "\"}}}");

        (await provider.ValidateAsync("1234", TestContext.Current.CancellationToken)).Message!.Length.Should().Be(300);
    }

    [Fact]
    public async Task Deleting_removes_the_domain_and_its_inbound_parse_host()
    {
        sendGrid.Answer(HttpStatusCode.NoContent).Answer(HttpStatusCode.NoContent);

        await provider.DeleteAsync("1234", "reply.acme.example.org", TestContext.Current.CancellationToken);

        sendGrid.Requests.Select(r => (r.Method, r.Path)).Should().Equal(
            ("DELETE", "/v3/whitelabel/domains/1234"),
            ("DELETE", "/v3/user/webhooks/parse/settings/reply.acme.example.org"));
    }

    [Fact]
    public async Task Deleting_a_domain_SendGrid_no_longer_has_is_not_an_error()
    {
        sendGrid.Answer(HttpStatusCode.NotFound).Answer(HttpStatusCode.NotFound);

        var delete = () => provider.DeleteAsync("1234", "reply.acme.example.org", TestContext.Current.CancellationToken);

        await delete.Should().NotThrowAsync();
        sendGrid.Requests.Should().HaveCount(2, "the second host is still removed");
    }

    [Fact]
    public async Task A_delete_SendGrid_refuses_for_another_reason_throws()
    {
        sendGrid.Answer(HttpStatusCode.Forbidden, """{"errors":[{"message":"access forbidden"}]}""");

        var delete = () => provider.DeleteAsync("1234", "reply.acme.example.org", TestContext.Current.CancellationToken);

        (await delete.Should().ThrowAsync<SendingDomainProviderException>()).Which.Message.Should().Be("access forbidden");
    }
}
