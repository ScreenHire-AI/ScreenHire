using Microsoft.AspNetCore.DataProtection;
using ScreenHire.Application.Scheduling;
using ScreenHire.Application.Slack;
using ScreenHire.Domain.Scheduling;
using ScreenHire.UnitTests.Distribution;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The OAuth <c>state</c>: the round trip's facts, protected and limited to <see cref="CalendarOAuthState.Lifetime"/>.
/// Data Protection's time-limited protector also compares the expiry with the real clock, so these tests run at a fixed
/// instant far in the future: a state issued then is still valid by the real clock, and only the injected clock can
/// expire it.
/// </summary>
public sealed class CalendarOAuthStateTests
{
    private static readonly DateTimeOffset Start = new(2100, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FixedTime clock = new(Start);
    private readonly CalendarOAuthState states;
    private readonly Guid organization = Guid.CreateVersion7();
    private readonly Guid user = Guid.CreateVersion7();

    public CalendarOAuthStateTests() => states = new CalendarOAuthState(new EphemeralDataProtectionProvider(), clock);

    [Fact]
    public void A_state_gives_back_who_started_the_round_trip_with_which_provider_and_the_nonce_its_cookie_holds()
    {
        var (state, nonce) = states.Issue(CalendarProvider.Microsoft, organization, user, "/jobs");

        var read = states.Read(state);

        read.Should().NotBeNull();
        read!.Provider.Should().Be(CalendarProvider.Microsoft);
        read.OrganizationId.Should().Be(organization);
        read.UserId.Should().Be(user);
        read.Nonce.Should().Be(nonce).And.NotBeEmpty();
        read.ReturnUrl.Should().Be("/jobs");
    }

    [Fact]
    public void Each_state_has_its_own_nonce()
    {
        states.Issue(CalendarProvider.Google, organization, user, "/").Nonce
            .Should().NotBe(states.Issue(CalendarProvider.Google, organization, user, "/").Nonce);
    }

    [Fact]
    public void A_state_is_valid_until_its_lifetime_has_passed_and_not_after()
    {
        var (state, _) = states.Issue(CalendarProvider.Google, organization, user, "/");

        clock.Now = Start + CalendarOAuthState.Lifetime - TimeSpan.FromSeconds(1);
        states.Read(state).Should().NotBeNull("the person is still on the provider's page");

        clock.Now = Start + CalendarOAuthState.Lifetime + TimeSpan.FromSeconds(1);
        states.Read(state).Should().BeNull("ten minutes is long enough to approve access");
    }

    [Fact]
    public void A_tampered_or_foreign_state_does_not_read()
    {
        var (state, _) = states.Issue(CalendarProvider.Google, organization, user, "/");
        var tampered = state[..^3] + (state.EndsWith("AAA", StringComparison.Ordinal) ? "BBB" : "AAA");
        var (foreign, _) = new CalendarOAuthState(new EphemeralDataProtectionProvider(), clock)
            .Issue(CalendarProvider.Google, organization, user, "/");

        states.Read(tampered).Should().BeNull();
        states.Read(foreign).Should().BeNull("another key ring never issued it");
        states.Read("garbage").Should().BeNull();
        states.Read("").Should().BeNull();
        states.Read(null).Should().BeNull();
        states.Read(new string('a', 4001)).Should().BeNull();
    }

    [Fact]
    public void A_state_from_another_flow_with_the_same_key_ring_does_not_read()
    {
        var keyRing = new EphemeralDataProtectionProvider();
        var (slack, _) = new SlackOAuthState(keyRing, clock).Issue(SlackOAuthPurpose.Install, organization, user, "/");

        new CalendarOAuthState(keyRing, clock).Read(slack).Should().BeNull("each flow has its own purpose");
    }
}
