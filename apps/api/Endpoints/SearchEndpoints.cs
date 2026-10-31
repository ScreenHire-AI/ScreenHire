using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Content;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The dashboard's search (Ctrl K): candidates by name or email and jobs by title, on the server, so it finds what the
/// reader's lists have not loaded. Session only: it is not part of the hiring API, and an API key's assistant searches
/// through the MCP tools. The tenant and job-scope filters and row-level security decide what it finds, so a member on
/// Only jobs they're on finds nothing on another team's job (ADR-0015).
/// </summary>
internal static class SearchEndpoints
{
    public const int MinLength = 2;

    // As the MCP tools' search (HiringTools).
    public const int MaxLength = 100;

    public const int CandidateLimit = 8;

    public const int JobLimit = 5;

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/search").WithTags("Search").RequireAuthorization();
        group.MapGet("/", Search).WithName("Search").Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<SearchResults>, ValidationProblem>> Search(
        string? q, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        // One query parameter, so it is checked here rather than through a validator of its own.
        var text = q?.Trim() ?? "";
        if (text.Length is < MinLength or > MaxLength)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "q", $"Search for {MinLength} to {MaxLength} characters."));
        }

        var contains = Like.Contains(text)!;
        var prefix = Like.StartsWith(text)!;

        // From the applications, so a candidate shows only through a job the reader sees; a candidate row on its own keeps
        // tenant-only row-level security (sign-in-and-tenancy.md).
        var applications = await db.Applications.AsNoTracking()
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new { x.Application, x.Job, Candidate = c })
            .Where(r => EF.Functions.ILike(r.Candidate.FullName, contains, Like.Escape)
                || EF.Functions.ILike(r.Candidate.Email, contains, Like.Escape))
            .OrderByDescending(r => EF.Functions.ILike(r.Candidate.FullName, prefix, Like.Escape))
            .ThenByDescending(r => r.Application.SubmittedAt)
            .ThenBy(r => r.Application.Id)
            .Take(CandidateLimit)
            .ToListAsync(cancellationToken);

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => EF.Functions.ILike(j.Title, contains, Like.Escape))
            .OrderByDescending(j => EF.Functions.ILike(j.Title, prefix, Like.Escape))
            .ThenByDescending(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Take(JobLimit)
            .Select(j => new JobSearchHit(j.Id, j.Title, j.Status))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new SearchResults(
            [.. applications.Select(r => new CandidateSearchHit(
                r.Application.Id, r.Candidate.Id, r.Candidate.FullName, r.Candidate.Email, r.Job.Id, r.Job.Title,
                r.Application.StageIn(r.Job)))],
            jobs));
    }
}
