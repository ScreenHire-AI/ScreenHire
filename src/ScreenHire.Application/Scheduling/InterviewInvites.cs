using System.Net;
using System.Text;
using MimeKit;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The calendar invitations (.ics) a booking sends (docs/architecture/interview-scheduling.md, "Calendar invitations"), so an
/// interview reaches a calendar with no Google or Outlook connection: the candidate's with every confirmation, move and
/// cancellation, and an interviewer's when their own calendar did not get the event. Every copy of one interview has the same
/// UID, and its sequence is the request's revision, so a move updates the event and a cancellation removes it.
/// </summary>
public static class InterviewInvites
{
    public const string FileName = "invite.ics";

    /// <summary>The UID of every copy of an interview, and the event id of an interviewer on ScreenHire's calendar.</summary>
    public static string Uid(Guid interviewId) => InterviewAttendee.IcsUid(interviewId);

    /// <summary>
    /// The candidate's invitation. <paramref name="organizer"/> is the address the email comes from or replies go to, so a
    /// calendar app that answers the invitation answers somewhere that reaches the team.
    /// </summary>
    public static EmailAttachment ForCandidate(
        InterviewRequest interview, string organizationName, string jobTitle, string candidateEmail, string? organizer, string? manageLink, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(interview);
        var meeting = InterviewText.Sentence(interview.MeetingNameOrDefault());
        var description = new List<string> { $"{meeting} with {InterviewText.Plain(organizationName)} about {InterviewText.Plain(jobTitle)}." };
        if (!string.IsNullOrEmpty(interview.MeetingUrl))
        {
            description.Add($"Meeting link: {interview.MeetingUrl}");
        }

        if (manageLink is not null)
        {
            description.Add($"Change or cancel: {manageLink}");
        }

        return Attach(Event(
            interview, $"{meeting} with {InterviewText.Plain(organizationName)}", string.Join('\n', description), now, organizer, new IcsPerson(candidateEmail)));
    }

    /// <summary>An interviewer's invitation: the title and body their calendar event would have had.</summary>
    public static EmailAttachment ForInterviewer(
        InterviewRequest interview, string candidateName, string jobTitle, IcsPerson interviewer, string? organizer, Uri? applicationLink, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(interview);
        return Attach(Event(
            interview,
            InterviewText.EventTitle(candidateName, jobTitle, interview.MeetingName),
            InterviewText.EventDescription(candidateName, jobTitle, applicationLink, interview.MeetingUrl),
            now,
            organizer,
            interviewer));
    }

    /// <summary>The bare address of a From or Reply-To value such as <c>Team &lt;team@example.com&gt;</c>, or null.</summary>
    public static string? AddressOf(string? mailbox) =>
        mailbox is { Length: > 0 } && MailboxAddress.TryParse(mailbox, out var parsed) ? parsed.Address : null;

    private static IcsEvent Event(InterviewRequest interview, string summary, string description, DateTimeOffset now, string? organizer, IcsPerson attendee)
    {
        var start = interview.StartsAt ?? throw new InvalidOperationException("Only a booked interview has an invitation.");
        return new IcsEvent(
            Uid(interview.Id),
            interview.Revision,
            start,
            start + interview.Duration,
            summary,
            description,
            interview.MeetingUrl,
            now,
            Cancelled: interview.Status == InterviewStatus.Cancelled,
            Organizer: AddressOf(organizer) is { } address ? new IcsPerson(address) : null,
            Attendees: [attendee]);
    }

    private static EmailAttachment Attach(IcsEvent ev) =>
        new(FileName, IcsWriter.InvitationContentType(ev), Encoding.UTF8.GetBytes(IcsWriter.Invitation(ev)));
}

/// <summary>
/// The email an interviewer gets with the invitation when their own calendar did not get the event (they use ScreenHire's
/// calendar, or theirs needs connecting again): ScreenHire's own wording, like the reminder.
/// </summary>
public static class InterviewTeamInviteEmail
{
    public static EmailMessage Compose(
        string to, string candidateName, string jobTitle, string time, bool cancelled, bool moved, string? meetingUrl, Uri? applicationLink, string? meetingName, EmailAttachment invite)
    {
        var meeting = InterviewText.Plain(meetingName ?? WorkflowStage.DefaultMeetingName);
        var who = InterviewText.Plain(candidateName);
        var job = InterviewText.Plain(jobTitle);
        var (subject, lead) = cancelled
            ? ($"{InterviewText.Sentence(meeting)} cancelled: {who}", $"The {meeting} with {who} for {job} is cancelled.")
            : moved
                ? ($"{InterviewText.Sentence(meeting)} moved: {who}", $"The {meeting} with {who} for {job} has a new time.")
                : ($"{InterviewText.Sentence(meeting)} booked: {who}", $"{who} booked a {meeting} for {job}.");
        var text = new List<string> { lead, time };
        var html = new List<string> { $"<p>{WebUtility.HtmlEncode(lead)}</p>", $"<p>{WebUtility.HtmlEncode(time)}</p>" };
        if (!cancelled && !string.IsNullOrEmpty(meetingUrl))
        {
            text.Add($"Meeting link: {meetingUrl}");
            html.Add($"<p><a href=\"{WebUtility.HtmlEncode(meetingUrl)}\">Join the meeting</a></p>");
        }

        var calendar = cancelled ? "The attached file removes it from your calendar." : "The attached invitation adds it to your calendar.";
        text.Add(calendar);
        html.Add($"<p>{WebUtility.HtmlEncode(calendar)}</p>");
        if (applicationLink is not null)
        {
            text.Add($"Open the application: {applicationLink}");
            html.Add($"<p><a href=\"{WebUtility.HtmlEncode(applicationLink.ToString())}\">Open the application</a></p>");
        }

        return new EmailMessage(to, subject, string.Join("\n\n", text), string.Join('\n', html), Attachments: [invite]);
    }
}
