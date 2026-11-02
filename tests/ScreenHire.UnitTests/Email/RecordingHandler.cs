using System.Net;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// Stands in for the email provider's servers: records each request (method, path, authorization and body) and answers
/// from a queue, so a test sees exactly what would have gone over the network. No network.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> answers = new();

    public List<RecordedRequest> Requests { get; } = [];

    public RecordingHandler Answer(HttpStatusCode status, string body = "")
    {
        answers.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
        var (status, text) = answers.Count > 0 ? answers.Dequeue() : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(text) };
    }
}

internal sealed record RecordedRequest(string Method, string Path, string? Authorization, string Body);
