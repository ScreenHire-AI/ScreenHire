using System.Text.RegularExpressions;

namespace ScreenHire.Application.Integrity.Content;

/// <summary>
/// The fixed text patterns content forensics look for (integrity-engine.md §10.10). Each is a plain observation a
/// reviewer can check against the quoted text; none says anything about a person. Bump the analyzer that uses a list
/// when the list changes.
/// </summary>
public static partial class ContentPatterns
{
    /// <summary>
    /// Tools that make resumes for people, matched against a file's producer and creator. Resume builders are common
    /// and legitimate; they are context for the AI-assistance picture, never a reason on their own.
    /// </summary>
    public static IReadOnlyList<KnownGenerator> Generators { get; } =
    [
        Builder("Kickresume"), Builder("Resume.io"), Builder("Zety"), Builder("Enhancv"), Builder("Novoresume"),
        Builder("Rezi"), Builder("Teal", "Teal HQ"), Builder("FlowCV"), Builder("Resume Genius"), Builder("LiveCareer"),
        Builder("MyPerfectResume"), Builder("Resume Worded"), Builder("Jobscan"), Builder("Huntr"),
        Builder("Reactive Resume"), Builder("VisualCV"), Builder("Canva"),
        Automatic("wkhtmltopdf"), Automatic("HeadlessChrome", "headless Chrome"), Automatic("Puppeteer"),
        Automatic("Playwright"), Automatic("WeasyPrint"), Automatic("ReportLab"), Automatic("jsPDF"), Automatic("pdfmake"),
        Automatic("PDFKit"),
    ];

    /// <summary>
    /// Wording addressed to a model or screening tool rather than to a person. Matched in hidden text and visible text
    /// alike: a resume has no reason to address a machine.
    /// </summary>
    public static Regex Instructions { get; } = InstructionPattern();

    /// <summary>Text a writing tool or template leaves behind, and what a reviewer should call it.</summary>
    public static IReadOnlyList<(Regex Pattern, string Name)> TemplateText { get; } =
    [
        (PlaceholderPattern(), "a placeholder in square brackets"),
        (ChatbotPattern(), "a chatbot's reply wording"),
    ];

    /// <summary>The first known generator named in <paramref name="tools"/>, and the tool string it matched.</summary>
    public static (KnownGenerator Generator, string Tool)? FindGenerator(IEnumerable<string> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
        {
            foreach (var generator in Generators)
            {
                if (Regex.IsMatch(tool, $@"(?<![A-Za-z]){Regex.Escape(generator.Match)}(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    return (generator, tool);
                }
            }
        }

        return null;
    }

    private static KnownGenerator Builder(string match, string? name = null) =>
        new(match, name ?? match, GeneratorKind.ResumeBuilder);

    private static KnownGenerator Automatic(string match, string? name = null) =>
        new(match, name ?? match, GeneratorKind.Automated);

    [GeneratedRegex(
        @"\b(?:ignore|disregard|forget)\s+(?:all\s+|any\s+)?(?:the\s+|your\s+)?(?:previous|prior|above|earlier|preceding|other)\s+(?:instructions|prompts|directions|context)\b"
        + @"|\byou\s+are\s+(?:now\s+)?(?:an?\s+)?(?:ai|assistant|language\s+model|llm|chatgpt|gpt|recruit(?:er|ing)\s+(?:bot|assistant|ai)|screening\s+(?:tool|system|bot|ai))\b"
        + @"|\b(?:note|message|instructions?)\s+(?:to|for)\s+(?:the\s+|any\s+)?(?:ai|llm|chatgpt|gpt|language\s+model|ats|screening\s+(?:tool|system|bot))\b"
        + @"|\bsystem\s+prompt\b"
        + @"|\b(?:rank|rate|score|recommend|mark|consider|select|shortlist)\s+(?:this|the)\s+(?:candidate|applicant|resume|cv)\b[^.\n]{0,60}?\b(?:highly|top|first|best|excellent|perfect|strong(?:ly)?|most\s+qualified|for\s+hire|for\s+interview)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InstructionPattern();

    [GeneratedRegex(
        @"\[(?:your|insert|company|employer|hiring\s+manager|job\s+title|position|role|name|team)\b[^\]\n]{0,40}\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(
        @"^\s*(?:certainly|sure|absolutely|of\s+course)[!,.]?\s+here(?:'s|\s+is)\b"
        + @"|\bas\s+an\s+ai(?:\s+language)?\s+model\b"
        + @"|\bI\s+hope\s+this\s+helps\b"
        + @"|\bfeel\s+free\s+to\s+(?:adjust|customi[sz]e|tailor|modify)\b"
        + @"|\bhere(?:'s|\s+is)\s+(?:a|an|your)\s+(?:revised|polished|refined|draft|improved|tailored)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ChatbotPattern();
}

/// <summary>A tool that makes resumes. <see cref="Match"/> is what its producer string contains.</summary>
public sealed record KnownGenerator(string Match, string Name, GeneratorKind Kind)
{
    /// <summary>How the tool reads in an explanation: "Kickresume, a resume builder".</summary>
    public string Describe() => Kind == GeneratorKind.ResumeBuilder
        ? $"{Name}, a resume builder"
        : $"{Name}, a tool that produces PDF files automatically, usually from a web page or a program";
}

public enum GeneratorKind
{
    ResumeBuilder,

    /// <summary>A library or headless browser that writes PDFs from code.</summary>
    Automated,
}
