using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// The careers side of one-click publish and teardown (docs/architecture/job-distribution.md): a job published through
/// the API is on the careers page with complete JobPosting JSON-LD and in the tenant's sitemap; closed, its page serves
/// 410 Gone with a past validThrough and it leaves the sitemap. Each test publishes in its own new organization.
/// </summary>
public sealed class CareersDistributionTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_published_job_is_on_the_careers_page_and_in_the_sitemap_and_closing_it_serves_410()
    {
        using var session = new ApiSession(Fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("careers"), Ct);
        await session.OnboardAsync(Ct);
        var slug = (await session.CurrentUserAsync(Ct))!.Value.GetProperty("organization").GetProperty("slug").GetString()!;
        var jobId = await CreateAndPublishAsync(session);
        var careers = Fixture.GetEndpoint("careers");
        var page = new Uri(careers, $"/{slug}/jobs/distributed-role").ToString();

        await GoToAsync("careers", $"/{slug}/jobs/distributed-role");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Distributed role");
        await Expect(Page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", page);
        using (var posting = await JsonLdAsync())
        {
            var json = posting.RootElement;
            json.GetProperty("@type").GetString().Should().Be("JobPosting");
            json.GetProperty("url").GetString().Should().Be(page);
            json.GetProperty("identifier").GetProperty("value").GetString().Should().Be(jobId);
            json.GetProperty("employmentType").GetString().Should().Be("CONTRACTOR", "Google's value for a contract role");
            json.GetProperty("validThrough").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
        }

        await ExpectNoAccessibilityViolationsAsync();
        (await SitemapAsync(slug)).Should().Contain($"<loc>{page}</loc>");

        using (var closed = await session.PostAsync($"/v1/jobs/{jobId}/close", new { }, Ct))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var gone = await Page.GotoAsync(page);
        gone!.Status.Should().Be(410);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("This job has closed");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" })).ToHaveCountAsync(0);
        using (var expired = await JsonLdAsync())
        {
            expired.RootElement.GetProperty("validThrough").GetDateTimeOffset().Should().BeBefore(DateTimeOffset.UtcNow, "Google reads a past validThrough as expired");
        }

        await ExpectNoAccessibilityViolationsAsync();
        (await SitemapAsync(slug)).Should().NotContain(page);
    }

    private static async Task<string> CreateAndPublishAsync(ApiSession session)
    {
        using var created = await session.PostAsync("/v1/jobs", new
        {
            title = "Distributed role",
            slug = "distributed-role",
            description = "<p>Help us run the systems our customers rely on every day, with a small team that owns its work.</p>",
            employmentType = "Contract",
            workplaceType = "Onsite",
            locations = new[] { new { city = "Leeds", country = "GB" } },
            validThrough = DateTimeOffset.UtcNow.AddDays(30),
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        using var published = await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct);
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        return jobId;
    }

    private async Task<JsonDocument> JsonLdAsync()
    {
        var scripts = Page.Locator("script[type='application/ld+json']");
        await Expect(scripts).ToHaveCountAsync(1);
        return JsonDocument.Parse(await scripts.TextContentAsync() ?? "");
    }

    private async Task<string> SitemapAsync(string slug)
    {
        using var client = Fixture.CreateHttpClient("careers");
        using var response = await client.GetAsync($"/{slug}/sitemap.xml", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        return await response.Content.ReadAsStringAsync(Ct);
    }
}
