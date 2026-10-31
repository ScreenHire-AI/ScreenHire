using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The shared half of each area's <c>GET .../{id}/history</c> route (docs/architecture/change-history.md). The area
/// checks that the caller can see the object, and answers 404 for another organization's, before it reads the page.
/// </summary>
internal static class ChangeHistoryRoutes
{
    /// <summary>A problem with the page size, or null when it is fine.</summary>
    public static ValidationProblem? LimitProblem(int? limit) =>
        limit is null or (>= 1 and <= ChangeHistoryQueries.MaxPageSize)
            ? null
            : TypedResults.ValidationProblem(ValidationExtensions.Problem("limit", $"Ask for 1 to {ChangeHistoryQueries.MaxPageSize} changes at a time."));

    /// <summary>
    /// The history route of a page whose subject is the organization itself (its name, its branding, its team, its API
    /// keys): the caller's own organization, so there is no id to check.
    /// </summary>
    public static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> OrganizationPageAsync(
        ChangeSubjectKind kind, Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        return await PageAsync(db, kind, SubjectId(organizationId), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : CursorProblem();
    }

    /// <summary>
    /// When a page whose subject is the organization last changed. With no recorded change it says Created when the
    /// organization was: the team and the API keys began with it.
    /// </summary>
    public static async Task<Results<Ok<LastUpdate>, NotFound>> OrganizationLastUpdateAsync(
        ChangeSubjectKind kind, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        var createdAt = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => (DateTimeOffset?)o.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (createdAt is null)
        {
            return TypedResults.NotFound();
        }

        var last = await ChangeHistoryQueries.LastAsync(db, kind, SubjectId(organizationId), cancellationToken);
        return TypedResults.Ok(LastUpdate.From(last, createdAt.Value));
    }

    /// <summary>The subject id of every history whose subject is the organization itself.</summary>
    public static string SubjectId(Guid organizationId) => organizationId.ToString("D");

    /// <summary>The 400 for a cursor that is not one of the object's changes.</summary>
    public static ValidationProblem CursorProblem() =>
        TypedResults.ValidationProblem(ValidationExtensions.Problem("before", "No change of this object has this id."));

    /// <summary>
    /// One page of the object's changes, newest first, older than the change <paramref name="before"/>, or null when
    /// <paramref name="before"/> is not one of this object's changes. The members its lines name get their current names.
    /// </summary>
    public static async Task<HistoryPage?> PageAsync(
        ScreenHireDbContext db, ChangeSubjectKind kind, string subjectId, Guid? before, int? limit, CancellationToken cancellationToken)
    {
        if (await ChangeHistoryQueries.PageAsync(db, kind, subjectId, before, limit ?? ChangeHistoryQueries.DefaultPageSize, cancellationToken)
            is not var (records, next))
        {
            return null;
        }

        var people = HistoryMappings.People(records);
        var names = people.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => people.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.DisplayName ?? u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Name ?? HistoryMappings.FormerTeammate, cancellationToken);
        return new HistoryPage([.. records.Select(r => r.ToEntry(names))], next);
    }
}
