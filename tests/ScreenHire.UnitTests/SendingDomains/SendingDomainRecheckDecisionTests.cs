using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.SendingDomains;

/// <summary>
/// What one look at a sending domain leads to (docs/architecture/notifications.md, "The daily email domain check"): only a verified
/// domain can be lost, only a second failing look loses it, and a domain the Owners were told about is saved as it is until
/// it passes.
/// </summary>
public sealed class SendingDomainRecheckDecisionTests
{
    private const SendingDomainRecheckAction Save = SendingDomainRecheckAction.Save;
    private const SendingDomainRecheckAction Schedule = SendingDomainRecheckAction.ScheduleConfirm;
    private const SendingDomainRecheckAction Lose = SendingDomainRecheckAction.LoseAndNotify;
    private const SendingDomainRecheckAction Recover = SendingDomainRecheckAction.Recover;

    // status, hasLostVerification, probePassed, confirming, expected action
    public static TheoryData<SendingDomainStatus, bool, bool, bool, SendingDomainRecheckAction> Table => new()
    {
        // Healthy and verified: a failing daily look schedules a second look, and only a failing second look loses it.
        { SendingDomainStatus.Verified, false, true, false, Save },
        { SendingDomainStatus.Verified, false, true, true, Save },
        { SendingDomainStatus.Verified, false, false, false, Schedule },
        { SendingDomainStatus.Verified, false, false, true, Lose },

        // The Owners were already told: the rule does not depend on the status column.
        { SendingDomainStatus.Verified, true, true, false, Recover },
        { SendingDomainStatus.Verified, true, true, true, Recover },
        { SendingDomainStatus.Verified, true, false, false, Save },
        { SendingDomainStatus.Verified, true, false, true, Save },

        // Never verified: saved after each look, never notifies.
        { SendingDomainStatus.Pending, false, true, false, Save },
        { SendingDomainStatus.Pending, false, true, true, Save },
        { SendingDomainStatus.Pending, false, false, false, Save },
        { SendingDomainStatus.Pending, false, false, true, Save },

        // Lost and pending now (the usual state after the Owners were told): recovers only when the look passes.
        { SendingDomainStatus.Pending, true, true, false, Recover },
        { SendingDomainStatus.Pending, true, true, true, Recover },
        { SendingDomainStatus.Pending, true, false, false, Save },
        { SendingDomainStatus.Pending, true, false, true, Save },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Each_combination_of_state_and_look_leads_to_one_action(
        SendingDomainStatus status, bool hasLostVerification, bool probePassed, bool confirming, SendingDomainRecheckAction expected) =>
        SendingDomainRecheck.Decide(status, hasLostVerification, probePassed, confirming).Should().Be(expected);

    [Fact]
    public void The_table_has_a_row_for_every_combination()
    {
        Table.Count.Should().Be(2 * 2 * 2 * 2);
    }
}
