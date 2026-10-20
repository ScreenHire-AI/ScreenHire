using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.Testing;

/// <summary>
/// Reads the email the stack sent, through the AppHost's Mailpit container's HTTP API. Each test uses its own address,
/// so a search by recipient only ever sees that test's messages.
/// </summary>
public sealed partial class Mailpit(Uri baseAddress)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The sign-in link tokens sent to <paramref name="address"/>, newest first.</summary>
    public async Task<IReadOnlyList<string>> SignInTokensAsync(string address, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = baseAddress };
        var search = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}", cancellationToken);

        var tokens = new List<string>();
        foreach (var summary in search.GetProperty("messages").EnumerateArray())
        {
            var message = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/message/{summary.GetProperty("ID").GetString()}", cancellationToken);
            if (SignInToken().Match(message.GetProperty("Text").GetString() ?? "") is { Success: true } match)
            {
                tokens.Add(match.Groups["token"].Value);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Waits until <paramref name="count"/> sign-in emails have reached <paramref name="address"/> and returns their
    /// tokens, newest first. The API sends before it answers, so this normally returns on the first look.
    /// </summary>
    public async Task<IReadOnlyList<string>> WaitForSignInTokensAsync(string address, int count, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        while (true)
        {
            var tokens = await SignInTokensAsync(address, cancellationToken);
            if (tokens.Count >= count)
            {
                return tokens;
            }

            try
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Mailpit received {tokens.Count} of {count} sign-in emails for the test address within {Timeout.TotalSeconds:0} s.");
            }
        }
    }

    /// <summary>The token from the newest sign-in email to <paramref name="address"/>.</summary>
    public async Task<string> WaitForSignInTokenAsync(string address, CancellationToken cancellationToken) =>
        (await WaitForSignInTokensAsync(address, 1, cancellationToken))[0];

    /// <summary>The link a sign-in email carries: the dashboard's verify page with the token in the fragment.</summary>
    public async Task<Uri> WaitForSignInLinkAsync(string address, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = baseAddress };
        await WaitForSignInTokensAsync(address, 1, cancellationToken);
        var search = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}", cancellationToken);
        var newest = search.GetProperty("messages")[0].GetProperty("ID").GetString();
        var message = await client.GetFromJsonAsync<JsonElement>($"/api/v1/message/{newest}", cancellationToken);
        return new Uri(SignInLink().Match(message.GetProperty("Text").GetString() ?? "").Value);
    }

    [GeneratedRegex(@"#token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex SignInToken();

    [GeneratedRegex(@"https?://\S+#token=[A-Za-z0-9_-]+")]
    private static partial Regex SignInLink();
}
