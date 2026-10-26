using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.Infrastructure.Persistence.History;

/// <summary>
/// Records every change to a tracked object (<see cref="TrackedKinds"/>) as a <see cref="ChangeRecord"/>, added to the
/// same save, so it commits in the same transaction as the change or not at all (docs/architecture/change-history.md).
/// One save is one record per object, with a line per change. The actor is the tenant's user, else its API key, else
/// nobody, which the screen shows as ScreenHire: the worker, an automation, the demo's setup.
/// </summary>
/// <remarks>
/// Added by <see cref="DatabaseRegistration.AddScreenHireDatabase"/> only, never by <c>UseScreenHireConventions</c>: the
/// owner-run seeder, the design-time factory and test contexts write no history, so seeded objects start theirs at
/// their first edit.
/// </remarks>
public sealed class ChangeHistoryInterceptor(TimeProvider time, EmailTemplates emailTemplates) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is ScreenHireDbContext db)
        {
            await RecordAsync(db, cancellationToken);
        }

        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is ScreenHireDbContext db)
        {
            // Nothing saves synchronously today; a synchronous save still gets its history, through the same code.
            RecordAsync(db, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        return result;
    }

    // A failed save rolls its records back with it; forget them, so a retried save does not add them twice.
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData?.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData?.Context);
        return Task.CompletedTask;
    }

    private static void Forget(DbContext? context)
    {
        foreach (var entry in context?.ChangeTracker.Entries<ChangeRecord>().Where(e => e.State == EntityState.Added).ToList() ?? [])
        {
            entry.State = EntityState.Detached;
        }
    }

    private async ValueTask RecordAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (db.Tenant.OrganizationId is not { } organizationId)
        {
            return;
        }

        // The interceptor runs before EF's own detection: without this, an edit made by setting a property would still
        // look unchanged here.
        var tracker = db.ChangeTracker;
        tracker.DetectChanges();
        var entries = tracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && TrackedKinds.FindAll(e.Metadata.ClrType).Count > 0)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            var context = new ChangeContext(db, emailTemplates);
            var subjects = new Dictionary<(ChangeSubjectKind Kind, string Id), List<ChangeLine>>();
            foreach (var entry in entries)
            {
                foreach (var kind in TrackedKinds.FindAll(entry.Metadata.ClrType))
                {
                    var lines = await kind.DescribeAsync(entry, context, cancellationToken);
                    if (lines.Count == 0)
                    {
                        continue;
                    }

                    var key = (kind.Subject, kind.SubjectId(entry.Entity));
                    if (!subjects.TryGetValue(key, out var subjectLines))
                    {
                        subjects[key] = subjectLines = [];
                    }

                    subjectLines.AddRange(lines);
                }
            }

            var now = time.GetUtcNow();
            foreach (var ((kind, id), lines) in subjects)
            {
                db.ChangeRecords.Add(new ChangeRecord
                {
                    OrganizationId = organizationId,
                    SubjectKind = kind,
                    SubjectId = id,
                    ActorUserId = db.Tenant.UserId,
                    ActorApiKeyId = db.Tenant.UserId is null ? db.Tenant.ApiKeyId : null,
                    OccurredAt = now,
                    Lines = lines,
                });
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }
}
