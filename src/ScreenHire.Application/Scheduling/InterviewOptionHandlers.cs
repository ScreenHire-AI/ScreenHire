using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Sends one of the interview emails that carry the candidate's link (the invitation in each of its three forms, the
/// options update and the request for more times), in the worker. Idempotent like every interview email: the send's id is
/// the id of the email's timeline entry, so a redelivery that finds it sends nothing. The link is issued here, so it never
/// sits in a message or in the copy the team reads.
/// </summary>
internal static partial class InterviewLinkEmail
{
    /// <summary>
    /// The email <paramref name="templateId"/> about <paramref name="interviewId"/>, when <paramref name="stillMeant"/> holds
    /// for the interview now (null when it does, or why not).
    /// </summary>
    public static async Task<OutgoingMessages> SendAsync(
        string templateId,
        Guid interviewId,
        Guid sendId,
        Guid? actorUserId,
        Func<InterviewRequest, DateTimeOffset, string?> stillMeant,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        CandidateMailRouting routing,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        if (await db.ApplicationEvents.AnyAsync(e => e.Id == sendId, cancellationToken))
        {
            return outgoing; // sent already
        }

        var now = time.GetUtcNow();
        var context = await InterviewContexts.LoadAsync(db, interviewId, cancellationToken);
        var stage = context?.Job.Workflow.FindStage(context.Application.CurrentStageId);
        var reason = context is null ? "the interview or its application is gone"
            : stillMeant(context.Interview, now) is { } why ? why
            : context.Application.HasEnded(stage) ? "the application has ended"
            : !email.IsConfigured ? "this host has no email sender"
            : !links.IsConfigured ? "the careers site address is not set up"
            : null;
        if (reason is not null)
        {
            LogNotSent(logger, templateId, sendId, reason);
            return outgoing;
        }

        var (interview, application, job, candidate, organization) = context!;
        if (await store.FindAsync(db, templateId, cancellationToken) is not { } template)
        {
            LogNotSent(logger, templateId, sendId, "the template is missing");
            return outgoing;
        }

        var culture = CandidateLanguage.Culture(application.Language);
        string Written(DateTimeOffset start) => InterviewText.Time(start, start + interview.Duration, interview.TimeZone, culture);

        // The time the email names: the team's picks for we'll suggest a time, else the booked time a move keeps.
        var times = templateId == EmailTemplateIds.InterviewInvitationProposed || (interview.Option == InterviewOption.OurSuggestion && interview.StartsAt is null)
            ? interview.ProposedTimes.Select(Written).ToList()
            : interview.StartsAt is { } booked ? [Written(booked)] : [];
        var model = ApplicationEmails.ModelFor(candidate, job.Title, organization.Name, stageName: null) with
        {
            MeetingName = interview.MeetingNameOrDefault(),
            BookingLink = links.Url(organization.Slug, links.Issue(interview)),
            InterviewLength = InterviewText.Length(interview.DurationMinutes),
            InterviewTime = times.Count == 0 ? null : string.Join(" or ", times),
            TeamNote = interview.TeamNote,
        };
        var header = await headers.ForAsync(db, organization, cancellationToken);
        // Routed like every email about an application: a reply address, so the candidate's answer lands in Messages.
        var route = await routing.ForAsync(db, application.OrganizationId, sendId, cancellationToken, candidate.Id);
        await email.SendAsync(templateId, template.Content, candidate.Email, model, header, cancellationToken, route);

        // The team's copy says where the candidate's own link was: it lets whoever holds it answer as the candidate.
        var kept = renderer.Render(template.Content, candidate.Email, model with { BookingLink = ApplicationEmails.QuestionnaireLinkNotKept }, templateId, header);
        var copy = application.PrepareEmail(sendId, templateId, kept.To, kept.Subject, kept.TextBody, kept.HtmlBody ?? "", now);
        db.ApplicationEmails.Add(copy);
        db.ApplicationEvents.Add(application.EmailSent(copy, now, actorUserId));
        outgoing.Add(new ApplicationEmailed(application.OrganizationId, application.Id, application.JobId, sendId, now, actorUserId));
        return outgoing;
    }

    /// <summary>The invitation's template for how the candidate picks.</summary>
    public static string InvitationTemplate(InterviewOption option) => option switch
    {
        InterviewOption.TheirTimes => EmailTemplateIds.InterviewInvitationSuggest,
        InterviewOption.OurSuggestion => EmailTemplateIds.InterviewInvitationProposed,
        _ => EmailTemplateIds.InterviewInvitation,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Interview email {TemplateId} {SendId} was not sent: {Reason}")]
    private static partial void LogNotSent(ILogger logger, string templateId, Guid sendId, string reason);
}

/// <summary>Tells the candidate the team changed how they pick a time ("We've updated your options"), on the link they already have.</summary>
public static class SendInterviewOptionsUpdatedHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendInterviewOptionsUpdated.IdempotencyKey))]
    public static Task<OutgoingMessages> Handle(
        SendInterviewOptionsUpdated message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        CandidateMailRouting routing,
        TimeProvider time,
        ILogger<SendInterviewOptionsUpdated> logger,
        CancellationToken cancellationToken) =>
        InterviewLinkEmail.SendAsync(
            EmailTemplateIds.InterviewOptionsUpdated, message.InterviewId, message.SendId, message.ActorUserId,
            (interview, now) => interview.IsPicking(now) ? null : "the candidate is no longer choosing a time",
            db, email, renderer, store, links, headers, routing, time, logger, cancellationToken);
}

/// <summary>Asks the candidate for more times when none of theirs works, with the team's message.</summary>
public static class SendInterviewMoreTimesHandler
{
    [Deduplicated(ValueSource.InputMember, nameof(SendInterviewMoreTimes.IdempotencyKey))]
    public static Task<OutgoingMessages> Handle(
        SendInterviewMoreTimes message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        EmailTemplates renderer,
        EmailTemplateStore store,
        InterviewLinks links,
        Branding.EmailHeaders headers,
        CandidateMailRouting routing,
        TimeProvider time,
        ILogger<SendInterviewMoreTimes> logger,
        CancellationToken cancellationToken) =>
        InterviewLinkEmail.SendAsync(
            EmailTemplateIds.InterviewMoreTimes, message.InterviewId, message.SendId, message.ActorUserId,
            (interview, now) => interview.Option == InterviewOption.TheirTimes && interview.IsPicking(now) && interview.CandidateTimes.Count == 0
                ? null
                : "the candidate's times are no longer what the team asked about",
            db, email, renderer, store, links, headers, routing, time, logger, cancellationToken);
}
