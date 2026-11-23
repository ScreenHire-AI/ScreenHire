using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Reports;

/// <summary>
/// Equality monitoring totals (docs/architecture/reports.md, Equality monitoring): for each library form of that kind, how
/// many candidates who answered it in the range gave each answer, across every job that sends it. Reads through EF, so the
/// tenant filter and the job access filter narrow it as row-level security does. Nothing names a candidate, and any count
/// under <see cref="SmallCounts.Minimum"/> is hidden (<see cref="SmallCounts"/>). Beside each answer's count are the
/// outcomes an impact-ratio bias audit divides (docs/compliance/bias-audit-method.md): how many of those candidates moved
/// forward, were hired, and were assigned an authenticity level above the lowest. Each column hides its small counts the
/// same way.
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

        var responses = await query.Select(r => new { FormId = r.FormTemplateId!.Value, r.ApplicationId, r.Answers }).ToListAsync(cancellationToken);
        var outcomes = await OutcomesAsync(db, [.. responses.Select(r => r.ApplicationId).Distinct()], cancellationToken);
        var formIds = responses.Select(r => r.FormId).Distinct().ToList();
        var forms = await db.FormTemplates.AsNoTracking()
            .Where(t => formIds.Contains(t.Id) || t.Kind == FormKind.EqualityMonitoring)
            .Include(t => t.Versions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var byForm = responses.ToLookup(r => r.FormId, r => new Response(r.Answers, outcomes.GetValueOrDefault(r.ApplicationId)));
        return new(
            [.. forms.Where(f => f.Kind == FormKind.EqualityMonitoring)
                .OrderBy(f => f.Name, StringComparer.CurrentCulture)
                .Select(f => Form(f, [.. byForm[f.Id]]))],
            SmallCounts.Minimum);
    }

    private sealed record Response(IReadOnlyList<ApplicationAnswer> Answers, Outcome Outcome);

    // What happened to one application, for the audit's rates. A default Outcome is none of the three.
    private readonly record struct Outcome(bool Advanced, bool Hired, bool Flagged);

    // Moved forward: entered a stage past the first that does not end the application (a template switch is not a move),
    // or is hired. Hired: in a hired stage now. Flagged: its newest authenticity assessment is above the lowest level, dismissed or not,
    // because NYC counts being assigned a classification (6 RCNY 5-300).
    private static async Task<Dictionary<Guid, Outcome>> OutcomesAsync(ScreenHireDbContext db, IReadOnlyList<Guid> applicationIds, CancellationToken cancellationToken)
    {
        var applications = await db.Applications.AsNoTracking()
            .Where(a => applicationIds.Contains(a.Id))
            .Select(a => new { a.Id, a.JobId, a.CurrentStageId })
            .ToListAsync(cancellationToken);
        var jobIds = applications.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, cancellationToken);
        var moves = (await db.ApplicationEvents.AsNoTracking()
                .Where(e => applicationIds.Contains(e.ApplicationId) && e.Type == ApplicationEventType.StageChanged)
                .Select(e => new { e.ApplicationId, e.Data })
                .ToListAsync(cancellationToken))
            .Where(e => e.Data.TemplateVersionId is null)
            .ToLookup(e => e.ApplicationId, e => e.Data.ToStageId);
        var flagged = (await db.VerificationAssessments.AsNoTracking()
                .Where(v => applicationIds.Contains(v.ApplicationId) && v.Kind == VerificationAssessmentKind.Authenticity)
                .Select(v => new { v.ApplicationId, v.Level, v.AssessedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.ApplicationId)
            .Where(g => g.MaxBy(v => v.AssessedAt)!.Level != nameof(AuthenticityLevel.NoConcerns))
            .Select(g => g.Key)
            .ToHashSet();

        return applications.ToDictionary(a => a.Id, a =>
        {
            var workflow = jobs[a.JobId].Workflow;
            var forward = moves[a.Id].Any(stageId => workflow.FindStage(stageId ?? "")?.Kind is { } kind
                && kind is not (StageKind.Applied or StageKind.Rejected or StageKind.Withdrawn));
            var hired = workflow.FindStage(a.CurrentStageId)?.Kind == StageKind.Hired;
            // Hired counts as moved forward too, even with no move event (a template switch placed it there), so a row's
            // hired count never exceeds its moved-forward count and the audit's selection rates stay nested.
            return new Outcome(forward || hired, hired, flagged.Contains(a.Id));
        });
    }

    // One form's totals: a row per question its newest published version asks, a cell per option and one for no answer.
    private static EqualityForm Form(FormTemplate form, IReadOnlyList<Response> responses)
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
            var given = responses.Select(r => (Answer: r.Answers.FirstOrDefault(a => a.QuestionId == question.Id), r.Outcome)).ToList();
            var values = Choices(question).Select(c => c.Value).ToList();
            foreach (var other in given.Select(g => g.Answer).OfType<ApplicationAnswer>().SelectMany(Values).Distinct(StringComparer.Ordinal).Where(v => !values.Contains(v, StringComparer.Ordinal)))
            {
                values.Add(other);
            }

            values.Add(NoAnswer);
            var groups = values
                .Select(value => given.Where(g => value == NoAnswer
                    ? g.Answer is null || !Values(g.Answer).Any()
                    : g.Answer is not null && Values(g.Answer).Contains(value, StringComparer.Ordinal)).Select(g => g.Outcome).ToList())
                .ToList();
            var shown = SmallCounts.Suppress([.. groups.Select(g => g.Count)]);
            var advanced = SmallCounts.Suppress([.. groups.Select(g => g.Count(o => o.Advanced))]);
            var hired = SmallCounts.Suppress([.. groups.Select(g => g.Count(o => o.Hired))]);
            var flaggedCounts = SmallCounts.Suppress([.. groups.Select(g => g.Count(o => o.Flagged))]);
            return new EqualityQuestion(
                question.Id,
                question.Label,
                [.. values.Select((value, i) => new EqualityAnswerCount(
                    value, value == NoAnswer ? "No answer" : labels.GetValueOrDefault((question.Id, value), value), shown[i],
                    // An outcome of a group whose own count is hidden stays hidden, or it would give the group's size away.
                    shown[i] is null ? null : advanced[i], shown[i] is null ? null : hired[i], shown[i] is null ? null : flaggedCounts[i]))]);
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
