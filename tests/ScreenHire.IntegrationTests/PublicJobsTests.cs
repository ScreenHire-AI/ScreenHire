using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>The public careers API, against the shared AppHost.</summary>
public sealed class PublicJobsTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Demo_organization_lists_only_open_jobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient("api");

        var board = await client.GetFromJsonAsync<JsonElement>("/v1/public/organizations/brightwater/jobs", cancellationToken);

        board.GetProperty("organizationName").GetString().Should().Be("Brightwater Studio");
        var jobs = board.GetProperty("jobs").EnumerateArray().ToList();
        jobs.Should().HaveCount(3, "the seed has three open jobs and one draft");
        jobs.Select(j => j.GetProperty("slug").GetString()).Should().NotContain("customer-success-lead");
    }

    [Fact]
    public async Task Unknown_organization_returns_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient("api");

        var response = await client.GetAsync("/v1/public/organizations/does-not-exist/jobs", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Board_lists_open_jobs_newest_first_and_hides_drafts_and_closed_jobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var organization = await fixture.CreateOrganizationAsync(org =>
        {
            var older = TestData.NewJob(org, "older");
            older.Publish(now.AddDays(-2));
            var newer = TestData.NewJob(org, "newer");
            newer.Publish(now.AddDays(-1));
            var closed = TestData.NewJob(org, "closed");
            closed.Publish(now.AddDays(-3));
            closed.Close(now);
            return [older, newer, closed, TestData.NewJob(org, "draft")];
        }, cancellationToken);
        using var client = fixture.CreateHttpClient("api");

        var board = await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{organization.Slug}/jobs", cancellationToken);

        board.GetProperty("organizationName").GetString().Should().Be(organization.Name);
        board.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("slug").GetString())
            .Should().Equal("newer", "older");
    }

    // A closed job that was public is 410 Gone (teardown); one closed before it was ever published never existed publicly.
    [Theory]
    [InlineData("open", HttpStatusCode.OK)]
    [InlineData("draft", HttpStatusCode.NotFound)]
    [InlineData("closed", HttpStatusCode.Gone)]
    [InlineData("closed-unpublished", HttpStatusCode.NotFound)]
    [InlineData("missing", HttpStatusCode.NotFound)]
    public async Task Job_page_is_served_only_while_the_job_is_open(string slug, HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var organization = await fixture.CreateOrganizationAsync(org =>
        {
            var open = TestData.NewJob(org, "open");
            open.Publish(now);
            var closed = TestData.NewJob(org, "closed");
            closed.Publish(now);
            closed.Close(now);
            var closedUnpublished = TestData.NewJob(org, "closed-unpublished");
            closedUnpublished.Close(now);
            return [open, closed, closedUnpublished, TestData.NewJob(org, "draft")];
        }, cancellationToken);
        using var client = fixture.CreateHttpClient("api");

        var response = await client.GetAsync($"/v1/public/organizations/{organization.Slug}/jobs/{slug}", cancellationToken);

        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.Gone)
        {
            var gone = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            gone.GetProperty("validThrough").GetDateTimeOffset().Should().BeCloseTo(now, TimeSpan.FromSeconds(1), "a closed job expired when it closed");
        }
    }

    [Fact]
    public async Task Job_is_not_served_under_another_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await fixture.CreateOrganizationAsync(org =>
        {
            var job = TestData.NewJob(org, "shared-slug", "Owner's job");
            job.Publish(DateTimeOffset.UtcNow);
            return [job];
        }, cancellationToken);
        var other = await fixture.CreateOrganizationAsync(cancellationToken: cancellationToken);
        // The same slug, open, in a third organization: a query not scoped to the route's organization would find two.
        await fixture.CreateOrganizationAsync(org =>
        {
            var job = TestData.NewJob(org, "shared-slug", "Someone else's job");
            job.Publish(DateTimeOffset.UtcNow);
            return [job];
        }, cancellationToken);
        using var client = fixture.CreateHttpClient("api");

        var ownResponse = await client.GetAsync($"/v1/public/organizations/{owner.Slug}/jobs/shared-slug", cancellationToken);
        var otherResponse = await client.GetAsync($"/v1/public/organizations/{other.Slug}/jobs/shared-slug", cancellationToken);

        ownResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ownResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("title").GetString().Should().Be("Owner's job");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
