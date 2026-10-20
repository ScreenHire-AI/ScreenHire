using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// A .NET SignalR client on the API's real-time hub, signed in with an <see cref="ApiSession"/>'s cookie as a browser
/// would be. It records every event it receives, so a test can wait for one and check what never arrived.
/// </summary>
internal sealed class EventsConnection : IAsyncDisposable
{
    private const string HubPath = "/v1/hubs/events";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<ReceivedEvent> received = new();
    private readonly TaskCompletionSource<Exception?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private EventsConnection(ApiSession session)
    {
        Hub = new HubConnectionBuilder()
            .WithUrl(new Uri(session.BaseAddress, HubPath), options => options.Cookies = session.Cookies)
            .Build();
        Hub.On<JsonElement>("ReceiveEvent", e => received.Enqueue(new ReceivedEvent(e.GetProperty("type").GetString()!, e.GetProperty("data"))));
        Hub.Closed += error =>
        {
            closed.TrySetResult(error);
            return Task.CompletedTask;
        };
    }

    public HubConnection Hub { get; }

    public IReadOnlyCollection<ReceivedEvent> Received => received;

    /// <summary>Completes when the server closes the connection.</summary>
    public Task<Exception?> Closed => closed.Task;

    /// <summary>Connects with <paramref name="session"/>'s cookie. Throws what SignalR throws, such as a 401.</summary>
    public static async Task<EventsConnection> ConnectAsync(ApiSession session, CancellationToken cancellationToken)
    {
        var connection = new EventsConnection(session);
        try
        {
            await connection.Hub.StartAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    /// <summary>Waits for an event of <paramref name="type"/> whose data has <paramref name="property"/> equal to <paramref name="value"/>.</summary>
    public async Task<JsonElement> WaitForAsync(string type, string property, Guid value, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        try
        {
            while (true)
            {
                if (Find(type, property, value) is { } match)
                {
                    return match;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No {type} with {property} {value} within {Deadline.TotalSeconds:0} seconds. Received: " +
                string.Join(", ", received.Select(e => $"{e.Type} {e.Data}")));
        }
    }

    /// <summary>Whether any received event, of any type, mentions <paramref name="id"/>.</summary>
    public bool ReceivedAnythingAbout(Guid id) =>
        received.Any(e => e.Data.GetRawText().Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync() => await Hub.DisposeAsync();

    private JsonElement? Find(string type, string property, Guid value) =>
        received.FirstOrDefault(e => e.Type == type
            && e.Data.TryGetProperty(property, out var actual)
            && actual.GetGuid() == value)?.Data;
}

internal sealed record ReceivedEvent(string Type, JsonElement Data);
