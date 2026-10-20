using System.Text;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Application.Integrity.Content;

/// <summary>
/// The text of an application as content forensics read it: free-text answers for duplicate checks, the answers that can
/// state a fact about work history as one source for the consistency check, and which kind of document each file is.
/// </summary>
public static class ApplicationText
{
    /// <summary>One free-text answer and the question it answers.</summary>
    public sealed record FreeTextAnswer(string QuestionId, string QuestionLabel, string Text);

    /// <summary>Answers to short- and long-text questions: the only answers written in the candidate's own words.</summary>
    public static IReadOnlyList<FreeTextAnswer> FreeText(Job job, IReadOnlyList<ApplicationAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(answers);
        var questions = ApplicationForm.QuestionsFor(job.Workflow).ToDictionary(q => q.Id, StringComparer.Ordinal);
        return [.. answers
            .Where(a => a.Value is { Length: > 0 } && questions.TryGetValue(a.QuestionId, out var q) && q.Type is QuestionType.ShortText or QuestionType.LongText)
            .Select(a => new FreeTextAnswer(a.QuestionId, questions[a.QuestionId].Label, a.Value!))];
    }

    /// <summary>
    /// The answers the consistency check compares, as one text, a line per question: the question, then the answer as the
    /// candidate saw it (option labels, not option ids). Text, choice and number answers can state an employer, a title,
    /// dates, years of experience or a qualification. Yes/no answers cannot, and are often about right to work or
    /// availability, so they are not sent to a model. Files and links are left out too: a link is never followed.
    /// </summary>
    public static string AnswersSource(Job job, IReadOnlyList<ApplicationAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(answers);
        var questions = ApplicationForm.QuestionsFor(job.Workflow).ToDictionary(q => q.Id, StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var answer in answers)
        {
            if (!questions.TryGetValue(answer.QuestionId, out var question) || Shown(question, answer) is not { Length: > 0 } shown)
            {
                continue;
            }

            var label = question.Label.Trim();
            text.Append(label);
            text.Append(label.EndsWith('?') || label.EndsWith(':') || label.EndsWith('.') ? " " : ": ");
            text.Append(shown).Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>What a file is, from the question it answers.</summary>
    public static DocumentKind KindOf(string questionId) => questionId switch
    {
        ApplicationForm.ResumeQuestionId => DocumentKind.Resume,
        ApplicationForm.LinkedInExportQuestionId => DocumentKind.LinkedInExport,
        _ => DocumentKind.Other,
    };

    /// <summary>How a document is named to a reviewer.</summary>
    public static string Describe(DocumentKind kind) => kind switch
    {
        DocumentKind.Resume => "resume",
        DocumentKind.LinkedInExport => "LinkedIn profile export",
        _ => "uploaded file",
    };

    /// <summary>How a consistency source is named to a reviewer.</summary>
    public static string Describe(ConsistencySource source) => source switch
    {
        ConsistencySource.Resume => "resume",
        ConsistencySource.Answers => "application answers",
        _ => "LinkedIn profile export",
    };

    private static string? Shown(Question question, ApplicationAnswer answer)
    {
        var options = question.Options?.ToDictionary(o => o.Id, o => o.Label, StringComparer.Ordinal) ?? [];
        return question.Type switch
        {
            QuestionType.File or QuestionType.Url or QuestionType.YesNo => null,
            QuestionType.SingleChoice => answer.Value is { } id ? options.GetValueOrDefault(id, id) : null,
            QuestionType.MultiChoice => answer.Values is { Count: > 0 } ids ? string.Join(", ", ids.Select(id => options.GetValueOrDefault(id, id))) : null,
            _ => answer.Value?.Trim(),
        };
    }
}
