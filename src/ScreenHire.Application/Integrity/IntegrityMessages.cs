using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// Analyze one application's integrity: run the analyzers that have not run and reassess. Cascaded from
/// <c>ApplicationSubmitted</c>'s handler, so the workflow engine's evaluation and this pipeline retry and fail
/// independently. Safe to send again at any time (an analyzer that finishes later does): the pipeline is idempotent.
/// </summary>
public sealed record AnalyzeApplicationIntegrity(Guid OrganizationId, Guid ApplicationId) : ITenantMessage;

/// <summary>
/// The integrity engine assessed an application, or reassessed it with a different result. Ids, the two level names and
/// the ruleset version only: never evidence (the dashboard reads that through the API). <c>eventId</c> is the timeline
/// entry, which automations key their runs on.
/// </summary>
public sealed record IntegrityAssessed(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid JobId,
    AuthenticityLevel Authenticity,
    AiAssistanceLevel AiAssistance,
    string RulesetVersion,
    Guid EventId,
    DateTimeOffset AssessedAt) : ITenantEvent;

public static partial class AnalyzeApplicationIntegrityHandler
{
    // The DbContext is a parameter, not hidden inside the pipeline, so Wolverine's EF Core transaction (and its
    // SaveChanges and outbox flush) wraps the handler. The analyzers resolve from the same scope and share it.
    public static async Task<OutgoingMessages> Handle(
        AnalyzeApplicationIntegrity message,
        ScreenHireDbContext db,
        IEnumerable<IIntegrityAnalyzer> analyzers,
        TimeProvider time,
        ILogger<AnalyzeApplicationIntegrity> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = await new IntegrityPipeline(db, analyzers, time).RunAsync(message.ApplicationId, cancellationToken);
        LogAnalyzed(logger, message.ApplicationId, outgoing.Count > 0);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Integrity of application {ApplicationId} analyzed; assessment changed: {Changed}")]
    private static partial void LogAnalyzed(ILogger logger, Guid applicationId, bool changed);
}

/// <summary>
/// The daily retention sweep for raw integrity data (docs/research/legal.md, "short raw-data retention"). It has no
/// tenant of its own, and row-level security shows the worker no tenant rows without one, so it fans out one
/// <see cref="PurgeOrganizationIntegrityData"/> per organization.
/// </summary>
public sealed record PurgeExpiredIntegrityData;

/// <summary>Deletes one organization's raw network and telemetry data whose retention has ended by <paramref name="Cutoff"/>.</summary>
public sealed record PurgeOrganizationIntegrityData(Guid OrganizationId, DateTimeOffset Cutoff) : ITenantMessage;

public static class PurgeExpiredIntegrityDataHandler
{
    public static async Task<OutgoingMessages> Handle(PurgeExpiredIntegrityData message, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizations = await db.Organizations.AsNoTracking().Select(o => o.Id).ToListAsync(cancellationToken);
        var outgoing = new OutgoingMessages();
        outgoing.AddRange(organizations.Select(id => new PurgeOrganizationIntegrityData(id, now)));
        return outgoing;
    }
}

public static partial class PurgeOrganizationIntegrityDataHandler
{
    public static async Task Handle(
        PurgeOrganizationIntegrityData message,
        ScreenHireDbContext db,
        ITelemetrySource telemetry,
        ILogger<PurgeOrganizationIntegrityData> logger,
        CancellationToken cancellationToken)
    {
        var observations = await db.SubmissionObservations.Where(o => o.ExpiresAt <= message.Cutoff).ExecuteDeleteAsync(cancellationToken);
        var sessions = await telemetry.PurgeExpiredAsync(message.Cutoff, cancellationToken);
        LogPurged(logger, message.OrganizationId, observations, sessions);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: purged {Observations} submission observations and {Sessions} telemetry sessions")]
    private static partial void LogPurged(ILogger logger, Guid organizationId, int observations, int sessions);
}

/// <summary>
/// Refuses to start a host with the Signal Network switched on (ADR-0005). Enabling it needs legal review, a DPIA, a
/// controller notice, grants for the application role and the audited query path, none of which exist yet, so the
/// flag being on is a configuration mistake, not a feature.
/// </summary>
public sealed class SignalNetworkGate : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.SignalNetwork.Enabled
            ? ValidateOptionsResult.Fail(
                "ScreenHire:Integrity:SignalNetwork:Enabled is on, but the Signal Network stays off until legal review and DPIA sign-off (ADR-0005).")
            : ValidateOptionsResult.Success;
    }
}
