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
    public void A_tenant_event_with_no_job_goes_to_its_whole_organization()
    {
        RealtimeGroups.For(new LiveUpdatesChecked(OrganizationId, Guid.CreateVersion7()))
            .Should().Equal($"org:{OrganizationId}");
    }

    [Fact]
    public void A_job_event_goes_to_everyone_who_sees_all_jobs_and_to_the_jobs_hiring_team()
    {
        RealtimeGroups.For(new JobPublished(OrganizationId, JobId, DateTimeOffset.UnixEpoch))
            .Should().Equal($"org:{OrganizationId}:all-jobs", $"org:{OrganizationId}:team:{JobId}")
            .And.Equal(RealtimeGroups.AllJobs(OrganizationId), RealtimeGroups.Team(OrganizationId, JobId));
    }

    [Fact]
    public void Every_event_that_names_a_job_is_a_job_event()
    {
        // A tenant event with a job id that reached the whole organization would tell members on Only jobs they're on
        // about jobs they cannot see (ADR-0015).
        ScreenHireMessaging.TenantEventTypes.Values
            .Where(type => type.GetProperty("JobId")?.PropertyType == typeof(Guid))
            .Should().OnlyContain(type => type.IsAssignableTo(typeof(IJobEvent)));
    }

    [Fact]
    public void The_job_groups_never_collide_with_a_record_topic()
    {
        RealtimeGroups.Team(OrganizationId, JobId).Should().NotBe(RealtimeGroups.Topic(OrganizationId, RealtimeTopics.Job, JobId));
    }

    [Fact]
    public void A_topic_event_goes_only_to_its_records_subscribers_in_its_organization()
    {
        RealtimeGroups.For(new JobProgress(OrganizationId, JobId))
            .Should().Equal($"org:{OrganizationId}:job:{JobId}")
            .And.Equal(RealtimeGroups.Topic(OrganizationId, RealtimeTopics.Job, JobId));
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
