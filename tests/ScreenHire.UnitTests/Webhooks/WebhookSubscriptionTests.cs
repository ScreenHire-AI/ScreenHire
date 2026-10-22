using System.Net;
using System.Text.Json;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Integrity;
using ScreenHire.Application.Webhooks;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.UnitTests.Webhooks;

public sealed class WebhookSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("127.0.0.1", false, false)]
    [InlineData("127.0.0.1", true, true)]
    [InlineData("::1", true, true)]
    [InlineData("::ffff:127.0.0.1", true, true)]
    [InlineData("10.0.0.1", true, false)]
    [InlineData("192.168.1.10", true, false)]
    [InlineData("169.254.169.254", true, false)]
    [InlineData("93.184.216.34", false, true)]
    public void Loopback_is_reachable_only_when_allowed_and_private_ranges_never(string address, bool allowLoopback, bool reachable) =>
        WebhookAddresses.MayReach(IPAddress.Parse(address), allowLoopback).Should().Be(reachable);

    [Fact]
    public void Each_endpoint_and_secret_version_has_its_own_secret_derived_from_the_platform_key()
    {
        var endpoint = Guid.CreateVersion7();
        var secret = WebhookSignature.EndpointSecret("platform", endpoint, 1);

        secret.Should().StartWith("whsec_").And.HaveLength("whsec_".Length + 64).And.Be(WebhookSignature.EndpointSecret("platform", endpoint, 1));
        WebhookSignature.EndpointSecret("platform", endpoint, 2).Should().NotBe(secret);
        WebhookSignature.EndpointSecret("platform", Guid.CreateVersion7(), 1).Should().NotBe(secret);
        WebhookSignature.EndpointSecret("other-platform", endpoint, 1).Should().NotBe(secret);
    }

    [Fact]
    public void A_domain_event_always_raises_the_same_webhook_event_id_and_types_never_share_one()
    {
        var submitted = new ApplicationSubmitted(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "applied", Guid.CreateVersion7(), Now);

        WebhookEvents.From(submitted).EventId.Should().Be(WebhookEvents.From(submitted).EventId);
        WebhookEvents.From(submitted).Body.Should().Be(WebhookEvents.From(submitted).Body);
        WebhookEvents.EventId("application.submitted", "key").Should().NotBe(WebhookEvents.EventId("application.moved", "key"));
        WebhookEvents.EventId("job.published", "key").Version.Should().Be(8);
    }

    [Fact]
    public void The_body_carries_the_event_envelope_and_ids_only()
    {
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var raised = WebhookEvents.From(new ApplicationMoved(organizationId, applicationId, Guid.CreateVersion7(), "applied", "screening", Guid.CreateVersion7(), Now));

        using var body = JsonDocument.Parse(raised.Body);
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("id", "type", "occurredAt", "organizationId", "data");
        root.GetProperty("id").GetGuid().Should().Be(raised.EventId);
        root.GetProperty("type").GetString().Should().Be(WebhookEventTypes.ApplicationMoved).And.Be(raised.EventType);
        root.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
        root.GetProperty("organizationId").GetGuid().Should().Be(organizationId);
        root.GetProperty("data").EnumerateObject().Select(p => p.Name).Should().Equal("applicationId", "jobId", "fromStageId", "toStageId");
        raised.OrganizationId.Should().Be(organizationId);
    }

    // Evidence, not verdicts (ADR-0004): two categorical levels and the ruleset, never a score or the evidence itself.
    [Fact]
    public void Integrity_assessed_carries_the_levels_and_the_ruleset_and_nothing_else()
    {
        var raised = WebhookEvents.From(new IntegrityAssessed(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), AuthenticityLevel.ReviewSuggested, AiAssistanceLevel.SomeIndicators, "2026.10.1",
            Guid.CreateVersion7(), Now));

        using var body = JsonDocument.Parse(raised.Body);
        var data = body.RootElement.GetProperty("data");
        data.EnumerateObject().Select(p => p.Name).Should().Equal("applicationId", "jobId", "authenticity", "aiAssistance", "rulesetVersion");
        data.EnumerateObject().Should().NotContain(p => p.Value.ValueKind == JsonValueKind.Number);
        data.GetProperty("authenticity").GetString().Should().Be(nameof(AuthenticityLevel.ReviewSuggested));
        data.GetProperty("aiAssistance").GetString().Should().Be(nameof(AiAssistanceLevel.SomeIndicators));
    }

    [Theory]
    [InlineData(AutomationRunStatus.Failed, true)]
    [InlineData(AutomationRunStatus.Blocked, true)]
    [InlineData(AutomationRunStatus.Completed, false)]
    public void Only_failed_and_blocked_runs_raise_automation_failed(AutomationRunStatus status, bool raised)
    {
        var finished = new AutomationRunFinished(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "hook", status, Now);

        var webhook = WebhookEvents.From(finished);

        (webhook is not null).Should().Be(raised);
        if (webhook is not null)
        {
            webhook.EventType.Should().Be(WebhookEventTypes.AutomationFailed);
            webhook.Body.Should().Contain($"\"status\":\"{status}\"");
        }
    }

    [Fact]
    public void A_delivery_stays_pending_while_attempts_remain_and_ends_on_success_or_the_last_failure()
    {
        var delivery = WebhookDelivery.For(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "job.closed", "{}", Now);

        delivery.RecordAttempt(Now, 500, "The receiver answered HTTP 500.", Now.AddMinutes(1));
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.NextAttemptAt.Should().Be(Now.AddMinutes(1));

        delivery.RecordAttempt(Now.AddMinutes(1), null, new string('x', 500), null);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);
        delivery.Attempts.Should().Be(2);
        delivery.LastError.Should().HaveLength(WebhookDelivery.MaxErrorLength);
        delivery.CompletedAt.Should().Be(Now.AddMinutes(1));

        var replay = delivery.Replay(Now.AddHours(1));
        replay.ReplayOfId.Should().Be(delivery.Id);
        replay.EventId.Should().Be(delivery.EventId);
        replay.Status.Should().Be(WebhookDeliveryStatus.Pending);
        replay.RecordAttempt(Now.AddHours(1), 204, null, null);
        replay.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        replay.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Turning_an_endpoint_back_on_clears_an_automatic_disable()
    {
        var endpoint = WebhookEndpoint.Create(Guid.CreateVersion7(), "https://hooks.example.com/in", null, ["job.published"], Now);
        endpoint.SetEnabled(false, Now);
        endpoint.ConsecutiveFailures = 5;
        endpoint.DisabledReason = "Disabled automatically";

        endpoint.Receives("job.published").Should().BeFalse();
        endpoint.SetEnabled(true, Now.AddHours(1));

        endpoint.Receives("job.published").Should().BeTrue();
        endpoint.Receives("job.closed").Should().BeFalse();
        endpoint.ConsecutiveFailures.Should().Be(0);
        endpoint.DisabledReason.Should().BeNull();
        endpoint.DisabledAt.Should().BeNull();
    }

    [Theory]
    [InlineData("https://hooks.example.com/in", false, null)]
    [InlineData("https://203.0.113.10/in", false, null)]
    [InlineData("http://hooks.example.com/in", false, "url")]
    [InlineData("http://hooks.example.com/in", true, "url")]
    [InlineData("http://127.0.0.1:5000/in", false, "url")]
    [InlineData("http://127.0.0.1:5000/in", true, null)]
    [InlineData("https://localhost/in", false, "url")]
    [InlineData("https://10.1.2.3/in", true, "url")]
    [InlineData("https://[fd00::1]/in", false, "url")]
    [InlineData("https://user:secret@hooks.example.com/in", false, "url")]
    [InlineData("https://hooks.example.com/in#part", false, "url")]
    [InlineData("ftp://hooks.example.com/in", false, "url")]
    [InlineData("/relative", false, "url")]
    public void Urls_must_be_https_to_a_public_host_unless_the_local_stack_allows_loopback(string url, bool allowLoopback, string? problem)
    {
        var problems = WebhookValidation.Problems(url, ["job.published"], null, allowLoopback);

        if (problem is null)
        {
            problems.Should().BeNull();
        }
        else
        {
            problems!.Keys.Should().Equal(problem);
        }
    }

    [Fact]
    public void Events_must_be_known_and_there_must_be_at_least_one()
    {
        WebhookValidation.Problems("https://hooks.example.com/in", [], null, false)!.Keys.Should().Equal("events");
        WebhookValidation.Problems("https://hooks.example.com/in", ["job.published", "candidate.created"], null, false)!["events"][0]
            .Should().Contain("candidate.created");
        WebhookValidation.Problems("https://hooks.example.com/in", WebhookEventTypes.All, new string('x', 201), false)!.Keys.Should().Equal("description");
        WebhookValidation.Problems("https://hooks.example.com/in", WebhookEventTypes.All, "Our ATS bridge", false).Should().BeNull();
    }
}
