namespace ScreenHire.Domain.Verification;

/// <summary>
/// The groups of checks an organization may turn off (verification-engine.md §10). Every analyzer belongs to exactly one
/// group (<see cref="CheckGroups.Of"/>), so a new analyzer cannot escape a switch.
/// </summary>
public enum CheckGroup
{
    /// <summary>The applicant's IP address: hosting, proxy and location (<c>network</c>).</summary>
    Network,

    /// <summary>Email address and phone number (<c>identity</c>).</summary>
    Contact,

    /// <summary>Uploaded documents (<c>documents</c>).</summary>
    Documents,

    /// <summary>Written answers and the consistency check (<c>answers</c>, <c>consistency</c>).</summary>
    Answers,

    /// <summary>Another application from the same person (<c>duplicates</c>).</summary>
    Duplicates,

    /// <summary>How the form was filled in: automation markers and behaviour (<c>automation</c>, <c>behavioural</c>).</summary>
    FormUse,
}

public static class CheckGroups
{
    /// <summary>The analyzer names and their group. A unit test checks every registered analyzer is here.</summary>
    public static IReadOnlyDictionary<string, CheckGroup> ByAnalyzer { get; } = new Dictionary<string, CheckGroup>(StringComparer.Ordinal)
    {
        ["network"] = CheckGroup.Network,
        ["identity"] = CheckGroup.Contact,
        ["documents"] = CheckGroup.Documents,
        ["answers"] = CheckGroup.Answers,
        ["consistency"] = CheckGroup.Answers,
        ["duplicates"] = CheckGroup.Duplicates,
        ["automation"] = CheckGroup.FormUse,
        ["behavioural"] = CheckGroup.FormUse,
    };

    /// <summary>The group of an analyzer, or null for one this map does not know.</summary>
    public static CheckGroup? Of(string analyzerName) => ByAnalyzer.TryGetValue(analyzerName, out var group) ? group : null;

    /// <summary>The name shown to people ("Network checks").</summary>
    public static string Label(CheckGroup group) => group switch
    {
        CheckGroup.Network => "Network",
        CheckGroup.Contact => "Contact details",
        CheckGroup.Documents => "Documents",
        CheckGroup.Answers => "Answers",
        CheckGroup.Duplicates => "Duplicates",
        _ => "Form use",
    };

    /// <summary>
    /// What an analyzer run records when its group is off, so a reviewer who finds nothing from it knows the check did not
    /// run rather than ran clean.
    /// </summary>
    public static string TurnedOffNote(CheckGroup group) => $"{Label(group)} checks are turned off in this organization's settings.";
}

/// <summary>Why an analyzer's run says it did not look, or did not count something, because of the organization's settings.</summary>
public enum CheckNotRunKind
{
    /// <summary>The group is turned off in Settings, Verification.</summary>
    TurnedOff,

    /// <summary>A domain or network the organization trusts stopped a signal.</summary>
    Trusted,

    /// <summary>
    /// The AI checks were paused because the organization reached its monthly cap. The rule-based checks ran. Never a mark
    /// against the candidate.
    /// </summary>
    Paused,
}

/// <summary>
/// The notes an analyzer run keeps when the organization's settings changed what it did. One definition for the analyzers that
/// write them and the read model that shows them to reviewers, so a quiet result is never read as a clean one.
/// </summary>
public static class RunNotes
{
    public const string TrustedNetwork = "The address is on a network the organization trusts, so no network signal was raised.";

    public const string TrustedEmail = "A shared email address was not counted because its domain is on the organization's trusted list.";

    public const string TrustedDevice = "A shared device was not counted because it was used from a network on the organization's trusted list.";

    public const string TrustedEmailAndDevice = "A shared email address (trusted domain) and a shared device (trusted network) were not counted.";

    public const string DeviceNotCompared = "The device was not compared because form use checks are turned off in this organization's settings.";

    public const string TrustedEmailAndDeviceNotCompared = "A shared email address was not counted (trusted domain), and the device was not compared because form use checks are turned off.";

    /// <summary>What the AI consistency check records when the organization's monthly cap paused it. Says nothing about the candidate.</summary>
    public const string AiChecksPaused = "The AI checks were paused because this organization reached its monthly AI cap. This says nothing about the candidate. The other checks ran.";

    /// <summary>The name a paused AI check is shown under, in place of the check group's.</summary>
    public const string AiChecksLabel = "AI checks";

    /// <summary>The kind of settings-made note <paramref name="note"/> is, or null for any other note.</summary>
    public static CheckNotRunKind? KindOf(string analyzer, string? note)
    {
        if (note is null)
        {
            return null;
        }

        if (note == AiChecksPaused)
        {
            return CheckNotRunKind.Paused;
        }

        if (CheckGroups.Of(analyzer) is { } group && note == CheckGroups.TurnedOffNote(group))
        {
            return CheckNotRunKind.TurnedOff;
        }

        return note switch
        {
            DeviceNotCompared => CheckNotRunKind.TurnedOff,
            TrustedNetwork or TrustedEmail or TrustedDevice or TrustedEmailAndDevice or TrustedEmailAndDeviceNotCompared => CheckNotRunKind.Trusted,
            _ => null,
        };
    }
}
