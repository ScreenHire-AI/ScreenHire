using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>Boots the full AppHost (requires Docker) and checks the public API metadata endpoint.</summary>
public sealed class MetaTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    // Fresh, isolated infrastructure per run: no shared data volumes, random ports (parallel worktrees, running dev stack).
    private static readonly string[] AppHostArgs = ["--ScreenHire:PersistData=false", "--ScreenHire:PinPorts=false"];

    [Fact]
    public async Task Meta_reports_name_version_and_environment_without_sign_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>(AppHostArgs, cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        using var client = app.CreateHttpClient("api", "http");
        using var response = await client.GetAsync("/v1/meta", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var meta = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        meta.GetProperty("name").GetString().Should().Be("ScreenHire");
        meta.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        meta.GetProperty("environment").GetString().Should().Be("Development", "the AppHost runs the API in Development");
    }
}
