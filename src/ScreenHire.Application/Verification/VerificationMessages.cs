using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Verification;

/// <summary>
/// Analyze one application's verification: run the analyzers that have not run and reassess. Cascaded from
/// <c>ApplicationSubmitted</c>'s handler, so the workflow engine's evaluation and this pipeline retry and fail
/// independently. Safe to send again at any time (an analyzer that finishes later does): the pipeline is idempotent.
/// </summary>
public sealed record AnalyzeApplicationVerification(Guid OrganizationId, Guid ApplicationId) : ITenantMessage;

/// <summary>
/// The verification engine assessed an application, or reassessed it with a different result. Ids, the two level names and
/// the ruleset version only: never evidence (the dashboard reads that through the API). <c>eventId</c> is the timeline
/// entry, which automations key their runs on.
/// </summary>
public sealed record VerificationAssessed(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid JobId,
    AuthenticityLevel Authenticity,
    AiAssistanceLevel AiAssistance,
    string RulesetVersion,
    Guid EventId,
    DateTimeOffset AssessedAt) : IJobEvent;

public static partial class AnalyzeApplicationVerificationHandler
{
    // The DbContext is a parameter, not hidden inside the pipeline, so Wolverine's EF Core transaction (and its
    // SaveChanges and outbox flush) wraps the handler. The analyzers resolve from the same scope and share it.
    public static async Task<OutgoingMessages> Handle(
        AnalyzeApplicationVerification message,
        ScreenHireDbContext db,
        IEnumerable<IVerificationAnalyzer> analyzers,
        TimeProvider time,
        ILogger<AnalyzeApplicationVerification> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = await new VerificationPipeline(db, analyzers, time).RunAsync(message.ApplicationId, cancellationToken);
        LogAnalyzed(logger, message.ApplicationId, outgoing.Count > 0);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Verification of application {ApplicationId} analyzed; assessment changed: {Changed}")]
    private static partial void LogAnalyzed(ILogger logger, Guid applicationId, bool changed);
}

/// <summary>
/// The daily retention sweep for raw verification data (docs/research/legal.md, "short raw-data retention"). It has no
/// tenant of its own, and row-level security shows the worker no tenant rows without one, so it fans out one
/// <see cref="PurgeOrganizationVerificationData"/> per organization.
/// </summary>
public sealed record PurgeExpiredVerificationData;

/// <summary>Deletes one organization's raw network and telemetry data whose retention has ended by <paramref name="Cutoff"/>.</summary>
public sealed record PurgeOrganizationVerificationData(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class PurgeExpiredVerificationDataHandler
{
    public static async Task<OutgoingMessages> Handle(PurgeExpiredVerificationData message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationVerificationData(id, now)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationVerificationDataHandler
{
    public static async Task Handle(
        PurgeOrganizationVerificationData message,
        ScreenHireDbContext db,
        ITelemetrySource telemetry,
        ILogger<PurgeOrganizationVerificationData> logger,
        CancellationToken cancellationToken)
    {
        var observations = await db.SubmissionObservations.Where(o => o.ExpiresAt <= message.Cutoff).ExecuteDeleteAsync(cancellationToken);
        var sessions = await telemetry.PurgeExpiredAsync(message.Cutoff, cancellationToken);
        LogPurged(logger, message.OrganizationId, observations, sessions);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: purged {Observations} submission observations and {Sessions} telemetry sessions")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int observations, int sessions);
}
