using System.Text.Json;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Fonts;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// The heading font catalogue's rules (docs/architecture/branding.md section 3, ADR-0031): which families may be picked,
/// their picker styles, the weights and files a family is hosted with, and reading Fontsource's answers.
/// </summary>
public sealed class HeadingFontCatalogueTests
{
    private static readonly Uri FilesUrl = new("https://cdn.jsdelivr.net/fontsource/fonts/");

    [Fact]
    public void Only_google_families_under_the_ofl_or_apache_with_latin_letters_may_be_picked()
    {
        var fonts = HeadingFontCatalogue.Build(
            [
                Font("roboto-slab", "Roboto Slab", "serif", "Apache-2.0"),
                Font("lora", "Lora", "serif", "OFL-1.1"),
                Font("ubuntu", "Ubuntu", "sans-serif", "UFL-1.0"),
                Font("comic-mono", "Comic Mono", "monospace", "MIT", google: false),
                Font("material-icons", "Material Icons", "icons", "Apache-2.0"),
                Font("noto-sans-jp", "Noto Sans JP", "sans-serif", "OFL-1.1", subsets: ["japanese"]),
                Font("bricolage-grotesque", "Bricolage Grotesque", "sans-serif", "OFL-1.1"),
                Font("Bad Id", "Bad Id", "serif", "OFL-1.1"),
                Font("quoted", "Quo\"ted", "serif", "OFL-1.1"),
            ],
            GoogleStyles.None);

        fonts.Select(f => f.Id).Should().Contain(["roboto-slab", "lora"])
            .And.NotContain(["ubuntu", "comic-mono", "material-icons", "noto-sans-jp", "Bad Id", "quoted"])
            .And.NotContain("bricolage-grotesque", "ScreenHire's own font is the default, which is no font at all");
        fonts.Single(f => f.Id == "roboto-slab").License.Should().Be(FontLicense.Apache);
    }

    [Fact]
    public void The_six_bundled_fonts_are_always_offered_and_marked_bundled_even_when_the_source_lists_none()
    {
        var fonts = HeadingFontCatalogue.Build([Font("lora", "Lora", "serif", "OFL-1.1")], GoogleStyles.None);

        fonts.Where(f => f.Bundled).Select(f => f.Id).Should().BeEquivalentTo(HeadingFonts.Bundled.Keys);
        fonts.Single(f => f.Id == "playfair-display").Styles.Should().Equal(HeadingFontStyle.Serif);
        fonts.Select(f => f.Family).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Styles_come_from_the_category_and_rounded_and_slab_from_googles_tags()
    {
        var google = new GoogleStyles(new HashSet<string> { "quicksand" }, new HashSet<string> { "arvo" });
        var fonts = HeadingFontCatalogue.Build(
            [
                Font("quicksand", "Quicksand", "sans-serif", "OFL-1.1"),
                Font("arvo", "Arvo", "serif", "OFL-1.1"),
                Font("lobster", "Lobster", "display", "OFL-1.1"),
                Font("pacifico", "Pacifico", "handwriting", "OFL-1.1"),
                Font("space-mono", "Space Mono", "monospace", "OFL-1.1"),
            ],
            google);

        Styles(fonts, "quicksand").Should().Equal(HeadingFontStyle.Sans, HeadingFontStyle.Rounded);
        Styles(fonts, "arvo").Should().Equal(HeadingFontStyle.Serif, HeadingFontStyle.Slab);
        Styles(fonts, "lobster").Should().Equal(HeadingFontStyle.Display);
        Styles(fonts, "pacifico").Should().Equal(HeadingFontStyle.Display);
        Styles(fonts, "space-mono").Should().BeEmpty("a monospace face is in the full list and search, under no filter");
    }

    [Theory]
    [InlineData(new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 }, new[] { 600, 700, 800 })]
    [InlineData(new[] { 400, 700 }, new[] { 700 })]
    [InlineData(new[] { 400 }, new[] { 400 })]
    [InlineData(new[] { 300, 500, 900 }, new[] { 500, 900 })]
    [InlineData(new int[0], new int[0])]
    public void A_family_is_hosted_at_the_weights_nearest_the_careers_titles(int[] available, int[] expected)
    {
        HeadingFontHosting.PickWeights(available).Should().Equal(expected);
    }

    [Fact]
    public void A_family_is_hosted_in_latin_and_latin_ext_and_never_another_subset()
    {
        var detail = Detail(
            ("latin", 400), ("latin", 700), ("latin-ext", 700), ("cyrillic", 700), ("vietnamese", 700), ("latin-ext", 400));

        HeadingFontHosting.ChooseFiles(detail).Select(f => (f.Subset, f.Weight)).Should().Equal(("latin", 700), ("latin-ext", 700));
    }

    [Fact]
    public void A_family_with_no_latin_file_has_nothing_to_host()
    {
        HeadingFontHosting.ChooseFiles(Detail(("cyrillic", 700), ("latin-ext", 700))).Should().BeEmpty();
    }

    [Fact]
    public void A_file_the_careers_stylesheet_would_refuse_is_never_hosted()
    {
        // The careers pages throw on a missing or malformed unicode range and on an odd weight, which would take every page
        // of the organization down; such a file is dropped before anything is stored.
        HeadingFontHosting.ChooseFiles(Detail(("latin", 700)) with { UnicodeRanges = new Dictionary<string, string>() })
            .Should().BeEmpty("latin without its range is no latin");
        HeadingFontHosting.ChooseFiles(Detail(("latin", 700), ("latin-ext", 700)) with
        {
            UnicodeRanges = new Dictionary<string, string> { ["latin"] = Latin, ["latin-ext"] = "U+0100-02BA;}body{" },
        }).Select(f => f.Subset).Should().Equal("latin");
        HeadingFontHosting.ChooseFiles(Detail(("latin", 650), ("latin", 1000), ("latin", 700))).Select(f => f.Weight).Should().Equal(700);
    }

    [Fact]
    public void Fontsource_detail_keeps_only_upright_woff2_files_at_its_own_address()
    {
        using var json = JsonDocument.Parse(
            """
            {
              "id": "roboto-slab", "family": "Roboto Slab", "category": "serif", "license": "Apache-2.0", "type": "google",
              "npmVersion": "5.3.0", "subsets": ["latin", "latin-ext"], "weights": [400, 700],
              "unicodeRange": { "latin": "U+0000-00FF", "latin-ext": "U+0100-02BA" },
              "variants": {
                "700": {
                  "normal": {
                    "latin": { "url": { "woff2": "https://cdn.jsdelivr.net/fontsource/fonts/roboto-slab@5.3.0/latin-700-normal.woff2" } },
                    "latin-ext": { "url": { "woff2": "https://elsewhere.example.com/latin-ext-700-normal.woff2" } }
                  },
                  "italic": {
                    "latin": { "url": { "woff2": "https://cdn.jsdelivr.net/fontsource/fonts/roboto-slab@5.3.0/latin-700-italic.woff2" } }
                  }
                },
                "bold": { "normal": {} }
              }
            }
            """);

        var detail = FontsourceSource.ParseDetail(json.RootElement, FilesUrl);

        detail.Should().NotBeNull();
        detail!.Version.Should().Be("5.3.0");
        detail.Google.Should().BeTrue();
        detail.UnicodeRanges.Should().ContainKey("latin-ext");
        detail.Files.Should().ContainSingle().Which.Should().Be(
            new SourceFontFile("latin", 700, new Uri("https://cdn.jsdelivr.net/fontsource/fonts/roboto-slab@5.3.0/latin-700-normal.woff2")));
    }

    [Fact]
    public void A_fontsource_entry_without_an_id_or_family_is_skipped()
    {
        using var json = JsonDocument.Parse("""[{ "family": "No Id" }, { "id": "no-family" }, { "id": "lora", "family": "Lora", "type": "google" }]""");

        json.RootElement.EnumerateArray().Select(FontsourceSource.ParseFont).OfType<SourceFont>().Select(f => f.Id).Should().Equal("lora");
    }

    private static SourceFont Font(string id, string family, string category, string license, bool google = true, string[]? subsets = null) =>
        new(id, family, category, license, google, subsets ?? ["latin", "latin-ext"], [400, 700], []);

    private const string Latin = "U+0000-00FF,U+0131,U+2000-206F";

    private static SourceFontDetail Detail(params (string Subset, int Weight)[] files) =>
        new("lora", "Lora", "serif", "OFL-1.1", true, "5.3.0",
            new Dictionary<string, string> { ["latin"] = Latin, ["latin-ext"] = "U+0100-02BA", ["cyrillic"] = "U+0400-045F" },
            [.. files.Select(f => new SourceFontFile(f.Subset, f.Weight, new Uri(FilesUrl, $"lora@5.3.0/{f.Subset}-{f.Weight}-normal.woff2")))]);

    private static IReadOnlyList<HeadingFontStyle> Styles(IReadOnlyList<HeadingFontOption> fonts, string id) => fonts.Single(f => f.Id == id).Styles;
}
