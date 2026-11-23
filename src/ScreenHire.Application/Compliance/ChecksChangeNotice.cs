using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Compliance;

/// <summary>
/// <c>ScreenHire:Compliance:ChangeNotices</c>: whether each organization is told, in the bell, when ScreenHire's checks change
/// (docs/research/compliance-gap-map.md, CO-2). Colorado asks a developer to give each deployer direct notice of a material
/// update (C.R.S. 6-1-1702(2)); the public changelog is docs/compliance/checks-changelog.md. Off until the owner turns it on.
/// </summary>
public sealed class ChangeNoticeOptions
{
    public const string Section = "ScreenHire:Compliance:ChangeNotices";

    public bool Enabled { get; set; }
}

/// <summary>The daily look at whether the checks changed recently; it fans out one notification per organization.</summary>
public sealed record AnnounceChecksChange;

public static class ChecksChange
{
    /// <summary>
    /// How long after a new rules version the bell is raised. Shorter than the bell's retention (30 days by default), so a
    /// notice the daily purge removed is not raised again the next morning.
    /// </summary>
    public const int NoticeDays = 14;

    /// <summary>The day a rules version was made, from its <c>yyyy-MM-dd.n</c> form, or null for a version without one.</summary>
    public static DateOnly? DateOf(string version) =>
        version.Length >= 10 && DateOnly.TryParseExact(version[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    /// <summary>Whether <paramref name="version"/> was made within <see cref="NoticeDays"/> of <paramref name="now"/>.</summary>
    public static bool IsRecent(string version, DateTimeOffset now) =>
        DateOf(version) is { } day && day >= DateOnly.FromDateTime(now.UtcDateTime).AddDays(-NoticeDays);
}

public static class AnnounceChecksChangeHandler
{
    /// <summary>
    /// The bell's key is the version, and a person has one row per kind and key, so the daily repeat raises nothing new
    /// (<see cref="RaiseNotificationHandler"/>). It goes to the people who can change the organization's settings, the
    /// verification settings included.
    /// </summary>
    public static async Task<OutgoingMessages> Handle(
        AnnounceChecksChange message, ScreenHireDbContext db, IOptions<ChangeNoticeOptions> options, TimeProvider time, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (!options.Value.Enabled || !ChecksChange.IsRecent(VerificationRuleset.Version, time.GetUtcNow()))
        {
            return outgoing;
        }

        var payload = new Dictionary<string, string> { ["version"] = VerificationRuleset.Version };
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        outgoing.AddRange(organizations.Select(id => new RaiseNotification(
            id, Permission.EditSettings, NotificationKinds.ChecksChanged, VerificationRuleset.Version, payload, "/settings/verification")));
        return outgoing;
    }
}
