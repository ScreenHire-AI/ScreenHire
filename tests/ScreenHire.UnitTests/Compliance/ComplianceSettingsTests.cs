using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Application.Compliance;
using ScreenHire.Application.Privacy;

namespace ScreenHire.UnitTests.Compliance;

/// <summary>
/// The compliance features (docs/research/compliance-gap-map.md) stay off until the owner turns each on after counsel's
/// review. The test stacks turn some on, so these pin the defaults a deployment gets with no configuration.
/// </summary>
public sealed class ComplianceSettingsTests
{
    [Fact]
    public void Every_compliance_feature_is_off_without_configuration()
    {
        var compliance = new ComplianceOptions();

        compliance.DecisionRecord.Enabled.Should().BeFalse("the decision record route is a 404 until the owner turns it on");
        compliance.ReviewRequests.Enabled.Should().BeFalse("the candidate's page offers no review request until the owner turns it on");
        new ChangeNoticeOptions().Enabled.Should().BeFalse("no bell notice about the checks until the owner turns it on");
        new RecordRetentionOptions().MinimumMonths.Should().BeNull("no retention floor until the owner sets one");
    }

    [Fact]
    public async Task The_change_notice_raises_nothing_while_it_is_off()
    {
        // Off, the handler returns before it reads anything, so it needs no database.
        var outgoing = await AnnounceChecksChangeHandler.Handle(
            new AnnounceChecksChange(),
            db: null!,
            Options.Create(new ChangeNoticeOptions()),
            TimeProvider.System,
            TestContext.Current.CancellationToken);

        outgoing.Should().BeEmpty();
    }
}
