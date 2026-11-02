using ScreenHire.Api.Configuration;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// Which domains an organization may send as, and the address the email provider posts replies to
/// (docs/architecture/email.md). A company cannot claim a mail provider's domain or ScreenHire's own.
/// </summary>
public sealed class SendingDomainEndpointsTests
{
    private static readonly DomainOptions Domains = new()
    {
        Scheme = "https",
        BaseDomain = "product.example.com",
        AppHost = "app.product.example.com",
        ApiHost = "api.product.example.com",
        CareersDomain = "careers.example.net",
        CareersHostPattern = "{tenant}.careers.example.net",
    };

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("googlemail.com")]
    [InlineData("outlook.com")]
    [InlineData("yahoo.com")]
    [InlineData("icloud.com")]
    [InlineData("proton.me")]
    [InlineData("product.example.com")]
    [InlineData("mail.product.example.com")]
    [InlineData("careers.example.net")]
    [InlineData("acme.careers.example.net")]
    public void A_mail_providers_domain_or_ScreenHires_own_is_not_yours(string domain)
    {
        SendingDomainEndpoints.IsNotYours(domain, Domains).Should().BeTrue();
    }

    [Theory]
    [InlineData("acme.example.org")]
    [InlineData("example.com")]
    [InlineData("notproduct.example.com")]
    [InlineData("gmail.com.acme.example.org")]
    [InlineData("mygmail.com")]
    [InlineData("example.net")]
    public void A_companys_own_domain_is_yours_even_when_it_ends_like_one_of_ours(string domain)
    {
        SendingDomainEndpoints.IsNotYours(domain, Domains).Should().BeFalse();
    }

    [Fact]
    public void A_deployment_with_no_configured_hosts_still_refuses_mail_providers_and_does_not_refuse_everything()
    {
        var bare = new DomainOptions { BaseDomain = "", CareersDomain = "" };

        SendingDomainEndpoints.IsNotYours("gmail.com", bare).Should().BeTrue();
        SendingDomainEndpoints.IsNotYours("acme.example.org", bare).Should().BeFalse();
    }

    [Fact]
    public void The_sandbox_posts_nowhere_real_and_needs_no_secret()
    {
        var url = SendingDomainEndpoints.InboundUrl(SendingDomainMode.Sandbox, new DomainOptions { ApiHost = "" }, new EmailOptions());

        url.Should().NotBeNull();
        url!.Host.Should().Be("sandbox.example.test");
        url.AbsolutePath.Should().Be("/v1/webhooks/email/inbound");
    }

    [Fact]
    public void With_SendGrid_the_inbound_url_is_on_the_api_host_with_the_secret_as_the_basic_password()
    {
        var email = new EmailOptions { SendGrid = new SendGridOptions { InboundSecret = "se/cr et" } };

        var url = SendingDomainEndpoints.InboundUrl(SendingDomainMode.SendGrid, Domains, email)!;

        url.Scheme.Should().Be("https");
        url.Host.Should().Be("api.product.example.com");
        url.AbsolutePath.Should().Be("/v1/webhooks/email/inbound");
        url.UserInfo.Should().Be("sendgrid:se%2Fcr%20et");
    }

    [Fact]
    public void With_SendGrid_and_no_secret_or_no_api_host_there_is_no_inbound_url()
    {
        SendingDomainEndpoints.InboundUrl(SendingDomainMode.SendGrid, Domains, new EmailOptions()).Should().BeNull();
        SendingDomainEndpoints.InboundUrl(
            SendingDomainMode.SendGrid, new DomainOptions { ApiHost = "" }, new EmailOptions { SendGrid = new SendGridOptions { InboundSecret = "secret" } })
            .Should().BeNull();
    }
}
