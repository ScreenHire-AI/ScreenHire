using System.Text;
using ScreenHire.Domain.Slack;

namespace ScreenHire.Application.Slack;

/// <summary>What a direct message is about.</summary>
public enum SlackDirectMessageKind
{
    Mention,
    Task,
}

/// <summary>
/// The words of every Slack message (docs/architecture/slack.md, "What a message says"). Each names the kind of event,
/// the candidate and the job, and links to the record, and nothing else: no stage or automation name, which can carry
/// a verification outcome (a routing automation may move someone to a stage named for a check), no teammate, and no
/// note or task text. Nothing here takes verification output, so no level can reach a channel (ADR-0004).
/// </summary>
public static class SlackMessages
{
    public static string ApplicationSubmitted(string candidate, string job, Uri? application) =>
        $"New application from {Link(application, candidate)} for {Escape(job)}";

    public static string ApplicationMoved(string candidate, string job, Uri? application) =>
        $"{Link(application, candidate)}'s stage changed on {Escape(job)}";

    public static string AutomationFailed(string candidate, string job, Uri? activity) =>
        $"An automation failed on {Link(activity, candidate)}'s application for {Escape(job)}";

    public static string JobPublished(string job, Uri? jobPage) => $"{Link(jobPage, job)} is published";

    public static string Mentioned(string candidate, string job, Uri? note) =>
        $"You were mentioned in a note on {Link(note, candidate)} for {Escape(job)}";

    public static string TaskGiven(string candidate, string job, Uri? application) =>
        $"You have a new task on {Link(application, candidate)} for {Escape(job)}";

    /// <summary>The line Send test message posts.</summary>
    public const string Test = "ScreenHire is connected. The events your team picked will post here.";

    /// <summary>The words for a channel event.</summary>
    public static string ForChannel(SlackChannelEvent kind, string candidate, string job, Uri? link) => kind switch
    {
        SlackChannelEvent.ApplicationSubmitted => ApplicationSubmitted(candidate, job, link),
        SlackChannelEvent.ApplicationMoved => ApplicationMoved(candidate, job, link),
        SlackChannelEvent.AutomationFailed => AutomationFailed(candidate, job, link),
        SlackChannelEvent.JobPublished => JobPublished(job, link),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Text safe in Slack's markup: Slack's three control characters escaped, so a candidate called
    /// <c>&lt;!channel&gt;</c> or <c>&lt;@U123&gt;</c> pings nobody and cannot make a link of their own.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            escaped.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ when char.IsControl(c) => " ",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    private static string Link(Uri? url, string text) =>
        url is null ? Escape(text) : $"<{url.AbsoluteUri}|{Escape(text)}>"; // Slack splits at the first |, so one in the text is fine
}
