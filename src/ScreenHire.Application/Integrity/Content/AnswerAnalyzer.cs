using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.Application.Integrity.Content;

/// <summary>
/// Answer forensics on the free-text answers (integrity-engine.md §10.10): <c>CNT_NEAR_DUPLICATE_ANSWER</c> when an
/// answer is nearly identical to another candidate's answer to the same organization, and the informational
/// <c>CNT_TEMPLATED_ANSWER</c> when an answer contains text a writing tool or template leaves behind. Only short- and
/// long-text answers count; choices and short answers ("One month") are alike by nature.
/// </summary>
/// <remarks>
/// Comparison uses the documented thresholds in <see cref="TextSimilarity"/>, within the organization only, and never
/// with the same candidate's other applications. The other applicant is never named: the comparison shows their job
/// and when they applied. Like duplicates, earlier applications are not revisited; the later one carries the signal.
/// This application's fingerprints are kept for the applications after it.
/// </remarks>
public sealed class AnswerAnalyzer(IContentRecords records) : IIntegrityAnalyzer
{
    private const int MaxQuotedLength = 1500;

    public string Name => "answers";

    public int Version => 1;

    public async Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var answers = ApplicationText.FreeText(context.Job, context.Application.Answers);
        var signals = new List<ObservedSignal>();
        signals.AddRange(answers.SelectMany(Templated));

        var comparable = answers
            .Select(a => (Answer: a, Words: TextSimilarity.Words(a.Text)))
            .Where(a => a.Words.Count >= TextSimilarity.MinWords)
            .Select(a => (a.Answer, a.Words.Count, Shingles: TextSimilarity.Shingles(a.Words)))
            .ToList();
        if (comparable.Count == 0)
        {
            return new AnalyzerResult(signals);
        }

        var fingerprints = comparable.Select(a => new AnswerFingerprint
        {
            OrganizationId = context.Application.OrganizationId,
            ApplicationId = context.Application.Id,
            CandidateId = context.Candidate.Id,
            QuestionId = a.Answer.QuestionId,
            SimHash = TextSimilarity.SimHash(a.Shingles),
            WordCount = a.Count,
            CreatedAt = context.Now,
        }).ToList();
        var others = await records.GetOtherCandidatesFingerprintsAsync(context, cancellationToken);
        var candidates = comparable.Zip(fingerprints)
            .Select(pair => (pair.First.Answer, pair.First.Shingles, Near: others
                .Where(o => o.ApplicationId != context.Application.Id && TextSimilarity.HammingDistance(o.SimHash, pair.Second.SimHash) <= TextSimilarity.MaxHammingDistance)
                .ToList()))
            .Where(c => c.Near.Count > 0)
            .ToList();

        if (candidates.Count > 0)
        {
            var earlier = (await records.GetApplicationsAsync([.. candidates.SelectMany(c => c.Near).Select(n => n.ApplicationId).Distinct()], cancellationToken))
                .ToDictionary(e => e.ApplicationId);
            foreach (var (answer, shingles, near) in candidates)
            {
                if (NearDuplicate(answer, shingles, near, earlier) is { } signal)
                {
                    signals.Add(signal);
                }
            }
        }

        await records.KeepFingerprintsAsync(fingerprints, cancellationToken);
        return new AnalyzerResult(signals);
    }

    private static ObservedSignal? NearDuplicate(
        ApplicationText.FreeTextAnswer answer, HashSet<ulong> shingles, List<AnswerFingerprint> near, Dictionary<Guid, EarlierApplication> earlier)
    {
        var matches = new List<(EarlierApplication Application, string Text, int Shared, int Combined)>();
        foreach (var fingerprint in near)
        {
            if (!earlier.TryGetValue(fingerprint.ApplicationId, out var application)
                || application.Answers.FirstOrDefault(a => a.QuestionId == fingerprint.QuestionId)?.Value is not { } text)
            {
                continue;
            }

            var other = TextSimilarity.Shingles(TextSimilarity.Words(text));
            if (TextSimilarity.IsNearDuplicate(shingles, other))
            {
                var (shared, combined) = TextSimilarity.Overlap(shingles, other);
                matches.Add((application, text, shared, combined));
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        var closest = matches.OrderByDescending(m => (double)m.Shared / m.Combined).ThenBy(m => m.Application.SubmittedAt).First();
        var applications = matches.Select(m => m.Application.ApplicationId).Distinct().Count();
        return new ObservedSignal(
            SignalCatalogue.NearDuplicateAnswer,
            SignalConfidence.High,
            [
                SignalEvidence.Comparison(
                    "This answer and the closest earlier one",
                    new ComparedText($"This application: {answer.QuestionLabel}", Cap(answer.Text)),
                    new ComparedText($"Another applicant to {closest.Application.JobTitle}, {closest.Application.SubmittedAt:yyyy-MM-dd}", Cap(closest.Text))),
                SignalEvidence.Metric(
                    "Three-word sequences the two answers share", closest.Shared, "sequences", closest.Combined, "of all sequences in either answer"),
                SignalEvidence.Fact("Other applications with a near-identical answer", TextSimilarity.Count(applications)),
            ],
            new Dictionary<string, string> { ["question"] = $"\"{answer.QuestionLabel}\"" },
            [new CounterEvidence("Candidates who share a course, a template or a careers adviser can write alike. Compare the two answers yourself.")]);
    }

    private static IEnumerable<ObservedSignal> Templated(ApplicationText.FreeTextAnswer answer)
    {
        foreach (var (pattern, name) in ContentPatterns.TemplateText)
        {
            var match = pattern.Match(answer.Text);
            if (match.Success)
            {
                yield return new ObservedSignal(
                    SignalCatalogue.TemplatedAnswer,
                    SignalConfidence.Medium,
                    [SignalEvidence.Quote("Template text", match.Value.Trim(), $"Answer to \"{answer.QuestionLabel}\"")],
                    new Dictionary<string, string> { ["question"] = $"\"{answer.QuestionLabel}\"", ["pattern"] = name });
                yield break; // one per answer is enough to point a reviewer at it
            }
        }
    }

    private static string Cap(string text) => text.Length <= MaxQuotedLength ? text : text[..MaxQuotedLength] + "...";
}
