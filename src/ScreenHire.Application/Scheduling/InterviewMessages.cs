using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Emails the candidate the link to pick an interview time, queued through the outbox when the interview is offered
/// (docs/architecture/interview-scheduling.md). Ids only: the worker issues the link (<see cref="InterviewLinks"/>) when it
/// renders the email, so the link never sits in a message or in the copy the team reads. <see cref="SendId"/> becomes the
/// email's timeline entry, so a redelivered message sends nothing twice. <see cref="ActorUserId"/> is the person who
/// offered the interview, or null when a workflow action did.
/// </summary>
public sealed record SendInterviewInvitation(Guid OrganizationId, Guid ApplicationId, Guid InterviewId, Guid SendId, Guid? ActorUserId) : ITenantMessage
{
    /// <summary>One intent per send: the same send delivered twice runs once, a later send of the same interview is new.</summary>
    public string IdempotencyKey => $"interview-invitation:{InterviewId}:{SendId}";
}

/// <summary>
/// Puts the booked interview on each interviewer's calendar. <see cref="Revision"/> is the request's revision at the
/// booking, so the key and the provider's event ids are stable across redelivery.
/// </summary>
public sealed record CreateInterviewEvents(Guid OrganizationId, Guid ApplicationId, Guid InterviewId, int Revision) : ITenantMessage
{
    public string IdempotencyKey => $"interview-events:create:{InterviewId}:{Revision}";
}

/// <summary>Moves each interviewer's event to the interview's new time. <see cref="Revision"/> is the request's revision at the move.</summary>
public sealed record UpdateInterviewEvents(Guid OrganizationId, Guid ApplicationId, Guid InterviewId, int Revision) : ITenantMessage
{
    public string IdempotencyKey => $"interview-events:update:{InterviewId}:{Revision}";
}

/// <summary>
/// Removes each interviewer's event after a cancellation. <see cref="Revision"/> is the request's revision at the
/// cancellation. <see cref="TellCandidate"/> is false when the application ended, which has its own message to the
/// candidate (or none), so a rejection is not followed by a notice that an interview was cancelled.
/// </summary>
public sealed record CancelInterviewEvents(Guid OrganizationId, Guid ApplicationId, Guid InterviewId, int Revision, bool TellCandidate = true) : ITenantMessage
{
    public string IdempotencyKey => $"interview-events:cancel:{InterviewId}:{Revision}";
}

/// <summary>
/// Cancels the Open or Booked interview of an application that has ended (rejected, withdrawn, hired or saved for later).
/// <see cref="CauseId"/> is the timeline entry of the change that ended it. The handler checks the application has ended
/// when it runs, so a move back in between cancels nothing.
/// </summary>
public sealed record EndApplicationInterviews(Guid OrganizationId, Guid ApplicationId, Guid CauseId) : ITenantMessage
{
    public string IdempotencyKey => $"interview-end:{ApplicationId}:{CauseId}";
}

/// <summary>
/// An interviewer left the organization: takes them off the interviews still to come (<see cref="CauseId"/> is the
/// membership that was removed). Their connection is already gone, so this never calls their calendar.
/// </summary>
public sealed record RemoveInterviewerFromInterviews(Guid OrganizationId, Guid UserId, Guid CauseId) : ITenantMessage
{
    public string IdempotencyKey => $"interview-interviewer-left:{CauseId}";
}

/// <summary>One event to delete from an interviewer's calendar: where it is, and what to look for if its id was never saved.</summary>
public sealed record InterviewEventToRemove(Guid InterviewId, Guid UserId, string? ProviderEventId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);

/// <summary>
/// Deletes events from interviewers' calendars without telling their guests, for a candidate who was erased: an event
/// holds the candidate's name and address, and the rows that said where it is are gone with the candidate. Ids and times
/// only, never a name or an address. <see cref="CauseId"/> is the erased candidate.
/// </summary>
public sealed record RemoveInterviewEvents(Guid OrganizationId, Guid CauseId, IReadOnlyList<InterviewEventToRemove> Events) : ITenantMessage
{
    public string IdempotencyKey => $"interview-events:remove:{CauseId}";
}
