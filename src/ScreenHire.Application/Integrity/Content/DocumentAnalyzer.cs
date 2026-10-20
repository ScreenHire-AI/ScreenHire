using System.Globalization;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity.Documents;

namespace ScreenHire.Application.Integrity.Content;

/// <summary>
/// Document forensics on the uploaded files (integrity-engine.md §10.10), from what extraction stored:
/// <c>CNT_RESUME_GENERATOR</c> when the file's metadata names a resume builder or an automatic PDF tool, with how soon
/// before submission the file was made; and <c>CNT_PROMPT_INJECTION</c> when a file hides text from people or addresses
/// a model or screening tool.
/// </summary>
/// <remarks>
/// Not ready until every file of the application has been read (<see cref="ExtractApplicationDocument"/>), which then
/// publishes <see cref="AnalyzeApplicationIntegrity"/> again.
/// </remarks>
public sealed class DocumentAnalyzer(IContentRecords records) : IIntegrityAnalyzer
{
    public const string NotReadNote = "Waiting for the uploaded files to be read.";

    /// <summary>A file made this soon before the application was sent is noted with the generator.</summary>
    public static readonly TimeSpan RecentCreation = TimeSpan.FromMinutes(15);

    private const int MaxQuotedSpans = 3;

    public string Name => "documents";

    public int Version => 1;

    public async Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Files.Count == 0)
        {
            return AnalyzerResult.Nothing("No files were uploaded.");
        }

        var documents = (await records.GetDocumentsAsync(context.Application.Id, cancellationToken))
            .Where(d => d.ExtractorVersion == DocumentExtraction.Version)
            .ToList();
        if (context.Files.Any(f => documents.All(d => d.FileId != f.Id)))
        {
            return AnalyzerResult.NotReady(NotReadNote);
        }

        var signals = new List<ObservedSignal>();
        foreach (var document in documents.Where(d => d.Status == DocumentExtractionStatus.Extracted).OrderBy(d => d.Kind).ThenBy(d => d.FileId))
        {
            if (Generator(document, context) is { } generator)
            {
                signals.Add(generator);
            }

            if (Injection(document) is { } injection)
            {
                signals.Add(injection);
            }
        }

        var notRead = documents.Count(d => d.Status != DocumentExtractionStatus.Extracted);
        return new AnalyzerResult(signals, notRead == 0 ? null : $"{Count(notRead)} of the uploaded files could not be read.");
    }

    private static ObservedSignal? Generator(ApplicationDocument document, IntegrityContext context)
    {
        if (ContentPatterns.FindGenerator(document.Metadata.Tools()) is not var (generator, tool))
        {
            return null;
        }

        var evidence = new List<SignalEvidence> { SignalEvidence.Fact($"Tool named in the {ApplicationText.Describe(document.Kind)} file", tool) };
        var recent = false;
        if (document.Metadata.CreatedAt is { } created)
        {
            var before = context.Application.SubmittedAt - created;
            if (before >= TimeSpan.Zero && before <= RecentCreation)
            {
                recent = true;
                evidence.Add(SignalEvidence.Metric(
                    "Time from the file being created to the application being sent",
                    Math.Round((decimal)before.TotalMinutes, 1), "minutes", (decimal)RecentCreation.TotalMinutes, "noted when under"));
            }
        }

        return new ObservedSignal(
            SignalCatalogue.ResumeGenerator,
            recent ? SignalConfidence.Medium : SignalConfidence.Low,
            evidence,
            new Dictionary<string, string> { ["tool"] = generator.Describe() },
            [new CounterEvidence("Resume builders and export tools are widely used by genuine applicants, and the file's details are easy to change.")]);
    }

    private static ObservedSignal? Injection(ApplicationDocument document)
    {
        var addressed = document.HiddenText.Where(s => ContentPatterns.Instructions.IsMatch(s.Text)).ToList();
        var visibleInstruction = ContentPatterns.Instructions.Match(document.Text);
        var hiddenLength = document.HiddenText.Sum(s => s.Text.Length);
        if (addressed.Count == 0 && !visibleInstruction.Success && hiddenLength < 20)
        {
            return null;
        }

        var what = ApplicationText.Describe(document.Kind);
        var evidence = new List<SignalEvidence>();
        foreach (var span in addressed.Concat(document.HiddenText.Except(addressed)).Take(MaxQuotedSpans))
        {
            evidence.Add(SignalEvidence.Quote(Capitalize(Reason(span.Reason)), span.Text, $"{Capitalize(what)}, page {span.Page.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (visibleInstruction.Success)
        {
            evidence.Add(SignalEvidence.Quote("Text addressed to a model or screening tool", Sentence(document.Text, visibleInstruction.Index), Capitalize(what)));
        }

        if (document.HiddenText.Count > 0)
        {
            evidence.Add(SignalEvidence.Metric("Hidden passages", document.HiddenText.Count, "passages"));
        }

        var instructions = addressed.Count > 0 || visibleInstruction.Success;
        return new ObservedSignal(
            SignalCatalogue.PromptInjection,
            instructions ? SignalConfidence.High : SignalConfidence.Medium,
            evidence,
            new Dictionary<string, string>
            {
                ["document"] = what,
                ["detail"] = instructions
                    ? "instructions addressed to an AI or screening tool"
                    : $"text people cannot see ({string.Join(", ", document.HiddenText.Select(s => Reason(s.Reason)).Distinct())})",
            },
            instructions ? null : [new CounterEvidence("Some templates and export tools leave invisible text behind. Read the quoted text before drawing a conclusion.")],
            Weight: instructions ? SignalWeight.High : SignalWeight.Medium);
    }

    private static string Reason(HiddenTextReason reason) => reason switch
    {
        HiddenTextReason.WhiteText => "white text on a white page",
        HiddenTextReason.TinyText => "text too small to read",
        HiddenTextReason.OffPage => "text placed off the page",
        _ => "text marked invisible",
    };

    // The sentence or line around a match, at most 300 characters, for a quote a reviewer can find in the file.
    private static string Sentence(string text, int index)
    {
        var start = Math.Max(text.LastIndexOfAny(['.', '\n'], Math.Max(0, index - 1)) + 1, index - 150);
        var end = text.IndexOfAny(['.', '\n'], index);
        end = end < 0 ? text.Length : Math.Min(end + 1, text.Length);
        end = Math.Min(end, start + 300);
        return text[start..end].Trim();
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
}
