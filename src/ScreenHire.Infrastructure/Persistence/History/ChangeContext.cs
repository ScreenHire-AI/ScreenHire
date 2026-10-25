using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.Infrastructure.Persistence.History;

/// <summary>
/// What a describer may look up while a save is being recorded: entities the save already holds first, otherwise one
/// query by id, through the tenant filter like any other read.
/// </summary>
public sealed class ChangeContext(ScreenHireDbContext db, EmailTemplates emailTemplates)
{
    /// <summary>Whether the save also adds a <typeparamref name="T"/> that <paramref name="match"/> picks.</summary>
    public bool Adds<T>(Func<T, bool> match)
        where T : class =>
        db.ChangeTracker.Entries<T>().Any(e => e.State == EntityState.Added && match(e.Entity));

    /// <summary>A hiring template's name, a system template's included, or null for no id.</summary>
    public async ValueTask<string?> TemplateNameAsync(Guid? templateId, CancellationToken cancellationToken)
    {
        if (templateId is not { } id)
        {
            return null;
        }

        if (db.ChangeTracker.Entries<HiringTemplate>().FirstOrDefault(e => e.Entity.Id == id) is { } tracked)
        {
            return tracked.Entity.Name;
        }

        return await db.HiringTemplates.AsNoTracking().Where(t => t.Id == id).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>The definition of the newest version of a template below <paramref name="version"/>, or null for none.</summary>
    public async ValueTask<HiringTemplateDefinition?> PreviousDefinitionAsync(Guid templateId, int version, CancellationToken cancellationToken)
    {
        var tracked = db.ChangeTracker.Entries<HiringTemplateVersion>()
            .Where(e => e.State != EntityState.Added && e.Entity.TemplateId == templateId && e.Entity.Version < version)
            .MaxBy(e => e.Entity.Version);
        if (tracked is not null)
        {
            return tracked.Entity.Definition;
        }

        return await db.HiringTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId && v.Version < version)
            .OrderByDescending(v => v.Version)
            .Select(v => v.Definition)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>ScreenHire's wording of a built-in email template, or null for the organization's own.</summary>
    public (string Subject, string Body)? BuiltInWording(string? builtInId) =>
        builtInId is not null && EmailTemplateIds.All.Contains(builtInId) && emailTemplates.Default(builtInId) is { } wording
            ? (wording.Subject, wording.Body)
            : null;
}
