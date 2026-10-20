using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The marketing site's SEO contract (ADR-0003): every page has a unique title, description and canonical URL,
/// hreflang, a build-time Open Graph image and valid JSON-LD of the right types, and ships no JavaScript (the only
/// <c>&lt;script&gt;</c> is JSON-LD). The production build is served without SITE_URL, so absolute URLs use the default
/// site host; tests compare paths, not hosts.
/// </summary>
public sealed partial class MarketingTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Marketing = "marketing";

    /// <summary>Every indexable page and the schema.org types its JSON-LD must contain besides Organization and WebSite.</summary>
    private static readonly (string Path, string[] Types)[] IndexablePages =
    [
        ("/", ["SoftwareApplication"]),
        ("/product/", ["BreadcrumbList", "SoftwareApplication"]),
        ("/how-detection-works/", ["BreadcrumbList", "FAQPage"]),
        ("/pricing/", ["BreadcrumbList", "SoftwareApplication", "FAQPage"]),
        ("/hiring-ai-laws/", ["BreadcrumbList", "CollectionPage"]),
        ("/hiring-ai-laws/eu-ai-act/", ["BreadcrumbList", "Article", "FAQPage"]),
        ("/hiring-ai-laws/uk-gdpr-automated-decisions/", ["BreadcrumbList", "Article", "FAQPage"]),
        ("/hiring-ai-laws/nyc-local-law-144/", ["BreadcrumbList", "Article", "FAQPage"]),
        ("/hiring-ai-laws/illinois/", ["BreadcrumbList", "Article", "FAQPage"]),
        ("/hiring-ai-laws/colorado/", ["BreadcrumbList", "Article", "FAQPage"]),
        ("/hiring-ai-laws/california/", ["BreadcrumbList", "Article", "FAQPage"]),
        ("/compare/", ["BreadcrumbList"]),
        ("/compare/gohire-alternative/", ["BreadcrumbList", "FAQPage"]),
        ("/blog/", ["BreadcrumbList", "Blog"]),
        ("/blog/why-no-fraud-score/", ["BreadcrumbList", "BlogPosting"]),
    ];

    public static TheoryData<string, string[]> Pages => new(IndexablePages.Select(page => (page.Path, page.Types)));

    public static TheoryData<string> LawEntries =>
    [
        "/hiring-ai-laws/eu-ai-act/",
        "/hiring-ai-laws/uk-gdpr-automated-decisions/",
        "/hiring-ai-laws/nyc-local-law-144/",
        "/hiring-ai-laws/illinois/",
        "/hiring-ai-laws/colorado/",
        "/hiring-ai-laws/california/",
    ];

    [Fact]
    public async Task Home_page_has_a_heading_and_ships_no_javascript()
    {
        var scriptRequests = RecordScriptRequests();

        await GoToAsync(Marketing, "/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Know who's really applying.");
        (await ScriptTypesAsync()).Should().NotBeEmpty("the page carries JSON-LD").And.OnlyContain(type => type == "application/ld+json");
        scriptRequests.Should().BeEmpty();
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_has_complete_seo_and_no_javascript(string path, string[] expectedTypes)
    {
        var scriptRequests = RecordScriptRequests();

        await GoToAsync(Marketing, path);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveCountAsync(1);
        (await Page.TitleAsync()).Should().NotBeNullOrWhiteSpace().And.Subject.Length.Should().BeLessThanOrEqualTo(70);
        (await AttributeAsync("meta[name='description']", "content")).Should().NotBeNullOrWhiteSpace();

        var canonical = await AttributeAsync("link[rel='canonical']", "href");
        canonical.Should().NotBeNull();
        var canonicalUri = new Uri(canonical!, UriKind.Absolute);
        canonicalUri.AbsolutePath.Should().Be(path, "the canonical URL is the page's own address");
        (await AttributeAsync("meta[property='og:url']", "content")).Should().Be(canonical);
        foreach (var language in new[] { "en-US", "en-GB", "x-default" })
        {
            (await AttributeAsync($"link[rel='alternate'][hreflang='{language}']", "href")).Should().Be(canonical, "hreflang {0}", language);
        }

        await ExpectOpenGraphImageAsync();

        var types = await JsonLdTypesAsync(canonicalUri);
        types.Should().Contain(["Organization", "WebSite", .. expectedTypes]);

        (await ScriptTypesAsync()).Should().OnlyContain(type => type == "application/ld+json", "pages without islands ship no JavaScript");
        scriptRequests.Should().BeEmpty();
        await ExpectNoAccessibilityViolationsAsync();

        // Phone width: nothing may force a horizontal scroll (wide tables scroll inside their own box).
        await Page.SetViewportSizeAsync(390, 844);
        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        overflow.Should().Be(0, "{0} must fit a 390px-wide phone without scrolling sideways", path);
    }

    [Fact]
    public async Task Sitemap_pages_have_unique_titles_descriptions_and_canonicals()
    {
        using var http = Fixture.CreateHttpClient(Marketing);
        var sitemap = await http.GetStringAsync(new Uri("/sitemap-0.xml", UriKind.Relative), TestContext.Current.CancellationToken);
        var paths = LocRegex().Matches(sitemap).Select(match => new Uri(match.Groups[1].Value).AbsolutePath).ToList();

        paths.Should().BeEquivalentTo(IndexablePages.Select(page => page.Path), "every indexable page has a row in Pages, so it gets the full SEO check");

        var titles = new List<string>();
        var descriptions = new List<string>();
        var canonicals = new List<string>();
        foreach (var path in paths)
        {
            var html = await http.GetStringAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
            titles.Add(WebUtility.HtmlDecode(TitleRegex().Match(html).Groups[1].Value));
            descriptions.Add(WebUtility.HtmlDecode(DescriptionRegex().Match(html).Groups[1].Value));
            canonicals.Add(CanonicalRegex().Match(html).Groups[1].Value);
        }

        titles.Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
        descriptions.Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
        canonicals.Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
    }

    [Theory]
    [MemberData(nameof(LawEntries))]
    public async Task Law_entry_shows_last_reviewed_date_sources_and_not_legal_advice(string path)
    {
        await GoToAsync(Marketing, path);

        var notice = Page.GetByRole(AriaRole.Complementary, new() { Name = "Not legal advice" });
        await Expect(notice).ToContainTextAsync("It is not legal advice");
        await Expect(notice).ToContainTextAsync("Last reviewed");
        var reviewed = await Page.Locator("time").First.GetAttributeAsync("datetime");
        DateOnly.TryParseExact(reviewed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _).Should().BeTrue();

        var sources = Page.GetByRole(AriaRole.Region, new() { Name = "Sources" }).GetByRole(AriaRole.Link);
        (await sources.CountAsync()).Should().BePositive();
        foreach (var href in await sources.EvaluateAllAsync<string[]>("links => links.map(l => l.href)"))
        {
            new Uri(href).Scheme.Should().Be("https", "sources link to the primary or published source");
        }
    }

    [Fact]
    public async Task Unknown_page_returns_404_and_is_not_indexed()
    {
        var response = await Page.GotoAsync(new Uri(Fixture.GetEndpoint(Marketing), "/no-such-page/").ToString());

        response.Should().NotBeNull();
        response!.Status.Should().Be(404);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("We couldn't find that page.");
        (await AttributeAsync("meta[name='robots']", "content")).Should().Be("noindex");
        await Expect(Page.Locator("link[rel='canonical']")).ToHaveCountAsync(0);
        (await ScriptTypesAsync()).Should().OnlyContain(type => type == "application/ld+json");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Robots_llms_txt_and_rss_are_served()
    {
        using var http = Fixture.CreateHttpClient(Marketing);
        var cancellationToken = TestContext.Current.CancellationToken;

        var robots = await http.GetStringAsync(new Uri("/robots.txt", UriKind.Relative), cancellationToken);
        robots.Should().Contain("Sitemap: ").And.Contain("/sitemap-index.xml");

        var llms = await http.GetStringAsync(new Uri("/llms.txt", UriKind.Relative), cancellationToken);
        llms.Should().StartWith("# ScreenHire");
        foreach (var path in IndexablePages.Select(page => page.Path))
        {
            llms.Should().Contain($"{path})", "llms.txt links every indexable page, including {0}", path);
        }

        var rss = await http.GetStringAsync(new Uri("/rss.xml", UriKind.Relative), cancellationToken);
        rss.Should().Contain("<rss").And.Contain("/blog/why-no-fraud-score/");
    }

    private List<string> RecordScriptRequests()
    {
        var requests = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.ResourceType == "script")
            {
                requests.Add(request.Url);
            }
        };
        return requests;
    }

    private Task<string[]> ScriptTypesAsync() => Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.type)");

    private Task<string?> AttributeAsync(string selector, string attribute) => Page.Locator(selector).GetAttributeAsync(attribute);

    private async Task ExpectOpenGraphImageAsync()
    {
        var image = await AttributeAsync("meta[property='og:image']", "content");
        image.Should().NotBeNull();
        (await AttributeAsync("meta[name='twitter:image']", "content")).Should().Be(image);

        using var http = Fixture.CreateHttpClient(Marketing);
        using var response = await http.GetAsync(new Uri(new Uri(image!).AbsolutePath, UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the build renders an Open Graph image for {0}", image);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
    }

    /// <summary>Parses every JSON-LD block, checks the shape of the node types the site uses, and returns all types.</summary>
    private async Task<List<string>> JsonLdTypesAsync(Uri canonical)
    {
        var types = new List<string>();
        var blocks = await Page.Locator("script[type='application/ld+json']").AllTextContentsAsync();
        blocks.Should().ContainSingle("Seo.astro wraps everything in one @graph");

        using var document = JsonDocument.Parse(blocks[0]);
        document.RootElement.GetProperty("@context").GetString().Should().Be("https://schema.org");
        foreach (var node in document.RootElement.GetProperty("@graph").EnumerateArray())
        {
            var type = node.GetProperty("@type").GetString();
            type.Should().NotBeNullOrWhiteSpace();
            types.Add(type!);
            ExpectValidNode(type!, node, canonical);
        }

        return types;
    }

    private static void ExpectValidNode(string type, JsonElement node, Uri canonical)
    {
        switch (type)
        {
            case "BreadcrumbList":
                var items = node.GetProperty("itemListElement").EnumerateArray().ToList();
                items.Select(item => item.GetProperty("position").GetInt32()).Should().Equal(Enumerable.Range(1, items.Count));
                items[^1].GetProperty("item").GetString().Should().Be(canonical.ToString(), "the last crumb is the current page");
                break;
            case "FAQPage":
                var questions = node.GetProperty("mainEntity").EnumerateArray().ToList();
                questions.Should().NotBeEmpty();
                questions.Should().AllSatisfy(question =>
                {
                    question.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
                    question.GetProperty("acceptedAnswer").GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();
                });
                break;
            case "Article" or "BlogPosting":
                node.GetProperty("headline").GetString().Should().NotBeNullOrWhiteSpace();
                node.GetProperty("url").GetString().Should().Be(canonical.ToString());
                DateOnly.TryParseExact(node.GetProperty("datePublished").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    .Should().BeTrue("datePublished is an ISO date");
                node.GetProperty("image").GetString().Should().EndWith(".png");
                break;
            case "SoftwareApplication":
                node.GetProperty("name").GetString().Should().Be("ScreenHire");
                node.GetProperty("applicationCategory").GetString().Should().NotBeNullOrWhiteSpace();
                // Offers appear only once a plan has a real price; a placeholder price is invalid structured data.
                if (node.TryGetProperty("offers", out var offers))
                {
                    offers.EnumerateArray().Should().AllSatisfy(offer =>
                        decimal.TryParse(offer.GetProperty("price").GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out _).Should().BeTrue());
                }

                break;
        }
    }

    [GeneratedRegex("<loc>([^<]+)</loc>")]
    private static partial Regex LocRegex();

    [GeneratedRegex("<title>([^<]*)</title>")]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<meta name=\"description\" content=\"([^\"]*)\"")]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex("<link rel=\"canonical\" href=\"([^\"]*)\"")]
    private static partial Regex CanonicalRegex();
}
