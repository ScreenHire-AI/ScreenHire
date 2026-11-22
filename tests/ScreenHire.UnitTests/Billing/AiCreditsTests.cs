using ScreenHire.Domain.Billing;

namespace ScreenHire.UnitTests.Billing;

/// <summary>
/// The pure rules of prepaid AI credits (ADR-0032, docs/architecture/billing.md "AI credits"): which part of a grant is written off
/// when it expires, whether auto-reload runs, and which alert a balance calls for. Every edge is a row here, so the integration
/// tests only prove the wiring.
/// </summary>
public sealed class AiCreditsTests
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Apr = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static CreditMovement Grant(Guid id, DateTimeOffset at, decimal dollars, DateTimeOffset? expires) =>
        new(id, at, AiCharge.ToMicros(dollars), expires, CreditEntryKind.Purchase);

    private static CreditMovement Debit(DateTimeOffset at, decimal dollars) =>
        new(Guid.CreateVersion7(), at, -AiCharge.ToMicros(dollars), null, CreditEntryKind.Usage);

    private static ExpiringCredit Left(Guid grant, decimal dollars) => new(grant, AiCharge.ToMicros(dollars));

    // ---- CreditLots.Expiring ----

    [Fact]
    public void A_grant_that_expired_with_nothing_used_writes_off_all_of_it()
    {
        var expiring = CreditLots.Expiring([Grant(A, Jan, 10m, Mar)], Apr);

        expiring.Should().Equal(Left(A, 10m));
    }

    [Fact]
    public void A_grant_that_has_not_expired_yet_writes_off_nothing()
    {
        CreditLots.Expiring([Grant(A, Jan, 10m, Mar)], Feb).Should().BeEmpty();
    }

    [Fact]
    public void Calls_use_the_grant_that_expires_first_so_the_later_one_keeps_its_whole_amount()
    {
        // B is older but expires later: first expiry first, not first in first out.
        var ledger = new[] { Grant(B, Jan, 10m, Apr), Grant(A, Feb, 10m, Mar), Debit(Feb.AddDays(1), 4m) };

        CreditLots.Expiring(ledger, Jun).Should().BeEquivalentTo([Left(A, 6m), Left(B, 10m)]);
    }

    [Fact]
    public void Of_two_grants_with_the_same_expiry_the_older_is_used_first()
    {
        var ledger = new[] { Grant(A, Jan, 10m, Apr), Grant(B, Feb, 10m, Apr), Debit(Feb.AddDays(1), 4m) };

        CreditLots.Expiring(ledger, Jun).Should().BeEquivalentTo([Left(A, 6m), Left(B, 10m)]);
    }

    [Fact]
    public void A_grant_that_never_expires_is_used_last_and_is_never_written_off()
    {
        var ledger = new[] { Grant(A, Jan, 10m, null), Grant(B, Feb, 10m, Apr), Debit(Feb.AddDays(1), 4m) };

        CreditLots.Expiring(ledger, Jun).Should().Equal(Left(B, 6m));
    }

    [Fact]
    public void A_call_larger_than_the_grant_it_draws_on_continues_into_the_next_one()
    {
        var ledger = new[] { Grant(A, Jan, 5m, Mar), Grant(B, Jan, 10m, Apr), Debit(Feb, 8m) };

        CreditLots.Expiring(ledger, Jun).Should().Equal(Left(B, 7m));
    }

    [Fact]
    public void A_debit_that_overshoots_the_balance_is_repaid_by_the_next_grant_before_it_can_expire()
    {
        // Two checks ran at once and took the balance 3 below zero. The next 10 buys only 7 of usable credit.
        var ledger = new[] { Grant(A, Jan, 5m, Mar), Debit(Feb, 8m), Grant(B, Feb.AddDays(1), 10m, Apr) };

        CreditLots.Expiring(ledger, Jun).Should().Equal(Left(B, 7m)); // A was spent and the shortfall took 3 of B
    }

    [Fact]
    public void A_grant_that_only_repays_a_shortfall_writes_off_nothing()
    {
        var ledger = new[] { Grant(A, Jan, 5m, Mar), Debit(Feb, 8m), Grant(B, Feb.AddDays(1), 3m, Apr) };

        CreditLots.Expiring(ledger, Jun).Should().BeEmpty();
    }

    [Fact]
    public void An_expiry_already_recorded_is_not_returned_again()
    {
        var recorded = new CreditMovement(Guid.CreateVersion7(), Apr, -AiCharge.ToMicros(10m), null, CreditEntryKind.Expiry, ExpiresGrant: A);

        CreditLots.Expiring([Grant(A, Jan, 10m, Mar), recorded], Jun).Should().BeEmpty();
    }

    [Fact]
    public void A_recorded_expiry_for_one_grant_leaves_another_to_be_returned()
    {
        var recorded = new CreditMovement(Guid.CreateVersion7(), Apr, -AiCharge.ToMicros(10m), null, CreditEntryKind.Expiry, ExpiresGrant: A);

        CreditLots.Expiring([Grant(A, Jan, 10m, Mar), Grant(B, Jan, 5m, Mar), recorded], Jun).Should().Equal(Left(B, 5m));
    }

    [Fact]
    public void A_grant_expiring_at_the_moment_of_a_call_is_gone_before_the_call_so_the_call_uses_the_next_grant()
    {
        var ledger = new[] { Grant(A, Jan, 10m, Mar), Grant(B, Jan, 10m, Apr), Debit(Mar, 4m) };

        CreditLots.Expiring(ledger, Jun).Should().BeEquivalentTo([Left(A, 10m), Left(B, 6m)]);
    }

    [Fact]
    public void A_call_just_before_the_expiry_still_uses_the_expiring_grant()
    {
        var ledger = new[] { Grant(A, Jan, 10m, Mar), Grant(B, Jan, 10m, Apr), Debit(Mar.AddTicks(-1), 4m) };

        CreditLots.Expiring(ledger, Jun).Should().BeEquivalentTo([Left(A, 6m), Left(B, 10m)]);
    }

    [Fact]
    public void A_grant_spent_in_full_before_it_expires_writes_off_nothing()
    {
        CreditLots.Expiring([Grant(A, Jan, 10m, Mar), Debit(Feb, 10m)], Jun).Should().BeEmpty();
    }

    // ---- AutoReload.Decide ----

    private static CreditSettings Settings(bool on = true, decimal? below = 5m, decimal? amount = 25m, decimal? max = null, bool card = true)
    {
        var settings = CreditSettings.For(Guid.CreateVersion7(), 5m, Jan);
        settings.SetAutoReload(on, below, amount, max, Jan);
        if (card)
        {
            settings.SaveCard("pm_fictional", Jan);
        }

        return settings;
    }

    private static long Micros(decimal dollars) => AiCharge.ToMicros(dollars);

    [Fact]
    public void Auto_reload_adds_the_configured_amount_when_the_balance_is_below_the_threshold()
    {
        var decision = AutoReload.Decide(Settings(), Micros(4.99m), 0m, reloadPending: false);

        decision.Outcome.Should().Be(AutoReloadOutcome.Reload);
        decision.Amount.Should().Be(25m);
        decision.Reloads.Should().BeTrue();
    }

    [Fact]
    public void Auto_reload_runs_at_a_negative_balance()
    {
        AutoReload.Decide(Settings(), Micros(-1m), 0m, false).Reloads.Should().BeTrue();
    }

    [Fact]
    public void Auto_reload_does_not_run_at_the_threshold_itself()
    {
        AutoReload.Decide(Settings(), Micros(5m), 0m, false).Outcome.Should().Be(AutoReloadOutcome.AboveThreshold);
    }

    [Fact]
    public void Auto_reload_is_off_with_no_settings_or_when_switched_off_or_missing_its_amounts()
    {
        AutoReload.Decide(null, 0, 0m, false).Outcome.Should().Be(AutoReloadOutcome.Off);
        AutoReload.Decide(Settings(on: false), 0, 0m, false).Outcome.Should().Be(AutoReloadOutcome.Off);
        AutoReload.Decide(Settings(below: null), 0, 0m, false).Outcome.Should().Be(AutoReloadOutcome.Off);
        AutoReload.Decide(Settings(amount: null), 0, 0m, false).Outcome.Should().Be(AutoReloadOutcome.Off);
    }

    [Fact]
    public void Auto_reload_with_no_saved_card_does_nothing_and_says_why()
    {
        AutoReload.Decide(Settings(card: false), 0, 0m, false).Outcome.Should().Be(AutoReloadOutcome.NoCard);
    }

    [Fact]
    public void Auto_reload_waits_while_an_earlier_reload_is_still_waiting_for_its_payment()
    {
        AutoReload.Decide(Settings(), 0, 0m, reloadPending: true).Outcome.Should().Be(AutoReloadOutcome.AlreadyReloading);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(25, true)] // 25 + 25 is exactly the maximum
    [InlineData(25.01, false)]
    [InlineData(50, false)]
    public void The_monthly_maximum_counts_what_auto_reload_already_charged_this_month(double reloaded, bool reloads)
    {
        var decision = AutoReload.Decide(Settings(max: 50m), 0, (decimal)reloaded, false);

        decision.Reloads.Should().Be(reloads);
        if (!reloads)
        {
            decision.Outcome.Should().Be(AutoReloadOutcome.MonthlyMaximum);
        }
    }

    [Fact]
    public void With_no_monthly_maximum_auto_reload_keeps_running_however_much_it_charged()
    {
        AutoReload.Decide(Settings(max: null), 0, 10_000m, false).Reloads.Should().BeTrue();
    }

    // ---- CreditAlerts.Due ----

    [Theory]
    [InlineData(0)]
    [InlineData(-250_000)]
    public void A_balance_at_or_below_zero_is_empty(long micros)
    {
        CreditAlerts.Due(micros, 5m, reloadStarting: false).Should().Be(CreditAlert.Empty);
    }

    [Fact]
    public void A_balance_below_the_low_balance_alert_is_low()
    {
        CreditAlerts.Due(Micros(4.99m), 5m, false).Should().Be(CreditAlert.Low);
    }

    [Fact]
    public void A_balance_at_the_alert_or_above_it_is_quiet()
    {
        CreditAlerts.Due(Micros(5m), 5m, false).Should().BeNull();
        CreditAlerts.Due(Micros(40m), 5m, false).Should().BeNull();
    }

    [Fact]
    public void An_alert_of_zero_never_says_low_only_empty()
    {
        CreditAlerts.Due(1, 0m, false).Should().BeNull();
        CreditAlerts.Due(0, 0m, false).Should().Be(CreditAlert.Empty);
    }

    [Theory]
    [InlineData(-1_000_000)]
    [InlineData(0)]
    [InlineData(2_000_000)]
    public void No_alert_goes_out_while_a_reload_is_starting_because_the_reload_is_the_remedy(long micros)
    {
        CreditAlerts.Due(micros, 5m, reloadStarting: true).Should().BeNull();
    }

    [Fact]
    public void A_balance_at_zero_is_empty_whenever_no_reload_is_starting_even_if_auto_reload_is_on_but_stopped_by_its_maximum()
    {
        // The rule is given only whether a reload is starting. Auto-reload being on, with its monthly maximum reached, is not "starting".
        var decision = AutoReload.Decide(Settings(max: 25m), 0, 25m, false);

        decision.Outcome.Should().Be(AutoReloadOutcome.MonthlyMaximum);
        CreditAlerts.Due(0, 5m, reloadStarting: decision.Reloads).Should().Be(CreditAlert.Empty);
    }
}
