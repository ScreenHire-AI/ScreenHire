using ScreenHire.Api.Content;

namespace ScreenHire.UnitTests.Content;

public sealed class RichTextTests
{
    [Fact]
    public void Formatting_tags_survive()
    {
        const string html = "<h2>About</h2><p>We <strong>build</strong> <em>things</em>.</p><ul><li>One</li></ul>";

        RichText.Sanitize(html).Should().Be(html);
    }

    [Theory]
    [InlineData("<p>Hi</p><script>alert(1)</script>", "<p>Hi</p>")]
    [InlineData("<p onclick=\"steal()\">Hi</p>", "<p>Hi</p>")]
    [InlineData("<p style=\"color:red\" class=\"x\">Hi</p>", "<p>Hi</p>")]
    [InlineData("<img src=x onerror=alert(1)><p>Hi</p>", "<p>Hi</p>")]
    [InlineData("<iframe src=\"https://example.com\"></iframe><p>Hi</p>", "<p>Hi</p>")]
    public void Scripts_handlers_styles_and_embeds_are_removed(string html, string expected)
    {
        RichText.Sanitize(html).Should().Be(expected);
    }

    [Fact]
    public void Unknown_tags_are_dropped_but_their_text_is_kept()
    {
        RichText.Sanitize("<div><span>Kept</span></div>").Should().Be("Kept");
    }

    [Fact]
    public void Links_keep_safe_targets_and_open_without_passing_on_the_page()
    {
        RichText.Sanitize("<a href=\"https://example.com/jobs\">Jobs</a>")
            .Should().Be("<a href=\"https://example.com/jobs\" rel=\"nofollow noopener noreferrer\">Jobs</a>");
        RichText.Sanitize("<a href=\"mailto:jobs@example.com\">Mail</a>").Should().Contain("href=\"mailto:jobs@example.com\"");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox")]
    public void Links_with_unsafe_schemes_lose_their_target(string href)
    {
        RichText.Sanitize($"<a href=\"{href}\">Click</a>").Should().NotContain(href);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_sanitizes_to_an_empty_string(string? html)
    {
        RichText.Sanitize(html).Should().BeEmpty();
    }
}
