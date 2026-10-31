using ScreenHire.Application.Slack;
using ScreenHire.Domain.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// Which of Slack's error codes mean a retry is pointless and what the settings page says about them; everything else
/// is worth a retry by the worker.
/// </summary>
public sealed class SlackErrorsTests
{
    [Theory]
    [InlineData("token_revoked", SlackProblem.Disconnected)]
    [InlineData("invalid_auth", SlackProblem.Disconnected)]
    [InlineData("account_inactive", SlackProblem.Disconnected)]
    [InlineData("app_uninstalled", SlackProblem.Disconnected)]
    [InlineData("channel_not_found", SlackProblem.ChannelNotFound)]
    [InlineData("not_in_channel", SlackProblem.NotInChannel)]
    [InlineData("is_archived", SlackProblem.ChannelArchived)]
    [InlineData("restricted_action", SlackProblem.CannotPost)]
    [InlineData("restricted_action_read_only_channel", SlackProblem.CannotPost)]
    [InlineData("restricted_action_thread_only_channel", SlackProblem.CannotPost)]
    [InlineData("no_permission", SlackProblem.CannotPost)]
    [InlineData("user_not_found", SlackProblem.CannotMessage)]
    [InlineData("cannot_dm_bot", SlackProblem.CannotMessage)]
    public void A_refusal_a_retry_cannot_fix_has_its_problem(string code, SlackProblem expected) =>
        SlackErrors.ProblemFor(code).Should().Be(expected);

    [Theory]
    [InlineData("ratelimited")]
    [InlineData("internal_error")]
    [InlineData("service_unavailable")]
    [InlineData("http_503")]
    [InlineData("timeout")]
    [InlineData("unreachable")]
    [InlineData("a_code_slack_adds_next_year")]
    [InlineData("")]
    public void Anything_that_may_work_on_a_retry_has_no_problem(string code) =>
        SlackErrors.ProblemFor(code).Should().BeNull();

    [Fact]
    public void A_refused_direct_message_in_a_channel_is_a_channel_that_cannot_be_posted_in() =>
        SlackErrors.InChannel(SlackProblem.CannotMessage).Should().Be(SlackProblem.CannotPost);

    [Theory]
    [InlineData(SlackProblem.Disconnected)]
    [InlineData(SlackProblem.ChannelNotFound)]
    [InlineData(SlackProblem.NotInChannel)]
    [InlineData(SlackProblem.ChannelArchived)]
    [InlineData(SlackProblem.CannotPost)]
    public void Every_other_problem_is_the_same_in_a_channel(SlackProblem problem) =>
        SlackErrors.InChannel(problem).Should().Be(problem);

    [Fact]
    public void The_exception_carries_the_code_and_its_problem()
    {
        var exception = new SlackApiException("not_in_channel");

        exception.Code.Should().Be("not_in_channel");
        exception.Problem.Should().Be(SlackProblem.NotInChannel);
        new SlackApiException("ratelimited").Problem.Should().BeNull();
    }
}
