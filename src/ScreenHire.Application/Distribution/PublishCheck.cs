using System.Text.RegularExpressions;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Application.Distribution;

/// <summary>What a publish check found, so a person can tell one problem from another.</summary>
public enum PublishCheckKind
{
    /// <summary>The description still holds a starting template's fill-in prompt.</summary>
    TemplatePrompt,

    /// <summary>An on-site or hybrid job has no city, and a chosen board needs one.</summary>
    NoCity,

    /// <summary>The job has no salary, and a chosen board needs one.</summary>
    NoSalary,

    /// <summary>The description is shorter than a chosen board accepts.</summary>
    ShortDescription,
}

/// <summary>The field of Edit details that fixes a problem.</summary>
public enum PublishCheckField
{
    Description,
    City,
    Salary,
}

/// <summary>One thing to check before a job goes live: a sentence, the field that fixes it and the boards it matters to.</summary>
public sealed record PublishCheckItem(PublishCheckKind Kind, PublishCheckField Field, string Text, IReadOnlyList<string> Boards);

/// <summary>
/// The warnings shown before a job goes live (docs/architecture/job-distribution.md, "Check before publishing"). They warn
/// and never block: publishing is not refused for any of them. The board rules are each adapter's
/// <see cref="BoardCapabilities.Requirements"/>, the same data its <c>Validate</c> reads.
/// </summary>
public static class PublishChecks
{
    /// <summary>
    /// What to check on <paramref name="job"/> before it goes to <paramref name="boards"/>. Each board comes with the content
    /// it would receive, because a customized board has its own description. <paramref name="templatePrompts"/> are the
    /// prompts of the built-in templates (<see cref="TemplatePrompts.From"/>).
    /// </summary>
    public static IReadOnlyList<PublishCheckItem> Run(
        JobSnapshot job,
        IReadOnlyList<(IJobBoardAdapter Board, JobSnapshot Content)> boards,
        IReadOnlyCollection<string> templatePrompts)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(boards);
        ArgumentNullException.ThrowIfNull(templatePrompts);
        var items = new List<PublishCheckItem>();

        var text = Checks.PlainText(job.Description);
        var left = templatePrompts.Where(prompt => text.Contains(prompt, StringComparison.Ordinal)).ToList();
        if (left.Count > 0)
        {
            var more = left.Count > 1 ? $" and {left.Count - 1} more" : "";
            items.Add(new(PublishCheckKind.TemplatePrompt, PublishCheckField.Description,
                $"The description still has the template's fill-in text, \"{left[0]}\"{more}.", []));
        }

        var problems = boards
            .SelectMany(b => Checks.Problems(b.Board.Capabilities.Needs, b.Content).Select(problem => (b.Board, problem)))
            .ToList();

        var tooShort = problems.Where(p => p.problem == RequirementProblem.ShortDescription)
            .Select(p => p.Board).OrderByDescending(b => b.Capabilities.Needs.MinDescriptionLength).ThenBy(b => b.Name).ToList();
        if (tooShort.Count > 0)
        {
            var strictest = tooShort[0];
            var minimum = strictest.Capabilities.Needs.MinDescriptionLength;
            items.Add(new(PublishCheckKind.ShortDescription, PublishCheckField.Description,
                tooShort.Count == 1
                    ? $"{strictest.Name} needs a description of at least {minimum} characters."
                    : $"{Names(tooShort)} need a longer description, up to {minimum} characters for {strictest.Name}.",
                [.. tooShort.Select(b => b.Name)]));
        }

        AddGroup(items, problems, RequirementProblem.NoCity, PublishCheckKind.NoCity, PublishCheckField.City, "a city for an on-site or hybrid job");
        AddGroup(items, problems, RequirementProblem.NoSalary, PublishCheckKind.NoSalary, PublishCheckField.Salary, "a salary");
        return items;
    }

    private static void AddGroup(
        List<PublishCheckItem> items,
        List<(IJobBoardAdapter Board, RequirementProblem problem)> problems,
        RequirementProblem problem,
        PublishCheckKind kind,
        PublishCheckField field,
        string needs)
    {
        var named = problems.Where(p => p.problem == problem).Select(p => p.Board).OrderBy(b => b.Name).ToList();
        if (named.Count > 0)
        {
            items.Add(new(kind, field, $"{Names(named)} {(named.Count == 1 ? "needs" : "need")} {needs}.", [.. named.Select(b => b.Name)]));
        }
    }

    private static string Names(IReadOnlyList<IJobBoardAdapter> boards)
    {
        var names = boards.Select(b => b.Name).ToList();
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}

/// <summary>The fill-in prompts of the built-in templates, which a job made from one keeps until someone writes over them.</summary>
public static partial class TemplatePrompts
{
    /// <summary>
    /// The prompts in <paramref name="descriptions"/>, the posting descriptions of the built-in template versions: each
    /// paragraph's text with tags removed and white space collapsed. Headings are not prompts ("About the role" is a
    /// good heading to keep). Compared exactly: no fuzzy guess.
    /// </summary>
    public static IReadOnlyList<string> From(IEnumerable<string?> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        return [.. descriptions
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .SelectMany(description => Paragraph().Matches(description!).Select(m => Checks.PlainText(m.Groups[1].Value)))
            .Where(prompt => prompt.Length > 0)
            .Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex("<p(?:\\s[^>]*)?>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Paragraph();
}
