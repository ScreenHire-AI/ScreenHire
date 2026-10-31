using Microsoft.AspNetCore.DataProtection;
using ScreenHire.Application.Slack;
using ScreenHire.UnitTests.Distribution;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// The OAuth <c>state</c>: the round trip's facts, protected and limited to <see cref="SlackOAuthState.Lifetime"/>.
/// Data Protection's time-limited protector also compares the expiry with the real clock, so these tests run at a fixed
/// instant far in the future: a state issued then is still valid by the real clock, and only the injected clock can
/// expire it.
/// </summary>
public sealed class SlackOAuthStateTests
{
    private static readonly DateTimeOffset Start = new(2100, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FixedTime clock = new(Start);
    private readonly SlackOAuthState states;
    private readonly Guid organization = Guid.CreateVersion7();
    private readonly Guid user = Guid.CreateVersion7();

    public SlackOAuthStateTests() => states = new SlackOAuthState(new EphemeralDataProtectionProvider(), clock);

    [Fact]
    public void A_state_gives_back_who_started_the_round_trip_and_the_nonce_its_cookie_holds()
    {
        var (state, nonce) = states.Issue(SlackOAuthPurpose.Link, organization, user, "/settings/slack");

        var read = states.Read(state);

        read.Should().NotBeNull();
        read!.Purpose.Should().Be(SlackOAuthPurpose.Link);
        read.OrganizationId.Should().Be(organization);
        read.UserId.Should().Be(user);
        read.Nonce.Should().Be(nonce).And.NotBeEmpty();
        read.ReturnUrl.Should().Be("/settings/slack");
    }

    [Fact]
    public void Each_state_has_its_own_nonce()
    {
        states.Issue(SlackOAuthPurpose.Install, organization, user, "/settings/slack").Nonce
            .Should().NotBe(states.Issue(SlackOAuthPurpose.Install, organization, user, "/settings/slack").Nonce);
    }

    [Fact]
    public void A_state_is_valid_until_its_lifetime_has_passed_and_not_after()
    {
        var (state, _) = states.Issue(SlackOAuthPurpose.Install, organization, user, "/settings/slack");

        clock.Now = Start + SlackOAuthState.Lifetime - TimeSpan.FromSeconds(1);
        states.Read(state).Should().NotBeNull("the person is still on Slack's page");

        clock.Now = Start + SlackOAuthState.Lifetime + TimeSpan.FromSeconds(1);
        states.Read(state).Should().BeNull("ten minutes is long enough to approve an install");
    }

    [Fact]
    public void A_tampered_or_foreign_state_does_not_read()
    {
        var (state, _) = states.Issue(SlackOAuthPurpose.Install, organization, user, "/settings/slack");
        var tampered = state[..^3] + (state.EndsWith("AAA", StringComparison.Ordinal) ? "BBB" : "AAA");
        var (foreign, _) = new SlackOAuthState(new EphemeralDataProtectionProvider(), clock)
            .Issue(SlackOAuthPurpose.Install, organization, user, "/settings/slack");

        states.Read(tampered).Should().BeNull();
        states.Read(foreign).Should().BeNull("another key ring never issued it");
        states.Read("garbage").Should().BeNull();
        states.Read("").Should().BeNull();
        states.Read(null).Should().BeNull();
        states.Read(new string('a', 4001)).Should().BeNull();
    }
}
