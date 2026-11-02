using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Views;

/// <summary>Which table a view filters: the Candidates page, or one job's candidates.</summary>
public enum ViewScope
{
    Candidates,
    Job,
}

/// <summary>
/// A named filter and sort that someone saved on a candidate table (docs/architecture/saved-views.md). Private to its
/// owner until <see cref="IsShared"/>; a shared view shows for everyone who can see its jobs: the job it filters, or every
/// job it picks on the Candidates page (ADR-0015, <see cref="ViewPolicy.MaySee"/>). Only the owner, or a role with Edit
/// anyone's views, renames, shares or deletes it (<see cref="ViewPolicy"/>). Deleting is soft so Undo can restore the
/// row for <see cref="RestoreWindow"/>; the API purges rows that stayed deleted.
/// </summary>
public sealed class SavedFilterView : ITenantOwned
{
    public const int MaxNameLength = 80;

    /// <summary>How long Undo can restore a deleted view. Longer than the offer on screen, which waits while it is held.</summary>
    public static readonly TimeSpan RestoreWindow = TimeSpan.FromMinutes(1);

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The user who saved it. A user id, because a membership id changes when someone leaves and rejoins.</summary>
    public Guid OwnerUserId { get; init; }

    public string Name { get; private set; } = "";

    public ViewScope Scope { get; init; }

    /// <summary>The job whose table the view filters; set exactly when <see cref="Scope"/> is Job.</summary>
    public Guid? JobId { get; init; }

    public bool IsShared { get; private set; }

    public ViewDefinition Definition { get; private set; } = new();

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When it was deleted, or null. A deleted view is hidden everywhere and can be restored until it is purged.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public static SavedFilterView Create(
        Guid organizationId, Guid ownerUserId, string name, ViewScope scope, Guid? jobId, bool shared, ViewDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if ((scope == ViewScope.Job) != jobId.HasValue)
        {
            throw new ArgumentException("A job view needs its job, and a candidates view takes none.", nameof(jobId));
        }

        return new SavedFilterView
        {
            OrganizationId = organizationId,
            OwnerUserId = ownerUserId,
            Name = name,
            Scope = scope,
            JobId = jobId,
            IsShared = shared,
            Definition = definition,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Renames it. Returns whether the name changed.</summary>
    public bool Rename(string name, DateTimeOffset now)
    {
        if (Name == name)
        {
            return false;
        }

        Name = name;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Shares it with the team, or makes it private. Returns whether it changed.</summary>
    public bool SetShared(bool shared, DateTimeOffset now)
    {
        if (IsShared == shared)
        {
            return false;
        }

        IsShared = shared;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Deletes it, or does nothing when it is already deleted (a second request keeps the first time).</summary>
    public void Delete(DateTimeOffset now) => DeletedAt ??= now;

    /// <summary>Whether it was deleted too long ago for Undo.</summary>
    public bool IsPastUndo(DateTimeOffset now) => DeletedAt is { } deletedAt && now - deletedAt > RestoreWindow;

    /// <summary>Brings a deleted view back. Returns whether it changed.</summary>
    public bool Restore(DateTimeOffset now)
    {
        if (DeletedAt is null)
        {
            return false;
        }

        DeletedAt = null;
        UpdatedAt = now;
        return true;
    }
}

/// <summary>
/// What a view filters and how it sorts, as the table holds it. Search text is not part of a view: it is a lookup, not a
/// filter. Stages are names on the Candidates page (they match across jobs) and stage ids on a job's table. Stored as
/// JSON and sent over the API as is.
/// </summary>
public sealed record ViewDefinition
{
    /// <summary>The built-in view this one narrows (<see cref="BuiltInViews"/>), or null.</summary>
    public string? BaseView { get; init; }

    public IReadOnlyList<Guid> JobIds { get; init; } = [];

    public IReadOnlyList<string> Stages { get; init; } = [];

    /// <summary>The authenticity level to show: a <c>AuthenticityLevel</c> name, or <c>Pending</c> for checks not yet run.</summary>
    public string? Authenticity { get; init; }

    /// <summary>The AI assistance level to show, an <c>AiAssistanceLevel</c> name.</summary>
    public string? AiAssistance { get; init; }

    public string? Tag { get; init; }

    /// <summary>The table's sorted column, or null for the table's own default.</summary>
    public string? SortColumn { get; init; }

    public bool SortDescending { get; init; }

    public bool Equals(ViewDefinition? other) =>
        other is not null && BaseView == other.BaseView && JobIds.SequenceEqual(other.JobIds) && Stages.SequenceEqual(other.Stages)
        && Authenticity == other.Authenticity && AiAssistance == other.AiAssistance && Tag == other.Tag
        && SortColumn == other.SortColumn && SortDescending == other.SortDescending;

    public override int GetHashCode() => HashCode.Combine(BaseView, JobIds.Count, Stages.Count, Authenticity, AiAssistance, Tag, SortColumn);
}

/// <summary>
/// The ids of the dashboard's built-in views (<c>saved-views.ts</c>). They are code, so the server only knows their ids:
/// a view can narrow one (<see cref="ViewDefinition.BaseView"/>) and a person can hide one.
/// </summary>
public static class BuiltInViews
{
    public static IReadOnlyList<string> Ids { get; } =
        ["needs-review", "verification-recommended", "overdue", "new-today", "not-screened", "interviewing"];

    public static bool Exists(string id) => Ids.Contains(id);
}

/// <summary>Who may change a view: its owner, or a role with Edit anyone's views (Owners and Admins among the built-in roles).</summary>
public static class ViewPolicy
{
    public static bool MayChange(SavedFilterView view, Guid? userId, IReadOnlySet<Permission>? permissions)
    {
        ArgumentNullException.ThrowIfNull(view);
        return userId is { } user && (view.OwnerUserId == user || permissions?.Contains(Permission.EditAnyView) == true);
    }

    /// <summary>
    /// Whether the person sees the view at all: their own, or a shared one whose picked jobs they all see. One job out of
    /// reach hides it, because the view carries the job ids (ADR-0015). <paramref name="seenJobs"/> holds the jobs they see,
    /// at least among those the view picks. A view of one job is checked by its job, through the job scope.
    /// </summary>
    public static bool MaySee(SavedFilterView view, Guid? userId, IReadOnlySet<Guid> seenJobs)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(seenJobs);
        return (userId is { } user && view.OwnerUserId == user) || (view.IsShared && view.Definition.JobIds.All(seenJobs.Contains));
    }
}
