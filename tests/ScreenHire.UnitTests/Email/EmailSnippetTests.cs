using ScreenHire.Api.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The snippet a row in Messages shows: the first line of the email's text part with words in it, never the quoted
/// email under a reply, and never longer than one row can hold.
/// </summary>
public sealed class EmailSnippetTests
{
    [Fact]
    public void The_first_line_with_words_is_the_snippet()
    {
        EmailSnippet.FirstLine("\n\n  Hi Jordan,  \nThanks for applying.").Should().Be("Hi Jordan,");
    }

    [Fact]
    public void Spaces_and_tabs_collapse_to_one()
    {
        EmailSnippet.FirstLine("Thursday\t works   for me.\r\nCasey").Should().Be("Thursday works for me.");
    }

    [Fact]
    public void Quoted_lines_are_skipped()
    {
        EmailSnippet.FirstLine("> Can you talk on Thursday?\n>\nYes, I can.").Should().Be("Yes, I can.");
    }

    [Fact]
    public void A_reply_with_nothing_above_the_quoted_email_has_no_snippet()
    {
        EmailSnippet.FirstLine("On Tue, Nov 17, 2026 at 9:00 AM Avery Quinn <avery@example.com> wrote:\n> Can you talk?").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n \n")]
    public void An_empty_text_part_has_no_snippet(string? text)
    {
        EmailSnippet.FirstLine(text).Should().BeNull();
    }

    [Fact]
    public void A_long_line_is_cut_at_a_word_with_an_ellipsis()
    {
        var line = string.Join(' ', Enumerable.Repeat("portfolio", 30));

        var snippet = EmailSnippet.FirstLine(line);

        snippet.Should().EndWith(" portfolio…", "a whole word, then the ellipsis");
        snippet!.Length.Should().BeInRange(EmailSnippet.MaxLength - "portfolio ".Length, EmailSnippet.MaxLength);
    }

    [Fact]
    public void A_long_word_is_cut_where_it_reaches_the_limit()
    {
        var snippet = EmailSnippet.FirstLine(new string('a', 300));

        snippet.Should().HaveLength(EmailSnippet.MaxLength).And.EndWith("…");
    }
}
