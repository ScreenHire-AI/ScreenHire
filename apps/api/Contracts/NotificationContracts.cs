using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The signed-in member's settings for teammates' changes as toasts, in their current organization. <c>toasts</c> turns
/// them on or off; <c>closeToasts</c> closes each after a few seconds, and off keeps it until dismissed; <c>off</c> lists
/// the kinds of change that do not toast. Everything starts on.
/// </summary>
public sealed record MemberNotificationSettings(bool Toasts, bool CloseToasts, IReadOnlyList<ToastKind> Off)
{
    internal static MemberNotificationSettings From(NotificationSettings settings) => new(settings.Toasts, settings.CloseToasts, settings.Off);
}

/// <summary>Replaces the signed-in member's notification settings; <c>off</c> names each kind at most once.</summary>
public sealed record UpdateNotificationSettingsRequest(bool Toasts, bool CloseToasts, IReadOnlyList<ToastKind> Off);
