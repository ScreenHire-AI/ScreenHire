using ScreenHire.Api.Content;

namespace ScreenHire.UnitTests.Content;

public sealed class LikeTests
{
    [Theory]
    [InlineData("engineer", "%engineer%")]
    [InlineData("  Casey ", "%Casey%")]
    [InlineData("50%", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"c:\x", @"%c:\\x%")]
    public void A_search_matches_its_text_anywhere_and_takes_wildcards_literally(string text, string pattern) =>
        Like.Contains(text).Should().Be(pattern);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_search_for_is_no_pattern(string? text) => Like.Contains(text).Should().BeNull();
}
