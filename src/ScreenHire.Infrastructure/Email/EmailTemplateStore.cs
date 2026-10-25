using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Infrastructure.Email;

/// <summary>One of the signed-in organization's templates as it sends: its id, its name and its wording.</summary>
/// <remarks><c>builtIn</c>: one of <see cref="EmailTemplateIds.BuiltIn"/>. <c>edited</c>: the organization changed a built-in's wording.</remarks>
public sealed record OrganizationEmailTemplate(
    string Id, string Name, bool BuiltIn, bool Edited, EmailTemplateContent Content, DateTimeOffset? UpdatedAt);

/// <summary>
/// Finds the organization's wording of a template: its row, or ScreenHire's wording of a built-in with none. Reads
/// through the tenant's query filter and row-level security, so it sees only the organization the context is set to.
/// </summary>
public sealed class EmailTemplateStore(EmailTemplates templates)
{
    /// <summary>ScreenHire's names for the built-in templates, which organizations cannot rename.</summary>
    public static IReadOnlyDictionary<string, string> BuiltInNames => EmailTemplateIds.Names;

    /// <summary>The built-in templates first, in their order, then the organization's own, oldest first.</summary>
    public async Task<List<OrganizationEmailTemplate>> ListAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.EmailTemplates.AsNoTracking().OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        var reworded = rows.Where(r => r.BuiltInId is not null).ToDictionary(r => r.BuiltInId!, StringComparer.Ordinal);
        return
        [
            .. EmailTemplateIds.BuiltIn.Select(id => reworded.TryGetValue(id, out var row) ? ToTemplate(row) : Default(id)),
            .. rows.Where(r => r.BuiltInId is null).Select(ToTemplate),
        ];
    }

    /// <summary>The template <paramref name="id"/> names, or null when the organization has no such template.</summary>
    public async Task<OrganizationEmailTemplate?> FindAsync(ScreenHireDbContext db, string id, CancellationToken cancellationToken)
    {
        if (EmailTemplateIds.All.Contains(id))
        {
            var row = await db.EmailTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.BuiltInId == id, cancellationToken);
            return row is null ? Default(id) : ToTemplate(row);
        }

        if (!Guid.TryParseExact(id, "D", out var rowId))
        {
            return null;
        }

        var own = await db.EmailTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == rowId && t.BuiltInId == null, cancellationToken);
        return own is null ? null : ToTemplate(own);
    }

    /// <summary>Which of <paramref name="ids"/> name no template of the organization.</summary>
    public static async Task<IReadOnlySet<string>> MissingAsync(ScreenHireDbContext db, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(EmailTemplateIds.IsOrganizationTemplate).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return new HashSet<string>();
        }

        var rowIds = wanted.Select(id => Guid.ParseExact(id, "D")).ToList();
        var found = await db.EmailTemplates.AsNoTracking()
            .Where(t => t.BuiltInId == null && rowIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        return wanted.Except(found.Select(f => f.ToString("D")), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
    }

    private OrganizationEmailTemplate Default(string id) => new(id, BuiltInNames[id], true, false, templates.Default(id), null);

    private static OrganizationEmailTemplate ToTemplate(EmailTemplate row) =>
        new(row.Key, row.BuiltInId is { } builtIn ? BuiltInNames[builtIn] : row.Name ?? "", row.BuiltInId is not null,
            row.BuiltInId is not null, new(row.Subject, row.Body), row.UpdatedAt);
}
