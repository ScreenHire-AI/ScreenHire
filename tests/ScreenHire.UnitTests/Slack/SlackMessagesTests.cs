using ScreenHire.Application.Slack;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// The words of every Slack message (docs/architecture/slack.md, "What a message says"): the kind of event, the candidate,
/// the job and one link, and nothing else. Which path a link has is chosen where the message is composed, so the
/// integration tests check the paths; these check how a link and a name are put into the text.
/// </summary>
public sealed class SlackMessagesTests
{
    private static readonly Uri Application = new("https://app.example.org/applications/0197a3c2-1111-7000-8000-000000000001");
    private static readonly Uri Activity = new("https://app.example.org/applications/0197a3c2-1111-7000-8000-000000000001?tab=activity");
    private static readonly Uri Job = new("https://app.example.org/jobs/0197a3c2-2222-7000-8000-000000000002");
    private static readonly Uri Note = new("https://app.example.org/applications/0197a3c2-1111-7000-8000-000000000001?tab=activity#note-0197a3c2-3333-7000-8000-000000000003");

    [Fact]
    public void A_new_application_names_the_candidate_and_the_job_and_links_to_the_application()
    {
        SlackMessages.ApplicationSubmitted("Robin Ellis", "Platform Engineer", Application)
            .Should().Be($"New application from <{Application.AbsoluteUri}|Robin Ellis> for Platform Engineer");
    }

    [Fact]
    public void A_move_says_the_stage_changed_without_naming_a_stage()
    {
        SlackMessages.ApplicationMoved("Robin Ellis", "Platform Engineer", Application)
            .Should().Be($"<{Application.AbsoluteUri}|Robin Ellis>'s stage changed on Platform Engineer");
    }

    [Fact]
    public void A_failed_automation_names_no_automation_and_links_to_the_activity()
    {
        SlackMessages.AutomationFailed("Robin Ellis", "Platform Engineer", Activity)
            .Should().Be($"An automation failed on <{Activity.AbsoluteUri}|Robin Ellis>'s application for Platform Engineer")
            .And.Contain("?tab=activity");
    }

    [Fact]
    public void A_published_job_links_to_the_job()
    {
        SlackMessages.JobPublished("Platform Engineer", Job).Should().Be($"<{Job.AbsoluteUri}|Platform Engineer> is published");
    }

    [Fact]
    public void A_mention_links_to_the_note_and_quotes_nothing_from_it()
    {
        SlackMessages.Mentioned("Robin Ellis", "Platform Engineer", Note)
            .Should().Be($"You were mentioned in a note on <{Note.AbsoluteUri}|Robin Ellis> for Platform Engineer")
            .And.Contain("#note-");
    }

    [Fact]
    public void A_task_names_the_candidate_and_the_job_and_nothing_of_the_task()
    {
        SlackMessages.TaskGiven("Robin Ellis", "Platform Engineer", Application)
            .Should().Be($"You have a new task on <{Application.AbsoluteUri}|Robin Ellis> for Platform Engineer");
    }

    [Theory]
    [InlineData(SlackChannelEvent.ApplicationSubmitted, "New application from")]
    [InlineData(SlackChannelEvent.ApplicationMoved, "stage changed on")]
    [InlineData(SlackChannelEvent.AutomationFailed, "An automation failed on")]
    [InlineData(SlackChannelEvent.JobPublished, "is published")]
    public void Each_channel_event_has_its_own_words(SlackChannelEvent kind, string expected) =>
        SlackMessages.ForChannel(kind, "Robin Ellis", "Platform Engineer", Application).Should().Contain(expected);

    [Fact]
    public void A_name_that_would_ping_the_channel_or_a_person_is_escaped_in_the_candidate_and_the_job()
    {
        var text = SlackMessages.ApplicationSubmitted("<!channel> <@U123> Tom & Jerry", "<!here> Lead & Co", Application);

        text.Should().Contain("|&lt;!channel&gt; &lt;@U123&gt; Tom &amp; Jerry>")
            .And.Contain("for &lt;!here&gt; Lead &amp; Co");
        text.Should().NotContain("<!channel>").And.NotContain("<@U123>").And.NotContain("<!here>");
    }

    [Fact]
    public void Escape_turns_slacks_three_control_characters_into_entities_and_control_characters_into_spaces()
    {
        SlackMessages.Escape("<!channel>").Should().Be("&lt;!channel&gt;");
        SlackMessages.Escape("<@U123>").Should().Be("&lt;@U123&gt;");
        SlackMessages.Escape("a & b").Should().Be("a &amp; b");
        SlackMessages.Escape("a\r\nb\tc\u0000d").Should().Be("a  b c d");
        SlackMessages.Escape("&lt;").Should().Be("&amp;lt;", "an entity typed into a name is escaped, not trusted");
    }

    [Fact]
    public void Without_a_link_the_message_is_plain_escaped_text()
    {
        SlackMessages.ApplicationSubmitted("Tom & Jerry", "Platform Engineer", null)
            .Should().Be("New application from Tom &amp; Jerry for Platform Engineer");
        SlackMessages.JobPublished("Lead <Dev>", null).Should().Be("Lead &lt;Dev&gt; is published");
    }

    [Fact]
    public void No_message_contains_a_verification_level_or_the_word_verification()
    {
        // Messages take no verification output, so a level cannot reach them; this pins that the words themselves stay out.
        var levels = Enum.GetNames<AuthenticityLevel>().Concat(Enum.GetNames<AiAssistanceLevel>()).ToList();
        string[] messages =
        [
            SlackMessages.ApplicationSubmitted("Robin Ellis", "Platform Engineer", Application),
            SlackMessages.ApplicationMoved("Robin Ellis", "Platform Engineer", Application),
            SlackMessages.AutomationFailed("Robin Ellis", "Platform Engineer", Activity),
            SlackMessages.JobPublished("Platform Engineer", Job),
            SlackMessages.Mentioned("Robin Ellis", "Platform Engineer", Note),
            SlackMessages.TaskGiven("Robin Ellis", "Platform Engineer", Application),
            SlackMessages.Test,
        ];

        foreach (var message in messages)
        {
            message.Should().NotContainAny(levels);
            message.Should().NotContainEquivalentOf("verif");
            message.Should().NotContainEquivalentOf("authentic");
        }
    }
}
