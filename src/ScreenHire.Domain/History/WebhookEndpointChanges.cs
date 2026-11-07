using ScreenHire.Domain.Webhooks;

namespace ScreenHire.Domain.History;

/// <summary>
/// What the history may see of a webhook endpoint's signing secret: when it last changed, nothing that derives it. The
/// secret's inputs are a row of their own (<see cref="WebhookEndpointSecret"/>), so a rotation is the only fact history
/// takes from it (docs/architecture/change-history.md, "Secrets").
/// </summary>
public sealed record SigningRotation(DateTimeOffset RotatedAt)
{
    public static SigningRotation Of(WebhookEndpointSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new(secret.RotatedAt);
    }
}

/// <summary>
/// A webhook endpoint's changes in words: "created the endpoint <b>hooks.example.com</b>", "stopped sending <b>Job
/// closed</b>", "turned off the endpoint <b>hooks.example.com</b>" and "rotated the signing secret". An endpoint is named
/// by its host, never its full address: the path or the query often carries a token, and history keeps every line for
/// good, so a change to the rest of the address reads "changed the address of" with no values (as an automation's
/// webhook does in <see cref="DefinitionChanges"/>). The worker's automatic turn-off is not a change anyone made: it is
/// the endpoint page's disabled reason.
/// </summary>
public static class WebhookEndpointChanges
{
    /// <param name="before">The endpoint before the save; null when the save added it.</param>
    /// <param name="after">The endpoint after the save; null when it was deleted, which has no history to show.</param>
    public static IReadOnlyList<ChangeLine> Describe(WebhookEndpoint? before, WebhookEndpoint? after)
    {
        if (after is null)
        {
            return [];
        }

        var host = Host(after.Url);
        if (before is null)
        {
            List<ChangeDetail> created = [new("Events", null, EventList(after.Events))];
            if (after.Description is { } description)
            {
                created.Add(new("Description", null, description));
            }

            return [ChangeLine.Of("created the endpoint ", host, details: created)];
        }

        var lines = new List<ChangeLine>();
        if (before.Url != after.Url)
        {
            var was = Host(before.Url);
            lines.Add(was != host
                ? ChangeLine.Of($"changed the address from {was} to ", host)
                : ChangeLine.Of("changed the address of ", host));
        }

        foreach (var type in after.Events.Except(before.Events, StringComparer.Ordinal))
        {
            lines.Add(ChangeLine.Of("started sending ", EventLabel(type)));
        }

        foreach (var type in before.Events.Except(after.Events, StringComparer.Ordinal))
        {
            lines.Add(ChangeLine.Of("stopped sending ", EventLabel(type)));
        }

        if (before.Description != after.Description)
        {
            var lead = after.Description is null ? "removed the description" : before.Description is null ? "added a description" : "changed the description";
            lines.Add(ChangeLine.Plain(lead, [new ChangeDetail("Description", before.Description, after.Description)]));
        }

        if (before.Enabled != after.Enabled)
        {
            lines.Add(ChangeLine.Of(after.Enabled ? "turned on the endpoint " : "turned off the endpoint ", host));
        }

        return lines;
    }

    /// <param name="before">The secret's facts before the save; null when the save added them, with the endpoint.</param>
    /// <param name="after">The facts after the save.</param>
    public static IReadOnlyList<ChangeLine> Rotation(SigningRotation? before, SigningRotation? after) =>
        before is null || after is null || before.RotatedAt == after.RotatedAt
            ? []
            : [ChangeLine.Plain("rotated the signing secret")];

    /// <summary>
    /// An event type in words, as the dashboard says it (<c>eventLabel</c> in <c>apps/dashboard/src/lib/webhooks.ts</c>):
    /// <c>application.submitted</c> is "Application submitted".
    /// </summary>
    public static string EventLabel(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var words = type.Replace('.', ' ').Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string EventList(IEnumerable<string> types) => string.Join(", ", types.Select(EventLabel));

    // The host alone: the rest of an address can hold a token.
    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "the endpoint";
}
