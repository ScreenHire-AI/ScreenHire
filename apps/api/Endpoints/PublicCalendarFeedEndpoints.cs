using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The subscribe link of ScreenHire's calendar (docs/architecture/interview-scheduling.md, "The subscribe link"): a member's
/// booked interviews as an iCalendar feed, which a phone's calendar app reads on its own. Anonymous, because a calendar app
/// cannot sign in; the token in the path is the secret (<see cref="CalendarFeedLinks"/>), and a forged token, another
/// organization's, one reset since or one turned off is a 404. The feed names the meeting, the job and a link to the
/// application, never the candidate, so a link that leaks shows no one's name or address. Outside the API's description:
/// it is not JSON, and only calendar apps read it.
/// </summary>
internal static class PublicCalendarFeedEndpoints
{
    public static IEndpointRouteBuilder MapPublicCalendarFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{InterviewHoursEndpoints.FeedPath}/{{token}}.ics", GetCalendarFeed)
            .WithName("GetCalendarFeed")
            .AllowAnonymous()
            .RequireRateLimiting(ApplyOptions.RateLimitPolicy)
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<Results<ContentHttpResult, NotFound>> GetCalendarFeed(
        string token,
        HttpResponse response,
        ScreenHireDbContext db,
        TenantContext tenant,
        CalendarFeedLinks feeds,
        IOptions<ScreenHire.Application.Applications.DashboardLinkOptions> dashboard,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (feeds.Read(token) is not { } link)
        {
            return TypedResults.NotFound();
        }

        // The token names the organization, so it is the tenant, as a careers page's slug is.
        tenant.SetPublicOrganization(link.OrganizationId);
        var hours = await db.InterviewHours.AsNoTracking().SingleOrDefaultAsync(h => h.MemberId == link.MemberId, cancellationToken);
        if (hours is null || !CalendarFeedLinks.Matches(hours.FeedSecret, link.Secret))
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        var since = now.AddDays(-30);
        var until = now.AddDays(180);
        var rows = await (
            from attendee in db.InterviewAttendees.AsNoTracking()
            where attendee.UserId == hours.UserId
            join interview in db.InterviewRequests.AsNoTracking() on attendee.InterviewId equals interview.Id
            where interview.Status == InterviewStatus.Booked && interview.StartsAt >= since && interview.StartsAt <= until
            join job in db.Jobs.AsNoTracking() on interview.JobId equals job.Id
            orderby interview.StartsAt
            select new { interview.Id, interview.ApplicationId, Start = interview.StartsAt!.Value, End = interview.EndsAt!.Value, interview.Revision, interview.MeetingName, interview.MeetingUrl, job.Title })
            .ToListAsync(cancellationToken);
        var events = rows.Select(r => new IcsEvent(
            InterviewInvites.Uid(r.Id),
            r.Revision,
            r.Start,
            r.End,
            $"{InterviewText.Sentence(r.MeetingName ?? Domain.Hiring.WorkflowStage.DefaultMeetingName)}: {InterviewText.Plain(r.Title)}",
            dashboard.Value.Url($"/applications/{r.ApplicationId}") is { } application ? $"Open the application: {application}" : null,
            r.MeetingUrl,
            now)).ToList();

        // A calendar app polls the feed: it must always ask again, and nobody else should keep a copy.
        response.Headers.CacheControl = "private, no-store";
        response.Headers["X-Robots-Tag"] = "noindex";
        return TypedResults.Text(IcsWriter.Feed("Interviews", events), "text/calendar; charset=utf-8", Encoding.UTF8);
    }
}
