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

    private static PostingState State(DistributionChanges changes, string boardId) => StateOf(changes.Added, boardId);

    private static PostingState StateOf(IEnumerable<JobPosting> postings, string boardId) => postings.Single(p => p.BoardId == boardId).State;

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
