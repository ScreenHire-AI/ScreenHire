using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The organization's shared tags (docs/architecture/tags.md). Tags live on applications as plain text, so the list is
/// read from them, through the tenant filter and job scope like every hiring read, plus the tags automations name. Tags
/// compare ignoring case everywhere; a tag shows as it is spelled where it is used most.
/// </summary>
internal static class OrganizationTags
{
    public static async Task<IReadOnlyList<TagSummary>> ListAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var counts = await db.Applications.AsNoTracking()
            .SelectMany(a => a.Tags)
            .GroupBy(t => t)
            .Select(g => new { Tag = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Automations in the jobs that can still run them, and in the organization's templates that are not archived.
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.Status != JobStatus.Closed).Select(j => j.Workflow).ToListAsync(cancellationToken);
        var drafts = await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.OrganizationId != null && v.Status == TemplateVersionStatus.Draft
                && db.HiringTemplates.Any(t => t.Id == v.TemplateId && t.ArchivedAt == null))
            .Select(v => v.Definition)
            .ToListAsync(cancellationToken);
        var published = await PublishedAsync(db, cancellationToken);

        var tags = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in counts.Where(r => !string.IsNullOrWhiteSpace(r.Tag)))
        {
            var entry = Find(tags, row.Tag);
            entry.Applications += row.Count;
            if (entry.Spelling is null || row.Count > entry.SpellingCount)
            {
                (entry.Spelling, entry.SpellingCount) = (row.Tag, row.Count);
            }
        }

        foreach (var tag in jobs.Concat(drafts.Select(d => d.Workflow)).SelectMany(WorkflowTags.Named))
        {
            Find(tags, tag);
        }

        foreach (var template in published)
        {
            foreach (var tag in WorkflowTags.Named(template.Definition.Workflow))
            {
                Find(tags, tag).Templates.Add(new TagTemplateUse(template.TemplateId, template.Name, template.Version));
            }
        }

        return
        [
            .. tags.Values
                .Select(e => new TagSummary(e.Spelling!, e.Applications, [.. e.Templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)]))
                .OrderByDescending(t => t.Applications)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// How the organization already spells <paramref name="tag"/>, ignoring case, or the tag as written when it is new, so
    /// "senior" added where "Senior" exists is "Senior" and never a second tag.
    /// </summary>
    public static async Task<string> SpellingOfAsync(ScreenHireDbContext db, string tag, CancellationToken cancellationToken)
    {
        var trimmed = tag.Trim();
        var pattern = Like.Exactly(trimmed);
        var used = await db.Applications.AsNoTracking()
            .SelectMany(a => a.Tags)
            .Where(t => EF.Functions.ILike(t, pattern, Like.Escape))
            .GroupBy(t => t)
            .Select(g => new { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Select(g => g.Tag)
            .FirstOrDefaultAsync(cancellationToken);
        if (used is not null)
        {
            return used;
        }

        // A tag no application has yet may still be one an automation names.
        return (await ListAsync(db, cancellationToken)).FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase))?.Name
            ?? trimmed;
    }

    /// <summary>The newest published version of each of the organization's hiring templates that is not archived.</summary>
    public static async Task<IReadOnlyList<PublishedTemplate>> PublishedAsync(ScreenHireDbContext db, CancellationToken cancellationToken) =>
        await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.OrganizationId != null && v.Status == TemplateVersionStatus.Published)
            .Join(
                db.HiringTemplates.Where(t => t.ArchivedAt == null),
                v => new { Id = v.TemplateId, Version = (int?)v.Version },
                t => new { t.Id, Version = t.LatestPublishedVersion },
                (v, t) => new PublishedTemplate(t.Id, t.Name, v.Version, v.Definition))
            .ToListAsync(cancellationToken);

    /// <summary>The templates whose newest published version names <paramref name="tag"/>.</summary>
    public static async Task<IReadOnlyList<TagTemplateUse>> PublishedNamingAsync(ScreenHireDbContext db, string tag, CancellationToken cancellationToken) =>
        [
            .. (await PublishedAsync(db, cancellationToken))
                .Where(t => WorkflowTags.Names(t.Definition.Workflow, tag))
                .Select(t => new TagTemplateUse(t.TemplateId, t.Name, t.Version))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
        ];

    internal sealed record PublishedTemplate(Guid TemplateId, string Name, int Version, HiringTemplateDefinition Definition);

    private static Entry Find(Dictionary<string, Entry> tags, string tag)
    {
        var trimmed = tag.Trim();
        if (!tags.TryGetValue(trimmed, out var entry))
        {
            entry = new Entry { Spelling = trimmed };
            tags[trimmed] = entry;
        }

        return entry;
    }

    private sealed class Entry
    {
        public string? Spelling { get; set; }

        public int SpellingCount { get; set; }

        public int Applications { get; set; }

        public List<TagTemplateUse> Templates { get; } = [];
    }
}
