using System.Text.RegularExpressions;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The top of every email to a candidate (docs/architecture/branding.md): the logo as a plain image at one address for
/// everyone, never a tracking pixel, or the organization's name; on the second colour's band, or on white.
/// </summary>
public sealed partial class EmailHeaderTests
{
    private const string Board = "https://brightwater.careers.example.com";

    private static readonly EmailTemplates Templates = new();

    [Fact]
    public void The_logo_is_one_plain_image_at_the_same_address_for_every_candidate()
    {
        var (organization, logo) = Branded();
        var header = EmailHeader.For(organization, logo, Board);

        var sam = Templates.Render(EmailTemplateIds.StageChange, "sam@example.com", Model("Sam Rivera"), header);
        var ana = Templates.Render(EmailTemplateIds.StageChange, "ana@example.com", Model("Ana Silva"), header);

        var src = $"{Board}/brand/images/{logo.Id:D}.png";
        Images().Matches(sam.HtmlBody!).Should().ContainSingle().Which.Groups["src"].Value.Should().Be(src);
        Images().Matches(ana.HtmlBody!).Should().ContainSingle().Which.Groups["src"].Value.Should().Be(src);
        src.Should().NotContain("?", "no query string that could name a recipient");
        sam.HtmlBody.Should().Contain("alt=\"Brightwater Studio\"").And.Contain("width=\"160\" height=\"40\"");
        sam.TextBody.Should().NotContain("brand/images", "the text part never carries the header");
    }

    [Fact]
    public void Without_a_logo_the_header_is_the_organizations_name()
    {
        var organization = new Organization { Name = "Brightwater <Studio>", Slug = "brightwater", HomeRegion = "eu" };

        var email = Templates.Render(EmailTemplateIds.StageChange, "sam@example.com", Model("Sam Rivera"), EmailHeader.For(organization, null, Board));

        email.HtmlBody.Should().NotContain("<img").And.Contain("Brightwater &lt;Studio&gt;</strong>").And.Contain("border-bottom: 1px solid");
    }

    [Fact]
    public void The_band_takes_the_second_colour_with_text_that_reads_on_it()
    {
        var organization = new Organization { Name = "Brightwater Studio", Slug = "brightwater", HomeRegion = "eu" };
        organization.SetBranding(null, "#1f4e5a", null, null, null, null);

        var email = Templates.Render(EmailTemplateIds.StageChange, "sam@example.com", Model("Sam Rivera"), EmailHeader.For(organization, null, Board));

        email.HtmlBody.Should().Contain("background: #1f4e5a;").And.Contain("color: #ffffff;");
    }

    [Fact]
    public void A_logo_still_scanning_or_not_in_use_is_left_out()
    {
        var (organization, logo) = Branded();
        var scanning = new BrandImage
        {
            Id = logo.Id, OrganizationId = logo.OrganizationId, Kind = BrandImageKind.Logo, BlobName = "x", ContentType = "image/png", Width = 400, Height = 100,
        };
        var other = new BrandImage
        {
            Id = Guid.NewGuid(), OrganizationId = logo.OrganizationId, Kind = BrandImageKind.Logo, BlobName = "y", ContentType = "image/png", Width = 400, Height = 100,
        };
        other.RecordScan(MalwareScanStatus.Clean, DateTimeOffset.UnixEpoch);

        EmailHeader.For(organization, scanning, Board).Logo.Should().BeNull();
        EmailHeader.For(organization, other, Board).Logo.Should().BeNull();
    }

    [Theory]
    [InlineData(400, 100, 160, 40)]
    [InlineData(1200, 100, 240, 20)]
    [InlineData(80, 20, 80, 20)]
    public void A_logo_is_scaled_down_to_fit_the_header_never_up(int width, int height, int drawnWidth, int drawnHeight)
    {
        EmailLogo.Scaled("https://example.com/l.png", width, height).Should().Be(new EmailLogo("https://example.com/l.png", drawnWidth, drawnHeight));
    }

    // The template's own markup is still text: the header's logo is the only image an email carries.
    [Fact]
    public void Markup_in_a_template_never_becomes_a_second_image()
    {
        var (organization, logo) = Branded();
        var email = Templates.Render(
            new EmailTemplateContent("Hello", "<img src=\"https://tracker.example.com/p.gif\">"), "sam@example.com", Model("Sam Rivera"),
            header: EmailHeader.For(organization, logo, Board));

        Images().Matches(email.HtmlBody!).Should().ContainSingle();
        email.HtmlBody.Should().Contain("&lt;img src=&quot;", "the template's tag reads as text");
    }

    private static EmailTemplateModel Model(string name) => new(name, "Warehouse lead", "Brightwater Studio", "Phone screen");

    private static (Organization Organization, BrandImage Logo) Branded()
    {
        var organization = new Organization { Name = "Brightwater Studio", Slug = "brightwater", HomeRegion = "eu" };
        var logo = new BrandImage
        {
            Id = Guid.NewGuid(), OrganizationId = organization.Id, Kind = BrandImageKind.Logo, BlobName = "x", ContentType = "image/png", Width = 400, Height = 100,
        };
        logo.RecordScan(MalwareScanStatus.NotScanned, DateTimeOffset.UnixEpoch);
        organization.SetBranding(null, null, null, logo.Id, null, null);
        return (organization, logo);
    }

    [GeneratedRegex("<img src=\"(?<src>[^\"]+)\"")]
    private static partial Regex Images();
}
