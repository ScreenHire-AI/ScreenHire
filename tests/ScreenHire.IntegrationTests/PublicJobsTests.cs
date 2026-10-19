using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>Boots the full AppHost (requires Docker) and exercises the public careers API end to end.</summary>
public sealed class PublicJobsTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    // Fresh, isolated infrastructure per run: no shared data volumes, random ports (parallel worktrees, running dev stack).
    private static readonly string[] AppHostArgs = ["--ScreenHire:PersistData=false", "--ScreenHire:PinPorts=false"];

    [Fact]
    public async Task Demo_organization_lists_only_open_jobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>(AppHostArgs, cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        using var client = app.CreateHttpClient("api", "http");
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
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>(AppHostArgs, cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        using var client = app.CreateHttpClient("api", "http");
        var response = await client.GetAsync("/v1/public/organizations/does-not-exist/jobs", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
