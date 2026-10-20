using System.Collections.Concurrent;
using System.Net.Http.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Distribution;

namespace ScreenHire.Application.Distribution.GoogleIndexing;

/// <summary>Sends one notification to Google's Indexing API. Throws when Google did not accept it.</summary>
public interface IIndexingClient
{
    Task NotifyAsync(string url, IndexingNotificationType type, CancellationToken cancellationToken);
}

/// <summary>
/// The local stand-in for the Indexing API (<c>Mode = Local</c>): accepts every notification and remembers it, so
/// development and the tests exercise the queue, the quota and the posting states with no Google account.
/// </summary>
public sealed partial class LocalIndexingClient(ILogger<LocalIndexingClient> logger) : IIndexingClient
{
    private readonly ConcurrentQueue<(string Url, IndexingNotificationType Type)> sent = new();

    /// <summary>Everything this process has "sent", oldest first.</summary>
    public IReadOnlyCollection<(string Url, IndexingNotificationType Type)> Sent => sent;

    public Task NotifyAsync(string url, IndexingNotificationType type, CancellationToken cancellationToken)
    {
        sent.Enqueue((url, type));
        LogNotified(logger, type, url);
        return Task.CompletedTask;
    }

    // A careers URL holds the organization and job slugs, which are public; no personal data.
    [LoggerMessage(Level = LogLevel.Information, Message = "Local Indexing API accepted {Type} for {Url}")]
    private static partial void LogNotified(ILogger logger, IndexingNotificationType type, string url);
}

/// <summary>
/// The real Indexing API: <c>POST https://indexing.googleapis.com/v3/urlNotifications:publish</c>, authorized with the
/// service account in <c>ScreenHire:Distribution:GoogleIndexing:ServiceAccountKeyJson</c>, which must be an owner of the
/// careers domain's Search Console property. Not exercised by any test: it needs the owner's Google Cloud project.
/// </summary>
public sealed class GoogleIndexingClient(HttpClient http, IOptions<DistributionOptions> options) : IIndexingClient
{
    public const string Scope = "https://www.googleapis.com/auth/indexing";

    private static readonly Uri PublishUri = new("https://indexing.googleapis.com/v3/urlNotifications:publish");

    private readonly Lazy<ITokenAccess> credential = new(() =>
        GoogleCredential.FromJson(
                options.Value.GoogleIndexing.ServiceAccountKeyJson
                ?? throw new InvalidOperationException("ScreenHire:Distribution:GoogleIndexing:ServiceAccountKeyJson is not set."))
            .CreateScoped(Scope));

    public async Task NotifyAsync(string url, IndexingNotificationType type, CancellationToken cancellationToken)
    {
        var token = await credential.Value.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, PublishUri)
        {
            Content = JsonContent.Create(new
            {
                url,
                type = type == IndexingNotificationType.Deleted ? "URL_DELETED" : "URL_UPDATED",
            }),
        };
        request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
