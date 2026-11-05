using Microsoft.Extensions.Options;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Application.Distribution.Feeds;
using ScreenHire.Application.Distribution.GoogleIndexing;

namespace ScreenHire.UnitTests.Distribution;

/// <summary>Which boards can be sent text of their own (Customize).</summary>
public sealed class BoardCapabilitiesTests
{
    [Theory]
    [InlineData(BoardMode.Hosted, false)]
    [InlineData(BoardMode.Notify, false)]
    [InlineData(BoardMode.Pull, true)]
    [InlineData(BoardMode.PullFromAts, true)]
    [InlineData(BoardMode.Push, true)]
    public void Only_boards_that_are_not_our_own_page_or_a_notification_take_text_of_their_own(BoardMode mode, bool canCustomize)
    {
        new BoardCapabilities(mode, true, true, true, true, null, null).CanCustomize.Should().Be(canCustomize);
    }

    [Fact]
    public void Every_mode_is_covered_above()
    {
        Enum.GetValues<BoardMode>().Should().BeEquivalentTo(
            [BoardMode.Hosted, BoardMode.Notify, BoardMode.Pull, BoardMode.PullFromAts, BoardMode.Push],
            "a new mode needs a decision about customizing it");
    }

    [Fact]
    public void The_careers_page_and_google_cannot_be_customized_and_the_feeds_the_job_board_api_and_a_push_board_can()
    {
        var time = TimeProvider.System;
        var options = Options.Create(new DistributionOptions());

        new CareersPageAdapter().Capabilities.CanCustomize.Should().BeFalse("it is the job's own page");
        new GoogleIndexingAdapter(null!, time, options).Capabilities.CanCustomize.Should().BeFalse("Google reads the careers page");
        new CareerjetFeedAdapter(time).Capabilities.CanCustomize.Should().BeTrue();
        new AdzunaFeedAdapter(time).Capabilities.CanCustomize.Should().BeTrue();
        new JobBoardApiAdapter(time).Capabilities.CanCustomize.Should().BeTrue();
        new SandboxBoardAdapter(null!, time, options).Capabilities.CanCustomize.Should().BeTrue("a push board is sent the text");
    }
}
