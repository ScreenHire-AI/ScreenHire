using ScreenHire.Api.Realtime;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Realtime;

namespace ScreenHire.UnitTests.Realtime;

public sealed class RealtimeGroupsTests
{
    private static readonly Guid OrganizationId = Guid.Parse("0199f0c4-0000-7000-8000-00000000000a");
    private static readonly Guid JobId = Guid.Parse("0199f0c4-0000-7000-8000-00000000000b");

    [Fact]
    public void A_tenant_event_goes_to_its_whole_organization()
    {
        RealtimeGroups.For(new JobPublished(OrganizationId, JobId, DateTimeOffset.UnixEpoch))
            .Should().Be($"org:{OrganizationId}");
    }

    [Fact]
    public void A_topic_event_goes_only_to_its_records_subscribers_in_its_organization()
    {
        RealtimeGroups.For(new JobProgress(OrganizationId, JobId))
            .Should().Be($"org:{OrganizationId}:job:{JobId}")
            .And.Be(RealtimeGroups.Topic(OrganizationId, RealtimeTopics.Job, JobId), "the hub adds subscribers to this group");
    }

    [Fact]
    public void The_same_record_id_in_two_organizations_is_two_groups()
    {
        var other = Guid.Parse("0199f0c4-0000-7000-8000-00000000000c");

        RealtimeGroups.Topic(OrganizationId, RealtimeTopics.Job, JobId)
            .Should().NotBe(RealtimeGroups.Topic(other, RealtimeTopics.Job, JobId));
    }

    [Fact]
    public void The_worker_answers_a_live_update_check_for_the_same_organization()
    {
        var checkId = Guid.CreateVersion7();

        CheckLiveUpdatesHandler.Handle(new CheckLiveUpdates(OrganizationId, checkId))
            .Should().Be(new LiveUpdatesChecked(OrganizationId, checkId));
    }

    // A topic event for the test only; the real ones live in ScreenHire.Application.
    private sealed record JobProgress(Guid OrganizationId, Guid JobId) : ITopicEvent
    {
        public string Topic => RealtimeTopics.Job;

        public Guid TopicId => JobId;
    }
}
