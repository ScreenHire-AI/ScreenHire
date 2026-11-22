using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Branding;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Applications;

/// <summary>
/// Sending a follow-up questionnaire (workflow-engine.md section 8): one request per application and form, which the first
/// send creates and a later send refreshes while it is unanswered. A <c>SendQuestionnaire</c> action and a person's
/// "Send a form" both go through <see cref="PrepareAsync"/>, so the two kinds of send keep one request and one count.
/// </summary>
public static class QuestionnaireSending
{
    /// <summary>
    /// The request for <paramref name="followUp"/>, created on the first send or sent again (a fresh expiry, counted), and
    /// tracked by <paramref name="db"/>. Null when the candidate answered it already, and then nothing changed.
    /// </summary>
    public static async Task<QuestionnaireRequest?> PrepareAsync(
        ScreenHireDbContext db, JobApplication application, FollowUpQuestionnaire followUp, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(followUp);
        var request = await db.QuestionnaireRequests
            .SingleOrDefaultAsync(r => r.ApplicationId == application.Id && r.QuestionnaireId == followUp.Id, cancellationToken);
        if (request is { IsCompleted: true })
        {
            return null;
        }

        if (request is null)
        {
            request = QuestionnaireRequest.Create(application, followUp, now);
            db.QuestionnaireRequests.Add(request);
        }
        else
        {
            request.Resend(followUp, now);
        }

        return request;
    }
}

/// <summary>
/// A person sent a follow-up questionnaire by hand from the candidate's page. The API made or refreshed the request and put
/// the send on the timeline in the same transaction; the worker issues the signed link and emails it, so the link never sits
/// in a message. <see cref="SendId"/> becomes the email's timeline entry, so a redelivered message sends nothing twice.
/// </summary>
public sealed record SendQuestionnaireLink(Guid OrganizationId, Guid ApplicationId, Guid RequestId, Guid SendId, Guid? ActorUserId) : ITenantMessage
{
    /// <summary>One intent per send: the same send delivered twice runs once, a later send of the same form is new.</summary>
    public string IdempotencyKey => $"questionnaire-link:{RequestId}:{SendId}";
}

/// <summary>
/// Emails the link of a questionnaire sent by hand, with the follow-up questions email, as the person who sent it. A
/// request answered or gone by now, or a host that cannot send, sends nothing, with a warning.
/// </summary>
public static partial class SendQuestionnaireLinkHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendQuestionnaireLink.IdempotencyKey))]
    public static async Task<OutgoingMessages> Handle(
        SendQuestionnaireLink message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        EmailHeaders headers,
        CandidateMailRouting routing,
        QuestionnaireLinks links,
        TimeProvider time,
        ILogger<SendQuestionnaireLink> logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == message.SendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var request = await db.QuestionnaireRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == message.RequestId, cancellationToken);
        var application = request is null
            ? null
            : await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken);
        if (request is null || application is null)
        {
            LogNotSent(logger, message.SendId, "the request or its application is gone");
            return outgoing;
        }

        if (request.IsCompleted || !links.IsConfigured)
        {
            LogNotSent(logger, message.SendId, request.IsCompleted ? "the candidate answered it already" : "the careers site address is not set up");
            return outgoing;
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var slug = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == application.OrganizationId).Select(o => o.Slug).SingleAsync(cancellationToken);
        var now = time.GetUtcNow();
        var result = await ApplicationEmails.SendAsync(
            db, email, renderer, store, headers, routing, application, job, EmailTemplateIds.QuestionnaireRequest, reason: null,
            links.Url(slug, links.Issue(request)), message.SendId, message.ActorUserId, automationRunId: null, now, cancellationToken);
        if (result.Event is null)
        {
            LogNotSent(logger, message.SendId, result.Detail);
            return outgoing;
        }

        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, message.SendId, now, message.ActorUserId));
        return outgoing;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Questionnaire link {SendId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid sendId, string reason);
}
