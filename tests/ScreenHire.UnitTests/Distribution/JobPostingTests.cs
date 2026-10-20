using ScreenHire.Domain.Distribution;

namespace ScreenHire.UnitTests.Distribution;

/// <summary>The per job × board state machine (docs/architecture/job-distribution.md §2).</summary>
public sealed class JobPostingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private const string Url = "https://careers.example.com/harbour/jobs/engineer";

    [Fact]
    public void A_new_posting_is_a_draft_and_queues_its_version()
    {
        var posting = NewPosting();
        posting.State.Should().Be(PostingState.Draft);

        posting.Queue(1, Url, [], T0).Should().BeTrue();

        posting.State.Should().Be(PostingState.Queued);
        posting.JobVersion.Should().Be(1);
        posting.Url.Should().Be(Url);
        posting.Sequence.Should().Be(1);
    }

    [Fact]
    public void Issues_keep_a_posting_that_is_not_on_the_board_in_draft()
    {
        var posting = NewPosting();

        posting.Queue(1, Url, ["Adzuna needs a description of at least 100 characters."], T0).Should().BeFalse();

        posting.State.Should().Be(PostingState.Draft);
        posting.Issues.Should().ContainSingle();
        posting.Queue(1, Url, [], T0.AddMinutes(1)).Should().BeTrue("fixing the issues lets it go");
        posting.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Issues_with_a_new_version_leave_a_live_posting_on_its_old_version()
    {
        var posting = LivePosting();

        posting.Queue(2, Url, ["Needs a city."], T0.AddHours(1)).Should().BeFalse();

        posting.State.Should().Be(PostingState.Live);
        posting.JobVersion.Should().Be(1);
        posting.Issues.Should().ContainSingle();
    }

    [Fact]
    public void Queueing_the_version_already_sent_or_an_older_one_changes_nothing()
    {
        var posting = LivePosting();
        posting.Queue(2, Url, [], T0.AddHours(1)).Should().BeTrue();
        var sequence = posting.Sequence;

        posting.Queue(2, Url, [], T0.AddHours(2)).Should().BeFalse("version 2 is already on its way");
        posting.Queue(1, Url, [], T0.AddHours(2)).Should().BeFalse("version 1 is stale");

        posting.JobVersion.Should().Be(2);
        posting.Sequence.Should().Be(sequence);
    }

    [Fact]
    public void Submitted_then_live_follows_the_board_and_ignores_answers_for_another_version()
    {
        var posting = NewPosting();
        posting.Queue(2, Url, [], T0);

        posting.MarkSubmitted(1, "ext-1", T0.AddMinutes(5), T0.AddMinutes(5), T0).Should().BeFalse("an answer about version 1 is stale");
        posting.MarkSubmitted(2, "ext-2", T0.AddMinutes(5), T0.AddMinutes(5), T0).Should().BeTrue();
        posting.State.Should().Be(PostingState.Submitted);
        posting.ExpectedBy.Should().Be(T0.AddMinutes(5));
        posting.NextCheckAt.Should().Be(T0.AddMinutes(5));

        posting.MarkLive(2, null, T0.AddMinutes(5)).Should().BeTrue();

        posting.State.Should().Be(PostingState.Live);
        posting.ExternalId.Should().Be("ext-2");
        posting.ExpectedBy.Should().BeNull();
        posting.NextCheckAt.Should().BeNull();
        posting.MarkLive(2, null, T0.AddMinutes(6)).Should().BeFalse("already live");
    }

    [Fact]
    public void A_rejection_records_the_boards_reason_and_publishing_again_retries()
    {
        var posting = NewPosting();
        posting.Queue(1, Url, [], T0);

        posting.Reject(1, "Salary required in this location.", T0).Should().BeTrue();

        posting.State.Should().Be(PostingState.Rejected);
        posting.LastError.Should().Be("Salary required in this location.");
        posting.Queue(1, Url, [], T0.AddMinutes(1)).Should().BeTrue("a rejected posting is not on the board, so the same version can go again");
        posting.LastError.Should().BeNull();
    }

    [Fact]
    public void A_posting_that_never_reached_the_board_closes_at_once()
    {
        var draft = NewPosting();
        draft.Queue(1, Url, ["Needs a city."], T0);

        draft.RequestClose(T0.AddHours(1), null, T0).Should().Be(CloseOutcome.ClosedNow);

        draft.State.Should().Be(PostingState.Closed);
        draft.ClosedAt.Should().Be(T0);
    }

    [Theory]
    [InlineData(PostingState.Queued)]
    [InlineData(PostingState.Submitted)]
    [InlineData(PostingState.Live)]
    public void A_posting_the_board_may_show_goes_to_closing_and_is_not_closed_until_confirmed(PostingState state)
    {
        var posting = PostingIn(state);

        posting.RequestClose(T0.AddMinutes(30), null, T0.AddHours(1)).Should().Be(CloseOutcome.SendClose);

        posting.State.Should().Be(PostingState.Closing);
        posting.ExpectedBy.Should().Be(T0.AddMinutes(30));
        posting.ClosedAt.Should().BeNull();
        posting.AwaitRemoval(T0.AddMinutes(40), T0.AddMinutes(40), T0.AddHours(1)).Should().BeTrue();
        posting.State.Should().Be(PostingState.Closing, "accepting a removal request is not removing");
        posting.MarkClosed(T0.AddHours(2)).Should().BeTrue();
        posting.State.Should().Be(PostingState.Closed);
    }

    [Fact]
    public void Closing_twice_sends_nothing_the_second_time()
    {
        var posting = LivePosting();
        posting.RequestClose(null, null, T0).Should().Be(CloseOutcome.SendClose);
        var sequence = posting.Sequence;

        posting.RequestClose(null, null, T0.AddMinutes(1)).Should().Be(CloseOutcome.Unchanged);

        posting.Sequence.Should().Be(sequence);
    }

    [Fact]
    public void A_late_publish_answer_cannot_reopen_a_closing_posting()
    {
        var posting = NewPosting();
        posting.Queue(1, Url, [], T0);
        posting.RequestClose(null, null, T0);

        posting.MarkSubmitted(1, "ext", null, null, T0).Should().BeFalse();
        posting.MarkLive(1, "ext", T0).Should().BeFalse();

        posting.State.Should().Be(PostingState.Closing);
    }

    [Fact]
    public void Marking_closed_needs_a_closing_posting()
    {
        LivePosting().MarkClosed(T0).Should().BeFalse();
    }

    [Fact]
    public void Posting_again_after_a_close_queues_it_with_a_new_request()
    {
        var posting = LivePosting();
        posting.RequestClose(null, null, T0);
        posting.MarkClosed(T0.AddMinutes(1));
        var sequence = posting.Sequence;

        posting.Queue(1, Url, [], T0.AddMinutes(2)).Should().BeTrue();

        posting.State.Should().Be(PostingState.Queued);
        posting.ClosedAt.Should().BeNull();
        posting.Sequence.Should().BeGreaterThan(sequence, "the new request must not look like a duplicate of the first");
    }

    [Fact]
    public void Resends_count_attempts_and_a_queued_posting_goes_to_error_after_the_last()
    {
        var posting = NewPosting();
        posting.Queue(1, Url, [], T0);

        posting.Resend(2, T0.AddMinutes(10)).Should().BeTrue();
        posting.Resend(2, T0.AddMinutes(20)).Should().BeTrue();
        posting.Resend(2, T0.AddMinutes(30)).Should().BeFalse();

        posting.State.Should().Be(PostingState.Error);
        posting.LastError.Should().NotBeNullOrEmpty();
        posting.Queue(1, Url, [], T0.AddHours(1)).Should().BeTrue("publishing again retries a posting in error");
    }

    [Fact]
    public void A_closing_posting_is_never_given_up_on()
    {
        var posting = LivePosting();
        posting.RequestClose(null, null, T0);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            posting.Resend(2, T0.AddMinutes(attempt)).Should().BeTrue();
        }

        posting.State.Should().Be(PostingState.Closing);
    }

    [Fact]
    public void Only_queued_submitted_and_live_postings_are_active()
    {
        foreach (var state in Enum.GetValues<PostingState>())
        {
            var active = state is PostingState.Queued or PostingState.Submitted or PostingState.Live;
            if (state is PostingState.Draft or PostingState.Queued or PostingState.Submitted or PostingState.Live or PostingState.Closing or PostingState.Closed)
            {
                PostingIn(state).IsActive.Should().Be(active, state.ToString());
            }
        }
    }

    private static JobPosting NewPosting() =>
        new() { OrganizationId = Guid.CreateVersion7(), JobId = Guid.CreateVersion7(), BoardId = "sandbox", CreatedAt = T0 };

    private static JobPosting LivePosting()
    {
        var posting = NewPosting();
        posting.Queue(1, Url, [], T0);
        posting.MarkLive(1, "ext-1", T0);
        return posting;
    }

    private static JobPosting PostingIn(PostingState state)
    {
        var posting = NewPosting();
        switch (state)
        {
            case PostingState.Draft:
                return posting;
            case PostingState.Queued:
                posting.Queue(1, Url, [], T0);
                return posting;
            case PostingState.Submitted:
                posting.Queue(1, Url, [], T0);
                posting.MarkSubmitted(1, "ext", null, null, T0);
                return posting;
            case PostingState.Live:
                return LivePosting();
            case PostingState.Closing:
                posting = LivePosting();
                posting.RequestClose(null, null, T0);
                return posting;
            case PostingState.Closed:
                posting = LivePosting();
                posting.RequestClose(null, null, T0);
                posting.MarkClosed(T0);
                return posting;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }
}
