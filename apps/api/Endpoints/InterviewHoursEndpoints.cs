using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// A member's own interview hours and the subscribe link of ScreenHire's calendar (Your account, Calendar;
/// docs/architecture/interview-scheduling.md, "Personal hours and ScreenHire's calendar"). Each route changes only the
/// caller's own row, so no permission is needed, as for connecting a calendar.
/// </summary>
internal static class InterviewHoursEndpoints
{
    public const string FeedPath = "/v1/public/calendar-feeds";

    public static IEndpointRouteBuilder MapInterviewHoursEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/account/interview-hours").WithTags("Account").RequireAuthorization();
        group.MapGet("", GetInterviewHours).WithName("GetInterviewHours").Produces(StatusCodes.Status401Unauthorized);
        var mutations = group.MapGroup("").RequireAntiforgeryToken();
        mutations.MapPut("", UpdateInterviewHours).WithName("UpdateInterviewHours").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPost("/feed", TurnOnCalendarFeed).WithName("TurnOnCalendarFeed").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/feed", TurnOffCalendarFeed).WithName("TurnOffCalendarFeed").Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<InterviewHoursSettings>, NotFound>> GetInterviewHours(
        ScreenHireDbContext db, ITenantContext tenant, CalendarFeedLinks feeds, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        if (tenant is not { MemberId: { } memberId })
        {
            return TypedResults.NotFound();
        }

        var hours = await db.InterviewHours.AsNoTracking().SingleOrDefaultAsync(h => h.MemberId == memberId, cancellationToken);
        return TypedResults.Ok(await SettingsAsync(db, tenant, hours, feeds, domains.Value, cancellationToken));
    }

    private static async Task<Results<Ok<InterviewHoursSettings>, NotFound, ValidationProblem>> UpdateInterviewHours(
        UpdateInterviewHoursRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        CalendarFeedLinks feeds,
        IOptions<DomainOptions> domains,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (InterviewHours.Problem(request.TimeZone, request.Weekly, request.BufferMinutes, request.MaxPerDay) is { } problem)
        {
            var field = problem.StartsWith("Choose a time zone", StringComparison.Ordinal) ? "timeZone"
                : problem.StartsWith("The time between", StringComparison.Ordinal) ? "bufferMinutes"
                : problem.StartsWith("The most interviews", StringComparison.Ordinal) ? "maxPerDay"
                : "weekly";
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(field, problem));
        }

        if (await ChangeRowAsync(db, tenant, time, h => h.Set(request.TimeZone!, request.Weekly, request.BufferMinutes, request.MaxPerDay, time.GetUtcNow()), cancellationToken) is not { } hours)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await SettingsAsync(db, tenant, hours, feeds, domains.Value, cancellationToken));
    }

    // Turns the subscribe link on, or gives it a new secret (Reset link), which ends every link issued before.
    private static async Task<Results<Ok<InterviewHoursSettings>, NotFound>> TurnOnCalendarFeed(
        ScreenHireDbContext db, ITenantContext tenant, CalendarFeedLinks feeds, IOptions<DomainOptions> domains, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await ChangeRowAsync(db, tenant, time, h => h.SetFeedSecret(CalendarFeedLinks.NewSecret(), time.GetUtcNow()), cancellationToken) is not { } hours)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await SettingsAsync(db, tenant, hours, feeds, domains.Value, cancellationToken));
    }

    private static async Task<Results<Ok<InterviewHoursSettings>, NotFound>> TurnOffCalendarFeed(
        ScreenHireDbContext db, ITenantContext tenant, CalendarFeedLinks feeds, IOptions<DomainOptions> domains, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await ChangeRowAsync(db, tenant, time, h => h.ClearFeed(time.GetUtcNow()), cancellationToken) is not { } hours)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await SettingsAsync(db, tenant, hours, feeds, domains.Value, cancellationToken));
    }

    // Applies the change to the caller's row and saves it. Two first saves at once (two tabs) both make the row and one loses on
    // the unique index; that one reads the row the other made and applies its change to it.
    private static async Task<InterviewHours?> ChangeRowAsync(
        ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, Action<InterviewHours> change, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (await RowAsync(db, tenant, time, cancellationToken) is not { } hours)
            {
                return null;
            }

            change(hours);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return hours;
            }
            catch (DbUpdateException exception) when (attempt == 0 && exception.IsUniqueViolation())
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    // The caller's row here, made on the first save in their own time zone (tracked, so the caller saves it).
    private static async Task<InterviewHours?> RowAsync(ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (tenant is not { OrganizationId: { } organizationId, MemberId: { } memberId, UserId: { } userId })
        {
            return null;
        }

        var hours = await db.InterviewHours.SingleOrDefaultAsync(h => h.MemberId == memberId, cancellationToken);
        if (hours is null)
        {
            var now = time.GetUtcNow();
            hours = InterviewHours.Create(organizationId, memberId, userId, now);
            hours.Set(await DefaultZoneAsync(db, memberId, cancellationToken), null, 0, null, now);
            db.InterviewHours.Add(hours);
        }

        return hours;
    }

    private static async Task<InterviewHoursSettings> SettingsAsync(
        ScreenHireDbContext db, ITenantContext tenant, InterviewHours? hours, CalendarFeedLinks feeds, DomainOptions domains, CancellationToken cancellationToken)
    {
        var zone = hours?.TimeZone ?? await DefaultZoneAsync(db, tenant.MemberId!.Value, cancellationToken);
        var feed = hours?.FeedSecret is { } secret
            ? domains.AppUrl($"{FeedPath}/{feeds.Issue(hours.OrganizationId, hours.MemberId, secret)}.ics").ToString()
            : null;
        return new InterviewHoursSettings(zone, hours?.Weekly, hours?.BufferMinutes ?? 0, hours?.MaxPerDay, feed);
    }

    // The zone the person chose on Your account, else UTC.
    private static async Task<string> DefaultZoneAsync(ScreenHireDbContext db, Guid memberId, CancellationToken cancellationToken)
    {
        var zone = await db.MemberPreferences.AsNoTracking().Where(p => p.MemberId == memberId).Select(p => p.TimeZone).SingleOrDefaultAsync(cancellationToken);
        return InterviewRequest.TryFindZone(zone, out _) ? zone! : "UTC";
    }
}
