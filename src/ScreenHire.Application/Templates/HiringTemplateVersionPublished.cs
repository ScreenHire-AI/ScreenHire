using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Templates;

/// <summary>
/// An organization published a hiring template version. Jobs copied from an older version now show "update available".
/// </summary>
public sealed record HiringTemplateVersionPublished(
    Guid OrganizationId, Guid TemplateId, Guid VersionId, int Version, DateTimeOffset PublishedAt) : ITenantEvent;

/// <summary>Runs in the worker once per <see cref="HiringTemplateVersionPublished"/>: reads the version back and logs it.</summary>
public static partial class HiringTemplateVersionPublishedHandler
{
    public static async Task Handle(
        HiringTemplateVersionPublished message, ScreenHireDbContext db, ILogger<HiringTemplateVersionPublished> logger, CancellationToken cancellationToken)
    {
        if (!await db.HiringTemplateVersions.AsNoTracking().AnyAsync(v => v.Id == message.VersionId, cancellationToken))
        {
            throw new InvalidOperationException($"Template version {message.VersionId} is not visible to organization {message.OrganizationId}.");
        }

        LogPublished(logger, message.TemplateId, message.Version);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hiring template {TemplateId} version {Version} published")]
    private static partial void LogPublished(ILogger logger, Guid templateId, int version);
}
