using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// The sites allowed to embed the careers pages (docs/architecture/careers-widget.md): each kept as the origin a browser
/// writes, so the careers pages can compare it with the page that frames them and send it in <c>frame-ancestors</c>.
/// </summary>
public sealed class EmbedOriginsTests
{
    [Theory]
    [InlineData("https://www.example.com", "https://www.example.com")]
    [InlineData("  www.example.com  ", "https://www.example.com")]
    [InlineData("https://www.example.com/careers?utm=x#jobs", "https://www.example.com")]
    [InlineData("HTTPS://WWW.Example.COM/", "https://www.example.com")]
    [InlineData("https://www.example.com:443", "https://www.example.com")]
    [InlineData("https://www.example.com:8443", "https://www.example.com:8443")]
    [InlineData("https://bücher.example", "https://xn--bcher-kva.example")]
    public void A_site_is_kept_as_its_origin(string input, string origin) =>
        EmbedOrigins.Normalize(input, allowLoopback: false).Should().Be((origin, (string?)null));

    [Theory]
    [InlineData("", "Enter the site's address.")]
    [InlineData("http://www.example.com", "A site that embeds the careers page uses https.")]
    [InlineData("ftp://www.example.com", "Enter a site's address, such as https://www.example.com.")]
    [InlineData("https://*.example.com", "Name each site in full, without a *.")]
    [InlineData("https://user:secret@www.example.com", "A site's address has no user name or password.")]
    [InlineData("https://203.0.113.7", "Use the site's name, not its IP address.")]
    [InlineData("https://[2001:db8::1]", "Use the site's name, not its IP address.")]
    [InlineData("https://localhost:3000", "A site that embeds the careers page is on the internet, not this computer.")]
    public void Anything_else_says_what_is_wrong(string input, string problem) =>
        EmbedOrigins.Normalize(input, allowLoopback: false).Should().Be(((string?)null, problem));

    [Theory]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("http://partner.localhost", "http://partner.localhost")]
    [InlineData("http://127.0.0.1:8080/page", "http://127.0.0.1:8080")]
    public void A_site_on_this_computer_may_use_http_only_where_loopback_is_allowed(string input, string origin)
    {
        EmbedOrigins.Normalize(input, allowLoopback: true).Origin.Should().Be(origin);
        EmbedOrigins.Normalize(input, allowLoopback: false).Origin.Should().BeNull();
    }

    [Fact]
    public void Loopback_never_lets_http_reach_a_site_on_the_internet() =>
        EmbedOrigins.Normalize("http://www.example.com", allowLoopback: true).Problem.Should().Be("A site that embeds the careers page uses https.");

    [Fact]
    public void A_list_drops_duplicates_and_keeps_its_order()
    {
        var organization = new Organization { Name = "Acme", Slug = "acme", HomeRegion = "eu" };

        organization.SetEmbedOrigins(["jobs.example.com", "https://www.example.com", "https://JOBS.example.com/"], allowLoopback: false);

        organization.EmbedOrigins.Should().Equal("https://jobs.example.com", "https://www.example.com");
    }

    [Fact]
    public void A_list_refuses_a_site_it_cannot_keep_and_more_than_twenty()
    {
        var organization = new Organization { Name = "Acme", Slug = "acme", HomeRegion = "eu" };

        organization.Invoking(o => o.SetEmbedOrigins(["http://www.example.com"], allowLoopback: false)).Should().Throw<ArgumentException>();
        organization.Invoking(o => o.SetEmbedOrigins(Enumerable.Range(1, EmbedOrigins.MaxCount + 1).Select(i => $"https://site{i}.example.com"), false))
            .Should().Throw<ArgumentException>();
        organization.SetEmbedOrigins(Enumerable.Range(1, EmbedOrigins.MaxCount).Select(i => $"https://site{i}.example.com"), false);
        organization.EmbedOrigins.Should().HaveCount(EmbedOrigins.MaxCount);
    }
}
