using ScreenHire.Api.Applications;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The Verification tab's map: a group is Found only for an authenticity signal that counts toward the level. AI help and
/// context that never counts are information only, and the application's source alone says a group had nothing to read.
/// </summary>
public sealed class VerificationGroupsTests
{
    private static VerificationSignalView Signal(CheckGroup group, bool counts = false) => new(
        Guid.NewGuid(), "CODE", "Title", SignalCategory.Network, SignalWeight.Medium, SignalConfidence.High, counts, false, false,
        "Explanation.", [], [], "network", 1, DateTimeOffset.UnixEpoch, false, null, null, group);

    private static Dictionary<CheckGroup, CheckGroupState> States(
        VerificationSignalView[] authenticity, VerificationSignalView[] ai, CheckNotRun[]? notRun = null, ApplicationSource? source = ApplicationSource.CareersSite) =>
        VerificationGroups.Of(authenticity, ai, notRun ?? [], source).ToDictionary(g => g.Group, g => g.State);

    [Fact]
    public void Only_an_authenticity_signal_that_counts_makes_a_group_Found()
    {
        var states = States(
            [Signal(CheckGroup.Network, counts: true), Signal(CheckGroup.Contact)],
            [Signal(CheckGroup.Documents, counts: true), Signal(CheckGroup.Answers)]);

        states[CheckGroup.Network].Should().Be(CheckGroupState.Found);
        states[CheckGroup.Contact].Should().Be(CheckGroupState.InformationOnly, "a context signal never counts");
        states[CheckGroup.Documents].Should().Be(CheckGroupState.InformationOnly, "AI help is information, even when it counts toward its own level");
        states[CheckGroup.Answers].Should().Be(CheckGroupState.InformationOnly);
        states[CheckGroup.Duplicates].Should().Be(CheckGroupState.NothingFound);
    }

    [Fact]
    public void A_hand_added_application_has_Network_and_Form_use_not_checked_and_a_careers_one_never_does()
    {
        var handAdded = States([], [], source: ApplicationSource.Recruiter);
        var careers = States([], [], source: ApplicationSource.CareersSite);

        handAdded[CheckGroup.Network].Should().Be(CheckGroupState.NotChecked);
        handAdded[CheckGroup.FormUse].Should().Be(CheckGroupState.NotChecked);
        handAdded[CheckGroup.Contact].Should().Be(CheckGroupState.NothingFound);
        careers.Values.Should().OnlyContain(state => state == CheckGroupState.NothingFound, "nothing tells an assistive-technology applicant apart");
    }

    [Fact]
    public void A_paused_AI_check_is_Paused_not_Nothing_found_and_a_finding_still_wins_over_it()
    {
        var paused = new CheckNotRun(CheckGroup.Answers, RunNotes.AiChecksLabel, CheckNotRunKind.Paused, RunNotes.AiChecksPaused);

        var states = States([], [], [paused]);
        states[CheckGroup.Answers].Should().Be(CheckGroupState.Paused, "nothing found while the AI check did not run must not read as clean");
        states[CheckGroup.Network].Should().Be(CheckGroupState.NothingFound);
        VerificationGroups.Of([], [], [paused], ApplicationSource.CareersSite).Single(g => g.Group == CheckGroup.Answers).Note.Should().Be(RunNotes.AiChecksPaused);

        States([Signal(CheckGroup.Answers, counts: true)], [], [paused])[CheckGroup.Answers].Should().Be(CheckGroupState.Found);
    }

    [Fact]
    public void A_group_is_Turned_off_only_by_its_own_note_and_findings_win_over_it()
    {
        var off = new CheckNotRun(CheckGroup.Network, "Network", CheckNotRunKind.TurnedOff, CheckGroups.TurnedOffNote(CheckGroup.Network));
        var partial = new CheckNotRun(CheckGroup.Duplicates, "Duplicates", CheckNotRunKind.TurnedOff, RunNotes.DeviceNotCompared);

        var states = States([], [], [off, partial]);
        states[CheckGroup.Network].Should().Be(CheckGroupState.TurnedOff);
        states[CheckGroup.Duplicates].Should().Be(CheckGroupState.NothingFound, "its own checks still ran");

        States([Signal(CheckGroup.Network, counts: true)], [], [off])[CheckGroup.Network].Should().Be(CheckGroupState.Found);
    }

}
