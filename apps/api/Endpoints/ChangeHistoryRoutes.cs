using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;

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

    /// <summary>One page of the object's changes, newest first, older than the cursor <paramref name="before"/>.</summary>
    public static async Task<HistoryPage> PageAsync(
        ScreenHireDbContext db, ChangeSubjectKind kind, string subjectId, long? before, int? limit, CancellationToken cancellationToken)
    {
        var (records, next) = await ChangeHistoryQueries.PageAsync(
            db, kind, subjectId, before, limit ?? ChangeHistoryQueries.DefaultPageSize, cancellationToken);
        return new HistoryPage([.. records.Select(r => r.ToEntry())], next);
    }
}
