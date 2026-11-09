using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.SendingDomains;

/// <summary>
/// The mark that the Owners were told a verified sending domain stopped verifying (docs/architecture/notifications.md): set once,
/// cleared only by the provider's yes, and untouched by a check that fails again.
/// </summary>
public sealed class SendingDomainVerificationLostTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static SendingDomain Verified()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };
        domain.RecordCheck(Records(SendingDomainRecordState.Matches), true, null, First);
        return domain;
    }

    [Fact]
    public void A_new_domain_has_not_lost_verification()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };

        domain.VerificationLostAt.Should().BeNull();
        domain.HasLostVerification.Should().BeFalse();
    }

    [Fact]
    public void Marking_verification_lost_sets_the_time_once_and_returns_false_the_second_time()
    {
        var domain = Verified();

        domain.MarkVerificationLost(First.AddDays(1)).Should().BeTrue();
        domain.MarkVerificationLost(First.AddDays(2)).Should().BeFalse("the Owners were already told");

        domain.VerificationLostAt.Should().Be(First.AddDays(1));
        domain.HasLostVerification.Should().BeTrue();
    }

    [Fact]
    public void A_failed_check_does_not_clear_it()
    {
        var domain = Verified();
        domain.MarkVerificationLost(First.AddDays(1));

        domain.RecordCheck(Records(SendingDomainRecordState.Missing), false, "A record was removed.", First.AddDays(2));

        domain.VerificationLostAt.Should().Be(First.AddDays(1));
        domain.Status.Should().Be(SendingDomainStatus.Pending);
    }

    [Fact]
    public void A_confirmed_check_clears_it_and_verifies_the_domain_again()
    {
        var domain = Verified();
        domain.RecordCheck(Records(SendingDomainRecordState.Missing), false, "A record was removed.", First.AddDays(1));
        domain.MarkVerificationLost(First.AddDays(1));

        domain.RecordCheck(Records(SendingDomainRecordState.Matches), true, null, First.AddDays(3));

        domain.VerificationLostAt.Should().BeNull();
        domain.HasLostVerification.Should().BeFalse();
        domain.Status.Should().Be(SendingDomainStatus.Verified);
        domain.VerifiedAt.Should().Be(First.AddDays(3));
    }

    [Fact]
    public void A_confirmed_check_of_a_domain_that_was_never_lost_leaves_it_unmarked()
    {
        var domain = Verified();

        domain.RecordCheck(Records(SendingDomainRecordState.Matches), true, null, First.AddDays(1));

        domain.VerificationLostAt.Should().BeNull();
    }

    private static List<SendingDomainRecord> Records(SendingDomainRecordState state) =>
    [
        new(SendingDomainRecordPurpose.Dkim, "CNAME", "s1._domainkey.acme.example.org", "s1.domainkey.u1.wl.sendgrid.net", state),
        new(SendingDomainRecordPurpose.Replies, "MX", "reply.acme.example.org", "mx.sendgrid.net", state, 10),
    ];
}
