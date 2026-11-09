using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Api.Applications;

/// <summary>
/// The Verification tab's map: for each of the six groups of checks, whether it found something that counts toward the level,
/// only information, nothing, could not look, or was turned off (docs/design/design-system.md section 16). It reads the
/// application's signals and what the organization's settings kept the checks from doing, and nothing about the person.
/// </summary>
internal static class VerificationGroups
{
    /// <summary>
    /// The groups that read the careers form: a candidate a recruiter added by hand had no network address, session or form
    /// telemetry, so Network and Form use had nothing to look at. A careers applicant is never marked this way, so an
    /// assistive-technology opt-out is not told apart (CLAUDE.md non-negotiable 4).
    /// </summary>
    private const string AddedByHand = "Added by hand, so there was no careers form for these checks to read.";

    private static readonly CheckGroup[] NeedingTheForm = [CheckGroup.Network, CheckGroup.FormUse];

    public static List<CheckGroupView> Of(
        IReadOnlyList<VerificationSignalView> authenticity,
        IReadOnlyList<VerificationSignalView> aiAssistance,
        IReadOnlyList<CheckNotRun> notRun,
        ApplicationSource? source) =>
    [
        .. Enum.GetValues<CheckGroup>().Select(group =>
        {
            // Only an authenticity signal can count toward the level the candidate shows. AI help is information, and so is
            // context that never counts.
            var found = authenticity.Count(s => s.Group == group && s.CountsTowardLevel);
            var notes = authenticity.Count(s => s.Group == group && !s.CountsTowardLevel) + aiAssistance.Count(s => s.Group == group);
            var groupNotes = notRun.Where(n => n.Group == group).ToList();
            // Off means the group did not run. A partial note (Duplicates did not compare the device because Form use is off, or a
            // trusted domain stopped a signal) leaves the group's own checks running, so it is only a note.
            var turnedOff = groupNotes.FirstOrDefault(n => n.Note == CheckGroups.TurnedOffNote(group));
            var state = found > 0 ? CheckGroupState.Found
                : notes > 0 ? CheckGroupState.InformationOnly
                : turnedOff is not null ? CheckGroupState.TurnedOff
                : source == ApplicationSource.Recruiter && NeedingTheForm.Contains(group) ? CheckGroupState.NotChecked
                : CheckGroupState.NothingFound;
            var note = state == CheckGroupState.NotChecked ? AddedByHand : (turnedOff ?? groupNotes.FirstOrDefault())?.Note;
            return new CheckGroupView(group, CheckGroups.Label(group), state, found, notes, CheckGroups.Checked(group), note);
        }),
    ];
}
