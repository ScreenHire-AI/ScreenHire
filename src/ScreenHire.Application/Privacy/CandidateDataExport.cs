using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// Everything the copy of a candidate's data is built from, as plain values. The loader fills it from the database; the
/// builder turns it into the file. Keeping the two apart lets a unit test prove what the file holds and what it leaves out.
/// </summary>
/// <param name="Name">The candidate's full name.</param>
/// <param name="Email">Their email address.</param>
/// <param name="Phone">Their phone number, when they gave one.</param>
/// <param name="CreatedAt">When they first applied.</param>
/// <param name="Applications">Their applications, in the order to list them.</param>
/// <param name="SavedNote">The note on their saved candidates entry, when the team saved them and wrote one.</param>
public sealed record DataExportSource(
    string Name,
    string Email,
    string? Phone,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DataExportApplication> Applications,
    string? SavedNote);

/// <summary>One application as the copy shows it.</summary>
/// <param name="JobTitle">The job's title.</param>
/// <param name="SubmittedAt">When the application arrived.</param>
/// <param name="StageName">The name of the stage it is in now; null when the stage is no longer in the job's workflow.</param>
/// <param name="Questions">The job's application form, to give each answer its question.</param>
/// <param name="Answers">The candidate's answers.</param>
/// <param name="PrivacyNoticeVersion">The version of the privacy notice they saw.</param>
/// <param name="AskedToBeKeptForFutureRoles">Whether they asked on this application's form to be kept for future roles.</param>
/// <param name="EmailsSent">What the team emailed them about it.</param>
/// <param name="Replies">What they emailed back.</param>
/// <param name="Notes">The team's notes on it.</param>
/// <param name="UploadedFiles">The names of the files they uploaded, with the question they answered.</param>
public sealed record DataExportApplication(
    string JobTitle,
    DateTimeOffset SubmittedAt,
    string? StageName,
    IReadOnlyList<Question> Questions,
    IReadOnlyList<ApplicationAnswer> Answers,
    string? PrivacyNoticeVersion,
    bool AskedToBeKeptForFutureRoles,
    IReadOnlyList<DataExportEmail> EmailsSent,
    IReadOnlyList<DataExportReply> Replies,
    IReadOnlyList<DataExportNote> Notes,
    IReadOnlyList<DataExportFile> UploadedFiles);

public sealed record DataExportEmail(string To, string Subject, string Body, DateTimeOffset SentAt);

public sealed record DataExportReply(string From, string Subject, string Body, DateTimeOffset ReceivedAt, IReadOnlyList<string> AttachmentNames);

public sealed record DataExportNote(string Text, DateTimeOffset WrittenAt);

public sealed record DataExportFile(string? Question, string FileName);

/// <summary>
/// Builds the copy of a candidate's data as one JSON document (docs/architecture/data-requests.md, "The copy"). It holds
/// what the candidate gave and what the organization wrote to or about them as a person: their details, each application
/// with its answers, the emails both ways, the team's notes (the author is shown only as "Team member"), the note on a
/// saved entry, and the names of files, never the files. It leaves out verification output and scorecards, which are the
/// organization's working judgement and not the candidate's data (ADR-0004; listed as a legal review item), and every
/// internal id.
/// </summary>
public static partial class CandidateDataExport
{
    /// <summary>How a note's author is shown: never a name, so the copy does not identify a colleague.</summary>
    public const string NoteAuthor = "Team member";

    public const string ContentType = "application/json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The blob a request's copy is written to: stable, so a redelivered message replaces it instead of adding another.</summary>
    public static string BlobNameFor(Guid organizationId, Guid requestId) => $"{organizationId:N}/{requestId:N}.json";

    /// <summary>The file's content as UTF-8 JSON.</summary>
    public static byte[] Build(DataExportSource source, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = new
        {
            generatedAt,
            candidate = new { name = source.Name, email = source.Email, phone = source.Phone, createdAt = source.CreatedAt },
            applications = source.Applications.Select(Application).ToList(),
            savedCandidateNote = source.SavedNote,
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    private static object Application(DataExportApplication application) => new
    {
        jobTitle = application.JobTitle,
        submittedAt = application.SubmittedAt,
        stage = application.StageName,
        privacyNoticeVersion = application.PrivacyNoticeVersion,
        askedToBeKeptForFutureRoles = application.AskedToBeKeptForFutureRoles,
        // A file question's answer is an internal upload id; the file itself is listed under uploadedFiles by name.
        answers = application.Answers
            .Where(a => application.Questions.FirstOrDefault(q => q.Id == a.QuestionId)?.Type != QuestionType.File)
            .Select(a => Answer(application.Questions, a))
            .ToList(),
        uploadedFiles = application.UploadedFiles.Select(f => new { question = f.Question, fileName = f.FileName }).ToList(),
        emailsSent = application.EmailsSent.Select(e => new { to = e.To, subject = e.Subject, body = e.Body, sentAt = e.SentAt }).ToList(),
        repliesReceived = application.Replies
            .Select(r => new { from = r.From, subject = r.Subject, body = r.Body, receivedAt = r.ReceivedAt, attachments = r.AttachmentNames })
            .ToList(),
        notes = application.Notes.Select(n => new { text = Anonymize(n.Text), writtenAt = n.WrittenAt, author = NoteAuthor }).ToList(),
    };

    // The question's label when the job's form still has it, else its id. A choice shows the option's label.
    private static object Answer(IReadOnlyList<Question> questions, ApplicationAnswer answer)
    {
        var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
        string Option(string value) => question?.Options?.FirstOrDefault(o => o.Id == value)?.Label ?? value;
        return new
        {
            question = question?.Label ?? answer.QuestionId,
            answer = answer.Value is { } value ? Option(value) : null,
            answers = answer.Values?.Select(Option).ToList(),
        };
    }

    // A mention in a note is stored as the colleague's user id; the copy shows the role instead of an id.
    private static string Anonymize(string text) => Mention().Replace(text, "@" + NoteAuthor);

    [GeneratedRegex(@"@\[[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\]")]
    private static partial Regex Mention();
}
