using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Activity;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Notifications;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The workspace's Activity feed, the bell's second tab (docs/architecture/notifications.md, "Activity"). Session only:
/// My jobs and the last time the person looked are a person's, and an API key is no person. No permission, because every
/// role may read what it can see, and the one write changes nothing but the caller's own preferences row, as the routes
/// under <c>/v1/account</c> do.
/// </summary>
internal static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/activity").WithTags("Activity").RequireAuthorization();
        group.MapGet("", GetActivity).WithName("GetActivity").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/seen", MarkActivitySeen).WithName("MarkActivitySeen")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Newest first. scope: My jobs or Everything I can see, the person's own choice from Your account when not given.
    // kinds: any of them, every kind when none. before: the nextBefore of the page before.
    private static async Task<Results<Ok<ActivityPage>, ValidationProblem, ProblemHttpResult>> GetActivity(
        ScreenHireDbContext db,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        ActivityScope? scope = null,
        ActivityKind[]? kinds = null,
        string? before = null,
        int? limit = null)
    {
        if (tenant is not { UserId: not null, MemberId: { } memberId })
        {
            return SignIn();
        }

        var problems = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (limit is { } size && size is < 1 or > ActivityFeed.MaxPageSize)
        {
            problems["limit"] = [$"A page holds 1 to {ActivityFeed.MaxPageSize} entries."];
        }

        VerificationActivity.Cursor? cursor = null;
        if (before is not null)
        {
            if (VerificationActivity.Cursor.TryParse(before, out var parsed))
            {
                cursor = parsed;
            }
            else
            {
                problems["before"] = ["Pass the nextBefore a page gave you."];
            }
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var preferences = await db.MemberPreferences.AsNoTracking()
            .Where(p => p.MemberId == memberId)
            .Select(p => new { p.Scope, p.ActivitySeenAt })
            .SingleOrDefaultAsync(cancellationToken);
        var within = scope ?? (preferences?.Scope == NoticeScope.Everything ? ActivityScope.Everything : ActivityScope.MyJobs);
        var page = await ActivityFeed.PageAsync(
            db, tenant, within, kinds?.Distinct().ToList() ?? [], cursor, limit ?? ActivityFeed.DefaultPageSize, preferences?.ActivitySeenAt,
            cancellationToken);
        return TypedResults.Ok(page);
    }

    // The server's clock, never the browser's, and never backwards: a second tab that opens the feed later keeps the later time.
    private static async Task<Results<NoContent, ProblemHttpResult>> MarkActivitySeen(
        ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await AccountEndpoints.PreferencesAsync(db, tenant, time, cancellationToken) is not { } preferences)
        {
            return SignIn();
        }

        preferences.MarkActivitySeen(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult SignIn() => TypedResults.Problem(title: "Sign in to see activity", statusCode: StatusCodes.Status401Unauthorized);
}
