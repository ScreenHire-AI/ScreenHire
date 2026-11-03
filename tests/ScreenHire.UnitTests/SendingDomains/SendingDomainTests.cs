using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.SendingDomains;

/// <summary>
/// The organization's sending domain (docs/architecture/email.md): which names are accepted, and the rule that mail
/// switches to the company's address only when the provider confirms the records.
/// </summary>
public sealed class SendingDomainTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("Sub.Example.COM.", "sub.example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("a-b.co.uk", "a-b.co.uk")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("a1.b2.c3.example.org", "a1.b2.c3.example.org")]
    public void A_plain_domain_name_is_stored_lower_case_without_a_trailing_dot(string input, string stored)
    {
        SendingDomain.TryNormalize(input, out var domain).Should().BeTrue();

        domain.Should().Be(stored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("192.168.0.1")]
    [InlineData("example.123")]
    [InlineData("casey@example.com")]
    [InlineData("http://x.com")]
    [InlineData("https://example.com/path")]
    [InlineData("example.com/path")]
    [InlineData("example.com:8080")]
    [InlineData("*.example.com")]
    [InlineData("example")]
    [InlineData("my_domain.example.com")]
    [InlineData("-bad.example.com")]
    [InlineData("bad-.example.com")]
    [InlineData("exa mple.com")]
    [InlineData("example..com")]
    [InlineData(".example.com")]
    public void Anything_that_is_not_a_domain_a_company_owns_is_refused(string? input)
    {
        SendingDomain.TryNormalize(input, out var domain).Should().BeFalse();

        domain.Should().BeEmpty();
    }

    [Fact]
    public void A_label_over_63_characters_or_a_name_too_long_to_carry_the_reply_label_is_refused()
    {
        SendingDomain.TryNormalize($"{new string('a', 64)}.example.com", out _).Should().BeFalse();
        SendingDomain.TryNormalize($"{new string('a', 63)}.example.com", out _).Should().BeTrue();

        var fourLabels = string.Join('.', Enumerable.Repeat(new string('a', 60), 4));
        SendingDomain.TryNormalize(fourLabels, out _).Should().BeTrue();
        SendingDomain.TryNormalize(fourLabels + "." + new string('b', 10), out _).Should().BeFalse("the name plus reply. must fit in 253 characters");
        SendingDomain.TryNormalize(string.Join('.', Enumerable.Repeat(new string('a', 60), 5)), out _).Should().BeFalse();
    }

    [Fact]
    public void Mail_comes_from_hiring_at_the_domain_and_replies_arrive_on_its_reply_subdomain()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };

        domain.FromAddress.Should().Be("hiring@acme.example.org");
        domain.ReplyHost.Should().Be("reply.acme.example.org");
    }

    [Fact]
    public void A_new_domain_is_pending_and_mail_keeps_ScreenHires_address()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };

        domain.Status.Should().Be(SendingDomainStatus.Pending);
        domain.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void The_providers_yes_verifies_the_domain_and_records_when()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };

        domain.RecordCheck(Matching(), providerConfirmed: true, providerMessage: null, First);

        domain.IsVerified.Should().BeTrue();
        domain.VerifiedAt.Should().Be(First);
        domain.LastCheckedAt.Should().Be(First);
        domain.ProviderMessage.Should().BeNull();
        domain.Records.Should().OnlyContain(r => r.State == SendingDomainRecordState.Matches);
    }

    [Fact]
    public void A_second_confirmation_keeps_the_first_verification_time()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };
        domain.RecordCheck(Matching(), true, null, First);

        domain.RecordCheck(Matching(), true, null, First.AddDays(1));

        domain.VerifiedAt.Should().Be(First);
        domain.LastCheckedAt.Should().Be(First.AddDays(1));
    }

    [Fact]
    public void Records_in_place_without_the_providers_yes_leave_the_domain_pending_with_the_providers_reason()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };

        domain.RecordCheck(Matching(), providerConfirmed: false, providerMessage: "Could not find DKIM record.", First);

        domain.IsVerified.Should().BeFalse("only the provider's yes verifies, so mail is never signed before the provider can");
        domain.ProviderMessage.Should().Be("Could not find DKIM record.");
        domain.VerifiedAt.Should().BeNull();
    }

    [Fact]
    public void A_verified_domain_whose_check_later_fails_returns_to_pending()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };
        domain.RecordCheck(Matching(), true, null, First);

        domain.RecordCheck(Matching(), false, "A record was removed.", First.AddDays(2));

        domain.Status.Should().Be(SendingDomainStatus.Pending);
        domain.VerifiedAt.Should().BeNull();
        domain.ProviderMessage.Should().Be("A record was removed.");
    }

    [Fact]
    public void A_confirmation_clears_an_earlier_providers_message()
    {
        var domain = new SendingDomain { Domain = "acme.example.org" };
        domain.RecordCheck(Matching(), false, "Could not find DKIM record.", First);

        domain.RecordCheck(Matching(), true, "stale words", First.AddHours(1));

        domain.ProviderMessage.Should().BeNull();
        domain.IsVerified.Should().BeTrue();
    }

    private static List<SendingDomainRecord> Matching() =>
    [
        new(SendingDomainRecordPurpose.Dkim, "CNAME", "s1._domainkey.acme.example.org", "s1.domainkey.u1.wl.sendgrid.net", SendingDomainRecordState.Matches),
        new(SendingDomainRecordPurpose.Replies, "MX", "reply.acme.example.org", "mx.sendgrid.net", SendingDomainRecordState.Matches, 10),
    ];
}
