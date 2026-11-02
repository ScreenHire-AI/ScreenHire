using Microsoft.Extensions.Options;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Email;

/// <summary>
/// The address a candidate's answer goes to (docs/architecture/email.md, ADR-0021): <c>reply+&lt;58 base32 characters&gt;@host</c>
/// carrying the organization, the send and a short MAC under the reply key. A damaged or foreign address finds nothing.
/// </summary>
public sealed class ReplyAddressesTests
{
    private const string Key = "unit-test-reply-key-0123456789abcdef";

    private static readonly Guid Organization = Guid.Parse("0197a3c4-5b6d-7e8f-9a0b-1c2d3e4f5a6b");
    private static readonly Guid Send = Guid.Parse("0197a3c4-aaaa-7bbb-8ccc-ddddeeeeffff");

    private static ReplyAddresses Addresses(string? key = Key, string? domain = "reply.example.com", bool sandbox = false) =>
        new(Options.Create(new EmailOptions { ReplyKey = key, ReplyDomain = domain, Sandbox = new EmailSandboxOptions { Enabled = sandbox } }));

    [Fact]
    public void An_address_made_for_a_send_parses_back_to_its_organization_and_send()
    {
        var addresses = Addresses();

        var address = addresses.For(Organization, Send);

        address.Should().StartWith("reply+").And.EndWith("@reply.example.com");
        addresses.TryParse(address).Should().Be(new ReplyTarget(Organization, Send));
    }

    [Fact]
    public void The_local_part_is_exactly_the_64_characters_mail_systems_allow()
    {
        var address = Addresses().For(Organization, Send)!;

        var local = address[..address.IndexOf('@', StringComparison.Ordinal)];

        local.Length.Should().Be(64);
        local.Length.Should().Be(ReplyAddresses.MaxLocalPartLength);
        local["reply+".Length..].Length.Should().Be(58);
        local.Should().MatchRegex("^reply\\+[a-z2-7]{58}$", "lowercase base32 survives a mail system that changes case");
    }

    [Fact]
    public void An_address_whose_case_was_changed_in_transit_still_parses()
    {
        var addresses = Addresses();
        var address = addresses.For(Organization, Send)!;

        addresses.TryParse(address.ToUpperInvariant()).Should().Be(new ReplyTarget(Organization, Send));
        addresses.TryParse($"<{address}>").Should().Be(new ReplyTarget(Organization, Send), "an angle-bracketed envelope address parses");
    }

    [Fact]
    public void The_reply_subdomain_of_a_sending_domain_replaces_the_default_host()
    {
        var addresses = Addresses();

        var address = addresses.For(Organization, Send, "reply.acme.example.org");

        address.Should().EndWith("@reply.acme.example.org");
        addresses.TryParse(address).Should().Be(new ReplyTarget(Organization, Send), "the token, not the host, is what is checked");
    }

    [Fact]
    public void An_address_with_one_character_changed_finds_nothing()
    {
        var addresses = Addresses();
        var address = addresses.For(Organization, Send)!;

        // A character in the middle: the last base32 character holds two bits that decode to nothing.
        var at = "reply+".Length + 10;
        var changed = address[at] == 'a' ? 'b' : 'a';
        var tampered = address[..at] + changed + address[(at + 1)..];

        addresses.TryParse(tampered).Should().BeNull();
    }

    [Fact]
    public void An_address_signed_with_another_key_finds_nothing()
    {
        var address = Addresses("another-key-another-key-another-key-1").For(Organization, Send);

        Addresses().TryParse(address).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("jordan@reply.example.com")]
    [InlineData("reply@reply.example.com")]
    [InlineData("reply+@reply.example.com")]
    [InlineData("reply+!!!!@reply.example.com")]
    [InlineData("reply+abc@reply.example.com")]
    [InlineData("other+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@reply.example.com")]
    public void An_address_that_is_not_one_of_ours_finds_nothing(string? address)
    {
        Addresses().TryParse(address).Should().BeNull();
    }

    [Fact]
    public void An_address_with_a_longer_token_than_ours_finds_nothing()
    {
        var addresses = Addresses();
        var address = addresses.For(Organization, Send)!;
        var longer = address.Replace("@", "aa@", StringComparison.Ordinal);

        addresses.TryParse(longer).Should().BeNull();
    }

    [Fact]
    public void Without_a_reply_key_no_address_is_made_and_none_is_accepted()
    {
        var address = Addresses().For(Organization, Send);

        var off = Addresses(key: null);

        off.Enabled.Should().BeFalse();
        off.For(Organization, Send).Should().BeNull();
        off.TryParse(address).Should().BeNull();
        Addresses(key: "").Enabled.Should().BeFalse();
    }

    [Fact]
    public void A_key_alone_is_not_enough_without_a_reply_domain_or_the_sandbox()
    {
        Addresses(domain: null).Enabled.Should().BeFalse("replies need somewhere to arrive");
        Addresses(domain: " ").For(Organization, Send).Should().BeNull();
    }

    [Fact]
    public void The_sandbox_replies_arrive_on_reply_localhost_when_no_domain_is_set()
    {
        var addresses = Addresses(domain: null, sandbox: true);

        addresses.Enabled.Should().BeTrue();
        addresses.DefaultHost.Should().Be("reply.localhost");
        addresses.For(Organization, Send).Should().EndWith("@reply.localhost");
    }

    [Fact]
    public void The_default_host_is_the_configured_domain_in_lower_case()
    {
        Addresses(domain: " Reply.Example.COM ").DefaultHost.Should().Be("reply.example.com");
    }
}
