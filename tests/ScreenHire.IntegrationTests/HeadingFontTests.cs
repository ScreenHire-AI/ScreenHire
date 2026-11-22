using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Npgsql;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Heading fonts from the Google Fonts catalogue, hosted by us (docs/architecture/branding.md section 3, ADR-0031),
/// against the sandbox catalogue the test stacks run: a family is listed with its styles, downloaded once on the
/// first save that names it, and served from our own storage with its licence. Each test uses a family no other test
/// saves, because the hosted fonts are shared by every organization on the stack.
/// </summary>
public sealed class HeadingFontTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_family_saved_from_the_catalogue_is_downloaded_once_and_served_from_our_storage_with_its_licence()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();

        var saved = (await first.PutAsync("/v1/organization/branding", new JsonObject { ["headingFont"] = "lora" })).Expect(HttpStatusCode.OK);
        saved.Text("headingFont.id").Should().Be("lora");
        saved.Text("headingFont.family").Should().Be("Lora");

        // The careers pages get the files, at the weights the titles use that Lora has (400 to 700): 600 and 700.
        var font = (await PublicBrandingAsync(first)).At("headingFont");
        font.At("bundled").GetBoolean().Should().BeFalse();
        font.At("category").GetString().Should().Be("serif");
        var files = font.At("files").EnumerateArray().Select(f => (f.At("fileName").GetString(), f.At("subset").GetString(), f.At("weight").GetString()))
            .ToList();
        files.Should().Equal(("latin-600.woff2", "latin", "600"), ("latin-700.woff2", "latin", "700"));
        font.At("files.0.unicodeRange").GetString().Should().StartWith("U+0000-00FF");

        using var anonymous = fixture.CreateHttpClient("api");
        using (var served = await anonymous.GetAsync("/v1/public/heading-fonts/lora/latin-700.woff2", Ct))
        {
            served.StatusCode.Should().Be(HttpStatusCode.OK);
            served.Content.Headers.ContentType!.MediaType.Should().Be("font/woff2");
            served.Headers.CacheControl!.ToString().Should().Contain("immutable");
            served.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
            (await served.Content.ReadAsByteArrayAsync(Ct))[..4].Should().Equal("wOF2"u8.ToArray());
        }

        (await anonymous.GetStringAsync("/v1/public/heading-fonts/lora/LICENSE.txt", Ct))
            .Should().Contain("SIL OPEN FONT LICENSE", "the licence is stored and served with the font");
        (await anonymous.GetAsync("/v1/public/heading-fonts/lora/latin-400.woff2", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "only the files that were hosted are served");
        (await anonymous.GetAsync("/v1/public/heading-fonts/merriweather/latin-700.woff2", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a family nobody saved is not hosted");

        var hosted = await HostedAsync("lora");
        hosted.License.Should().Be("Ofl");
        var blob = await BlobAsync("lora/5.3.0/latin-700.woff2");
        var etag = (await blob.GetPropertiesAsync(cancellationToken: Ct)).Value.ETag;

        // A second organization's save of the same family downloads nothing: the row and the files stay as they were.
        using var second = await fixture.SignedInWithOrganizationAsync();
        (await second.PutAsync("/v1/organization/branding", new JsonObject { ["headingFont"] = "lora" })).Expect(HttpStatusCode.OK);
        (await HostedAsync("lora")).Should().Be(hosted, "the family is hosted once, for everyone");
        (await blob.GetPropertiesAsync(cancellationToken: Ct)).Value.ETag.Should().Be(etag, "no file is written again");

        var history = (await first.GetAsync("/v1/organization/branding/history")).Expect(HttpStatusCode.OK);
        history.Text("entries.0.lines.0.text").Should().Be("changed the heading font to Lora");
    }

    [Fact]
    public async Task A_font_outside_the_catalogue_or_under_another_licence_is_refused_and_nothing_is_downloaded()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        foreach (var font in new[] { "ubuntu", "no-such-font", "Lora", "../lora", "lora;}" })
        {
            var refused = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["headingFont"] = font }))
                .Expect(HttpStatusCode.BadRequest);
            refused.ProblemKeys().Should().Equal("headingFont");
            refused.Text("errors.headingFont.0").Should().Be("Pick one of the heading fonts.");
        }

        (await owner.GetAsync("/v1/organization/branding")).Expect(HttpStatusCode.OK).At("headingFont").ValueKind.Should().Be(JsonValueKind.Null);
        (await HostedCountAsync("ubuntu")).Should().Be(0, "Ubuntu's licence is neither the OFL nor Apache 2.0");
    }

    [Fact]
    public async Task The_catalogue_lists_the_bundled_six_and_every_family_that_may_be_hosted_with_its_styles()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var catalogue = (await owner.GetAsync("/v1/heading-fonts")).Expect(HttpStatusCode.OK);
        catalogue.At("complete").GetBoolean().Should().BeTrue();
        var fonts = catalogue.At("fonts").EnumerateArray().ToDictionary(f => f.At("id").GetString()!);

        fonts.Values.Where(f => f.At("bundled").GetBoolean()).Select(f => f.At("id").GetString())
            .Should().BeEquivalentTo(["inter", "montserrat", "nunito", "fraunces", "playfair-display"], "the six stay");
        Styles(fonts["roboto-slab"]).Should().Equal("Serif", "Slab");
        Styles(fonts["quicksand"]).Should().Equal("Sans", "Rounded");
        Styles(fonts["lobster"]).Should().Equal("Display");
        fonts["roboto-slab"].At("license").GetString().Should().Be("Apache");
        fonts.Should().NotContainKey("ubuntu").And.NotContainKey("bricolage-grotesque");
        fonts.Values.Select(f => f.At("family").GetString()).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_preview_comes_through_the_api_to_a_signed_in_person_only()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        using (var preview = await owner.Session.Client.GetAsync("/v1/heading-fonts/merriweather/preview", Ct))
        {
            preview.StatusCode.Should().Be(HttpStatusCode.OK);
            preview.Content.Headers.ContentType!.MediaType.Should().Be("font/woff2");
            (await preview.Content.ReadAsByteArrayAsync(Ct))[..4].Should().Equal("wOF2"u8.ToArray());
        }

        (await owner.Session.Client.GetAsync("/v1/heading-fonts/inter/preview", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a bundled font ships with the dashboard");
        (await owner.Session.Client.GetAsync("/v1/heading-fonts/ubuntu/preview", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a family that may not be picked has no preview");
        using var anonymous = fixture.CreateHttpClient("api");
        (await anonymous.GetAsync("/v1/heading-fonts/merriweather/preview", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await HostedCountAsync("merriweather")).Should().Be(0, "a preview is not a hosted family");
    }

    private static string[] Styles(JsonElement font) => [.. font.At("styles").EnumerateArray().Select(s => s.GetString()!)];

    private async Task<JsonElement> PublicBrandingAsync(Tenant owner)
    {
        using var anonymous = fixture.CreateHttpClient("api");
        var slug = (await owner.Session.CurrentUserAsync(Ct))!.Value.At("organization.slug").GetString();
        return (await anonymous.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}", Ct)).At("branding");
    }

    private async Task<HostedRow> HostedAsync(string id)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT license, version, hosted_at FROM hosted_fonts WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue($"{id} is hosted");
        return new HostedRow(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2));
    }

    private async Task<long> HostedCountAsync(string id)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM hosted_fonts WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<BlobClient> BlobAsync(string name) =>
        new BlobServiceClient(await fixture.App.GetConnectionStringAsync("blobs", Ct) ?? throw new InvalidOperationException("No blob connection."))
            .GetBlobContainerClient("heading-fonts").GetBlobClient(name);

    private sealed record HostedRow(string License, string Version, DateTimeOffset HostedAt);
}
