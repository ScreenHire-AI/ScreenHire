using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Verification;

/// <summary>
/// Checks one application's duplicates again because another application now shares a detail with it (the pipeline sends
/// it when a later application's duplicate check finds this one), or because one that did is gone. The earlier application
/// then carries the same evidence as the later one, and the review log says when and why
/// (verification-engine.md §10.2). <c>CauseApplicationId</c> is the application that matched it, or the one erased; it only
/// keys the message, and the log never names it.
/// </summary>
public sealed record RecheckApplicationDuplicates(Guid OrganizationId, Guid ApplicationId, Guid CauseApplicationId) : ITenantMessage
{
    /// <summary>One intent, one key: this application rechecked because of that one (or because of an erasure).</summary>
    public string IdempotencyKey => $"recheck-duplicates:{ApplicationId}:{CauseApplicationId}";
}

public static partial class RecheckApplicationDuplicatesHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(RecheckApplicationDuplicates.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        RecheckApplicationDuplicates message,
        ScreenHireDbContext db,
        IEnumerable<IVerificationAnalyzer> analyzers,
        TimeProvider time,
        ILogger<RecheckApplicationDuplicates> logger,
        CancellationToken cancellationToken)
    {
        // A re-check never sends another one: the match is symmetric, and the application that caused this one has already
        // been checked against everything there was.
        var outgoing = await new VerificationPipeline(db, analyzers, time).RerunAsync(message.ApplicationId, "duplicates", cancellationToken);
        LogRechecked(logger, message.ApplicationId, outgoing.Count > 0);
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate check of application {ApplicationId} repeated; level announced: {Announced}")]
    private static partial void LogRechecked(ILogger logger, Guid applicationId, bool announced);
}
