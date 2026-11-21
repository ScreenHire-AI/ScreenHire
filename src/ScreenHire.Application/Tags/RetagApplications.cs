using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;

namespace ScreenHire.Application.Tags;

/// <summary><c>ScreenHire:Tags</c>: how Settings, Tags changes the organization's applications (docs/architecture/tags.md).</summary>
public sealed class TagOptions
{
    public const string Section = "ScreenHire:Tags";

    /// <summary>The most applications one <see cref="RetagApplications"/> changes; a full batch sends the next straight away.</summary>
    public int BatchSize { get; set; } = 200;
}

/// <summary>
/// Renames <see cref="Tag"/> to <see cref="NewName"/> on every application of the organization that has it, merging it
/// where the application already has the new name, or takes it off every application when <see cref="NewName"/> is null.
/// Tags compare ignoring case. Sent by Settings, Tags once the jobs' pipelines, the template drafts and the saved views
/// are changed; <see cref="ActorUserId"/> is the person who asked, and the timeline names them.
/// </summary>
public sealed record RetagApplications(Guid OrganizationId, string Tag, string? NewName, Guid? ActorUserId) : ITenantMessage;

/// <summary>
/// Changes up to <see cref="TagOptions.BatchSize"/> applications in the handler's transaction, through the change tracker
/// and the application's own <c>RenameTag</c> and <c>RemoveTag</c>, so each change is a TagRemoved and TagAdded on its
/// timeline (automations replay those) and one ApplicationTagsChanged for open dashboards. A full batch sends itself
/// again for the rest. A changed application no longer matches, so a redelivered or repeated message finds nothing to do.
/// </summary>
public static partial class RetagApplicationsHandler
{
    private const string Escape = "\\";

    public static async Task<OutgoingMessages> Handle(
        RetagApplications message,
        ScreenHireDbContext db,
        IOptions<TagOptions> options,
        TimeProvider time,
        ILogger<RetagApplications> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(db);
        var outgoing = new OutgoingMessages();
        var from = message.Tag.Trim();
        var to = message.NewName?.Trim();
        // Equal ignoring case, as ILIKE with every wildcard taken literally (apps/api's Like.Exactly does the same).
        var pattern = from.Replace(Escape, Escape + Escape, StringComparison.Ordinal).Replace("%", Escape + "%", StringComparison.Ordinal).Replace("_", Escape + "_", StringComparison.Ordinal);
        var batchSize = Math.Max(1, options.Value.BatchSize);

        // An application still to change has the old tag in some case other than the new name's exact spelling, so a
        // rename that changes only the case ("senior" to "Senior") stops matching once done. The worker's tenant sees
        // every job.
        var matching = to is null
            ? db.Applications.Where(a => a.Tags.Any(t => EF.Functions.ILike(t, pattern, Escape)))
            : db.Applications.Where(a => a.Tags.Any(t => EF.Functions.ILike(t, pattern, Escape) && t != to));
        var batch = await matching
            .OrderBy(a => a.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        var changed = 0;
        foreach (var application in batch)
        {
            IReadOnlyList<Domain.Applications.ApplicationEvent> events = to is null
                ? application.RemoveTag(from, now, message.ActorUserId) is { } removed ? [removed] : []
                : application.RenameTag(from, to, now, message.ActorUserId);
            if (events.Count == 0)
            {
                continue;
            }

            db.ApplicationEvents.AddRange(events);
            outgoing.Add(new ApplicationTagsChanged(application.OrganizationId, application.Id, application.JobId, events[^1].Id, now, message.ActorUserId));
            changed++;
        }

        // More may be waiting: the next batch is its own message, so no single transaction grows without bound. A batch
        // that changed nothing would only repeat itself, so it ends the run.
        if (batch.Count == batchSize && changed > 0)
        {
            outgoing.Add(message);
        }

        LogRetagged(logger, message.OrganizationId, changed, to is null ? "deleted" : "renamed");
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: {Count} applications had a tag {Change}")]
    private static partial void LogRetagged(ILogger logger, Guid organizationId, int count, string change);
}

public static class TagsRegistration
{
    /// <summary>The options of <see cref="RetagApplications"/>. Registered by the worker's messaging setup.</summary>
    public static IServiceCollection AddScreenHireTags(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<TagOptions>()
            .BindConfiguration(TagOptions.Section)
            .Validate(o => o.BatchSize >= 1, "ScreenHire:Tags:BatchSize must be at least 1.")
            .ValidateOnStart();
        return services;
    }
}
