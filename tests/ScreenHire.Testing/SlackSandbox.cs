using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.Testing;

/// <summary>
/// Reads what the local Slack sandbox workspace received for a signed-in organization, through the API's sandbox route
/// (<c>GET /v1/slack/sandbox/messages</c>, docs/architecture/slack.md). A test uses it to see that a post really went,
/// rather than that a page said it would.
/// </summary>
public static class SlackSandbox
{
    /// <summary>The sandbox channel #hiring, which the sandbox workspace adds the app to.</summary>
    public const string HiringChannel = "C0SBHIRING";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The texts the organization's sandbox workspace holds for <paramref name="conversation"/>, newest first.</summary>
    public static async Task<IReadOnlyList<string>> MessagesAsync(ApiSession session, string conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var response = await session.Client.GetAsync("/v1/slack/sandbox/messages", cancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(cancellationToken));
        var all = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return [.. all.EnumerateArray()
            .Where(m => m.GetProperty("conversation").GetString() == conversation)
            .Select(m => m.GetProperty("text").GetString()!)];
    }

    /// <summary>
    /// Waits until <paramref name="conversation"/> holds a message containing <paramref name="contains"/> and returns it.
    /// A post from the worker (a published job, a new application) goes through the outbox and a handler first.
    /// </summary>
    public static async Task<string> WaitForMessageAsync(ApiSession session, string conversation, string contains, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        while (true)
        {
            var found = (await MessagesAsync(session, conversation, cancellationToken)).FirstOrDefault(t => t.Contains(contains, StringComparison.Ordinal));
            if (found is not null)
            {
                return found;
            }

            try
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The Slack sandbox received no message containing \"{contains}\" in {conversation} within {Timeout.TotalSeconds:0} s.");
            }
        }
    }
}
