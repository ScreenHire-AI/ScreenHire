using System.Text.Json;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The names integrators, workflows and roles use say verification (ADR-0019), and the rejection guardrail still holds
/// a fact written before the rename (CLAUDE.md non-negotiable 2).
/// </summary>
public sealed class VerificationNamesTests
{
    [Theory]
    [InlineData("verification.authenticity")]
    [InlineData("verification.aiAssistance")]
    [InlineData("verification.result")]
    [InlineData("integrity.authenticity")]
    [InlineData("integrity.aiAssistance")]
    public void A_verification_fact_and_a_leftover_integrity_fact_are_both_signals(string fact) =>
        FactSchema.IsSignal(fact).Should().BeTrue();

    [Fact]
    public void The_fact_catalogue_names_the_levels_verification()
    {
        var names = FactSchema.ApplicationFacts.Select(f => f.Name).ToList();

        names.Should().Contain(["verification.authenticity", "verification.aiAssistance"]);
        names.Should().NotContain(name => name.StartsWith("integrity.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_webhook_event_is_verification_assessed()
    {
        WebhookEventTypes.All.Should().Contain("verification.assessed");
        WebhookEventTypes.All.Should().NotContain(type => type.Contains("integrity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_review_permission_is_review_verification()
    {
        RolePermissions.Permissions.Select(p => p.Permission).Should().Contain(Permission.ReviewVerification);
        Enum.GetNames<Permission>().Should().NotContain(name => name.Contains("Integrity", StringComparison.Ordinal));
    }

    [Fact]
    public void A_workflow_serializes_its_policy_and_trigger_with_their_new_names()
    {
        var json = JsonSerializer.Serialize(
            new AutomationTrigger(TriggerType.VerificationAssessed), JsonColumns.SerializerOptions);

        json.Should().Contain("\"VerificationAssessed\"");
        Enum.GetNames<TriggerType>().Should().NotContain(name => name.Contains("Integrity", StringComparison.Ordinal));
        typeof(HiringWorkflow).GetProperty("VerificationPolicy").Should().NotBeNull();
    }
}
