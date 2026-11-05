using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.UnitTests.Privacy;

/// <summary>The data request's own rules (docs/architecture/data-requests.md): what each move does to the row.</summary>
public sealed class DataRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Member = Guid.CreateVersion7();

    [Fact]
    public void A_new_request_is_open_and_due_the_configured_time_after_it_arrived()
    {
        var request = DataRequest.Open(Guid.NewGuid(), Guid.NewGuid(), DataRequestKind.Copy, Now, TimeSpan.FromDays(30));

        request.Status.Should().Be(DataRequestStatus.Open);
        request.ReceivedAt.Should().Be(Now);
        request.DueAt.Should().Be(Now.AddDays(30));
        request.CandidateId.Should().NotBeNull();
        request.IsOverdue(Now.AddDays(30)).Should().BeFalse("the target is the due instant itself");
        request.IsOverdue(Now.AddDays(30).AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void A_request_with_an_unknown_kind_or_a_negative_target_is_refused()
    {
        var unknown = () => DataRequest.Open(Guid.NewGuid(), Guid.NewGuid(), (DataRequestKind)99, Now, TimeSpan.FromDays(1));
        var past = () => DataRequest.Open(Guid.NewGuid(), Guid.NewGuid(), DataRequestKind.Erasure, Now, TimeSpan.FromDays(-1));

        unknown.Should().Throw<ArgumentOutOfRangeException>();
        past.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Declining_keeps_the_trimmed_reason_and_who_and_when_and_ends_the_waiting()
    {
        var request = Open(DataRequestKind.Erasure);

        request.Decline("  We must keep payroll records.  ", Member, Now.AddDays(1));

        request.Status.Should().Be(DataRequestStatus.Declined);
        request.DeclineReason.Should().Be("We must keep payroll records.");
        request.ResolvedAt.Should().Be(Now.AddDays(1));
        request.ResolvedByUserId.Should().Be(Member);
        request.IsOverdue(Now.AddYears(1)).Should().BeFalse("only an open request is overdue");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_decline_needs_a_reason(string reason)
    {
        var request = Open(DataRequestKind.Copy);

        var decline = () => request.Decline(reason, Member, Now);

        decline.Should().Throw<ArgumentException>();
        request.Status.Should().Be(DataRequestStatus.Open);
    }

    [Fact]
    public void A_reason_may_be_500_characters_and_not_501()
    {
        Open(DataRequestKind.Copy).Invoking(r => r.Decline(new string('a', DataRequest.MaxDeclineReasonLength), Member, Now)).Should().NotThrow();
        Open(DataRequestKind.Copy).Invoking(r => r.Decline(new string('a', DataRequest.MaxDeclineReasonLength + 1), Member, Now)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Only_an_open_request_can_be_declined_or_completed()
    {
        var declined = Open(DataRequestKind.Copy);
        declined.Decline("No.", Member, Now);
        var completed = Open(DataRequestKind.Erasure);
        completed.Complete(Member, Now);

        declined.Invoking(r => r.Complete(Member, Now)).Should().Throw<InvalidOperationException>();
        completed.Invoking(r => r.Decline("Too late.", Member, Now)).Should().Throw<InvalidOperationException>();
        completed.Status.Should().Be(DataRequestStatus.Completed);
        completed.ResolvedByUserId.Should().Be(Member);
    }

    [Fact]
    public void Sending_the_copy_completes_the_request_records_where_it_is_and_a_repeat_changes_nothing()
    {
        var request = Open(DataRequestKind.Copy);

        request.CopySent("org/request.json", Now.AddDays(7), Member, Now).Should().BeTrue();
        request.CopySent("org/other.json", Now.AddDays(9), Member, Now.AddHours(1)).Should().BeFalse("a redelivered message sends nothing twice");

        request.Status.Should().Be(DataRequestStatus.Completed);
        request.ExportBlobName.Should().Be("org/request.json");
        request.ExportExpiresAt.Should().Be(Now.AddDays(7));
        request.CopySentAt.Should().Be(Now);
    }

    [Fact]
    public void An_erasure_request_has_no_copy_to_send()
    {
        Open(DataRequestKind.Erasure).Invoking(r => r.CopySent("x", Now, Member, Now)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Forgetting_the_candidate_clears_the_copy_and_the_reason_hands_back_the_blob_and_completes_an_open_request()
    {
        var open = Open(DataRequestKind.Erasure);
        var sent = Open(DataRequestKind.Copy);
        sent.CopySent("org/sent.json", Now.AddDays(7), Member, Now);
        var declined = Open(DataRequestKind.Copy);
        declined.Decline("Nothing is held under that name.", Member, Now);

        open.ForgetCandidate(null, Now.AddDays(2)).Should().BeNull();
        sent.ForgetCandidate(null, Now.AddDays(2)).Should().Be("org/sent.json");
        declined.ForgetCandidate(null, Now.AddDays(2)).Should().BeNull();

        open.Status.Should().Be(DataRequestStatus.Completed);
        open.ResolvedAt.Should().Be(Now.AddDays(2));
        open.ResolvedByUserId.Should().BeNull("the retention schedule has no member");
        sent.ExportBlobName.Should().BeNull();
        sent.ExportExpiresAt.Should().BeNull();
        sent.Status.Should().Be(DataRequestStatus.Completed);
        declined.DeclineReason.Should().BeNull();
        declined.Status.Should().Be(DataRequestStatus.Declined, "the answer stands, only the words go");
    }

    [Fact]
    public void The_link_cooldown_is_open_until_a_link_goes_and_again_after_the_cooldown()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), "Casey", "Morgan", "casey@example.com", null, Now);
        var cooldown = TimeSpan.FromMinutes(5);

        candidate.DataRequestLinkDue(Now, cooldown).Should().BeTrue();
        candidate.MarkDataRequestLinkSent(Now);
        candidate.DataRequestLinkDue(Now.AddMinutes(4), cooldown).Should().BeFalse();
        candidate.DataRequestLinkDue(Now.AddMinutes(5), cooldown).Should().BeTrue();
    }

    private static DataRequest Open(DataRequestKind kind) => DataRequest.Open(Guid.NewGuid(), Guid.NewGuid(), kind, Now, TimeSpan.FromDays(30));
}
