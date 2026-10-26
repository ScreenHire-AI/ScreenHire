using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// Where each of the organization's templates is used and when it was last used (docs/design/design-system.md section
/// 21). Reads through the tenant filter and row-level security, like every hiring read, in one query per kind of row,
/// then works out the uses in memory with <see cref="TemplateUses"/>.
/// </summary>
/// <remarks>
/// Used by: jobs that are not closed for a hiring template; hiring templates that are not archived, by their current
/// versions (the newest published one and the draft), for a scorecard; automations in jobs that are not closed and in
/// those current versions for an email. Last used: the newest job started from a hiring template; the newest job with a
/// stage naming a scorecard, or scorecard given at such a stage; the newest email sent from an email template.
/// </remarks>
internal static class TemplateUsage
{
    public static async Task<TemplateUsageList> ListAsync(ScreenHireDbContext db, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A built-in became the team's when the organization was created, so its "not used in 6 months" counts from then.
        var joined = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.CreatedAt).SingleAsync(cancellationToken);
        var templates = await db.HiringTemplates.AsNoTracking()
            .Select(t => new HiringRow(t.Id, t.Name, t.OrganizationId == null, t.CreatedAt, t.ArchivedAt != null))
            .ToListAsync(cancellationToken);
        var current = await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.Status == TemplateVersionStatus.Draft
                || db.HiringTemplates.Any(t => t.Id == v.TemplateId && t.LatestPublishedVersion == v.Version))
            .Select(v => new { v.TemplateId, v.Definition })
            .ToListAsync(cancellationToken);
        var jobs = await db.Jobs.AsNoTracking()
            .Select(j => new JobRow(j.Id, j.Title, j.Status, j.CreatedAt, j.SourceTemplateId, j.Workflow))
            .ToListAsync(cancellationToken);
        var given = await db.ApplicationScorecards.AsNoTracking()
            .GroupBy(s => new { s.JobId, s.StageId })
            .Select(g => new { g.Key.JobId, g.Key.StageId, Last = g.Max(s => s.SubmittedAt) })
            .ToListAsync(cancellationToken);
        var scorecards = await db.ScorecardTemplates.AsNoTracking()
            .Select(t => new OwnRow(t.Id, t.CreatedAt, t.ArchivedAt != null))
            .ToListAsync(cancellationToken);
        var emails = await db.EmailTemplates.AsNoTracking()
            .Where(t => t.BuiltInId == null)
            .Select(t => new OwnRow(t.Id, t.CreatedAt, t.ArchivedAt != null))
            .ToListAsync(cancellationToken);
        // The sent-email entry keeps its template in its JSON data, which LINQ cannot reach through the column's converter.
        var sent = await db.Database.SqlQuery<LastSent>(
                $"""
                SELECT data->>'emailTemplateId' AS template_id, max(occurred_at) AS sent_at
                FROM application_events
                WHERE type = 'EmailSent' AND data->>'emailTemplateId' IS NOT NULL
                GROUP BY data->>'emailTemplateId'
                """)
            .ToListAsync(cancellationToken);

        var live = jobs.Where(j => j.Status != JobStatus.Closed).ToList();
        var shown = templates.Where(t => !t.Archived).ToDictionary(t => t.Id);
        var versions = current.Where(v => shown.ContainsKey(v.TemplateId)).GroupBy(v => v.TemplateId).ToList();

        var started = jobs.Where(j => j.SourceTemplateId is not null).GroupBy(j => j.SourceTemplateId!.Value).ToDictionary(g => g.Key, g => g.Max(j => j.CreatedAt));
        var hiring = templates.Select(t =>
        {
            DateTimeOffset? lastUsed = started.TryGetValue(t.Id, out var at) ? at : null;
            var since = t.IsSystem ? Later(t.CreatedAt, joined) : t.CreatedAt;
            return new TemplateUse(
                t.Id.ToString("D"),
                [.. live.Where(j => j.SourceTemplateId == t.Id).OrderBy(j => j.Title, StringComparer.CurrentCulture).Select(j => new UsageItem(UsageKind.Job, j.Id, j.Title))],
                lastUsed,
                !t.Archived && TemplateUses.NotUsedRecently(lastUsed, since, now));
        });

        // Scorecards: which hiring templates name each one, and when a stage naming it last took a job or a scorecard.
        var namedBy = versions
            .SelectMany(g => g.SelectMany(v => v.Definition.Workflow.ScorecardTemplateIds()).Distinct(StringComparer.Ordinal).Select(key => (key, g.Key)))
            .ToLookup(pair => pair.key, pair => shown[pair.Key], StringComparer.Ordinal);
        var lastGiven = given.ToDictionary(g => (g.JobId, g.StageId), g => g.Last);
        var scorecardUsed = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            foreach (var stage in job.Workflow.Stages ?? [])
            {
                if (stage?.ScorecardTemplateId is not { } key)
                {
                    continue;
                }

                var at = lastGiven.TryGetValue((job.Id, stage.Id), out var last) && last > job.CreatedAt ? last : job.CreatedAt;
                if (!scorecardUsed.TryGetValue(key, out var known) || at > known)
                {
                    scorecardUsed[key] = at;
                }
            }
        }

        TemplateUse Scorecard(string key, DateTimeOffset since, bool archived)
        {
            DateTimeOffset? lastUsed = scorecardUsed.TryGetValue(key, out var at) ? at : null;
            return new(
                key,
                [.. namedBy[key].OrderBy(t => t.Name, StringComparer.CurrentCulture).Select(t => new UsageItem(UsageKind.HiringTemplate, t.Id, t.Name))],
                lastUsed,
                !archived && TemplateUses.NotUsedRecently(lastUsed, since, now));
        }

        // Emails: the automations that send each one, in live jobs and in the current versions of the templates shown.
        var automations = new List<(string Key, UsageItem Item)>();
        foreach (var job in live)
        {
            automations.AddRange(TemplateUses.EmailUses(job.Workflow)
                .Select(use => (use.EmailTemplate, new UsageItem(UsageKind.JobAutomation, job.Id, job.Title, use.AutomationId, use.AutomationName))));
        }

        foreach (var group in versions)
        {
            var template = shown[group.Key];
            automations.AddRange(group
                .SelectMany(v => TemplateUses.EmailUses(v.Definition.Workflow))
                .DistinctBy(use => (use.EmailTemplate, use.AutomationId))
                .Select(use => (use.EmailTemplate, new UsageItem(UsageKind.TemplateAutomation, template.Id, template.Name, use.AutomationId, use.AutomationName))));
        }

        var sentBy = automations.ToLookup(a => a.Key, a => a.Item, StringComparer.Ordinal);
        var lastSent = sent.ToDictionary(s => s.TemplateId, s => s.SentAt, StringComparer.Ordinal);
        TemplateUse Email(string key, DateTimeOffset since, bool archived)
        {
            DateTimeOffset? lastUsed = lastSent.TryGetValue(key, out var at) ? at : null;
            return new(
                key,
                [.. sentBy[key].OrderBy(i => i.Name, StringComparer.CurrentCulture).ThenBy(i => i.AutomationName, StringComparer.CurrentCulture)],
                lastUsed,
                !archived && TemplateUses.NotUsedRecently(lastUsed, since, now));
        }

        return new(
            [.. hiring],
            [
                .. BuiltInScorecards.All.Select(b => Scorecard(b.Id, joined, archived: false)),
                .. scorecards.Select(s => Scorecard(s.Id.ToString("D"), s.CreatedAt, s.Archived)),
            ],
            [
                .. EmailTemplateIds.BuiltIn.Select(id => Email(id, joined, archived: false)),
                .. emails.Select(e => Email(e.Id.ToString("D"), e.CreatedAt, e.Archived)),
            ]);
    }

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private sealed record HiringRow(Guid Id, string Name, bool IsSystem, DateTimeOffset CreatedAt, bool Archived);

    private sealed record JobRow(Guid Id, string Title, JobStatus Status, DateTimeOffset CreatedAt, Guid? SourceTemplateId, HiringWorkflow Workflow);

    private sealed record OwnRow(Guid Id, DateTimeOffset CreatedAt, bool Archived);

    /// <summary>A row of the sent-email query: a template id and when an email last went out from it.</summary>
    private sealed record LastSent(string TemplateId, DateTimeOffset SentAt);
}
