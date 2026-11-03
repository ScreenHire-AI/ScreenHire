using ScreenHire.Application.Applications;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The email the person who sent a candidate an email gets when the candidate answers, and how a mail route is applied to
/// an email. ScreenHire's own fixed wording: the reply's text never leaves ScreenHire.
/// </summary>
public sealed class ReplyEmailTests
{
    private static readonly Uri Link = new("https://app.example.com/applications/0197a3c4-5b6d-7e8f-9a0b-1c2d3e4f5a6b?tab=messages");

    [Fact]
    public void The_notification_names_who_replied_and_where_and_links_to_the_messages_tab()
    {
        var email = ReplyEmail.Compose("robin@example.com", "Casey Morgan", "Platform engineer", "Brightwater Studio", Link);

        email.To.Should().Be("robin@example.com");
        email.Subject.Should().Be("Casey Morgan replied");
        email.TextBody.Should().Contain("Casey Morgan replied to your email about Platform engineer at Brightwater Studio.")
            .And.Contain(Link.ToString());
        email.HtmlBody.Should().Contain("<a href=\"").And.Contain("Read the reply");
    }

    [Fact]
    public void Names_in_the_html_part_are_encoded()
    {
        var email = ReplyEmail.Compose("robin@example.com", "Casey <b>Morgan</b>", "Lead & guide", "Bright \"water\"", Link);

        email.HtmlBody.Should().NotContain("<b>").And.Contain("Casey &lt;b&gt;Morgan&lt;/b&gt;").And.Contain("Lead &amp; guide");
    }

    [Fact]
    public void The_messages_path_is_the_applications_messages_tab()
    {
        var id = Guid.Parse("0197a3c4-5b6d-7e8f-9a0b-1c2d3e4f5a6b");

        ReplyEmail.MessagesPath(id).Should().Be($"/applications/{id}?tab=messages");
    }

    [Fact]
    public void A_route_adds_its_from_reply_to_and_custom_arguments_to_an_email()
    {
        var route = new MailRoute("Brightwater <hiring@brightwater.example.org>", "reply+x@reply.brightwater.example.org", new Dictionary<string, string> { ["emailId"] = "e1" });

        var routed = route.Apply(new EmailMessage("casey@example.com", "Subject", "Text"));

        routed.From.Should().Be("Brightwater <hiring@brightwater.example.org>");
        routed.ReplyTo.Should().Be("reply+x@reply.brightwater.example.org");
        routed.CustomArgs.Should().ContainKey("emailId");
        routed.To.Should().Be("casey@example.com");
    }

    [Fact]
    public void A_route_with_nothing_set_changes_nothing()
    {
        var original = new EmailMessage("casey@example.com", "Subject", "Text", From: "ScreenHire <no-reply@example.com>");

        MailRoute.None.Apply(original).Should().Be(original);
    }
}
