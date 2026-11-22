using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Reports;

/// <summary>
/// Equality monitoring totals (docs/architecture/reports.md, Equality monitoring): for each library form of that kind, how
/// many candidates who answered it in the range gave each answer, across every job that sends it. Reads through EF, so the
/// tenant filter and the job access filter narrow it as row-level security does. Nothing names a candidate, and any count
/// under <see cref="SmallCounts.Minimum"/> is hidden (<see cref="SmallCounts"/>).
/// </summary>
internal static class EqualityReportReads
{
    private const string NoAnswer = "";

    public static async Task<EqualityReport> ReadAsync(
        ScreenHireDbContext db, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken)
    {
        var query = db.QuestionnaireRequests.AsNoTracking()
            .Where(r => r.Kind == FormKind.EqualityMonitoring && r.FormTemplateId != null && r.CompletedAt >= from && r.CompletedAt < to);
        if (jobIds.Count > 0)
        {
            query = query.Where(r => jobIds.Contains(r.JobId));
        }

        var responses = await query.Select(r => new { FormId = r.FormTemplateId!.Value, r.Answers }).ToListAsync(cancellationToken);
        var formIds = responses.Select(r => r.FormId).Distinct().ToList();
        var forms = await db.FormTemplates.AsNoTracking()
            .Where(t => formIds.Contains(t.Id) || t.Kind == FormKind.EqualityMonitoring)
            .Include(t => t.Versions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var byForm = responses.ToLookup(r => r.FormId, r => r.Answers);
        return new(
            [.. forms.Where(f => f.Kind == FormKind.EqualityMonitoring)
                .OrderBy(f => f.Name, StringComparer.CurrentCulture)
                .Select(f => Form(f, [.. byForm[f.Id]]))],
            SmallCounts.Minimum);
    }

    // One form's totals: a row per question its newest published version asks, a cell per option and one for no answer.
    private static EqualityForm Form(FormTemplate form, IReadOnlyList<IReadOnlyList<ApplicationAnswer>> responses)
    {
        var newest = form.LatestPublished ?? form.Versions.MaxBy(v => v.Version)!;
        if (responses.Count < SmallCounts.Minimum)
        {
            return new(form.Id, newest.Content.Name, null, []);
        }

        // Labels come from every version, the newest winning, so an answer to an option a later version dropped still reads.
        var labels = new Dictionary<(string, string), string>();
        foreach (var version in form.Versions.OrderByDescending(v => v.Version))
        {
            foreach (var question in version.Content.Questions ?? [])
            {
                foreach (var (value, label) in Choices(question))
                {
                    labels.TryAdd((question.Id, value), label);
                }
            }
        }

        var questions = (newest.Content.Questions ?? []).Select(question =>
        {
            var given = responses.Select(answers => answers.FirstOrDefault(a => a.QuestionId == question.Id)).ToList();
            var values = Choices(question).Select(c => c.Value).ToList();
            foreach (var other in given.OfType<ApplicationAnswer>().SelectMany(Values).Distinct(StringComparer.Ordinal).Where(v => !values.Contains(v, StringComparer.Ordinal)))
            {
                values.Add(other);
            }

            values.Add(NoAnswer);
            var counts = values
                .Select(value => value == NoAnswer
                    ? given.Count(a => a is null || !Values(a).Any())
                    : given.Count(a => a is not null && Values(a).Contains(value, StringComparer.Ordinal)))
                .ToList();
            var shown = SmallCounts.Suppress(counts);
            return new EqualityQuestion(
                question.Id,
                question.Label,
                [.. values.Select((value, i) => new EqualityAnswerCount(
                    value, value == NoAnswer ? "No answer" : labels.GetValueOrDefault((question.Id, value), value), shown[i]))]);
        });
        return new(form.Id, newest.Content.Name, responses.Count, [.. questions]);
    }

    private static IEnumerable<string> Values(ApplicationAnswer answer) =>
        answer.Values is { Count: > 0 } values ? values : answer.Value is { Length: > 0 } value ? [value] : [];

    private static IEnumerable<(string Value, string Label)> Choices(Question question) =>
        question.Type == QuestionType.YesNo
            ? [("yes", "Yes"), ("no", "No")]
            : (question.Options ?? []).Select(o => (o.Id, o.Label));
}

/// <summary>
/// The rule that keeps an equality total from pointing at a person: a count under <see cref="Minimum"/>, zero included, is
/// hidden; and when one count of a question is hidden, the next smallest is hidden with it, so the hidden one cannot be
/// worked out by taking the others from the number of responses.
/// </summary>
public static class SmallCounts
{
    public const int Minimum = 5;

    /// <summary>Each of <paramref name="counts"/>, or null where it is hidden.</summary>
    public static IReadOnlyList<int?> Suppress(IReadOnlyList<int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var shown = counts.Select(c => c < Minimum ? (int?)null : c).ToArray();
        if (shown.Count(c => c is null) == 1 && shown.Any(c => c is not null))
        {
            var smallest = Array.FindIndex(shown, c => c == shown.Where(s => s is not null).Min());
            shown[smallest] = null;
        }

        return shown;
    }
}
