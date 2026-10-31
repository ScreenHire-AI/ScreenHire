using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ScreenHire.Testing;

/// <summary>
/// A company's own web page on this machine, for the careers widget's end-to-end tests: a Kestrel server on
/// <c>127.0.0.1</c> and a random port that answers every path with one page holding <see cref="Snippet"/>. Browsers
/// resolve any <c>*.localhost</c> name to it, so <see cref="Origin"/> gives one test several sites on one server. It is a
/// real loopback server, not a routed page, because Chromium's Local Network Access stops a page it cannot place on this
/// machine from loading the careers pages here. The test stack lets an organization list such a site
/// (<c>ScreenHire:CareersEmbedding:AllowLoopback</c>). Start one per test.
/// </summary>
public sealed class HostPage : IAsyncDisposable
{
    private readonly WebApplication app;

    private HostPage(WebApplication app, int port)
    {
        this.app = app;
        Port = port;
    }

    public int Port { get; }

    /// <summary>What the page holds under its heading: the embed code a test pastes.</summary>
    public string Snippet { get; set; } = "";

    /// <summary>The site at <c>http://{name}.localhost:{port}</c>, as a browser writes its origin.</summary>
    public string Origin(string name) => $"http://{name}.localhost:{Port}";

    public static async Task<HostPage> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        HostPage? page = null;
        app.MapGet("/{**path}", () => Results.Content(
            $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Careers</title></head><body><h1>Work with us</h1>{page!.Snippet}</body></html>",
            "text/html; charset=utf-8"));
        await app.StartAsync(cancellationToken);
        var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        page = new HostPage(app, address.Port);
        return page;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
