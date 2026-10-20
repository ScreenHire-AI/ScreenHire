using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

public sealed class BrandPaletteTests
{
    private const string PageBackground = "#fbf6ee";
    private const string Surface = "#ffffff";

    // Light, dark, saturated, mid-tone and near-white colours: every one must come out meeting the minimums.
    public static TheoryData<string> Colors =>
    [
        "#1f4e5a", "#c2462f", "#4338ca", "#b45309", "#ffd400", "#00ff00", "#ff0000", "#0000ff", "#808080",
        "#777777", "#fbf6ee", "#ffffff", "#000000", "#e91e63", "#00bcd4", "#8bc34a", "#a1887f", "#f5f5dc",
    ];

    [Theory]
    [MemberData(nameof(Colors))]
    public void Every_derived_color_meets_its_contrast_minimum(string brand)
    {
        var palette = BrandPalette.Derive(brand);

        BrandPalette.ContrastRatio(palette.Brand, palette.OnBrand).Should().BeGreaterThanOrEqualTo(BrandPalette.TextContrast);
        foreach (var background in new[] { PageBackground, Surface })
        {
            BrandPalette.ContrastRatio(palette.BrandText, background).Should().BeGreaterThanOrEqualTo(BrandPalette.TextContrast);
            BrandPalette.ContrastRatio(palette.BrandUi, background).Should().BeGreaterThanOrEqualTo(BrandPalette.UiContrast);
        }
    }

    [Fact]
    public void A_colour_that_already_passes_is_used_as_it_is()
    {
        var palette = BrandPalette.Derive("#1F4E5A");

        palette.Should().Be(new BrandPalette("#1f4e5a", "#ffffff", "#1f4e5a", "#1f4e5a", BrandAdjusted: false));
    }

    [Fact]
    public void A_light_brand_gets_dark_text_on_it_and_a_darker_shade_for_text_on_the_page()
    {
        var palette = BrandPalette.Derive("#ffd400");

        palette.Brand.Should().Be("#ffd400");
        palette.OnBrand.Should().Be("#231f1a");
        palette.BrandAdjusted.Should().BeFalse();
        palette.BrandText.Should().NotBe("#ffd400");
    }

    [Fact]
    public void A_mid_tone_neither_white_nor_ink_can_sit_on_is_darkened()
    {
        var palette = BrandPalette.Derive("#808080");

        palette.BrandAdjusted.Should().BeTrue();
        palette.OnBrand.Should().Be("#ffffff");
        BrandPalette.ContrastRatio(palette.Brand, "#ffffff").Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [InlineData("1f4e5a")]
    [InlineData("#1f4e5")]
    [InlineData("#1f4e5g")]
    [InlineData("teal")]
    [InlineData("#1f4e5a; background:url(x)")]
    public void Anything_but_a_hex_colour_is_refused(string color)
    {
        BrandPalette.IsValid(color).Should().BeFalse();
        var derive = () => BrandPalette.Derive(color);
        derive.Should().Throw<FormatException>();
    }

    [Fact]
    public void The_organization_keeps_the_colour_normalized()
    {
        var organization = new Organization { Name = "Acme", Slug = "acme", HomeRegion = "eu" };

        organization.SetBrandColor(" #ABCDEF ");

        organization.BrandColor.Should().Be("#abcdef");
        organization.GetBrandPalette()!.Brand.Should().NotBeNull();
        organization.SetBrandColor(null);
        organization.GetBrandPalette().Should().BeNull();
    }
}
