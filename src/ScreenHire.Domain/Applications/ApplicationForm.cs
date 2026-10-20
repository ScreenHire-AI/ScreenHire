using System.Globalization;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One answer to a screening question, in the form automation conditions read (<c>answers.{QuestionId}</c>):
/// <c>yes</c> or <c>no</c> for yes/no questions, an option id for single choice, the text, number or URL for the
/// others, and an upload id for a file question. Multiple choice answers are option ids in <c>values</c>.
/// </summary>
/// <remarks>
/// <c>value</c>: Every type except <c>MultiChoice</c>.
/// <c>values</c>: Option ids, for <c>MultiChoice</c>.
/// </remarks>
public sealed record ApplicationAnswer(string QuestionId, string? Value = null, IReadOnlyList<string>? Values = null);

/// <summary>A problem with one answer, worded for the candidate. <see cref="QuestionId"/> names the question.</summary>
public sealed record ApplicationFormError(string QuestionId, string Message);

/// <summary>
/// The questions a candidate answers when applying to a job, and the rules their answers must meet. The job's
/// questionnaire comes first; every form also asks for a resume, as an optional file question with the id
/// <see cref="ResumeQuestionId"/>, unless the questionnaire has its own question with that id.
/// </summary>
/// <remarks>
/// Knockout answers are valid answers. Whether an answer makes someone ineligible is for an automation and a person to
/// decide after submission (workflow-engine.md §2), never for this form.
/// </remarks>
public static class ApplicationForm
{
    public const string ResumeQuestionId = "resume";

    /// <summary>
    /// A file question with this id asks for the candidate's own LinkedIn profile export (PDF), which the consistency
    /// check compares with the resume and answers. A job opts in by adding the question; ScreenHire never fetches a
    /// profile itself (CLAUDE.md non-negotiable 5).
    /// </summary>
    public const string LinkedInExportQuestionId = "linkedin_export";

    public const int ShortTextMaxLength = 300;

    public const int LongTextMaxLength = 5000;

    public const int UrlMaxLength = 2000;

    /// <summary>The resume question a form gets when the job's questionnaire has none.</summary>
    public static Question DefaultResumeQuestion { get; } =
        new(ResumeQuestionId, QuestionType.File, "Resume or CV", AcceptedFileTypes: [".pdf", ".docx"]);

    /// <summary>The job's questions, with the resume question first.</summary>
    public static IReadOnlyList<Question> QuestionsFor(HiringWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var questions = workflow.Questionnaire.Questions;
        var resume = questions.FirstOrDefault(q => q.Id == ResumeQuestionId) ?? DefaultResumeQuestion;
        return [resume, .. questions.Where(q => q.Id != ResumeQuestionId)];
    }

    /// <summary>The longest text a question accepts.</summary>
    public static int MaxLengthOf(Question question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var limit = question.Type switch
        {
            QuestionType.LongText => LongTextMaxLength,
            QuestionType.Url => UrlMaxLength,
            _ => ShortTextMaxLength,
        };
        return question.MaxLength is { } max ? Math.Min(max, limit) : limit;
    }

    /// <summary>
    /// Checks <paramref name="answers"/> against <paramref name="questions"/>: no unknown or repeated questions, every
    /// required question answered, and each answer the right shape for its type. File answers are checked for shape
    /// only; whether the upload exists is the caller's to check.
    /// </summary>
    public static IReadOnlyList<ApplicationFormError> Validate(IReadOnlyList<Question> questions, IReadOnlyList<ApplicationAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(answers);
        var errors = new List<ApplicationFormError>();
        var byId = questions.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var answered = new Dictionary<string, ApplicationAnswer>(StringComparer.Ordinal);

        foreach (var answer in answers)
        {
            if (!byId.ContainsKey(answer.QuestionId))
            {
                errors.Add(new(answer.QuestionId, "This job has no question with this id."));
            }
            else if (!answered.TryAdd(answer.QuestionId, answer))
            {
                errors.Add(new(answer.QuestionId, "Answer each question once."));
            }
        }

        foreach (var question in questions)
        {
            var answer = answered.GetValueOrDefault(question.Id);
            if (answer is null || IsEmpty(answer))
            {
                if (question.Required)
                {
                    errors.Add(new(question.Id, question.Type == QuestionType.File ? "Add a file." : "Answer this question."));
                }

                continue;
            }

            if (Check(question, answer) is { } message)
            {
                errors.Add(new(question.Id, message));
            }
        }

        return errors;
    }

    /// <summary>The answers with empty ones left out and text trimmed, as they are stored.</summary>
    public static IReadOnlyList<ApplicationAnswer> Normalize(IReadOnlyList<ApplicationAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return [.. answers.Where(a => !IsEmpty(a)).Select(a => a with
        {
            Value = string.IsNullOrWhiteSpace(a.Value) ? null : a.Value.Trim(),
            Values = a.Values is { Count: > 0 } values ? [.. values] : null,
        })];
    }

    private static bool IsEmpty(ApplicationAnswer answer) =>
        string.IsNullOrWhiteSpace(answer.Value) && (answer.Values is null || answer.Values.Count == 0);

    private static string? Check(Question question, ApplicationAnswer answer)
    {
        var value = answer.Value?.Trim();
        if (question.Type != QuestionType.MultiChoice && answer.Values is { Count: > 0 })
        {
            return "This question takes one answer.";
        }

        var options = question.Options?.Select(o => o.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        switch (question.Type)
        {
            case QuestionType.ShortText or QuestionType.LongText:
                return value!.Length > MaxLengthOf(question) ? $"Keep this to {MaxLengthOf(question)} characters or fewer." : null;
            case QuestionType.YesNo:
                return value is "yes" or "no" ? null : "Choose yes or no.";
            case QuestionType.SingleChoice:
                return options.Contains(value!) ? null : "Choose one of the options.";
            case QuestionType.MultiChoice:
                if (answer.Value is not null)
                {
                    return "Choose from the options.";
                }

                return answer.Values!.All(options.Contains) && answer.Values!.Distinct(StringComparer.Ordinal).Count() == answer.Values!.Count
                    ? null
                    : "Choose from the options.";
            case QuestionType.Number:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return "Enter a number.";
                }

                if (question.Min is { } min && number < min)
                {
                    return $"Enter {min.ToString(CultureInfo.InvariantCulture)} or more.";
                }

                return question.Max is { } max && number > max ? $"Enter {max.ToString(CultureInfo.InvariantCulture)} or less." : null;
            case QuestionType.Url:
                if (value!.Length > MaxLengthOf(question))
                {
                    return $"Keep this to {MaxLengthOf(question)} characters or fewer.";
                }

                return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? null
                    : "Enter a full web address, starting with https://.";
            case QuestionType.File:
                return Guid.TryParse(value, out _) ? null : "Upload the file again.";
            default:
                return "This question cannot be answered here.";
        }
    }
}
