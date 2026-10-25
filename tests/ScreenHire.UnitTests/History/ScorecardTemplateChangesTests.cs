using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;

namespace ScreenHire.UnitTests.History;

/// <summary>A library scorecard's history lines (change-history.md): created, renamed and its questions changed, matched by id.</summary>
public sealed class ScorecardTemplateChangesTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();

    [Fact]
    public void A_new_scorecard_says_where_it_was_copied_from()
    {
        var created = Template("Technical interview");

        Sentences(ScorecardTemplateChanges.Describe(null, created)).Should().Equal("created the scorecard");
        Sentences(ScorecardTemplateChanges.Describe(null, created, "Interview")).Should().Equal("created the scorecard from Interview");
    }

    [Fact]
    public void A_rename_and_changed_questions_are_one_line_each_with_the_questions_in_the_details()
    {
        var before = Template("Tech interview");
        var after = Template("Technical interview");
        after.Update(after.Name,
        [
            new("problem", ScorecardQuestionType.Rating10, "Problem solving", Required: true),
            new("notes", ScorecardQuestionType.LongText, "Notes on the exercise"),
        ], Monday);

        var lines = ScorecardTemplateChanges.Describe(before, after);

        Sentences(lines).Should().Equal("renamed the scorecard from Tech interview to Technical interview", "changed the questions");
        lines[1].Details.Should().BeEquivalentTo(
        [
            new ChangeDetail("Problem solving", "Problem solving. Rating, 1 to 5", "Problem solving. Rating, 1 to 10. required"),
            new ChangeDetail("Notes on the exercise", null, "Notes on the exercise. Long text"),
        ]);
    }

    [Fact]
    public void An_unchanged_save_says_nothing() =>
        ScorecardTemplateChanges.Describe(Template("Same"), Template("Same")).Should().BeEmpty();

    private static ScorecardTemplate Template(string name) =>
        ScorecardTemplate.Create(Organization, name, [new("problem", ScorecardQuestionType.Rating5, "Problem solving")], Monday);

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(l => l.Sentence());
}
