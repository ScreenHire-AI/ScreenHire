using System.Net;

namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// The line every email to a candidate ends with: a link to their page, where they see where each application stands and
/// read and answer their messages (docs/architecture/candidate-portal.md). ScreenHire's fixed wording, added after an
/// organization's template is rendered and sanitized, as the header is, so a template cannot change or remove it. It is
/// added to the email that goes out and never to the copy the team keeps: the link opens the candidate's page, so it
/// goes to the candidate alone.
/// </summary>
public static class CandidatePortalFooter
{
    /// <summary>The sentence before the link, in the text and the HTML part alike.</summary>
    public const string Sentence = "See where your application stands and read your messages:";

    /// <summary>The words of the HTML link, which sits on a line of its own after the sentence.</summary>
    public const string LinkText = "Your applications and messages";

    /// <summary><paramref name="message"/> with the footer after its body. Without a link the message is returned as it is.</summary>
    public static EmailMessage Append(EmailMessage message, string? link)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(link))
        {
            return message;
        }

        var text = $"{message.TextBody.TrimEnd()}\n\n{Sentence} {link}\n";
        var line = $"<p>{Sentence} <a href=\"{WebUtility.HtmlEncode(link)}\">{LinkText}</a></p>";
        var html = message.HtmlBody is { Length: > 0 } body ? InBody(body, line) : null;
        return message with { TextBody = text, HtmlBody = html };
    }

    // A rendered template is a whole document, so the line goes inside its body. ScreenHire's own emails are a fragment.
    private static string InBody(string html, string line)
    {
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? $"{html}\n{line}" : $"{html[..end]}{line}\n{html[end..]}";
    }
}
