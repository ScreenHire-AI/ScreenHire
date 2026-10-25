using System.Text.RegularExpressions;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// History lines name things with the dashboard's words. These label maps live in TypeScript and C#; this test fails
/// when one side changes without the other, so change both together.
/// </summary>
public sealed partial class ChangeLabelsTests
{
    private static readonly string Workflow = File.ReadAllText(RepositoryFiles.PathTo("apps", "dashboard", "src", "lib", "workflow.ts"));
    private static readonly string Format = File.ReadAllText(RepositoryFiles.PathTo("apps", "dashboard", "src", "lib", "format.ts"));

    [Fact]
    public void Question_types_match_the_dashboard() =>
        Map(Workflow, "questionTypeLabels").Should().BeEquivalentTo(Enum.GetValues<QuestionType>().ToDictionary(t => t.ToString(), ChangeLabels.Question));

    [Fact]
    public void Scorecard_answer_kinds_match_the_dashboard() =>
        Map(Workflow, "scorecardQuestionTypeLabels").Should().BeEquivalentTo(
            Enum.GetValues<ScorecardQuestionType>().ToDictionary(t => t.ToString(), ChangeLabels.ScorecardKind));

    [Fact]
    public void Stage_kinds_match_the_dashboard() =>
        Map(Workflow, "stageKindLabels").Should().BeEquivalentTo(Enum.GetValues<StageKind>().ToDictionary(k => k.ToString(), ChangeLabels.Stage));

    [Fact]
    public void Triggers_and_actions_the_dashboard_offers_match()
    {
        foreach (var (key, label) in Map(Workflow, "triggerLabels"))
        {
            ChangeLabels.Trigger(Enum.Parse<TriggerType>(key)).Should().Be(label);
        }

        foreach (var (key, label) in Map(Workflow, "actionLabels"))
        {
            ChangeLabels.Action(Enum.Parse<ActionType>(key)).Should().Be(label);
        }
    }

    [Fact]
    public void Ai_use_is_the_name_before_the_dashboards_explanation()
    {
        foreach (var (key, label) in Map(Workflow, "aiUseLabels"))
        {
            ChangeLabels.AiUse(Enum.Parse<AiUsePolicy>(key)).Should().Be(label.Split(':')[0]);
        }
    }

    [Fact]
    public void Employment_types_match_the_dashboard() =>
        Map(Format, "employmentLabels").Should().BeEquivalentTo(Enum.GetValues<EmploymentType>().ToDictionary(t => t.ToString(), ChangeLabels.Employment));

    // `export const name: Record<...> = { Key: 'Label', ... };` as a dictionary.
    private static Dictionary<string, string> Map(string source, string name)
    {
        var start = source.IndexOf($"const {name}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the dashboard defines {name}");
        var body = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];
        return Entry().Matches(body).ToDictionary(m => m.Groups["key"].Value, m => m.Groups["label"].Value);
    }

    [GeneratedRegex("""^\s+(?<key>\w+): (['"])(?<label>.+?)\1,?\s*$""", RegexOptions.Multiline)]
    private static partial Regex Entry();
}
