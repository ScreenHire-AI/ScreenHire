using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// A stand-in for a calendar provider's servers: answers the requests a provider sends, in order, and records each one.
/// No network. An answer is a status and a JSON body, or an exception the "network" throws.
/// </summary>
internal sealed class StubCalendarServer : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> answers = new();

    public List<Call> Calls { get; } = [];

    public StubCalendarServer Answer(HttpStatusCode status, string json = "", TimeSpan? retryAfter = null)
    {
        answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            if (json.Length > 0)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            if (retryAfter is { } wait)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            }

            return response;
        });
        return this;
    }

    public StubCalendarServer Ok(string json) => Answer(HttpStatusCode.OK, json);

    public StubCalendarServer Throw(Exception exception)
    {
        answers.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add(new Call(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
        return answers.Count > 0 ? answers.Dequeue()() : throw new InvalidOperationException($"No answer queued for {request.Method} {request.RequestUri!.AbsolutePath}.");
    }

    /// <summary>One request the provider sent.</summary>
    internal sealed record Call(HttpMethod Method, Uri Url, string? Authorization, string Body, Dictionary<string, string> Headers)
    {
        public string Path => Url.AbsolutePath;

        public Dictionary<string, string> Query => QueryHelpers.ParseQuery(Url.Query).ToDictionary(p => p.Key, p => p.Value.ToString());

        public Dictionary<string, string> Form => QueryHelpers.ParseQuery(Body).ToDictionary(p => p.Key, p => p.Value.ToString());

        public JsonNode Json => JsonNode.Parse(Body)!;
    }
}
