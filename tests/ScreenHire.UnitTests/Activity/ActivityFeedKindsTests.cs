using ScreenHire.Api.Activity;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;

namespace ScreenHire.UnitTests.Activity;

/// <summary>
/// The workspace's Activity feed (docs/architecture/notifications.md, "Activity"): a new timeline entry type is a decision
/// about the feed, its kind for the filter or a reason to leave it out, and who the feed says made an entry.
/// </summary>
public sealed class ActivityFeedKindsTests
{
    [Fact]
    public void Every_timeline_entry_type_has_a_kind_in_the_feed_or_is_left_out()
    {
        var classified = ActivityFeed.Kinds.Keys.Concat(ActivityFeed.Left).ToList();

        classified.Should().OnlyHaveUniqueItems("a type is in the feed or left out, not both");
        classified.Should().BeEquivalentTo(Enum.GetValues<ApplicationEventType>(), "a new timeline entry type is a decision about the feed");
    }

    [Fact]
    public void Who_made_an_entry_an_automation_first_then_a_member_a_key_the_candidate_or_ScreenHire()
    {
        var application = Guid.CreateVersion7();
        ApplicationEvent Entry(ApplicationEventType type, ApplicationEventData data, Guid? actor = null) =>
            new() { ApplicationId = application, Type = type, Data = data, ActorUserId = actor };

        ActivityFeed.ActorOf(Entry(ApplicationEventType.TagAdded, new(Tag: "x", AutomationRunId: Guid.CreateVersion7()))).Should().Be(ActivityActor.Automation);
        ActivityFeed.ActorOf(Entry(ApplicationEventType.StageChanged, new(), Guid.CreateVersion7())).Should().Be(ActivityActor.Member);
        ActivityFeed.ActorOf(Entry(ApplicationEventType.StageChanged, new(ApiKeyId: Guid.CreateVersion7()))).Should().Be(ActivityActor.ApiKey);
        ActivityFeed.ActorOf(Entry(ApplicationEventType.Submitted, new())).Should().Be(ActivityActor.Candidate, "nobody else applies with no actor");
        ActivityFeed.ActorOf(Entry(ApplicationEventType.InterviewBooked, new())).Should().Be(ActivityActor.Candidate);
        ActivityFeed.ActorOf(Entry(ApplicationEventType.InterviewCancelled, new()))
            .Should().Be(ActivityActor.ScreenHire, "ScreenHire also cancels with no actor, when the application ends, so the feed names nobody");
        ActivityFeed.ActorOf(Entry(ApplicationEventType.VerificationAssessed, new())).Should().Be(ActivityActor.ScreenHire);
    }
}
