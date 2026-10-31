using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in member's own notification settings: which teammates' changes their dashboard shows as toasts
/// (docs/architecture/realtime.md). Stored on their membership, so each organization keeps its own. Session only: an API
/// key is no member and has no settings. No permission, because the route changes nothing but the caller's own
/// membership, as switching organization needs none. Not in the change history: a personal preference, not
/// configuration the team shares.
/// </summary>
internal static class NotificationSettingsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/notification-settings").WithTags("Notifications").RequireAuthorization();
        group.MapGet("", GetNotificationSettings).WithName("GetNotificationSettings").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("", UpdateNotificationSettings).WithName("UpdateNotificationSettings")
            .RequireAntiforgeryToken()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<MemberNotificationSettings>, NotFound>> GetNotificationSettings(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var settings = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.Id == tenant.MemberId)
            .Select(m => m.Notifications)
            .SingleOrDefaultAsync(cancellationToken);
        return settings is null ? TypedResults.NotFound() : TypedResults.Ok(MemberNotificationSettings.From(settings));
    }

    private static async Task<Results<Ok<MemberNotificationSettings>, NotFound, ValidationProblem>> UpdateNotificationSettings(
        UpdateNotificationSettingsRequest request, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        // A PUT replaces the settings, so a missing list is bad input rather than "nothing off".
        if (request.Off is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("off", "List the kinds of change that are off, or none."));
        }

        if (request.Off.Distinct().Count() != request.Off.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("off", "Name each kind of change once."));
        }

        var member = await db.OrganizationMembers.SingleOrDefaultAsync(m => m.Id == tenant.MemberId, cancellationToken);
        if (member is null)
        {
            return TypedResults.NotFound();
        }

        if (member.SetNotifications(new NotificationSettings { Toasts = request.Toasts, CloseToasts = request.CloseToasts, Off = request.Off }))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(MemberNotificationSettings.From(member.Notifications));
    }
}
