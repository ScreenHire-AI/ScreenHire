namespace ScreenHire.Api.Contracts;

/// <summary>
/// One notification in the signed-in member's bell. The dashboard builds the plain line from <c>kind</c> and <c>payload</c>,
/// so the wording lives in one place and a kind it does not know still shows with its link.
/// </summary>
/// <remarks>
/// <c>kind</c>: What happened, such as <c>sending-domain-failing</c>. <c>payload</c>: The facts the line is made from.
/// <c>link</c>: The dashboard page that fixes it, as a path. <c>readAt</c>: When the member read it, or null while unread.
/// </remarks>
public sealed record NotificationItem(
    Guid Id,
    string Kind,
    IReadOnlyDictionary<string, string> Payload,
    string Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>The member's notifications that are not dismissed, newest first, and how many of all of them are unread.</summary>
/// <remarks><c>unreadCount</c>: Counts every unread one, including any beyond the page that <c>items</c> holds.</remarks>
public sealed record NotificationList(IReadOnlyList<NotificationItem> Items, int UnreadCount);
