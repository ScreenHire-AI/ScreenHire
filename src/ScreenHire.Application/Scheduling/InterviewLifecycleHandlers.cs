using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Cancels the Open or Booked interview of an application that has ended, so the row, the dashboard, the careers page and
/// the calendars agree (docs/architecture/interview-scheduling.md). Runs in the worker after the change that ended it
/// (a move to Hired, Rejected or Withdrawn, from the move route, an automation or the MCP server, or Keep in mind), and
/// checks the application has ended when it runs. The candidate gets no <c>interview-cancelled</c> email: a rejection, or
/// no message at all, is the team's word on the application, and a notice about the interview beside it would only add
/// noise. The calendar provider still tells the candidate when it removes a Booked interview's organizer event.
/// </summary>
public static partial class EndApplicationInterviewsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(EndApplicationInterviews.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        EndApplicationInterviews message, ScreenHireDbContext db, TimeProvider time, ILogger<EndApplicationInterviews> logger, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var live = await db.InterviewRequests.AsNoTracking()
            .Where(r => r.ApplicationId == message.ApplicationId && (r.Status == InterviewStatus.Open || r.Status == InterviewStatus.Booked))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        foreach (var interviewId in live)
        {
            var context = await InterviewContexts.LockAndLoadAsync(db, message.OrganizationId, interviewId, cancellationToken);
            if (context is null)
            {
                continue;
            }

            var (interview, application, _, _, _) = context;
            var stage = context.Job.Workflow.FindStage(application.CurrentStageId);
            if (!application.HasEnded(stage) || interview.Status is not (InterviewStatus.Open or InterviewStatus.Booked))
            {
                continue; // moved back, or another change got here first
            }

            var now = time.GetUtcNow();
            var wasBooked = interview.Status == InterviewStatus.Booked;
            interview.Cancel(now, byCandidate: false);
            db.ApplicationEvents.Add(application.InterviewCancelled(interview, now, actorUserId: null));
            if (wasBooked)
            {
                var attendees = await db.InterviewAttendees.Where(a => a.InterviewId == interviewId).ToListAsync(cancellationToken);
                attendees.ForEach(a => a.MarkPending());
                outgoing.Add(new CancelInterviewEvents(application.OrganizationId, application.Id, interviewId, interview.Revision, TellCandidate: false));
            }

            outgoing.Add(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interviewId, interview.Status.ToString()));
            LogEnded(logger, interviewId, message.ApplicationId);
        }

        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Interview {InterviewId} cancelled because application {ApplicationId} ended")]
    private static partial void LogEnded(ILogger logger, Guid interviewId, Guid applicationId);
}

/// <summary>
/// Takes a member who left off the interviews still to come. An Open interview drops them, and the next interviewer
/// becomes the organizer if they were; one with nobody left is cancelled. A Booked interview drops them too, but if they
/// were the organizer it is cancelled and the candidate told: the candidate's calendar invitation sits on their event, which
/// ScreenHire can no longer reach, so it could not be moved later. Their connection is gone already, so this makes no
/// calendar call for them.
/// </summary>
public static partial class RemoveInterviewerFromInterviewsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(RemoveInterviewerFromInterviews.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        RemoveInterviewerFromInterviews message, ScreenHireDbContext db, TimeProvider time, ILogger<RemoveInterviewerFromInterviews> logger, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var live = await db.InterviewAttendees.AsNoTracking()
            .Where(a => a.UserId == message.UserId)
            .Join(db.InterviewRequests, a => new { a.OrganizationId, a.InterviewId }, r => new { r.OrganizationId, InterviewId = r.Id }, (a, r) => new { r.Id, r.Status })
            .Where(x => x.Status == InterviewStatus.Open || x.Status == InterviewStatus.Booked)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        // All the locks in one sorted pass, not interview by interview: a booking takes its locks in sorted order too, so
        // this transaction cannot hold one of them while waiting for another in a circle with it.
        var interviewers = await db.InterviewAttendees.AsNoTracking().Where(a => live.Contains(a.InterviewId)).Select(a => a.UserId).ToListAsync(cancellationToken);
        await InterviewLocks.TakeAsync(db, message.OrganizationId, interviewers, cancellationToken);
        foreach (var interviewId in live)
        {
            var context = await InterviewContexts.LoadAsync(db, interviewId, cancellationToken);
            if (context is null || context.Interview.Status is not (InterviewStatus.Open or InterviewStatus.Booked))
            {
                continue;
            }

            var (interview, application, _, _, _) = context;
            var attendees = await db.InterviewAttendees.Where(a => a.InterviewId == interviewId)
                .OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.Id).ToListAsync(cancellationToken);
            if (attendees.SingleOrDefault(a => a.UserId == message.UserId) is not { } leaving)
            {
                continue;
            }

            var remaining = attendees.Where(a => a.Id != leaving.Id).ToList();
            db.InterviewAttendees.Remove(leaving);
            var booked = interview.Status == InterviewStatus.Booked;
            var cancel = remaining.Count == 0 || (booked && leaving.IsOrganizer);
            if (cancel)
            {
                var now = time.GetUtcNow();
                interview.Cancel(now, byCandidate: false);
                db.ApplicationEvents.Add(application.InterviewCancelled(interview, now, actorUserId: null));
                if (booked)
                {
                    remaining.ForEach(a => a.MarkPending());
                    outgoing.Add(new CancelInterviewEvents(application.OrganizationId, application.Id, interviewId, interview.Revision));
                }
            }
            else if (leaving.IsOrganizer)
            {
                remaining[0].MakeOrganizer(); // Open: the booking has not made anyone's event yet
            }

            outgoing.Add(new InterviewChanged(application.OrganizationId, application.Id, application.JobId, interviewId, interview.Status.ToString()));
            LogLeft(logger, interviewId, cancel);
        }

        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "An interviewer left interview {InterviewId}; cancelled: {Cancelled}")]
    private static partial void LogLeft(ILogger logger, Guid interviewId, bool cancelled);
}

/// <summary>
/// Deletes interviewers' events for an erased candidate, without telling the guests. It carries only ids and times, and
/// looks an event up by the id its create used when no id was saved. A calendar that is only down throws, so Wolverine
/// retries; an interviewer with no usable calendar, or an event the provider refuses for good, is logged by id and left
/// (the event is on a calendar ScreenHire can no longer reach).
/// </summary>
public static partial class RemoveInterviewEventsHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(RemoveInterviewEvents.IdempotencyKey))]
    public static async Task Handle(
        RemoveInterviewEvents message, CalendarAccess calendar, ILogger<RemoveInterviewEvents> logger, CancellationToken cancellationToken)
    {
        foreach (var user in message.Events.GroupBy(e => e.UserId))
        {
            if (await calendar.GetSessionAsync(user.Key, cancellationToken) is not { } session)
            {
                LogUnreachable(logger, user.Key, user.Count());
                continue;
            }

            foreach (var remove in user)
            {
                try
                {
                    var eventId = remove.ProviderEventId
                        ?? await session.Provider.FindEventIdAsync(
                            session.AccessToken, InterviewText.EventIdempotencyId(remove.InterviewId, remove.UserId), remove.WindowStart, remove.WindowEnd, cancellationToken);
                    if (eventId is not null)
                    {
                        await session.Provider.RemoveEventAsync(session.AccessToken, eventId, cancellationToken);
                    }
                }
                catch (CalendarApiException e) when (e.Permanent)
                {
                    LogRefused(logger, remove.InterviewId, remove.UserId, e.Code);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} interview events of member {UserId} were not removed: their calendar is not connected")]
    private static partial void LogUnreachable(ILogger logger, Guid userId, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The calendar refused to remove the event of interview {InterviewId} for member {UserId}: {Code}")]
    private static partial void LogRefused(ILogger logger, Guid interviewId, Guid userId, string code);
}
