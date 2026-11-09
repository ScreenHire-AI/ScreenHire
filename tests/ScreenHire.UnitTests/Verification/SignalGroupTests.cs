using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// A signal's check group comes from one map (<see cref="CheckGroups.ByAnalyzer"/>), through the analyzer that observes it.
/// The Verification tab, the ruleset's summary and "What was checked" all read it, so a code that no analyzer owns, or one
/// stored under an analyzer the catalogue does not name, would put a finding in the wrong group.
/// </summary>
public sealed class SignalGroupTests
{
    [Fact]
    public void Every_catalogue_code_belongs_to_a_known_analyzer_and_so_to_a_group()
    {
        foreach (var definition in SignalCatalogue.All.Values)
        {
            CheckGroups.ByAnalyzer.Should().ContainKey(definition.Analyzer, $"{definition.Code} names the analyzer that observes it");
            SignalCatalogue.GroupOf(definition.Code).Should().NotBeNull();
        }
    }

    [Fact]
    public void Every_group_says_what_it_checks_without_saying_what_it_means()
    {
        foreach (var group in Enum.GetValues<CheckGroup>())
        {
            CheckGroups.Checked(group).Should().NotBeNullOrWhiteSpace($"{group} lists its checks under What was checked");
            CheckGroups.Checked(group).Should().NotContainAny("fraud", "fake", "suspicious", "honest");
        }
    }

    [Fact]
    public async Task Stored_signals_carry_the_analyzer_the_catalogue_names_for_their_code()
    {
        var harness = new VerificationHarness();
        var seen = new HashSet<string>();
        foreach (var applicant in SyntheticApplicants.All)
        {
            var result = await harness.SubmitAsync(applicant);
            foreach (var signal in result.Signals)
            {
                seen.Add(signal.Code);
                signal.Source.Should().Be(SignalCatalogue.Get(signal.Code).Analyzer, $"{signal.Code} is observed by that analyzer");
                CheckGroups.OfSignal(signal).Should().Be(SignalCatalogue.GroupOf(signal.Code));
            }
        }

        seen.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("retired", "NET_RETIRED", SignalCategory.Network, CheckGroup.Network)]
    [InlineData("retired", "ID_RETIRED", SignalCategory.Identity, CheckGroup.Contact)]
    [InlineData("retired", "DOC_RETIRED", SignalCategory.Document, CheckGroup.Documents)]
    [InlineData("retired", "CNT_RETIRED", SignalCategory.Content, CheckGroup.Answers)]
    [InlineData("retired", "AUTO_RETIRED", SignalCategory.Automation, CheckGroup.FormUse)]
    [InlineData("retired", "BEH_RETIRED", SignalCategory.Behavioural, CheckGroup.FormUse)]
    public void A_stored_signal_from_a_retired_analyzer_takes_its_categorys_group(string source, string code, SignalCategory category, CheckGroup expected)
    {
        var signal = new VerificationSignal
        {
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Code = code,
            Category = category,
            Source = source,
            Explanation = "Explanation.",
        };
        CheckGroups.OfSignal(signal).Should().Be(expected);
    }

    [Theory]
    [InlineData(CheckGroup.Network, "network")]
    [InlineData(CheckGroup.Contact, "contact details")]
    [InlineData(CheckGroup.Documents, "documents")]
    [InlineData(CheckGroup.Answers, "answers")]
    [InlineData(CheckGroup.Duplicates, "duplicates")]
    [InlineData(CheckGroup.FormUse, "form use")]
    public void The_summary_names_a_group_as_a_phrase(CheckGroup group, string expected) =>
        CheckGroups.SentenceName(group).Should().Be(expected);
}
