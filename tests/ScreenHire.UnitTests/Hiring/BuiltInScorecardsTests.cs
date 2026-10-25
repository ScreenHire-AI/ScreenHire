using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>ScreenHire's built-in scorecards: valid, well named, and the defaults the default workflow copies.</summary>
public sealed class BuiltInScorecardsTests
{
    [Fact]
    public void Every_built_in_is_a_valid_scorecard()
    {
        foreach (var builtIn in BuiltInScorecards.All)
        {
            DefinitionValidator.ValidateScorecardQuestions(builtIn.Questions, builtIn.Id).Should().BeEmpty(builtIn.Id);
            builtIn.Name.Length.Should().BeLessThanOrEqualTo(ScorecardTemplate.MaxNameLength);
        }
    }

    [Fact]
    public void Ids_are_unique_and_can_never_be_mistaken_for_an_own_scorecard()
    {
        BuiltInScorecards.All.Select(b => b.Id).Should().OnlyHaveUniqueItems();
        foreach (var id in BuiltInScorecards.All.Select(b => b.Id))
        {
            Guid.TryParse(id, out _).Should().BeFalse(id);
            id.Should().MatchRegex("^[a-z][a-z0-9-]{0,39}$");
        }
    }

    [Theory]
    [InlineData("screening", true)]
    [InlineData("hourly-interview", true)]
    [InlineData("0199e8a0-0000-7000-8000-000000000001", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("Screening", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_key_is_a_built_in_id_or_a_uuid(string? key, bool wellFormed) =>
        BuiltInScorecards.IsWellFormed(key).Should().Be(wellFormed);
}
