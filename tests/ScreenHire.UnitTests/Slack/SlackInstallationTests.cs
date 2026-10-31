using ScreenHire.Domain.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// Connecting a workspace again (docs/architecture/slack.md): the same workspace keeps the team's channel and events,
/// another one starts over, and either way the new token replaces the old and a revoked token stops being a problem.
/// </summary>
public sealed class SlackInstallationTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Connector = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset First = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = First.AddDays(7);

    private static SlackInstallation Configured()
    {
        var installation = SlackInstallation.Connect(Organization, "T0ONE", "One", "U0BOT", "protected-one", Connector, First);
        installation.ChooseChannel("C0HIRING", "hiring", [SlackChannelEvent.JobPublished, SlackChannelEvent.ApplicationSubmitted], First);
        return installation;
    }

    [Fact]
    public void Another_workspace_clears_the_channel_and_the_events_and_says_the_links_must_go()
    {
        var installation = Configured();

        var changed = installation.Reconnect("T0TWO", "Two", "U0BOT2", "protected-two", Connector, Later);

        changed.Should().BeTrue();
        installation.ChannelId.Should().BeNull();
        installation.ChannelName.Should().BeNull();
        installation.Events.Should().BeEmpty();
        installation.TeamId.Should().Be("T0TWO");
        installation.TeamName.Should().Be("Two");
        installation.BotUserId.Should().Be("U0BOT2");
        installation.ProtectedBotToken.Should().Be("protected-two");
        installation.ConnectedAt.Should().Be(Later);
    }

    [Fact]
    public void The_same_workspace_keeps_the_channel_and_the_events_and_says_the_links_stay()
    {
        var installation = Configured();

        var changed = installation.Reconnect("T0ONE", "One renamed", "U0BOT", "protected-new", Connector, Later);

        changed.Should().BeFalse();
        installation.ChannelId.Should().Be("C0HIRING");
        installation.ChannelName.Should().Be("hiring");
        installation.Events.Should().Equal(SlackChannelEvent.ApplicationSubmitted, SlackChannelEvent.JobPublished);
        installation.TeamName.Should().Be("One renamed");
        installation.ProtectedBotToken.Should().Be("protected-new");
        installation.ConnectedAt.Should().Be(Later);
        installation.UpdatedAt.Should().Be(Later);
    }

    [Theory]
    [InlineData("T0ONE")]
    [InlineData("T0TWO")]
    public void Connecting_again_clears_a_revoked_token_whichever_workspace_it_is(string teamId)
    {
        var installation = Configured();
        installation.RecordProblem(SlackProblem.Disconnected, First.AddDays(1));
        installation.IsDisconnected.Should().BeTrue();

        installation.Reconnect(teamId, "Any", "U0BOT", "protected-new", Connector, Later);

        installation.Problem.Should().BeNull();
        installation.ProblemAt.Should().BeNull();
        installation.IsDisconnected.Should().BeFalse();
    }

    [Fact]
    public void The_same_workspace_also_clears_a_channel_problem_but_keeps_the_channel()
    {
        var installation = Configured();
        installation.RecordProblem(SlackProblem.NotInChannel, First.AddDays(1));

        installation.Reconnect("T0ONE", "One", "U0BOT", "protected-new", Connector, Later);

        installation.Problem.Should().BeNull();
        installation.ChannelId.Should().Be("C0HIRING");
    }
}
