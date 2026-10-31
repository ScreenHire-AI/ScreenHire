using ScreenHire.Infrastructure.Content;

namespace ScreenHire.UnitTests.Content;

public sealed class RichTextTests
{
    [Fact]
    public void The_toolbars_elements_survive()
    {
        const string html = "<h2>About</h2><p>We <strong>build</strong> <em>things</em>.<br>Every day.</p><h3>Perks</h3>"
            + "<ul><li>One</li></ul><ol><li>First</li><li>Second</li></ol>";

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

    [Theory]
    [InlineData("<p><b>Bold</b> and <i>italic</i></p>", "<p><strong>Bold</strong> and <em>italic</em></p>")]
    [InlineData("<h1>Title</h1><h4>Small</h4><h6>Smaller</h6>", "<h2>Title</h2><h3>Small</h3><h3>Smaller</h3>")]
    public void Synonyms_become_the_toolbars_elements(string html, string expected)
    {
        RichText.Sanitize(html).Should().Be(expected);
    }

    [Theory]
    [InlineData("<p><u>Under</u> <s>struck</s></p>", "<p>Under struck</p>")]
    [InlineData("<blockquote><p>Quoted</p></blockquote>", "<p>Quoted</p>")]
    [InlineData("<div><span>Kept</span></div>", "<p>Kept</p>")]
    public void Other_tags_go_and_their_text_stays(string html, string expected)
    {
        RichText.Sanitize(html).Should().Be(expected);
    }

    [Fact]
    public void Links_keep_safe_targets_and_open_without_passing_on_the_page()
    {
        RichText.Sanitize("<p><a href=\"https://example.com/jobs\">Jobs</a></p>")
            .Should().Be("<p><a href=\"https://example.com/jobs\" rel=\"nofollow noopener noreferrer\">Jobs</a></p>");
        RichText.Sanitize("<p><a href=\"mailto:jobs@example.com\">Mail</a></p>").Should().Contain("href=\"mailto:jobs@example.com\"");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox")]
    [InlineData("/careers/apply")]
    [InlineData("apply.html")]
    public void Links_with_unsafe_or_relative_targets_are_only_their_text(string href)
    {
        RichText.Sanitize($"<p><a href=\"{href}\">Click</a></p>").Should().Be("<p>Click</p>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p> <br> </p><h2></h2><ul><li> </li></ul>")]
    public void Nothing_sanitizes_to_an_empty_string(string? html)
    {
        RichText.Sanitize(html).Should().BeEmpty();
        RichText.IsBlank(html).Should().BeTrue();
    }

    [Fact]
    public void Plain_text_reads_as_the_text_box_read_it()
    {
        const string text = "We build tools.\nFor small teams.\n\n- Remote\n- Flexible hours\n\n- Pension\n\nApply by Friday & say hi <3";

        RichText.Sanitize(text).Should().Be(
            "<p>We build tools.<br>For small teams.</p><ul><li>Remote</li><li>Flexible hours</li><li>Pension</li></ul>"
            + "<p>Apply by Friday &amp; say hi &lt;3</p>");
    }

    [Fact]
    public void Loose_text_and_inline_elements_between_blocks_become_paragraphs()
    {
        RichText.Sanitize("Intro with <strong>bold</strong>.\n\nSecond<h2>Heading</h2>After")
            .Should().Be("<p>Intro with <strong>bold</strong>.</p><p>Second</p><h2>Heading</h2><p>After</p>");
    }

    [Fact]
    public void A_list_item_holds_its_text_directly_as_the_editor_writes_it()
    {
        RichText.Sanitize("<ul><li><p>One</p></li><li><p>Two</p><p>lines</p></li></ul>")
            .Should().Be("<ul><li>One</li><li>Two<br>lines</li></ul>");
    }

    [Fact]
    public void Whitespace_inside_and_between_blocks_collapses()
    {
        RichText.Sanitize("<p>\n  Many    spaces\n here  </p>\n\n<ul>\n  <li> One </li>\n</ul>\n<p>Line<br>\n next<br></p>")
            .Should().Be("<p>Many spaces here</p><ul><li>One</li></ul><p>Line<br>next</p>");
    }

    public static TheoryData<string> Corpus() =>
    [
        "<h2>About the role</h2><p>We are <strong>hiring</strong> a <em>designer</em>.</p><ul><li>Remote</li></ul>",
        "<p>Tiptap writes this</p><ul><li><p>item</p></li></ul><ol><li><p>first</p></li></ol><p></p>",
        "<p>Hi {{ firstName }},</p><p>{% if stageName == \"Offer\" %}Congratulations{% endif %}</p>",
        "<p>AT&amp;T &lt;tags&gt; and&nbsp;spaces</p>",
        "Plain text\n\n- with\n- a list\n\nand <a href=\"https://example.com\">a link</a>",
        "<p><b>old</b> <h4>tags</h4></p><blockquote>quote</blockquote>",
    ];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Sanitizing_twice_changes_nothing(string html)
    {
        var once = RichText.Sanitize(html);

        RichText.Sanitize(once).Should().Be(once);
    }

    [Fact]
    public void Plain_text_has_a_blank_line_between_blocks_and_marks_each_list_item()
    {
        const string html = "<h2>Next steps</h2><p>Hi Jordan,<br>thanks.</p><ul><li>One</li><li>Two</li></ul>"
            + "<ol><li>First</li><li>Second</li></ol>"
            + "<p>Read <a href=\"https://example.com/faq\">our FAQ</a> or https://example.com/q, "
            + "<a href=\"https://example.com/q\">https://example.com/q</a> and <a href=\"mailto:a@example.com\">a@example.com</a>.</p>";

        RichText.ToPlainText(html).Should().Be(
            "Next steps\n\nHi Jordan,\nthanks.\n\n- One\n- Two\n\n1. First\n2. Second\n\n"
            + "Read our FAQ (https://example.com/faq) or https://example.com/q, https://example.com/q and a@example.com.");
    }
}
