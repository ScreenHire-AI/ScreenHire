using ScreenHire.Api.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The snippet a row in Messages shows: the first line of the email's text part that says something, not a lone greeting,
/// never the quoted email under a reply, and never longer than one row can hold.
/// </summary>
public sealed class EmailSnippetTests
{
    [Fact]
    public void The_first_line_after_a_lone_greeting_is_the_snippet()
    {
        EmailSnippet.FirstLine("\n\n  Hi Jordan,  \n\nThanks for applying.\nAvery").Should().Be("Thanks for applying.");
    }

    [Theory]
    [InlineData("Dear Ms Quinn\nWe would like to meet you.")]
    [InlineData("Hello all!\nWe would like to meet you.")]
    [InlineData("Good morning Jordan Reyes,\nWe would like to meet you.")]
    public void Each_kind_of_lone_greeting_is_skipped(string text)
    {
        EmailSnippet.FirstLine(text).Should().Be("We would like to meet you.");
    }

    [Fact]
    public void A_line_that_opens_with_a_greeting_and_says_more_is_kept()
    {
        EmailSnippet.FirstLine("Hi Avery, I can do Friday.\nCasey").Should().Be("Hi Avery, I can do Friday.");
    }

    [Fact]
    public void An_email_that_is_only_a_greeting_shows_it()
    {
        EmailSnippet.FirstLine("Hi Casey,\n> Are you free?").Should().Be("Hi Casey,");
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
    [InlineData("On Tue, Nov 17, 2026 at 9:00 AM Avery Quinn\n<avery@example.com> wrote:\n> Can you talk?")]
    [InlineData("On Tue, Nov 17, 2026 at 9:00 AM Avery Quinn <avery@example.com>\nwrote:\n\n> Can you talk?")]
    [InlineData("-----Original Message-----\nFrom: Avery Quinn <avery@example.com>\nCan you talk?")]
    public void A_wrapped_or_Outlook_quote_header_also_starts_the_quoted_email(string text)
    {
        EmailSnippet.FirstLine(text).Should().BeNull();
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
