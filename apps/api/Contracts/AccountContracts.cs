using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The signed-in person's own account in their current organization (docs/architecture/sign-in-and-tenancy.md, "Your
/// account"). <c>email</c> is the address they sign in with, shown read only. <c>name</c> is null while they have none.
/// <c>timeZone</c> (IANA) and <c>language</c> (BCP 47) are null until set, and the dashboard then uses the browser's.
/// <c>shortcutsEnabled</c> and <c>confirmMoves</c> are null until saved here, and the dashboard then keeps the browser's
/// earlier choice and saves it.
/// </summary>
public sealed record AccountDetails(
    string Email, string? Name, string DisplayName, string? PhotoUrl, string? TimeZone, string? Language, DateFormat DateFormat,
    bool? ShortcutsEnabled, bool? ConfirmMoves);

/// <summary>The person's name (global, the same in every organization), and their time zone, language and date format here.</summary>
public sealed record UpdateProfileRequest(string Name, string? TimeZone, string? Language, DateFormat DateFormat);

/// <summary>The browser's time zone, saved only while the person has none, so digests arrive at 08:00 their time.</summary>
public sealed record FillTimeZoneRequest(string TimeZone);

/// <summary>The person's working habits here: single-key shortcuts, and whether a move that sends emails or creates tasks asks first.</summary>
public sealed record UpdateAccountPreferencesRequest(bool ShortcutsEnabled, bool ConfirmMoves);

/// <summary>
/// A profile photo as base64, at most 512 KB once decoded, PNG, JPEG or WebP (read from the bytes, not the name). The
/// dashboard crops and scales it to 256 pixels first, which also drops what the camera wrote into it.
/// </summary>
public sealed record UploadPhotoRequest(string Data);

/// <summary>
/// The person's notification choices here (docs/architecture/notifications.md, "Each person's choices"). <c>pauseToasts</c>
/// stops every toast; <c>closeToasts</c> closes each after a few seconds; <c>toastsOff</c> lists the kinds of toast turned
/// off (the In the app column). <c>rows</c> are the grid's rows this person sees, in order. <c>slackLinked</c> says whether
/// their Slack account is linked, which shows the Slack column; <c>slackAvailable</c> whether the workspace is connected.
/// </summary>
public sealed record AccountNotifications(
    NoticeScope Scope, bool PauseToasts, bool CloseToasts, IReadOnlyList<ToastKind> ToastsOff, IReadOnlyList<NoticeRowState> Rows,
    bool SlackAvailable, bool SlackLinked);

/// <summary>
/// One row of the grid. <c>toasts</c> are the toast kinds its In the app switch covers (none: nothing toasts for it).
/// <c>email</c> is the person's choice and <c>emailDefault</c> where it starts. <c>slack</c> is null for a row with no Slack
/// message. <c>scoped</c> rows follow My jobs or Everything I can see.
/// </summary>
public sealed record NoticeRowState(
    NoticeKind Kind, IReadOnlyList<ToastKind> Toasts, EmailChoice Email, EmailChoice EmailDefault, bool? Slack, bool Scoped);

/// <summary>Saves the whole grid. A row the person does not see is ignored; <c>slack</c> is ignored for a row without one.</summary>
public sealed record UpdateAccountNotificationsRequest(
    NoticeScope Scope, bool PauseToasts, bool CloseToasts, IReadOnlyList<ToastKind> ToastsOff, IReadOnlyList<NoticeRowChoice> Rows);

public sealed record NoticeRowChoice(NoticeKind Kind, EmailChoice Email, bool? Slack);

/// <summary>
/// How the person signs in: the email link to <c>email</c> (when this ScreenHire sends them), the providers they have
/// signed in with, and how many passkeys they have of <c>passkeyLimit</c>.
/// </summary>
public sealed record AccountSignIn(string Email, bool EmailLinks, IReadOnlyList<LinkedSignIn> Providers, int Passkeys, int PasskeyLimit);

public sealed record LinkedSignIn(string Id, string Name);
