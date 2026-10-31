using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// The branding check before a colour saves (ADR-0018): which pairs a colour fails, in words, and the nearest shade that
/// passes them all.
/// </summary>
public sealed class BrandColorCheckTests
{
    private const string PageBackground = "#fbf6ee";

    [Fact]
    public void A_dark_brand_colour_passes_as_it_is_with_white_text()
    {
        var check = BrandColorCheck.CheckBrand("#1F4E5A");

        check.Passes.Should().BeTrue();
        check.Color.Should().Be("#1f4e5a");
        check.TextColor.Should().Be("#ffffff");
        check.Pairs.Select(p => p.Name).Should().Equal(ContrastPairName.ButtonText, ContrastPairName.Links);
        check.Suggestion.Should().BeNull();
        check.Problems().Should().BeEmpty();
    }

    [Fact]
    public void A_pale_brand_colour_fails_as_links_and_suggests_a_darker_shade_that_passes_both_pairs()
    {
        var check = BrandColorCheck.CheckBrand("#ffd400");

        check.Passes.Should().BeFalse();
        check.Pairs.Single(p => p.Name == ContrastPairName.ButtonText).Passes.Should().BeTrue("ink reads on a pale fill");
        check.Pairs.Single(p => p.Name == ContrastPairName.Links).Passes.Should().BeFalse();
        check.Problems().Should().ContainSingle().Which.Should()
            .StartWith("Links in #ffd400 are hard to read on the page: ")
            .And.EndWith($"and they need 4.5:1. {check.Suggestion} is the nearest shade that passes.");

        var suggested = BrandColorCheck.CheckBrand(check.Suggestion!);
        suggested.Passes.Should().BeTrue();
        BrandPalette.ContrastRatio(check.Suggestion!, PageBackground).Should().BeGreaterThanOrEqualTo(BrandPalette.TextContrast);
    }

    [Fact]
    public void A_mid_tone_fails_both_pairs_and_names_each()
    {
        var check = BrandColorCheck.CheckBrand("#808080");

        check.Pairs.Should().OnlyContain(p => !p.Passes);
        check.Problems().Should().HaveCount(2);
        check.Problems()[0].Should().StartWith("Text on buttons in #808080 is hard to read: ");
        BrandColorCheck.CheckBrand(check.Suggestion!).Passes.Should().BeTrue();
    }

    [Fact]
    public void A_ratio_just_under_its_minimum_never_reads_as_the_minimum()
    {
        // White on #777777 is 4.48:1: shown as 4.4, and failing.
        var pair = BrandColorCheck.CheckBrand("#777777").Pairs.Single(p => p.Name == ContrastPairName.ButtonText);

        pair.Foreground.Should().Be("#ffffff");
        pair.Ratio.Should().Be(4.4);
        pair.Passes.Should().BeFalse();
    }

    [Theory]
    [InlineData("#e9dcc4", "#231f1a")]
    [InlineData("#1f4e5a", "#ffffff")]
    public void A_second_colour_that_white_or_ink_reads_on_passes(string color, string text)
    {
        var check = BrandColorCheck.CheckSecond(color);

        check.Passes.Should().BeTrue();
        check.TextColor.Should().Be(text);
        check.Pairs.Should().ContainSingle().Which.Name.Should().Be(ContrastPairName.HeaderText);
    }

    [Fact]
    public void A_mid_tone_second_colour_suggests_the_nearest_shade_either_way()
    {
        var check = BrandColorCheck.CheckSecond("#808080");

        check.Passes.Should().BeFalse();
        check.Problems().Should().ContainSingle().Which.Should().StartWith("Text on a header in #808080 is hard to read: ");
        var suggested = BrandColorCheck.CheckSecond(check.Suggestion!);
        suggested.Passes.Should().BeTrue();
        // The nearest shade is a small step, not black or white.
        check.Suggestion.Should().NotBe("#000000").And.NotBe("#ffffff");
    }

    [Theory]
    [MemberData(nameof(BrandPaletteTests.Colors), MemberType = typeof(BrandPaletteTests))]
    public void Every_suggestion_passes(string color)
    {
        foreach (var check in new[] { BrandColorCheck.CheckBrand(color), BrandColorCheck.CheckSecond(color) })
        {
            if (!check.Passes)
            {
                (check.Name() == ContrastPairName.HeaderText ? BrandColorCheck.CheckSecond(check.Suggestion!) : BrandColorCheck.CheckBrand(check.Suggestion!))
                    .Passes.Should().BeTrue(color);
            }
        }
    }

    [Fact]
    public void A_colour_that_passes_derives_a_palette_of_itself()
    {
        // What saves since the check is what the careers pages use: Derive changes only colours saved before it.
        var palette = BrandPalette.Derive("#1f4e5a");

        palette.Should().Be(new BrandPalette("#1f4e5a", "#ffffff", "#1f4e5a", "#1f4e5a", false));
    }
}

file static class ColorCheckExtensions
{
    public static ContrastPairName Name(this ColorCheck check) => check.Pairs[0].Name;
}
