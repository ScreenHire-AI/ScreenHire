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

    // Automation emails go through the outbox, the worker queue and up to two handlers before they are sent.
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(60);

    // One client for every instance: the fixture hands out a new Mailpit per access, and the wait polls every 100 ms.
    private static readonly HttpClient Client = new();

    /// <summary>The sign-in link tokens sent to <paramref name="address"/>, newest first.</summary>
    public async Task<IReadOnlyList<string>> SignInTokensAsync(string address, CancellationToken cancellationToken) =>
        [.. (await SignInLinksAsync(address, cancellationToken)).Select(TokenOf)];

    /// <summary>
    /// Waits until <paramref name="count"/> sign-in emails have reached <paramref name="address"/> and returns their
    /// tokens, newest first. The API sends before it answers, so this normally returns on the first look.
    /// </summary>
    public async Task<IReadOnlyList<string>> WaitForSignInTokensAsync(string address, int count, CancellationToken cancellationToken) =>
        [.. (await WaitForSignInLinksAsync(address, count, cancellationToken)).Select(TokenOf)];

    /// <summary>The token from the newest sign-in email to <paramref name="address"/>.</summary>
    public async Task<string> WaitForSignInTokenAsync(string address, CancellationToken cancellationToken) =>
        (await WaitForSignInTokensAsync(address, 1, cancellationToken))[0];

    /// <summary>The link the newest sign-in email carries: the dashboard's verify page with the token in the fragment.</summary>
    public async Task<Uri> WaitForSignInLinkAsync(string address, CancellationToken cancellationToken) =>
        (await WaitForSignInLinksAsync(address, 1, cancellationToken))[0];

    /// <summary>
    /// The token from the newest invitation email to <paramref name="address"/>. Invitation links carry
    /// <c>#invitation=</c>, not <c>#token=</c>, so they never pass for a sign-in link.
    /// </summary>
    public async Task<string> WaitForInvitationTokenAsync(string address, CancellationToken cancellationToken) =>
        (await WaitForInvitationAsync(address, cancellationToken)).Groups["token"].Value;

    /// <summary>The link the newest invitation email to <paramref name="address"/> carries: the dashboard's accept page.</summary>
    public async Task<Uri> WaitForInvitationLinkAsync(string address, CancellationToken cancellationToken) =>
        new((await WaitForInvitationAsync(address, cancellationToken)).Value);

    // Skips the address's other email (a sign-in link), so an invitation that arrives second is still found.
    private async Task<Match> WaitForInvitationAsync(string address, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        while (true)
        {
            var invitation = (await MessagesAsync(address, cancellationToken))
                .Select(message => InvitationToken().Match(message.Text))
                .FirstOrDefault(match => match.Success);
            if (invitation is not null)
            {
                return invitation;
            }

            try
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Mailpit received no invitation email for the test address within {Timeout.TotalSeconds:0} s.");
            }
        }
    }

    /// <summary>Waits until at least <paramref name="count"/> emails have reached <paramref name="address"/>, then returns them all.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> WaitForMessagesAsync(string address, int count, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(MessageTimeout);
        while (true)
        {
            var messages = await MessagesAsync(address, cancellationToken);
            if (messages.Count >= count)
            {
                return messages;
            }

            try
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Mailpit received {messages.Count} of {count} emails for the test address within {MessageTimeout.TotalSeconds:0} s.");
            }
        }
    }

    private async Task<IReadOnlyList<Uri>> WaitForSignInLinksAsync(string address, int count, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        while (true)
        {
            var links = await SignInLinksAsync(address, cancellationToken);
            if (links.Count >= count)
            {
                return links;
            }

            try
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Mailpit received {links.Count} of {count} sign-in emails for the test address within {Timeout.TotalSeconds:0} s.");
            }
        }
    }

    private async Task<IReadOnlyList<Uri>> SignInLinksAsync(string address, CancellationToken cancellationToken) =>
        [.. (await MessagesAsync(address, cancellationToken))
            .Select(message => SignInLink().Match(message.Text))
            .Where(match => match.Success)
            .Select(match => new Uri(match.Value))];

    /// <summary>
    /// Every email to <paramref name="address"/>, newest first: subject and bodies. The one place that searches Mailpit
    /// and fetches message bodies.
    /// </summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesAsync(string address, CancellationToken cancellationToken)
    {
        var search = await Client.GetFromJsonAsync<JsonElement>(
            new Uri(baseAddress, $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}"), cancellationToken);

        var messages = new List<MailpitMessage>();
        foreach (var summary in search.GetProperty("messages").EnumerateArray())
        {
            var message = await Client.GetFromJsonAsync<JsonElement>(
                new Uri(baseAddress, $"/api/v1/message/{summary.GetProperty("ID").GetString()}"), cancellationToken);
            messages.Add(new MailpitMessage(
                message.GetProperty("Subject").GetString() ?? "", message.GetProperty("Text").GetString() ?? "", message.GetProperty("HTML").GetString() ?? ""));
        }

        return messages;
    }

    private static string TokenOf(Uri link) => SignInToken().Match(link.Fragment).Groups["token"].Value;

    [GeneratedRegex(@"#token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex SignInToken();

    [GeneratedRegex(@"https?://\S+#token=[A-Za-z0-9_-]+")]
    private static partial Regex SignInLink();

    [GeneratedRegex(@"https?://\S+#invitation=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex InvitationToken();
}

/// <summary>One email as Mailpit stored it.</summary>
public sealed record MailpitMessage(string Subject, string Text, string Html);
