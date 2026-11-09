using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Verification.Content;

/// <summary>
/// Reads the consistency check again for an application whose AI check was paused when it was first assessed and has run
/// since (the cap was raised, or the month changed). The pipeline already holds a run for the analyzer, so without this the
/// candidate's Verification tab would keep saying the AI checks were paused. <c>CompletedTicks</c> only keys the message to
/// the outcome it follows.
/// </summary>
public sealed record RecheckApplicationConsistency(Guid OrganizationId, Guid ApplicationId, long CompletedTicks) : ITenantMessage
{
    public string IdempotencyKey => $"recheck-consistency:{ApplicationId}:{CompletedTicks}";
}

public static partial class RecheckApplicationConsistencyHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(RecheckApplicationConsistency.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        RecheckApplicationConsistency message,
        ScreenHireDbContext db,
        IEnumerable<IVerificationAnalyzer> analyzers,
        TimeProvider time,
        ILogger<RecheckApplicationConsistency> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = await new VerificationPipeline(db, analyzers, time).RerunAsync(message.ApplicationId, "consistency", cancellationToken);
        LogRechecked(logger, message.ApplicationId, outgoing.Count > 0);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consistency check of application {ApplicationId} read again after a pause; level announced: {Announced}")]
    private static partial void LogRechecked(ILogger logger, Guid applicationId, bool announced);
}
