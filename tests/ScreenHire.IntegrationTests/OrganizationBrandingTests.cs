using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// <c>/v1/organization/branding</c>: an owner saves a brand colour, the server derives the contrast-checked palette, and
/// the organization's careers pages get it through the public API.
/// </summary>
public sealed class OrganizationBrandingTests(AppHostFixture fixture)
{
    [Fact]
    public async Task An_owner_sets_a_brand_colour_and_the_careers_pages_get_its_palette()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var saved = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#4338CA" })).Expect(HttpStatusCode.OK);

        saved.Text("brandColor").Should().Be("#4338ca");
        saved.Text("palette.brand").Should().Be("#4338ca");
        saved.At("brandAdjusted").GetBoolean().Should().BeFalse();
        (await owner.GetAsync("/v1/organization/branding")).Expect(HttpStatusCode.OK).Text("brandColor").Should().Be("#4338ca");
        using var anonymous = fixture.CreateHttpClient("api");
        var slug = (await owner.Session.CurrentUserAsync(TestContext.Current.CancellationToken))!.Value.At("organization.slug").GetString();
        var publicOrganization = await anonymous.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}", TestContext.Current.CancellationToken);
        publicOrganization.At("branding.brand").GetString().Should().Be("#4338ca");
    }

    [Fact]
    public async Task A_mid_tone_is_darkened_until_white_text_on_it_is_readable()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var saved = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#808080" })).Expect(HttpStatusCode.OK);

        saved.At("brandAdjusted").GetBoolean().Should().BeTrue();
        BrandPalette.ContrastRatio(saved.Text("palette.brand"), saved.Text("palette.onBrand")).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public async Task A_malformed_colour_is_refused_and_null_clears_the_brand()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#123456" })).Expect(HttpStatusCode.OK);

        var refused = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "red; background:url(x)" }))
            .Expect(HttpStatusCode.BadRequest);
        var cleared = (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = null })).Expect(HttpStatusCode.OK);

        refused.ProblemKeys().Should().Equal("brandColor");
        cleared.At("brandColor").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.At("palette").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
