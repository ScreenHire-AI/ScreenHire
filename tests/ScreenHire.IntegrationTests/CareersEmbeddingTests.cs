using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The sites allowed to embed an organization's careers pages (docs/architecture/careers-widget.md): Settings,
/// Organization saves them through <c>PUT /v1/organization/embedding</c>, the public responses carry them for the careers
/// pages' <c>frame-ancestors</c>, and an application from inside one keeps it as its source site.
/// </summary>
public sealed class CareersEmbeddingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Sites(params string[] origins) => new() { ["origins"] = new JsonArray([.. origins.Select(o => (JsonNode)JsonValue.Create(o))]) };

    [Fact]
    public async Task An_owner_lists_the_sites_as_origins_and_each_one_has_its_history_line()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Count("embedOrigins").Should().Be(0);

        var saved = (await owner.PutAsync("/v1/organization/embedding", Sites("www.example.com/careers", "https://JOBS.example.com", "https://www.example.com")))
            .Expect(HttpStatusCode.OK);

        Origins(saved.At("embedOrigins")).Should().Equal("https://www.example.com", "https://jobs.example.com");
        Origins((await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).At("embedOrigins"))
            .Should().Equal("https://www.example.com", "https://jobs.example.com");
        var history = (await owner.GetAsync("/v1/organization/history")).Expect(HttpStatusCode.OK).Body.GetRawText();
        history.Should().Contain("embed the careers page").And.Contain("https://jobs.example.com");

        (await owner.PutAsync("/v1/organization/embedding", Sites("https://jobs.example.com"))).Expect(HttpStatusCode.OK);
        (await owner.GetAsync("/v1/organization/history")).Expect(HttpStatusCode.OK).Body.GetRawText()
            .Should().Contain("embedding the careers page");
    }

    [Fact]
    public async Task A_site_that_is_not_https_a_wildcard_or_too_many_is_refused_with_why()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var http = (await owner.PutAsync("/v1/organization/embedding", Sites("https://ok.example.com", "http://www.example.com"))).Expect(HttpStatusCode.BadRequest);
        var wildcard = (await owner.PutAsync("/v1/organization/embedding", Sites("https://*.example.com"))).Expect(HttpStatusCode.BadRequest);
        var many = (await owner.PutAsync("/v1/organization/embedding", Sites([.. Enumerable.Range(1, 21).Select(i => $"https://site{i}.example.com")])))
            .Expect(HttpStatusCode.BadRequest);

        http.ProblemKeys().Should().Equal("origins[1]");
        http.At("errors").GetProperty("origins[1]")[0].GetString().Should().Be("A site that embeds the careers page uses https.");
        wildcard.ProblemKeys().Should().Equal("origins[0]");
        many.ProblemKeys().Should().Equal("origins");
        (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Count("embedOrigins").Should().Be(0);
    }

    [Fact]
    public async Task A_site_on_this_computer_is_allowed_where_the_stack_allows_loopback()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var saved = (await owner.PutAsync("/v1/organization/embedding", Sites("http://partner.localhost"))).Expect(HttpStatusCode.OK);

        Origins(saved.At("embedOrigins")).Should().Equal("http://partner.localhost");
    }

    [Fact]
    public async Task Only_owners_and_admins_change_the_sites()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var anonymous = fixture.CreateHttpClient("api");

        (await recruiter.PutAsync("/v1/organization/embedding", Sites("https://www.example.com"))).Expect(HttpStatusCode.Forbidden);
        (await anonymous.PutAsJsonAsync("/v1/organization/embedding", Sites("https://www.example.com"), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_public_pages_get_the_list_and_an_application_from_a_listed_site_keeps_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync("/v1/organization/embedding", Sites("https://www.example.com"))).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, owner);
        using var client = fixture.CreateHttpClient("api");

        var organization = await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}/", Ct);
        var board = await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}/jobs", Ct);
        Origins(organization.GetProperty("embedOrigins")).Should().Equal("https://www.example.com");
        Origins(board.GetProperty("embedOrigins")).Should().Equal("https://www.example.com");

        var embedded = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.GenuineHuman, Ct, embedOrigin: "https://www.example.com");
        var stranger = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.Bot, Ct, embedOrigin: "https://other.example.org");

        var detail = (await owner.GetAsync($"/v1/applications/{embedded}")).Expect(HttpStatusCode.OK);
        detail.Text("sourceSite").Should().Be("https://www.example.com");
        detail.Text("source").Should().Be("CareersSite", "the frame shows the same pages, notice and checks");
        (await owner.GetAsync($"/v1/applications/{stranger}")).Expect(HttpStatusCode.OK).At("sourceSite").ValueKind
            .Should().Be(JsonValueKind.Null, "only the organization's own listed sites are recorded");

        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == embedded, Ct);
        application.Source.Should().Be(ApplicationSource.CareersSite);
        (await db.SubmissionObservations.AsNoTracking().AnyAsync(o => o.ApplicationId == embedded, Ct))
            .Should().BeTrue("the Verification checks see a widget application as they see any careers-page one");
    }

    [Fact]
    public async Task One_organization_list_never_reaches_another()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        (await first.PutAsync("/v1/organization/embedding", Sites("https://www.example.com"))).Expect(HttpStatusCode.OK);
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, second);

        (await second.PutAsync("/v1/organization/embedding", Sites("https://jobs.example.org"))).Expect(HttpStatusCode.OK);
        var applied = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.GenuineHuman, Ct, embedOrigin: "https://www.example.com");

        Origins((await first.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).At("embedOrigins"))
            .Should().Equal(["https://www.example.com"], "the second organization's save changes only its own list");
        (await second.GetAsync($"/v1/applications/{applied}")).Expect(HttpStatusCode.OK).At("sourceSite").ValueKind
            .Should().Be(JsonValueKind.Null, "a site listed by another organization is not this one's");
    }

    private static IEnumerable<string> Origins(JsonElement array) => array.EnumerateArray().Select(o => o.GetString()!);
}
