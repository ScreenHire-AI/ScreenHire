using System.Net;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Automations;

public sealed class TransactionalEmailTests
{
    private static readonly EmailTemplates Templates = new();

    [Theory]
    [InlineData(EmailTemplateIds.ApplicationReceived, "We received your application for Warehouse lead")]
    [InlineData(EmailTemplateIds.StageChange, "An update on your application for Warehouse lead")]
    [InlineData(EmailTemplateIds.ApplicationRejected, "Your application for Warehouse lead")]
    public void Every_template_renders_a_subject_a_text_body_and_an_html_body(string templateId, string subject)
    {
        var email = Templates.Render(templateId, "sam@example.com", new("Sam Rivera", "Warehouse lead", "Brightwater", "Phone screen", "No right to work."));

        email.To.Should().Be("sam@example.com");
        email.Subject.Should().Be(subject);
        email.TextBody.Should().Contain("Hi Sam Rivera").And.Contain("Brightwater");
        email.HtmlBody.Should().Contain("<strong>Warehouse lead</strong>");
    }

    [Fact]
    public void The_rejection_email_tells_the_candidate_why()
    {
        var email = Templates.Render(EmailTemplateIds.ApplicationRejected, "sam@example.com", new("Sam", "Driver", "Brightwater", Reason: "This role needs a driving licence."));

        email.TextBody.Should().Contain("The reason: This role needs a driving licence.")
            .And.Contain("made automatically").And.Contain("a person will review your application");
    }

    [Fact]
    public void Values_cannot_inject_markup_or_extra_headers()
    {
        var email = Templates.Render(
            EmailTemplateIds.StageChange, "sam@example.com", new("<script>alert(1)</script>", "Lead\r\nBcc: someone@example.org", "Org", "Screening"));

        email.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        email.Subject.Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void Every_template_id_the_validator_accepts_has_template_files()
    {
        foreach (var id in EmailTemplateIds.All)
        {
            Templates.Invoking(t => t.Render(id, "a@example.com", new("A", "B", "C"))).Should().NotThrow();
        }
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void Webhooks_reach_public_addresses_only(string address, bool allowed) =>
        WebhookAddresses.IsPublic(IPAddress.Parse(address)).Should().Be(allowed);
}
