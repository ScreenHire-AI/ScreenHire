using ScreenHire.Api.Content;

namespace ScreenHire.UnitTests.Content;

public sealed class SlugsTests
{
    [Theory]
    [InlineData("Senior .NET Engineer", "senior-net-engineer")]
    [InlineData("Frontend Engineer (Svelte)", "frontend-engineer-svelte")]
    [InlineData("  Café Manager -- Nights  ", "cafe-manager-nights")]
    [InlineData("!!!", "job")]
    public void A_title_becomes_lowercase_words_joined_by_single_hyphens(string title, string expected)
    {
        Slugs.FromTitle(title).Should().Be(expected);
    }

    [Fact]
    public void A_long_title_leaves_room_for_a_suffix()
    {
        Slugs.FromTitle(new string('a', 300)).Length.Should().Be(Slugs.MaxBaseLength);
    }

    [Fact]
    public void A_taken_slug_gets_the_first_free_number()
    {
        HashSet<string> taken = ["engineer", "engineer-2", "engineer-4"];

        Slugs.FirstFree("engineer", taken).Should().Be("engineer-3");
        Slugs.FirstFree("designer", taken).Should().Be("designer");
    }
}
