using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace ScreenHire.IntegrationTests.Production;

/// <summary>
/// The API reference is public in production at the same path as in development: it documents the public API and holds
/// no data, and the dashboard's API keys and Webhooks pages link to it.
/// </summary>
public sealed class ApiReferenceTests(ProductionApiFixture fixture) : IClassFixture<ProductionApiFixture>
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task The_api_reference_and_its_openapi_file_are_served_with_production_settings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            starting.CancelAfter(StartTimeout);
            await fixture.App.ResourceNotifications.WaitForResourceAsync(
                "api", [KnownResourceStates.Running, .. KnownResourceStates.TerminalStates], starting.Token);
        }

        using var client = fixture.CreateHttpClient("api");

        using var reference = await GetOnceListeningAsync(client, "/scalar", cancellationToken);
        using var openApi = await client.GetAsync("/openapi/v1.json", cancellationToken);
        using var meta = await client.GetAsync("/v1/meta", cancellationToken);

        reference.StatusCode.Should().Be(HttpStatusCode.OK);
        reference.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var page = await reference.Content.ReadAsStringAsync(cancellationToken);
        page.Should().Contain("\"telemetry\":false", "the public page sends Scalar no telemetry")
            .And.Contain("\"withDefaultFonts\":false", "nor loads fonts from Scalar's CDN");
        openApi.StatusCode.Should().Be(HttpStatusCode.OK);
        (await openApi.Content.ReadAsStringAsync(cancellationToken)).Should().Contain("\"openapi\"");
        (await meta.Content.ReadAsStringAsync(cancellationToken)).Should().Contain("\"Production\"", "the fixture runs the API in Production");
    }

    // The process is running before Kestrel listens, so the first requests may be refused.
    private static async Task<HttpResponseMessage> GetOnceListeningAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ListenTimeout);
        while (true)
        {
            try
            {
                return await client.GetAsync(path, deadline.Token);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
            }
        }
    }
}
