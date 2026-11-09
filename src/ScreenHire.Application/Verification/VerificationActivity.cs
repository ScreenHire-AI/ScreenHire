using System.Globalization;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Application.Verification;

/// <summary>What one entry of the Verification area's activity log is.</summary>
public enum VerificationActivityKind
{
    /// <summary>One analyzer's pass over one application.</summary>
    CheckRun,

    /// <summary>One call to a language model.</summary>
    ModelCall,
}

/// <summary>How an entry of the activity log ended. A word for what happened, never a verdict on a person.</summary>
public enum VerificationActivityOutcome
{
    /// <summary>The check ran and raised nothing, or the model answered.</summary>
    Completed,

    /// <summary>The check ran and wrote signals.</summary>
    Found,

    /// <summary>The check did not look: its group was off, the AI checks were paused, or it could not look.</summary>
    NotRun,

    /// <summary>A model call failed or timed out.</summary>
    Failed,
}

/// <summary>
/// The rules behind the activity log (verification-engine.md 10.12): what a run or call ended as, which group a call belongs
/// to, the cursor between pages, and how the two sources (check runs and model calls) become one newest-first page.
/// </summary>
public static class VerificationActivity
{
    /// <summary>What an analyzer run ended as, from its signal count and the note it kept.</summary>
    /// <remarks>
    /// A note that is not a trusted-list note and a run with no signals means the check did not look: the group was off, the
    /// AI checks were paused, or something it needed was missing. A trusted-list note means it looked and the list stopped a signal.
    /// </remarks>
    public static VerificationActivityOutcome OutcomeOfRun(string analyzer, int signalCount, string? note)
    {
        if (signalCount > 0)
        {
            return VerificationActivityOutcome.Found;
        }

        return note is not null && RunNotes.KindOf(analyzer, note) != CheckNotRunKind.Trusted
            ? VerificationActivityOutcome.NotRun
            : VerificationActivityOutcome.Completed;
    }

    public static VerificationActivityOutcome OutcomeOfCall(AiCallOutcome outcome) =>
        outcome == AiCallOutcome.Ok ? VerificationActivityOutcome.Completed : VerificationActivityOutcome.Failed;

    /// <summary>The group a model call's check belongs to: the consistency check is part of Answers; a connection test has none.</summary>
    public static CheckGroup? GroupOfCall(string checkName) =>
        checkName.StartsWith("consistency", StringComparison.Ordinal) ? CheckGroup.Answers : null;

    /// <summary>The analyzers of a group, for filtering runs.</summary>
    public static IReadOnlyList<string> AnalyzersOf(CheckGroup group) =>
        [.. CheckGroups.ByAnalyzer.Where(pair => pair.Value == group).Select(pair => pair.Key)];

    /// <summary>The note values that mean a run did look (see <see cref="OutcomeOfRun"/>).</summary>
    public static IReadOnlyList<string> TrustedNotes => RunNotes.TrustedNotes;

    /// <summary>Orders two ids as Postgres orders uuids (byte by byte, as the lower-case text reads), so a page break agrees with the database.</summary>
    public static int CompareIds(Guid left, Guid right) =>
        string.CompareOrdinal(left.ToString("D", CultureInfo.InvariantCulture), right.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>A position in the feed: the time and id of the last entry of the page before.</summary>
    public readonly record struct Cursor(DateTimeOffset At, Guid Id)
    {
        public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{At.UtcTicks}.{Id:N}");

        public static bool TryParse(string? text, out Cursor cursor)
        {
            cursor = default;
            var parts = text?.Split('.');
            if (parts is not [var ticks, var id]
                || !long.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || value > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(id, "N", out var guid)
                || guid == Guid.Empty)
            {
                return false;
            }

            cursor = new Cursor(new DateTimeOffset(value, TimeSpan.Zero), guid);
            return true;
        }
    }

    /// <summary>
    /// Merges two newest-first sources, each already cut to <paramref name="limit"/> + 1 rows, into one page of at most
    /// <paramref name="limit"/> entries, newest first by time and then id. <c>More</c> says rows were left over.
    /// </summary>
    public static (IReadOnlyList<T> Items, bool More) MergePage<T>(
        IEnumerable<T> first, IEnumerable<T> second, int limit, Func<T, DateTimeOffset> at, Func<T, Guid> id)
    {
        var merged = first.Concat(second)
            .OrderByDescending(at)
            .ThenByDescending(id, Comparer<Guid>.Create(CompareIds))
            .ToList();
        return (merged.Take(limit).ToList(), merged.Count > limit);
    }
}
