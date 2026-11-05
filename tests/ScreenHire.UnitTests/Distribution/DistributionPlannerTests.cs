using Microsoft.Extensions.Options;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Application.Distribution.Feeds;
using ScreenHire.Application.Distribution.GoogleIndexing;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Distribution;

/// <summary>One-click publish and teardown across boards, as the API runs them inside its transaction.</summary>
public sealed class DistributionPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private readonly FixedTime time = new(Now);
    private readonly Organization organization = new() { Name = "Harbour & Co", Slug = "harbour", HomeRegion = "eu" };

    [Fact]
    public async Task The_first_publish_posts_to_every_available_board_with_one_event()
    {
        var planner = Planner(out _);
        var postings = new List<JobPosting>();

        var changes = await planner.PublishAsync(organization, Content(1), postings, null, CancellationToken.None);

        changes.Added.Select(p => p.BoardId).Should().BeEquivalentTo(["careers", "careerjet", "talent", "push"]);
        State(changes, "careers").Should().Be(PostingState.Live, "we serve the careers page ourselves");
        State(changes, "careerjet").Should().Be(PostingState.Submitted);
        changes.Added.Single(p => p.BoardId == "careerjet").ExpectedBy.Should().Be(Now + TimeSpan.FromDays(1) + PullBoardAdapter.CacheLifetime);
        State(changes, "push").Should().Be(PostingState.Queued, "the worker calls push boards");
        changes.Messages.OfType<JobPostingsChanged>().Should().ContainSingle();
        changes.Messages.OfType<SyncJobPosting>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { BoardId = "push", JobVersion = 1, Action = PostingAction.Publish });
    }

    [Fact]
    public async Task A_board_with_issues_waits_in_draft_and_raises_no_command()
    {
        var planner = Planner(out _);

        var changes = await planner.PublishAsync(organization, Content(1, "<p>Short.</p>"), [], null, CancellationToken.None);

        var talent = changes.Added.Single(p => p.BoardId == "talent");
        talent.State.Should().Be(PostingState.Draft);
        talent.Issues.Should().ContainSingle().Which.Should().Contain("Talent.com");
    }

    [Fact]
    public async Task Partner_gated_boards_are_offered_only_to_cleared_paying_organizations()
    {
        var planner = Planner(out var registry, partnerBoard: true);

        registry.AvailableTo(organization).Select(a => a.BoardId).Should().NotContain("partner");
        organization.IndeedEligible = true;
        organization.IsTrial = true;
        registry.AvailableTo(organization).Select(a => a.BoardId).Should().NotContain("partner", "trial tenants are excluded");
        organization.IsTrial = false;
        registry.AvailableTo(organization).Select(a => a.BoardId).Should().Contain("partner");

        var changes = await planner.PublishAsync(organization, Content(1), [], null, CancellationToken.None);
        changes.Added.Select(p => p.BoardId).Should().Contain("partner");
    }

    [Fact]
    public async Task Republishing_a_new_version_updates_the_boards_the_job_is_on_and_skips_closed_ones()
    {
        var planner = Planner(out _);
        var postings = (await planner.PublishAsync(organization, Content(1), [], null, CancellationToken.None)).Added.ToList();
        var push = postings.Single(p => p.BoardId == "push");
        push.MarkLive(1, "ext-1", Now);
        await planner.CloseAsync(Content(1), postings, "careerjet", CancellationToken.None);

        var changes = await planner.PublishAsync(organization, Content(2), postings, null, CancellationToken.None);

        changes.Added.Should().BeEmpty();
        postings.Where(p => p.BoardId != "careerjet").Should().OnlyContain(p => p.JobVersion == 2);
        postings.Single(p => p.BoardId == "careerjet").State.Should().Be(PostingState.Closing, "a board taken down stays down");
        changes.Messages.OfType<SyncJobPosting>().Should().ContainSingle().Which.Action.Should().Be(PostingAction.Update);
    }

    [Fact]
    public async Task Republishing_unchanged_content_changes_nothing()
    {
        var planner = Planner(out _);
        var postings = (await planner.PublishAsync(organization, Content(1), [], null, CancellationToken.None)).Added.ToList();

        var changes = await planner.PublishAsync(organization, Content(1), postings, null, CancellationToken.None);

        changes.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Republishing_leaves_a_board_that_was_unconfigured_or_is_no_longer_allowed_as_it_is()
    {
        var planner = Planner(out _, partnerBoard: true);
        organization.IndeedEligible = true;
        var postings = (await planner.PublishAsync(organization, Content(1), [], null, CancellationToken.None)).Added.ToList();
        postings.Select(p => p.BoardId).Should().Contain(["push", "partner"]);

        // The push board's configuration is removed, and the organization loses partner clearance.
        var withoutPush = new DistributionPlanner(
            new BoardRegistry([new CareersPageAdapter(), new CareerjetFeedAdapter(time), new TalentFeedAdapter(time), new FakePushBoard("partner", partnerGated: true)]),
            time);
        organization.IndeedEligible = false;

        var changes = await withoutPush.PublishAsync(organization, Content(2), postings, null, CancellationToken.None);

        postings.Single(p => p.BoardId == "careerjet").JobVersion.Should().Be(2);
        postings.Single(p => p.BoardId == "push").Should().BeEquivalentTo(new { JobVersion = 1, State = PostingState.Queued }, "nothing can reach it");
        postings.Single(p => p.BoardId == "partner").JobVersion.Should().Be(1, "the organization may no longer post there");
        changes.Messages.OfType<SyncJobPosting>().Should().BeEmpty();
    }

    [Fact]
    public async Task Teardown_closes_only_what_is_confirmed_and_asks_push_boards_to_remove_the_rest()
    {
        var planner = Planner(out _);
        var postings = (await planner.PublishAsync(organization, Content(1, "<p>Short.</p>"), [], null, CancellationToken.None)).Added.ToList();

        var changes = await planner.CloseAsync(Content(1), postings, null, CancellationToken.None);

        StateOf(postings, "careers").Should().Be(PostingState.Closed, "our own 410 is confirmed at once");
        StateOf(postings, "talent").Should().Be(PostingState.Closed, "the draft never reached Talent.com");
        StateOf(postings, "careerjet").Should().Be(PostingState.Closing, "Careerjet drops it on its next crawl and never confirms");
        postings.Single(p => p.BoardId == "careerjet").ExpectedBy.Should().Be(Now + TimeSpan.FromDays(1) + PullBoardAdapter.CacheLifetime);
        StateOf(postings, "push").Should().Be(PostingState.Closing);
        postings.Single(p => p.BoardId == "push").ExpectedBy.Should().Be(Now + TimeSpan.FromMinutes(3), "the push board's removal estimate");
        changes.Messages.OfType<SyncJobPosting>().Should().ContainSingle().Which.Action.Should().Be(PostingAction.Close);
        changes.Messages.OfType<JobPostingsChanged>().Should().ContainSingle();

        var again = await planner.CloseAsync(Content(1), postings, null, CancellationToken.None);
        again.Messages.Should().BeEmpty("closing twice sends nothing");
    }

    [Fact]
    public void The_sync_commands_idempotency_key_is_job_board_version_action_and_request()
    {
        var posting = new JobPosting { OrganizationId = Guid.CreateVersion7(), JobId = Guid.CreateVersion7(), BoardId = "sandbox", CreatedAt = Now };
        posting.Queue(3, "https://careers.example.com/harbour/jobs/a", [], Now);

        var first = SyncJobPosting.For(posting, PostingAction.Publish);

        first.IdempotencyKey.Should().Be($"{posting.JobId}:sandbox:3:Publish:1");
        SyncJobPosting.For(posting, PostingAction.Publish).IdempotencyKey.Should().Be(first.IdempotencyKey, "the same request gives the same key");
        posting.Resend(5, Now.AddMinutes(10));
        SyncJobPosting.For(posting, PostingAction.Publish).IdempotencyKey.Should().NotBe(first.IdempotencyKey, "a resend is a new request");
    }

    [Fact]
    public async Task A_board_with_its_own_text_is_validated_and_sent_on_that_text_while_another_board_gets_the_jobs()
    {
        var own = new RecordingBoard("own");
        var follower = new RecordingBoard("follower");
        var planner = new DistributionPlanner(new BoardRegistry([own, follower]), time);
        var content = Content(1);
        var customized = Posting("own");
        customized.Customize("Staff Engineer, Platform", "<p>Written for this board.</p>", content.Snapshot, Now);

        var changes = await planner.PublishAsync(organization, content, [customized], ["own", "follower"], CancellationToken.None);

        own.Validated.Should().ContainSingle().Which.Snapshot.Should().BeEquivalentTo(
            content.Snapshot with { Title = "Staff Engineer, Platform", Description = "<p>Written for this board.</p>" });
        own.Sent.Should().ContainSingle().Which.Snapshot.Title.Should().Be("Staff Engineer, Platform");
        follower.Validated.Should().ContainSingle().Which.Snapshot.Should().Be(content.Snapshot);
        follower.Sent.Should().ContainSingle().Which.Snapshot.Should().Be(content.Snapshot);
        changes.Added.Select(p => p.BoardId).Should().Equal("follower");
    }

    [Fact]
    public async Task An_override_that_fails_its_boards_check_keeps_the_posting_in_draft_even_when_the_job_passes()
    {
        var planner = Planner(out _);
        var postings = new List<JobPosting> { Posting("talent"), Posting("careerjet") };
        postings[0].Customize(null, "<p>Short.</p>", Content(1).Snapshot, Now);

        await planner.PublishAsync(organization, Content(1), postings, null, CancellationToken.None);

        postings.Single(p => p.BoardId == "talent").State.Should().Be(PostingState.Draft);
        postings.Single(p => p.BoardId == "talent").Issues.Should().ContainSingle().Which.Should().Contain("Talent.com");
        postings.Single(p => p.BoardId == "careerjet").State.Should().Be(PostingState.Submitted, "it follows the job, which is long enough");
    }

    [Fact]
    public async Task An_override_that_passes_its_boards_check_lets_a_short_job_through_on_that_board_only()
    {
        var planner = Planner(out _);
        var shortJob = Content(1, "<p>Short.</p>");
        var postings = new List<JobPosting> { Posting("talent") };
        postings[0].Customize(null, "<p>Build and run the systems our customers rely on every day, with a small team that owns its work.</p>", shortJob.Snapshot, Now);

        var changes = await planner.PublishAsync(organization, shortJob, postings, ["talent", "careerjet"], CancellationToken.None);

        postings.Single().State.Should().Be(PostingState.Submitted);
        changes.Added.Single(p => p.BoardId == "careerjet").State.Should().Be(PostingState.Submitted, "Careerjet has no length check");
        var withoutOverride = await planner.PublishAsync(organization, shortJob, [], ["talent"], CancellationToken.None);
        withoutOverride.Added.Single().State.Should().Be(PostingState.Draft, "the same job without an override still fails Talent.com's check");
    }

    [Fact]
    public async Task A_new_job_version_keeps_a_boards_own_text()
    {
        var board = new RecordingBoard("own");
        var planner = new DistributionPlanner(new BoardRegistry([board]), time);
        var posting = Posting("own");
        posting.Customize("Own title", null, Content(1).Snapshot, Now);
        await planner.PublishAsync(organization, Content(1), [posting], null, CancellationToken.None);

        await planner.PublishAsync(organization, Content(2), [posting], null, CancellationToken.None);

        posting.JobVersion.Should().Be(2);
        board.Sent.Select(c => (c.Version, c.Snapshot.Title)).Should().Equal((1, "Own title"), (2, "Own title"));
    }

    [Fact]
    public async Task Reposting_sends_the_same_version_again_with_the_boards_new_text_and_touches_no_other_board()
    {
        var own = new RecordingBoard("own");
        var other = new RecordingBoard("other");
        var planner = new DistributionPlanner(new BoardRegistry([own, other]), time);
        var postings = (await planner.PublishAsync(organization, Content(1), [], null, CancellationToken.None)).Added.ToList();
        var posting = postings.Single(p => p.BoardId == "own");
        posting.State.Should().Be(PostingState.Submitted);
        posting.Sequence.Should().Be(1);
        posting.Customize("Own title", null, Content(1).Snapshot, Now);

        var changes = await planner.RepostAsync(organization, Content(1), posting, isNew: false, CancellationToken.None);

        posting.JobVersion.Should().Be(1, "no job version is made");
        posting.Sequence.Should().Be(2, "it is a new request for the board");
        posting.State.Should().Be(PostingState.Submitted);
        own.Updated.Should().ContainSingle().Which.Snapshot.Title.Should().Be("Own title", "the board already had the posting, so it is an update");
        other.Updated.Should().BeEmpty();
        other.Sent.Should().ContainSingle("the other board was published once and not touched again");
        changes.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Reposting_raises_the_posting_event_even_when_the_state_ends_where_it_began()
    {
        var planner = Planner(out _);
        var postings = (await planner.PublishAsync(organization, Content(1), [], ["careerjet"], CancellationToken.None)).Added.ToList();
        var posting = postings.Single();
        var before = posting.State;
        posting.Customize(null, "<p>Written for Careerjet, with its own words about the work.</p>", Content(1).Snapshot, Now);

        var changes = await planner.RepostAsync(organization, Content(1), posting, isNew: false, CancellationToken.None);

        before.Should().Be(PostingState.Submitted);
        posting.State.Should().Be(before);
        changes.Messages.OfType<JobPostingsChanged>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { OrganizationId = organization.Id, JobId = Content(1).JobId, ChangedAt = Now });
    }

    [Fact]
    public async Task Reposting_a_posting_that_did_not_exist_returns_it_as_added_and_publishes_it()
    {
        var board = new RecordingBoard("own");
        var planner = new DistributionPlanner(new BoardRegistry([board]), time);
        var posting = Posting("own");
        posting.Customize("Own title", null, Content(1).Snapshot, Now);

        var changes = await planner.RepostAsync(organization, Content(1), posting, isNew: true, CancellationToken.None);

        changes.Added.Should().ContainSingle().Which.Should().BeSameAs(posting);
        posting.State.Should().Be(PostingState.Submitted);
        posting.JobVersion.Should().Be(1);
        board.Sent.Should().ContainSingle().Which.Snapshot.Title.Should().Be("Own title");
        board.Updated.Should().BeEmpty("a board that never had the posting is published to, not updated");
        changes.Messages.OfType<JobPostingsChanged>().Should().ContainSingle();
    }

    [Fact]
    public async Task Reposting_to_a_push_board_asks_the_worker_to_update_it_at_the_same_version()
    {
        var planner = Planner(out _);
        var postings = (await planner.PublishAsync(organization, Content(1), [], ["push"], CancellationToken.None)).Added.ToList();
        var posting = postings.Single();
        posting.MarkLive(1, "ext-1", Now);
        posting.Customize("Own title", null, Content(1).Snapshot, Now);

        var changes = await planner.RepostAsync(organization, Content(1), posting, isNew: false, CancellationToken.None);

        posting.State.Should().Be(PostingState.Queued);
        posting.Sequence.Should().Be(2);
        changes.Messages.OfType<SyncJobPosting>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { BoardId = "push", JobVersion = 1, Action = PostingAction.Update, Sequence = 2 });
        changes.Messages.OfType<JobPostingsChanged>().Should().ContainSingle();
    }

    [Fact]
    public void Google_is_hidden_until_configured()
    {
        Registry(GoogleIndexingMode.Off).Find(Boards.GoogleIndexing).Should().BeNull();
        Registry(GoogleIndexingMode.Google).Find(Boards.GoogleIndexing).Should().BeNull("Google mode without a service account key is not configured");
        Registry(GoogleIndexingMode.Google, key: "{}").Find(Boards.GoogleIndexing).Should().NotBeNull();
        Registry(GoogleIndexingMode.Local).Find(Boards.GoogleIndexing).Should().NotBeNull();
        Registry(GoogleIndexingMode.Off).Find(Boards.Sandbox).Should().BeNull("the sandbox is off unless enabled");
    }

    private static BoardRegistry Registry(GoogleIndexingMode mode, string? key = null)
    {
        var options = Options.Create(new DistributionOptions { GoogleIndexing = new GoogleIndexingOptions { Mode = mode, ServiceAccountKeyJson = key } });
        // The adapters only read their options to decide whether they are configured; no database is touched.
        return new BoardRegistry([new GoogleIndexingAdapter(null!, TimeProvider.System, options), new SandboxBoardAdapter(null!, TimeProvider.System, options)]);
    }

    private DistributionPlanner Planner(out BoardRegistry registry, bool partnerBoard = false)
    {
        List<IJobBoardAdapter> adapters = [new CareersPageAdapter(), new CareerjetFeedAdapter(time), new TalentFeedAdapter(time), new FakePushBoard()];
        if (partnerBoard)
        {
            adapters.Add(new FakePushBoard("partner", partnerGated: true));
        }

        registry = new BoardRegistry(adapters);
        return new DistributionPlanner(registry, time);
    }

    private PostingContent Content(int version, string description = "<p>Build and run the systems our customers rely on every day, with a small team that owns its work.</p>") => new(
        Guid.Parse("0199f0c4-0000-7000-8000-000000000001"), version,
        new JobSnapshot("Engineer", "engineer", description, null, EmploymentType.FullTime, WorkplaceType.Onsite,
            [new JobLocation("London", null, "GB")], [], null, null),
        Now, Now, "https://careers.example.com/harbour/jobs/engineer", organization.Name, organization.Slug);

    private JobPosting Posting(string boardId) =>
        new() { OrganizationId = organization.Id, JobId = Content(1).JobId, BoardId = boardId, CreatedAt = Now };

    private static PostingState State(DistributionChanges changes, string boardId) => StateOf(changes.Added, boardId);

    private static PostingState StateOf(IEnumerable<JobPosting> postings, string boardId) => postings.Single(p => p.BoardId == boardId).State;

    /// <summary>A pull board that records the content it is asked to validate and the content it is sent.</summary>
    private sealed class RecordingBoard(string boardId) : IJobBoardAdapter
    {
        public List<PostingContent> Validated { get; } = [];

        /// <summary>Every delivery, a first publish or an update, in order.</summary>
        public List<PostingContent> Sent { get; } = [];

        public List<PostingContent> Updated { get; } = [];

        public string BoardId => boardId;

        public string Name => boardId;

        public BoardCapabilities Capabilities => new(BoardMode.Pull, true, true, false, false, null, null);

        public bool IsConfigured => true;

        public IReadOnlyList<string> Validate(PostingContent content)
        {
            Validated.Add(content);
            return [];
        }

        public Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request.Content);
            return Task.FromResult(new BoardResult(BoardOutcome.Submitted));
        }

        public Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request.Content);
            Updated.Add(request.Content);
            return Task.FromResult(new BoardResult(BoardOutcome.Submitted));
        }

        public Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class FakePushBoard(string boardId = "push", bool partnerGated = false) : IJobBoardAdapter
    {
        public string BoardId => boardId;

        public string Name => boardId;

        public BoardCapabilities Capabilities => new(
            BoardMode.Push, true, true, true, true, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), PartnerGated: partnerGated);

        public bool IsConfigured => true;

        public IReadOnlyList<string> Validate(PostingContent content) => [];

        // The planner never calls a push board; the worker does.
        public Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}

/// <summary>The Indexing API's daily quota goes to deletes first, then the oldest notifications.</summary>
public sealed class IndexingPlanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Deletes_go_first_then_the_oldest_up_to_the_remaining_quota()
    {
        var oldUpdate = Notification(IndexingNotificationType.Updated, 0);
        var newUpdate = Notification(IndexingNotificationType.Updated, 5);
        var lateDelete = Notification(IndexingNotificationType.Deleted, 9);
        var earlyDelete = Notification(IndexingNotificationType.Deleted, 7);

        IndexingPlan.Choose([oldUpdate, newUpdate, lateDelete, earlyDelete], remainingQuota: 3)
            .Should().Equal(earlyDelete, lateDelete, oldUpdate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Nothing_is_sent_once_the_quota_is_spent(int remaining)
    {
        IndexingPlan.Choose([Notification(IndexingNotificationType.Deleted, 0)], remaining).Should().BeEmpty();
    }

    private static PendingNotification Notification(IndexingNotificationType type, int minutes) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), type, T0.AddMinutes(minutes));
}
