using ScreenHire.Domain.Candidates;

namespace ScreenHire.UnitTests.Applications;

/// <summary>
/// The delivery flag on a candidate (docs/architecture/email.md): a bounce or a spam complaint reported by the email
/// provider stops email to the address. It never moves a verification level (ADR-0004).
/// </summary>
public sealed class CandidateEmailIssueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static Candidate NewCandidate() =>
        Candidate.Create(Guid.CreateVersion7(), "Casey", "Morgan", "casey.morgan@example.com", null, Now);

    [Fact]
    public void A_new_candidate_can_receive_email()
    {
        var candidate = NewCandidate();

        candidate.CanReceiveEmail.Should().BeTrue();
        candidate.EmailIssue.Should().Be(CandidateEmailIssue.None);
        candidate.EmailIssueAt.Should().BeNull();
        candidate.EmailIssueReason.Should().BeNull();
    }

    [Fact]
    public void A_bounce_flags_the_address_with_the_time_and_the_providers_reason()
    {
        var candidate = NewCandidate();

        candidate.FlagEmail(CandidateEmailIssue.Bounced, "550 mailbox not found", Now).Should().BeTrue();

        candidate.EmailIssue.Should().Be(CandidateEmailIssue.Bounced);
        candidate.CanReceiveEmail.Should().BeFalse();
        candidate.EmailIssueAt.Should().Be(Now);
        candidate.EmailIssueReason.Should().Be("550 mailbox not found");
    }

    [Fact]
    public void The_same_report_again_changes_nothing_and_keeps_the_first_time()
    {
        var candidate = NewCandidate();
        candidate.FlagEmail(CandidateEmailIssue.Bounced, "550 mailbox not found", Now);

        candidate.FlagEmail(CandidateEmailIssue.Bounced, "552 mailbox full", Now.AddDays(1)).Should().BeFalse();

        candidate.EmailIssueAt.Should().Be(Now);
        candidate.EmailIssueReason.Should().Be("550 mailbox not found");
    }

    [Fact]
    public void A_complaint_after_a_bounce_upgrades_the_flag()
    {
        var candidate = NewCandidate();
        candidate.FlagEmail(CandidateEmailIssue.Bounced, "550", Now);

        candidate.FlagEmail(CandidateEmailIssue.SpamComplaint, null, Now.AddHours(1)).Should().BeTrue();

        candidate.EmailIssue.Should().Be(CandidateEmailIssue.SpamComplaint);
        candidate.EmailIssueAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_complaint_is_never_downgraded_to_a_bounce()
    {
        var candidate = NewCandidate();
        candidate.FlagEmail(CandidateEmailIssue.SpamComplaint, null, Now);

        candidate.FlagEmail(CandidateEmailIssue.Bounced, "550", Now.AddHours(1)).Should().BeFalse();

        candidate.EmailIssue.Should().Be(CandidateEmailIssue.SpamComplaint);
        candidate.EmailIssueAt.Should().Be(Now);
    }

    [Fact]
    public void A_long_reason_is_cut_to_300_characters_and_a_blank_one_is_dropped()
    {
        var candidate = NewCandidate();
        candidate.FlagEmail(CandidateEmailIssue.Bounced, "  " + new string('x', 400) + "  ", Now);
        candidate.EmailIssueReason!.Length.Should().Be(Candidate.MaxEmailIssueReasonLength);

        var other = NewCandidate();
        other.FlagEmail(CandidateEmailIssue.Bounced, "   ", Now);
        other.EmailIssueReason.Should().BeNull();
    }

    [Fact]
    public void Flagging_with_None_is_a_mistake_because_clearing_has_its_own_method()
    {
        var flag = () => NewCandidate().FlagEmail(CandidateEmailIssue.None, null, Now);

        flag.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Clearing_removes_the_flag_its_time_and_its_reason()
    {
        var candidate = NewCandidate();
        candidate.FlagEmail(CandidateEmailIssue.Bounced, "550", Now);

        candidate.ClearEmailIssue().Should().BeTrue();

        candidate.CanReceiveEmail.Should().BeTrue();
        candidate.EmailIssueAt.Should().BeNull();
        candidate.EmailIssueReason.Should().BeNull();
    }

    [Fact]
    public void Clearing_an_address_with_no_flag_changes_nothing()
    {
        NewCandidate().ClearEmailIssue().Should().BeFalse();
    }
}
