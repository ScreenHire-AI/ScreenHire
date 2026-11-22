using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The "Choose how the candidate schedules" task (<see cref="ApplicationTaskKind.ChooseScheduling"/>) is done where its work
/// is done: when anyone offers the application an interview (ADR-0017 decision 11), never by ticking it.
/// </summary>
public static class SchedulingTasks
{
    /// <summary>The task's words: "Choose how Grace schedules an interview."</summary>
    public static string Note(string candidateFirstName, string meetingName) =>
        $"Choose how {InterviewText.Plain(candidateFirstName)} schedules a {InterviewText.Plain(meetingName)}.";

    /// <summary>
    /// Marks the application's open choose-scheduling tasks done by <paramref name="userId"/> (null for an automation) and
    /// returns the events that say so, for the caller to publish with the offer.
    /// </summary>
    public static async Task<IReadOnlyList<ApplicationTaskCompleted>> CompleteAsync(
        ScreenHireDbContext db, JobApplication application, Guid? userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(application);
        var open = await db.ApplicationTasks
            .Where(t => t.ApplicationId == application.Id && t.Kind == ApplicationTaskKind.ChooseScheduling && t.CompletedAt == null)
            .ToListAsync(cancellationToken);
        return [.. open.Where(t => t.Complete(userId, now)).Select(t => new ApplicationTaskCompleted(
            application.OrganizationId, t.Id, application.Id, application.JobId, now, userId, t.AssigneeUserId))];
    }
}
