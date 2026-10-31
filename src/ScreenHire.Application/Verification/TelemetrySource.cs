using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Verification;

/// <summary>
/// The telemetry the careers page's module sent (<c>packages/telemetry</c>), from <c>telemetry_snapshots</c>: the
/// snapshots of every page view in the application's session, added up per field.
/// </summary>
public sealed class DatabaseTelemetrySource(ScreenHireDbContext db) : ITelemetrySource
{
    public async Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var snapshots = await db.TelemetrySnapshots.AsNoTracking()
            .Where(s => s.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        return snapshots.Count == 0 ? null : TelemetryRules.Combine(applicationId, snapshots);
    }

    public IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, VerificationContext context) =>
        TelemetryRules.Evaluate(session, context);

    public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.TelemetrySnapshots.Where(s => s.ExpiresAt <= cutoff).ExecuteDeleteAsync(cancellationToken);
}

/// <summary>
/// The behavioural rules (verification-engine.md §10.2). Each reads aggregates only, reports a <c>BEH_*</c> signal, and
/// never raises a level on its own: the ruleset's <c>telemetry-never-alone</c> keeps a behavioural signal as context
/// unless a category other than telemetry corroborates. Only free-text answers count. Pasting a name, an email address or
/// a link is normal, and autofilled fields are left out, because a browser or password manager filled them.
/// </summary>
public static class TelemetryRules
{
    /// <summary>The shortest answer the paste rule looks at: about a sentence. Shorter answers are often pasted.</summary>
    public const int PasteMinimumCharacters = 40;

    /// <summary>The share of an answer's characters, in percent, that must have been pasted.</summary>
    public const int PasteDominantPercent = 80;

    /// <summary>The fewest times the page was hidden, and an answer field focused, for focus switching.</summary>
    public const int FocusSwitchMinimum = 5;

    /// <summary>The session's snapshots as one set of aggregates: per field, every page view's counts added up.</summary>
    public static TelemetrySessionAggregates Combine(Guid applicationId, IReadOnlyList<TelemetrySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var fields = snapshots
            .SelectMany(s => s.Fields)
            .GroupBy(f => f.FieldId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new FieldTelemetry(
                g.Key,
                g.Sum(f => f.PasteCount),
                g.Sum(f => f.PastedCharacters),
                g.Sum(f => f.TypedCharacters),
                g.Sum(f => f.FocusCount),
                g.Sum(f => f.BlurCount),
                g.Sum(f => f.ActiveMilliseconds),
                g.Any(f => f.Autofilled)))
            .ToList();
        var page = new PageTelemetry(
            snapshots.Sum(s => s.Page.VisibilityChanges),
            [.. snapshots.SelectMany(s => s.Page.AutomationMarkers).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        return new TelemetrySessionAggregates(applicationId, fields, page, snapshots.Max(s => s.ReceivedAt));
    }

    public static IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, VerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        var textQuestions = ApplicationForm.QuestionsFor(context.Job.Workflow)
            .Where(q => q.Type is QuestionType.ShortText or QuestionType.LongText)
            .ToDictionary(q => q.Id, StringComparer.Ordinal);
        var answers = session.Fields
            .Where(f => !f.Autofilled && textQuestions.ContainsKey(f.FieldId))
            .Select(f => (Field: f, Question: textQuestions[f.FieldId]))
            .ToList();

        var signals = new List<ObservedSignal>();
        foreach (var (field, question) in answers)
        {
            var entered = field.PastedCharacters + field.TypedCharacters;
            if (entered >= PasteMinimumCharacters && field.PastedCharacters * 100L >= (long)PasteDominantPercent * entered)
            {
                signals.Add(new ObservedSignal(
                    SignalCatalogue.PasteDominant,
                    SignalConfidence.Medium,
                    [
                        SignalEvidence.Metric("Characters pasted", field.PastedCharacters, "characters", entered, "Characters entered"),
                        SignalEvidence.Fact("Pastes", Count(field.PasteCount)),
                    ],
                    new Dictionary<string, string> { ["field"] = AnswerTo(question) }));
            }
        }

        // The answer the candidate came back to most often, when they also left the page often.
        var revisited = answers.Where(a => a.Field.FocusCount >= FocusSwitchMinimum).OrderByDescending(a => a.Field.FocusCount).ToList();
        if (session.Page.VisibilityChanges >= FocusSwitchMinimum && revisited.Count > 0)
        {
            var (field, question) = revisited[0];
            signals.Add(new ObservedSignal(
                SignalCatalogue.FocusSwitching,
                SignalConfidence.Low,
                [
                    SignalEvidence.Metric("Times the candidate left the page", session.Page.VisibilityChanges, "times", FocusSwitchMinimum, "Shown from"),
                    SignalEvidence.Metric("Times the answer field was returned to", field.FocusCount, "times", FocusSwitchMinimum, "Shown from"),
                ],
                new Dictionary<string, string> { ["field"] = Quoted(question) }));
        }

        return signals;
    }

    private static string AnswerTo(Question question) => $"the answer to {Quoted(question)}";

    private static string Quoted(Question question) => $"\"{question.Label}\"";

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
}
