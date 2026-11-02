using System.Text.RegularExpressions;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// Erasing a candidate deletes their applications while messages about them may still be queued. A handler that finds its
/// application gone must treat the work as done: throwing "is not visible" retries the message into the dead-letter table
/// for a candidate nobody can bring back. The handlers' behaviour needs Postgres and is covered by the integration tests;
/// this scan only keeps the old throw from coming back in the files that were changed to return instead.
/// </summary>
public sealed partial class ErasedApplicationHandlersTests
{
    private static readonly string[] HandlerFiles =
    [
        "src/ScreenHire.Application/Applications/ApplicationSubmitted.cs",
        "src/ScreenHire.Application/Applications/ApplicationMoved.cs",
        "src/ScreenHire.Application/Applications/ScanApplicationFile.cs",
        "src/ScreenHire.Application/Verification/Content/ContentMessages.cs",
        "src/ScreenHire.Application/Verification/VerificationPipeline.cs",
        "src/ScreenHire.Application/Automations/RunAutomationsHandler.cs",
        "src/ScreenHire.Application/Automations/ExecuteAutomationActionHandler.cs",
        "src/ScreenHire.Application/Automations/AutomationFailures.cs",
    ];

    [Fact]
    public void Handlers_of_an_application_do_not_throw_when_it_is_not_visible()
    {
        var offenders = Offenders(HandlerFiles);

        offenders.Should().BeEmpty("an erased application is finished work, not a failure to retry");
    }

    [Fact]
    public void The_scan_finds_the_throw_it_guards_against()
    {
        // JobClosed still throws for a job that is not visible, so the pattern must see it.
        Offenders(["src/ScreenHire.Application/Jobs/JobClosed.cs"]).Should().ContainSingle();
    }

    private static List<string> Offenders(IEnumerable<string> files) =>
        [.. files.Where(file => NotVisibleThrow().IsMatch(File.ReadAllText(RepositoryFiles.PathTo(file.Split('/')))))];

    [GeneratedRegex(@"throw new InvalidOperationException\([^;]*is not visible")]
    private static partial Regex NotVisibleThrow();
}
