using ScreenHire.Api.Auth;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Auth;

public sealed class InvitationsTests
{
    [Fact]
    public void A_token_names_its_organization_and_is_url_safe_and_unique()
    {
        var organizationId = Guid.CreateVersion7();

        var token = Invitations.NewToken(organizationId);

        token.Should().MatchRegex("^[A-Za-z0-9_-]{64}$");
        Invitations.OrganizationOf(token).Should().Be(organizationId);
        Invitations.NewToken(organizationId).Should().NotBe(token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-invitation")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")] // 16 bytes: an organization id with no secret
    [InlineData("a sign-in token with spaces")]
    public void Anything_else_names_no_organization(string token) =>
        Invitations.OrganizationOf(token).Should().BeNull();

    [Fact]
    public void The_email_encodes_the_organization_name_and_keeps_the_subject_on_one_line()
    {
        var link = new Uri("https://app.example.com/invitations/accept#invitation=abc");

        var message = Invitations.Compose(
            "invitee@example.com", "Acme <b>Labs</b>\r\nBcc: x", "Avery Quinn", Invitations.RoleWords(RoleAccess.BuiltIn(OrganizationRole.Admin)), link, TimeSpan.FromDays(7));

        message.Subject.Should().NotContain("\n").And.NotContain("\r");
        message.HtmlBody.Should().Contain("Acme &lt;b&gt;Labs&lt;/b&gt;").And.NotContain("<b>Labs</b>");
        message.TextBody.Should().Contain(link.ToString()).And.Contain("as an admin").And.Contain("7 days");
    }
}
