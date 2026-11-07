using ScreenHire.Application.Applications;

namespace ScreenHire.Application.Portal;

/// <summary>
/// How a kept copy of an email reads on the candidate's own page. A copy the team keeps says
/// <see cref="ApplicationEmails.QuestionnaireLinkNotKept"/> where the candidate's own link was, because the link lets whoever
/// holds it act as the candidate. On the candidate's page that text would read as a fault, so it says where the link is.
/// </summary>
public static class PortalMessageText
{
    /// <summary>What the page shows in place of the team's placeholder.</summary>
    public const string LinkIsInTheEmail = "(your link is in the email we sent you)";

    /// <summary>The kept copy's plain text, ready to show to the candidate it was sent to.</summary>
    public static string ForCandidate(string keptText)
    {
        ArgumentNullException.ThrowIfNull(keptText);
        return keptText.Replace(ApplicationEmails.QuestionnaireLinkNotKept, LinkIsInTheEmail, StringComparison.Ordinal);
    }

    /// <summary>The subject of a reply to <paramref name="subject"/>: one "Re: " in front, never two.</summary>
    public static string ReplySubject(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var trimmed = subject.Trim();
        return trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"Re: {trimmed}";
    }

    /// <summary>The <c>Message-ID</c> a reply written on the page is stored under, so sending the same form twice stores it once.</summary>
    public static string MessageIdFor(string key) => $"{MessageIdPrefix}{key}@portal.invalid>";

    /// <summary>The start of every <see cref="MessageIdFor"/>: how a day's replies from pages are counted.</summary>
    public const string MessageIdPrefix = "<portal-";
}
