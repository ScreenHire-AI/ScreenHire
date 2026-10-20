using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>Candidate journeys on the careers pages, against the seeded (read-only) Brightwater organization.</summary>
public sealed class CareersTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] OpenJobTitles = ["Senior Product Designer", "Senior .NET Engineer", "Frontend Engineer (Svelte)"];

    [Fact]
    public async Task Board_lists_Brightwaters_open_jobs()
    {
        await GoToAsync("careers", "/brightwater");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Open roles at Brightwater Studio");
        var jobs = Page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Listitem);
        await Expect(jobs).ToHaveCountAsync(OpenJobTitles.Length);
        foreach (var title in OpenJobTitles)
        {
            await Expect(jobs.Filter(new() { HasText = title })).ToHaveCountAsync(1);
        }

        await Expect(Page.GetByText("Customer Success Lead")).ToHaveCountAsync(0); // the seeded draft
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task Candidate_opens_a_job_from_the_board()
    {
        await GoToAsync("careers", "/brightwater");

        await Page.GetByRole(AriaRole.Link).Filter(new() { HasText = "Senior .NET Engineer" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Uri(Fixture.GetEndpoint("careers"), "/brightwater/jobs/senior-dotnet-engineer").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Senior .NET Engineer");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
    }

    // Google for Jobs' required properties: title, description, datePosted, hiringOrganization, and a location —
    // jobLocation for on-site/hybrid roles, or jobLocationType TELECOMMUTE plus applicantLocationRequirements for remote.
    [Theory]
    [InlineData("senior-dotnet-engineer", "Senior .NET Engineer", true)]
    [InlineData("frontend-engineer-svelte", "Frontend Engineer (Svelte)", false)]
    public async Task Job_page_carries_valid_JobPosting_json_ld(string slug, string title, bool remote)
    {
        await GoToAsync("careers", $"/brightwater/jobs/{slug}");

        var scripts = Page.Locator("script[type='application/ld+json']");
        await Expect(scripts).ToHaveCountAsync(1);
        using var document = JsonDocument.Parse(await scripts.TextContentAsync() ?? "");
        var posting = document.RootElement;

        posting.GetProperty("@context").GetString().Should().Be("https://schema.org");
        posting.GetProperty("@type").GetString().Should().Be("JobPosting");
        posting.GetProperty("title").GetString().Should().Be(title);
        posting.GetProperty("description").GetString().Should().StartWith("<p>Join Brightwater Studio");
        DateTimeOffset.TryParse(posting.GetProperty("datePosted").GetString(), out _).Should().BeTrue("datePosted must be an ISO 8601 date");
        posting.GetProperty("employmentType").GetString().Should().Be("FULL_TIME");
        posting.GetProperty("hiringOrganization").GetProperty("name").GetString().Should().Be("Brightwater Studio");

        if (remote)
        {
            posting.GetProperty("jobLocationType").GetString().Should().Be("TELECOMMUTE");
            var countries = posting.GetProperty("applicantLocationRequirements").EnumerateArray().ToList();
            countries.Select(c => c.GetProperty("@type").GetString()).Should().AllBe("Country");
            countries.Select(c => c.GetProperty("name").GetString()).Should().Equal("GB", "IE");
        }
        else
        {
            var address = posting.GetProperty("jobLocation").GetProperty("address");
            address.GetProperty("addressLocality").GetString().Should().Be("London");
            address.GetProperty("addressCountry").GetString().Should().Be("GB");
        }

        var salary = posting.GetProperty("baseSalary");
        salary.GetProperty("currency").GetString().Should().Be("GBP");
        var range = salary.GetProperty("value");
        range.GetProperty("minValue").GetDecimal().Should().BeLessThanOrEqualTo(range.GetProperty("maxValue").GetDecimal());
        range.GetProperty("unitText").GetString().Should().Be("YEAR");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "About the role" })).ToContainTextAsync("Join Brightwater Studio");
        await ExpectNoAccessibilityViolationsAsync();
    }
}
