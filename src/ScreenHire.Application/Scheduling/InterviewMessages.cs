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

/// <summary>Removes each interviewer's event after a cancellation. <see cref="Revision"/> is the request's revision at the cancellation.</summary>
public sealed record CancelInterviewEvents(Guid OrganizationId, Guid ApplicationId, Guid InterviewId, int Revision) : ITenantMessage
{
    public string IdempotencyKey => $"interview-events:cancel:{InterviewId}:{Revision}";
}
