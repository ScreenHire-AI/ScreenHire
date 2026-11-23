using ScreenHire.Application.Compliance;
using ScreenHire.Domain.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The public release notes for the checks (docs/compliance/checks-changelog.md) and the bell notice that points at them
/// (compliance gap map, CO-2): a new rules version cannot ship without its entry, and the notice runs for 14 days from the
/// date in the version, inside the bell's own retention, so the daily repeat never raises one the purge removed.
/// </summary>
public sealed class ChecksChangelogTests
{
    [Fact]
    public void The_current_rules_version_has_an_entry_in_the_changelog()
    {
        var changelog = File.ReadAllText(RepositoryFiles.PathTo("docs", "compliance", "checks-changelog.md"));

        changelog.Should().Contain($"\n## {VerificationRuleset.Version}\n",
            "a new rules version ships with a line saying what changed for reviewers (C.R.S. 6-1-1702(2))");
    }

    [Fact]
    public void The_notice_runs_for_two_weeks_after_the_versions_date()
    {
        var made = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

        ChecksChange.DateOf("2026-11-09.1").Should().Be(new DateOnly(2026, 11, 9));
        ChecksChange.DateOf("draft").Should().BeNull();
        ChecksChange.IsRecent("2026-11-09.1", made.AddDays(ChecksChange.NoticeDays)).Should().BeTrue();
        ChecksChange.IsRecent("2026-11-09.1", made.AddDays(ChecksChange.NoticeDays + 1)).Should().BeFalse();
        ChecksChange.IsRecent("draft", made).Should().BeFalse("a version without a date is never announced");
        ChecksChange.DateOf(VerificationRuleset.Version).Should().NotBeNull("the shipped version has the dated form the notice reads");
    }

    [Fact]
    public void The_notice_ends_before_the_bell_forgets_it()
    {
        ChecksChange.NoticeDays.Should().BeLessThan(new ScreenHire.Application.Notifications.NotificationOptions().RetentionDays);
    }
}
