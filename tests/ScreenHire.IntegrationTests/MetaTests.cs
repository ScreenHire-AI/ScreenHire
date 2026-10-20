using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>The public API metadata endpoint, against the shared AppHost.</summary>
public sealed class MetaTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Meta_reports_name_version_and_environment_without_sign_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient("api");

        using var response = await client.GetAsync("/v1/meta", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var meta = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        meta.GetProperty("name").GetString().Should().Be("ScreenHire");
        meta.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        meta.GetProperty("environment").GetString().Should().Be("Development", "the AppHost runs the API in Development");
    }
}
