using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The local sandbox calendar: a provider with no third party, for Development and tests, as the sandbox workspace is
/// for Slack. Its consent page is the API's own <see cref="AuthorizePath"/>, which sends the browser straight back with
/// a code. The events it "creates" land in <c>calendar_sandbox_events</c>, and a member's busy time is their sandbox busy
/// rows (<c>POST /v1/calendar/sandbox/busy</c>) plus those events, so the tests read what a calendar would have shown.
/// </summary>
/// <remarks>
/// The tenant is the concrete <see cref="TenantContext"/>, not <see cref="ITenantContext"/>, for the reason
/// <c>SandboxSlackClient</c> gives: <c>CalendarAccess</c> holds this provider, so Wolverine's generated handler code builds
/// it, and a handler that takes both types gets two variables of the same name.
/// </remarks>
public sealed class SandboxCalendarProvider(ScreenHireDbContext db, TenantContext tenant, TimeProvider time) : ICalendarProvider
{
    public const string AuthorizePath = "/v1/calendar/sandbox/authorize";

    /// <summary>The prefix of a connect code; the signed-in person's user id follows.</summary>
    public const string ConnectCodePrefix = "sandbox-connect.";

    /// <summary>How long a sandbox access token lasts, so a refresh is easy to provoke by setting a connection's expiry.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);

    /// <summary>The reserved host of every sandbox meeting link (RFC 2606), so a test can recognise one.</summary>
    public const string MeetingHost = "meet.example.org";

    private const string AccessPrefix = "sandbox-access.";

    private const string RefreshPrefix = "sandbox-refresh.";

    public CalendarProvider Id => CalendarProvider.Sandbox;

    public Uri AuthorizeUrl(string state, Uri redirectUri) =>
        new(redirectUri, $"{AuthorizePath}?state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri.ToString())}");

    public async Task<CalendarGrant> ExchangeCodeAsync(string code, Uri redirectUri, CancellationToken cancellationToken)
    {
        // The code comes back in the address, so it only names the person signed in here: anyone else's id would connect
        // another person's calendar, in any organization (Users is not a tenant table).
        if (!code.StartsWith(ConnectCodePrefix, StringComparison.Ordinal) || !Guid.TryParse(code[ConnectCodePrefix.Length..], out var userId)
            || userId != tenant.UserId || tenant.OrganizationId is not { } organizationId)
        {
            throw new CalendarApiException("invalid_code", permanent: true);
        }

        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken)
            ?? throw new CalendarApiException("invalid_code", permanent: true);
        return Grant(RefreshPrefix, organizationId, userId, email);
    }

    public Task<CalendarGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var (organizationId, userId) = Check(refreshToken, RefreshPrefix);
        return Task.FromResult(Grant(null, organizationId, userId, null));
    }

    public Task RevokeAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IReadOnlyList<BusyInterval>> FreeBusyAsync(
        string accessToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken)
    {
        var (_, userId) = Check(accessToken, AccessPrefix);
        var blocks = await db.CalendarSandboxBusy.AsNoTracking()
            .Where(b => b.UserId == userId && b.StartsAt < windowEnd && b.EndsAt > windowStart)
            .Select(b => new BusyInterval(b.StartsAt, b.EndsAt))
            .ToListAsync(cancellationToken);
        var events = await db.CalendarSandboxEvents.AsNoTracking()
            .Where(e => e.UserId == userId && !e.Deleted && e.StartsAt < windowEnd && e.EndsAt > windowStart)
            .Select(e => new BusyInterval(e.StartsAt, e.EndsAt))
            .ToListAsync(cancellationToken);
        return [.. blocks.Concat(events).OrderBy(b => b.Start).ThenBy(b => b.End)];
    }

    public async Task<CalendarEventResult> CreateEventAsync(string accessToken, EventSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var (organizationId, userId) = Check(accessToken, AccessPrefix);
        if (await FindAsync(userId, spec.IdempotencyId, cancellationToken) is { } existing)
        {
            return new CalendarEventResult(existing.ProviderEventId, existing.MeetingUrl);
        }

        var created = new CalendarSandboxEvent
        {
            OrganizationId = organizationId,
            UserId = userId,
            ProviderEventId = spec.IdempotencyId,
            Summary = spec.Title,
            Description = spec.Description,
            MeetingUrl = spec.CreateMeetingLink ? MeetingUrl(spec.IdempotencyId) : null,
            StartsAt = spec.Start,
            EndsAt = spec.End,
        };
        db.CalendarSandboxEvents.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return new CalendarEventResult(created.ProviderEventId, created.MeetingUrl);
    }

    public async Task<CalendarEventResult> MoveEventAsync(
        string accessToken, string eventId, DateTimeOffset newStart, DateTimeOffset newEnd, CancellationToken cancellationToken)
    {
        var (_, userId) = Check(accessToken, AccessPrefix);
        var found = await FindAsync(userId, eventId, cancellationToken) ?? throw new CalendarApiException("event_not_found");
        found.StartsAt = newStart;
        found.EndsAt = newEnd;
        found.Deleted = false;
        await db.SaveChangesAsync(cancellationToken);
        return new CalendarEventResult(found.ProviderEventId, found.MeetingUrl);
    }

    public async Task CancelEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
    {
        var (_, userId) = Check(accessToken, AccessPrefix);
        if (await FindAsync(userId, eventId, cancellationToken) is { Deleted: false } found)
        {
            found.Deleted = true;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // Gone for good, not marked deleted: the row holds the candidate's name, and an erasure leaves none of it.
    public async Task RemoveEventAsync(string accessToken, string eventId, CancellationToken cancellationToken)
    {
        var (_, userId) = Check(accessToken, AccessPrefix);
        if (await FindAsync(userId, eventId, cancellationToken) is { } found)
        {
            db.CalendarSandboxEvents.Remove(found);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // The sandbox's event id is the idempotency id.
    public async Task<string?> FindEventIdAsync(string accessToken, string idempotencyId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken)
    {
        var (_, userId) = Check(accessToken, AccessPrefix);
        return await FindAsync(userId, idempotencyId, cancellationToken) is { } found ? found.ProviderEventId : null;
    }

    /// <summary>The meeting link the sandbox gives an event: on a reserved host, so nothing real is ever opened.</summary>
    public static string MeetingUrl(string eventId) => $"https://{MeetingHost}/sandbox/{Uri.EscapeDataString(eventId)}";

    /// <summary>The sandbox's access token for a member, as <see cref="ExchangeCodeAsync"/> gives it. For tests that need to call the provider directly.</summary>
    public static string AccessTokenFor(Guid organizationId, Guid userId) => $"{AccessPrefix}{organizationId:N}.{userId:N}";

    private Task<CalendarSandboxEvent?> FindAsync(Guid userId, string eventId, CancellationToken cancellationToken) =>
        db.CalendarSandboxEvents.SingleOrDefaultAsync(e => e.UserId == userId && e.ProviderEventId == eventId, cancellationToken);

    private CalendarGrant Grant(string? refreshPrefix, Guid organizationId, Guid userId, string? email) =>
        new(
            AccessTokenFor(organizationId, userId),
            refreshPrefix is null ? null : $"{refreshPrefix}{organizationId:N}.{userId:N}",
            time.GetUtcNow() + AccessTokenLifetime,
            email);

    // A token of another organization is refused as a provider refuses a token it did not issue for this account.
    private (Guid OrganizationId, Guid UserId) Check(string token, string prefix)
    {
        if (token.StartsWith(prefix, StringComparison.Ordinal))
        {
            var parts = token[prefix.Length..].Split('.');
            if (parts.Length == 2
                && Guid.TryParseExact(parts[0], "N", out var organizationId) && Guid.TryParseExact(parts[1], "N", out var userId)
                && organizationId == tenant.OrganizationId)
            {
                return (organizationId, userId);
            }
        }

        throw new CalendarApiException("invalid_auth", permanent: true);
    }
}
