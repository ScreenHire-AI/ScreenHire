using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Applications;

/// <summary>A note's author changed its text. Ids only: open dashboards refetch the timeline, which holds the text.</summary>
public sealed record ApplicationNoteEdited(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid NoteId, DateTimeOffset EditedAt) : ITenantEvent;

/// <summary>A note was deleted, and its text with it. Open dashboards refetch the timeline, where the note is gone.</summary>
public sealed record ApplicationNoteDeleted(Guid OrganizationId, Guid ApplicationId, Guid JobId, Guid NoteId, DateTimeOffset DeletedAt) : ITenantEvent;

/// <summary>
/// A note mentioned a teammate who was not mentioned in it before: on adding the note, or by an edit. Never raised for
/// the note's own author. The dashboard of <see cref="MentionedUserId"/> shows a notification card, and the worker
/// emails them (<see cref="TeammateMentionedHandler"/>). Ids only: every member of the organization receives it.
/// </summary>
/// <remarks>
/// <c>mentionId</c>: The mention row, which is the email's state. <c>authorUserId</c>: Who wrote the note; null when an
/// API key did.
/// </remarks>
public sealed record TeammateMentioned(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid JobId,
    Guid NoteId,
    Guid MentionId,
    Guid MentionedUserId,
    Guid? AuthorUserId,
    DateTimeOffset MentionedAt) : ITenantEvent;

/// <summary>
/// Where the dashboard lives, from the same <c>ScreenHire:Domains</c> settings as the API (ADR-0003): the worker needs it
/// to link to a note from an email. Nothing here names a host.
/// </summary>
public sealed class DashboardLinkOptions
{
    public const string Section = "ScreenHire:Domains";

    /// <summary><c>https</c> everywhere except local development.</summary>
    public string Scheme { get; set; } = "https";

    /// <summary>The dashboard's host, such as <c>app.example.com</c>; empty when this host was given none.</summary>
    public string AppHost { get; set; } = "";

    /// <summary>An absolute dashboard URL for <paramref name="localPath"/>, or null without an <see cref="AppHost"/>.</summary>
    public Uri? Url(string localPath) => AppHost.Length == 0 ? null : new Uri(new Uri($"{Scheme}://{AppHost}"), localPath);
}

public static class DashboardLinkRegistration
{
    public static IServiceCollection AddDashboardLinks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<DashboardLinkOptions>().BindConfiguration(DashboardLinkOptions.Section);
        return services;
    }
}

/// <summary>
/// The email a mentioned teammate gets: who mentioned them and where, with a link to the note. ScreenHire's own wording,
/// like the sign-in and invitation emails, and never the note's text, so candidate details stay in ScreenHire.
/// </summary>
public static class MentionEmail
{
    /// <summary>Where the email's link goes: the note on the candidate's Activity tab.</summary>
    public static string NotePath(Guid applicationId, Guid noteId) => $"/applications/{applicationId}?tab=activity#note-{noteId}";

    public static EmailMessage Compose(string to, string author, string candidateName, string jobTitle, string organizationName, Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var where = $"{candidateName}, {jobTitle}, at {organizationName}";
        var text = $"""
            {author} mentioned you in a note on {where}.

            Open the note: {link}
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(author)} mentioned you in a note on {WebUtility.HtmlEncode(where)}.</p>
            <p><a href="{WebUtility.HtmlEncode(link.ToString())}">Open the note</a></p>
            """;
        return new EmailMessage(to, $"{author} mentioned you on {candidateName}", text, html);
    }
}

/// <summary>
/// Emails the teammate a note mentioned, in the worker. Idempotent on its own: the mention row records the send
/// (<see cref="ApplicationNoteMention.EmailedAt"/>), so a redelivery finds it and sends nothing. A mention that is gone
/// (the note was deleted, or an edit took the mention out) or a person who left the organization gets no email. The
/// email goes before the row commits, so a crash between the two can repeat it once, as with emails to candidates.
/// </summary>
public static partial class TeammateMentionedHandler
{
    public static async Task Handle(
        TeammateMentioned message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        TimeProvider time,
        ILogger<TeammateMentioned> logger,
        CancellationToken cancellationToken)
    {
        var mention = await db.ApplicationNoteMentions.SingleOrDefaultAsync(m => m.Id == message.MentionId, cancellationToken);
        if (mention is null || mention.EmailedAt is not null)
        {
            return; // removed since, or sent already
        }

        var link = links.Value.Url(MentionEmail.NotePath(message.ApplicationId, mention.NoteId));
        if (!email.IsConfigured)
        {
            LogNotSent(logger, mention.Id, "no email sender is configured");
            return;
        }

        if (link is null)
        {
            // Email works but this worker has no ScreenHire:Domains:AppHost: a deployment mistake, not a choice.
            LogNoDashboardAddress(logger, mention.Id);
            return;
        }

        // Narrowed to this organization: the members filter also shows a user's memberships elsewhere.
        var member = await db.OrganizationMembers.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == message.OrganizationId && m.UserId == mention.UserId, cancellationToken);
        var note = await db.ApplicationNotes.AsNoTracking().IgnoreAutoIncludes().SingleOrDefaultAsync(n => n.Id == mention.NoteId, cancellationToken);
        if (!member || note is null || note.AuthorUserId == mention.UserId)
        {
            LogNotSent(logger, mention.Id, "the person is not a member, or wrote the note");
            return;
        }

        var to = await db.Users.Where(u => u.Id == mention.UserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        var place = await (
            from application in db.Applications.AsNoTracking()
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join job in db.Jobs on application.JobId equals job.Id
            join organization in db.Organizations on application.OrganizationId equals organization.Id
            where application.Id == note.ApplicationId
            select new { Candidate = candidate.FullName, Job = job.Title, Organization = organization.Name }).SingleOrDefaultAsync(cancellationToken);
        if (to is null || place is null)
        {
            LogNotSent(logger, mention.Id, "the person or the application is gone");
            return;
        }

        var author = note.AuthorUserId is { } authorId
            ? await db.Users.Where(u => u.Id == authorId).Select(u => u.DisplayName ?? u.Email).SingleOrDefaultAsync(cancellationToken)
            : await db.ApiKeys.Where(k => k.Id == note.AuthorApiKeyId).Select(k => k.Name).SingleOrDefaultAsync(cancellationToken);

        await email.SendAsync(
            MentionEmail.Compose(to, author ?? "A teammate", place.Candidate, place.Job, place.Organization, link), cancellationToken);
        mention.MarkEmailed(time.GetUtcNow());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mention {MentionId} was not emailed: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid mentionId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mention {MentionId} was not emailed: the worker has no ScreenHire:Domains:AppHost to link to")]
    private static partial void LogNoDashboardAddress(ILogger logger, Guid mentionId);
}
