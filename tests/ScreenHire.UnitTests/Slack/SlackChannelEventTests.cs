using ScreenHire.Domain.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// The events a channel can get, pinned: adding one is a decision, and a verification event must never be one of them,
/// because levels post only as a link to the candidate (ADR-0004, docs/architecture/slack.md).
/// </summary>
public sealed class SlackChannelEventTests
{
    [Fact]
    public void A_channel_gets_exactly_the_four_hiring_events_and_no_verification_event()
    {
        Enum.GetNames<SlackChannelEvent>().Should().Equal("ApplicationSubmitted", "ApplicationMoved", "AutomationFailed", "JobPublished");
    }
}
