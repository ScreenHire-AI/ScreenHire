using ScreenHire.Application.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>The duplicate re-check message: one intent has one key, so a redelivery or a second send is dropped.</summary>
public sealed class RecheckApplicationDuplicatesTests
{
    [Fact]
    public void The_same_application_rechecked_for_the_same_cause_has_one_key()
    {
        var organizationId = Guid.CreateVersion7();
        var application = Guid.CreateVersion7();
        var cause = Guid.CreateVersion7();

        var first = new RecheckApplicationDuplicates(organizationId, application, cause);
        var second = new RecheckApplicationDuplicates(organizationId, application, cause);

        first.IdempotencyKey.Should().Be(second.IdempotencyKey);
        first.IdempotencyKey.Should().Be($"recheck-duplicates:{application}:{cause}:matched");
    }

    [Fact]
    public void A_different_cause_or_a_different_application_is_a_different_intent()
    {
        var organizationId = Guid.CreateVersion7();
        var application = Guid.CreateVersion7();
        var cause = Guid.CreateVersion7();
        var key = new RecheckApplicationDuplicates(organizationId, application, cause).IdempotencyKey;

        new RecheckApplicationDuplicates(organizationId, application, Guid.CreateVersion7()).IdempotencyKey.Should().NotBe(key);
        new RecheckApplicationDuplicates(organizationId, Guid.CreateVersion7(), cause).IdempotencyKey.Should().NotBe(key);
        new RecheckApplicationDuplicates(organizationId, cause, application).IdempotencyKey.Should().NotBe(key, "the two directions are separate rechecks");
    }

    [Fact]
    public void An_erasure_is_not_a_repeat_of_the_match_by_the_same_application()
    {
        var organizationId = Guid.CreateVersion7();
        var application = Guid.CreateVersion7();
        var cause = Guid.CreateVersion7();

        new RecheckApplicationDuplicates(organizationId, application, cause, CauseErased: true).IdempotencyKey
            .Should().NotBe(new RecheckApplicationDuplicates(organizationId, application, cause).IdempotencyKey, "the later application matched, then was erased");
    }
}
