using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.OAuth;

/// <summary>
/// Daily: OAuth client registrations nobody approved, and codes and tokens past their own expiry (ADR-0028). Client
/// registration is open to anyone, so a registration no person approved within a day goes. The worker sees no
/// organization's rows without a tenant, so it hands each organization's sweep to a tenant message, as
/// <c>PurgeExpiredSlackMessages</c> does. A used refresh token stays until it would have expired: reuse detection needs it.
/// </summary>
public sealed record PurgeExpiredOAuthData;

/// <summary>Deletes one organization's OAuth codes and tokens that expired before <paramref name="Cutoff"/>.</summary>
public sealed record PurgeOrganizationOAuthData(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static partial class PurgeExpiredOAuthDataHandler
{
    /// <summary>How long a registration nobody approved is kept, and how long past expiry a code or token is kept.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    public static async Task<OutgoingMessages> Handle(
        PurgeExpiredOAuthData message, ScreenHireDbContext db, TimeProvider time, ILogger<PurgeExpiredOAuthData> logger,
        CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - Grace;
        var clients = await db.OAuthClients.Where(c => c.LastUsedAt == null && c.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        LogClientsPurged(logger, clients);

        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationOAuthData(id, cutoff)));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Clients} OAuth client registrations nobody approved")]
    private static partial void LogClientsPurged(ILogger logger, int clients);
}

public static partial class PurgeOrganizationOAuthDataHandler
{
    public static async Task Handle(
        PurgeOrganizationOAuthData message, ScreenHireDbContext db, ILogger<PurgeOrganizationOAuthData> logger,
        CancellationToken cancellationToken)
    {
        var codes = await db.OAuthCodes.Where(c => c.ExpiresAt < message.Cutoff).ExecuteDeleteAsync(cancellationToken);
        var tokens = await db.OAuthTokens.Where(t => t.ExpiresAt < message.Cutoff).ExecuteDeleteAsync(cancellationToken);
        if (codes + tokens > 0)
        {
            LogPurged(logger, message.OrganizationId, codes, tokens);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: purged {Codes} OAuth codes and {Tokens} OAuth tokens")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int codes, int tokens);
}
