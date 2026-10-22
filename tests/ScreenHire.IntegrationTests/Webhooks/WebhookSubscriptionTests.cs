using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Webhooks;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Webhooks;

/// <summary>
/// Webhook subscriptions through the running stack (docs/architecture/workflow-engine.md §5): a receiver on loopback
/// (<see cref="WebhookReceiver"/>) gets each subscribed event signed with its endpoint's secret, retried with backoff
/// when it fails, replayed on request, and an endpoint that keeps failing is disabled with the reason in the API. The
/// test stack shortens the retry delays and the disable limit (<see cref="AppHostFixture.WebhookRetryDelays"/>). Each
/// test uses its own organization and receiver.
/// </summary>
public sealed class WebhookSubscriptionTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Subscribed_events_arrive_signed_with_the_endpoint_secret_and_carry_ids_not_candidate_details()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var created = await CreateEndpointAsync(recruiter, receiver, "job.published", "application.submitted", "application.moved", "integrity.assessed");
        var secret = created.Text("signingSecret");
        var endpointId = created.Id("endpoint.id");
        secret.Should().StartWith("whsec_");
        created.Text("endpoint.events.0").Should().Be("application.moved", "events are stored sorted");

        var read = (await recruiter.GetAsync($"/v1/webhooks/{endpointId}")).Expect(HttpStatusCode.OK);
        read.Body.TryGetProperty("signingSecret", out _).Should().BeFalse("the secret is shown once");
        read.Body.GetRawText().Should().NotContain(secret);
        (await recruiter.GetAsync("/v1/webhooks")).Expect(HttpStatusCode.OK).Count().Should().Be(1);

        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Webhook Engineer"));
        await recruiter.PublishJobAsync(job.Id());
        var published = await receiver.WaitForEventAsync("job.published", job.Id(), Ct);
        published.IsSignedWith(secret).Should().BeTrue();
        published.IsSignedWith(secret + "0").Should().BeFalse();
        published.Header("Idempotency-Key").Should().Be(published.EventId.ToString());
        published.Header("ScreenHire-Event-Id").Should().Be(published.EventId.ToString());
        published.Header("ScreenHire-Event-Type").Should().Be("job.published");
        Guid.TryParse(published.Header("ScreenHire-Delivery-Id"), out _).Should().BeTrue();
        published.Json.GetProperty("organizationId").GetGuid().Should().Be(recruiter.OrganizationId);
        published.Json.GetProperty("data").GetProperty("jobId").GetGuid().Should().Be(job.Id());
        published.Json.GetProperty("data").GetProperty("version").GetInt32().Should().Be(1);

        var email = ApiSession.NewEmail("candidate");
        var application = await recruiter.CreateApplicationAsync(job.Id(), email, "Robin Lake");
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" }))
            .Expect(HttpStatusCode.OK);
        var submitted = await receiver.WaitForEventAsync("application.submitted", application.Id(), Ct);
        var moved = await receiver.WaitForEventAsync("application.moved", application.Id(), Ct);
        var assessed = await receiver.WaitForEventAsync("integrity.assessed", application.Id(), Ct);
        submitted.Json.GetProperty("data").GetProperty("stageId").GetString().Should().Be("applied");
        moved.Json.GetProperty("data").GetProperty("toStageId").GetString().Should().Be("screening");
        assessed.Json.GetProperty("data").GetProperty("authenticity").ValueKind.Should().Be(JsonValueKind.String, "a categorical level, never a score");
        foreach (var webhook in new[] { submitted, moved, assessed })
        {
            webhook.IsSignedWith(secret).Should().BeTrue();
            webhook.Body.Should().NotContain(email).And.NotContain("Robin").And.NotContain("Lake");
        }

        // Closing is not subscribed. A second publish after it is, so once that arrives the close has had its chance.
        await recruiter.CloseJobAsync(job.Id());
        var second = await recruiter.CreateJobAsync(Samples.RemoteJob("Second Webhook Engineer"));
        await recruiter.PublishJobAsync(second.Id());
        await receiver.WaitForEventAsync("job.published", second.Id(), Ct);
        receiver.Received.Select(r => r.Type).Should().NotContain("job.closed");

        // At least five: a later analyzer can reassess the application with a different result, which is another event.
        var log = await WaitForDeliveriesAsync(
            recruiter, endpointId, d => d.Count(e => e.Status == "Succeeded") >= 5 && d.All(e => e.Status == "Succeeded"), "five successful deliveries");
        var first = log.Single(d => d.EventId == published.EventId);
        first.Attempts.Should().Be(1);
        first.LastStatusCode.Should().Be(200);
        first.Payload.Should().Be(published.Body, "the log keeps the exact body that was signed");
    }

    [Fact]
    public async Task Rotating_the_secret_signs_later_deliveries_with_the_new_secret_only()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var created = await CreateEndpointAsync(recruiter, receiver, "job.published");
        var endpointId = created.Id("endpoint.id");
        var before = created.Text("signingSecret");

        var first = await PublishNewJobAsync(recruiter, "Before rotation");
        (await receiver.WaitForEventAsync("job.published", first, Ct)).IsSignedWith(before).Should().BeTrue();

        var rotated = (await recruiter.PostAsync($"/v1/webhooks/{endpointId}/rotate-secret")).Expect(HttpStatusCode.OK);
        var after = rotated.Text("signingSecret");
        after.Should().StartWith("whsec_").And.NotBe(before);

        var second = await PublishNewJobAsync(recruiter, "After rotation");
        var delivered = await receiver.WaitForEventAsync("job.published", second, Ct);
        delivered.IsSignedWith(after).Should().BeTrue();
        delivered.IsSignedWith(before).Should().BeFalse("the old secret stops at once");
    }

    [Fact]
    public async Task A_failing_receiver_is_retried_with_backoff_until_it_accepts()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        receiver.RespondWith(500, 503);
        var endpointId = (await CreateEndpointAsync(recruiter, receiver, "job.published")).Id("endpoint.id");

        var jobId = await PublishNewJobAsync(recruiter, "Retried");
        var attempts = await receiver.WaitForAsync(r => r.Body.Contains(jobId.ToString(), StringComparison.Ordinal), 3, "three attempts", Ct);

        attempts.Select(a => a.Body).Distinct().Should().ContainSingle("every attempt sends the same body");
        attempts.Select(a => a.Header("ScreenHire-Delivery-Id")).Distinct().Should().ContainSingle("the attempts are one delivery");
        (attempts[1].ReceivedAt - attempts[0].ReceivedAt).Should().BeGreaterThanOrEqualTo(AppHostFixture.WebhookRetryDelays[0]);
        (attempts[2].ReceivedAt - attempts[1].ReceivedAt).Should().BeGreaterThanOrEqualTo(AppHostFixture.WebhookRetryDelays[1]);

        var log = await WaitForDeliveriesAsync(recruiter, endpointId, d => d.Any(e => e.Status == "Succeeded"), "the delivery to succeed");
        var delivery = log.Single();
        delivery.Attempts.Should().Be(3);
        delivery.LastStatusCode.Should().Be(200);
        delivery.LastError.Should().BeNull();
        var endpoint = (await recruiter.GetAsync($"/v1/webhooks/{endpointId}")).Expect(HttpStatusCode.OK);
        endpoint.At("enabled").GetBoolean().Should().BeTrue();
        endpoint.At("consecutiveFailures").GetInt32().Should().Be(0, "a failed attempt that is later retried is not a failed delivery");
    }

    [Fact]
    public async Task Replaying_a_delivery_sends_the_same_event_again_as_a_delivery_of_its_own()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var created = await CreateEndpointAsync(recruiter, receiver, "job.published");
        var endpointId = created.Id("endpoint.id");
        var jobId = await PublishNewJobAsync(recruiter, "Replayed");
        var original = await receiver.WaitForEventAsync("job.published", jobId, Ct);
        var delivery = (await WaitForDeliveriesAsync(recruiter, endpointId, d => d.Any(e => e.Status == "Succeeded"), "the first delivery")).Single();

        var replay = (await recruiter.PostAsync($"/v1/webhooks/{endpointId}/deliveries/{delivery.Id}/replay")).Expect(HttpStatusCode.Accepted);
        replay.At("replayOfId").GetGuid().Should().Be(delivery.Id);
        replay.At("eventId").GetGuid().Should().Be(original.EventId);

        var both = await receiver.WaitForAsync(r => r.Body.Contains(jobId.ToString(), StringComparison.Ordinal), 2, "the replay", Ct);
        both[1].Body.Should().Be(original.Body);
        both[1].EventId.Should().Be(original.EventId, "receivers deduplicate on the event id");
        both[1].Header("ScreenHire-Delivery-Id").Should().Be(replay.Id().ToString());
        both[1].IsSignedWith(created.Text("signingSecret")).Should().BeTrue();

        var log = await WaitForDeliveriesAsync(recruiter, endpointId, d => d.Count(e => e.Status == "Succeeded") == 2, "the replay to succeed");
        log[0].ReplayOfId.Should().Be(delivery.Id, "newest first");
        (await recruiter.PostAsync($"/v1/webhooks/{endpointId}/deliveries/{Guid.NewGuid()}/replay")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_endpoint_whose_deliveries_keep_failing_is_disabled_with_the_reason_and_receives_nothing_more()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var perDelivery = AppHostFixture.WebhookRetryDelays.Length + 1;
        receiver.RespondWith([.. Enumerable.Repeat(500, perDelivery * AppHostFixture.WebhookDisableAfterFailedDeliveries)]);
        var endpointId = (await CreateEndpointAsync(recruiter, receiver, "job.published")).Id("endpoint.id");

        for (var i = 0; i < AppHostFixture.WebhookDisableAfterFailedDeliveries; i++)
        {
            await PublishNewJobAsync(recruiter, $"Failing {i}");
        }

        await WolverineStore.WaitUntilAsync(
            async () => !(await recruiter.GetAsync($"/v1/webhooks/{endpointId}")).At("enabled").GetBoolean(), "the endpoint to be disabled", Ct);
        var endpoint = (await recruiter.GetAsync($"/v1/webhooks/{endpointId}")).Expect(HttpStatusCode.OK);
        endpoint.Text("disabledReason").Should().Contain("Disabled automatically").And.Contain("HTTP 500");
        endpoint.At("disabledAt").ValueKind.Should().Be(JsonValueKind.String);
        var failed = await DeliveriesAsync(recruiter, endpointId);
        failed.Should().HaveCount(AppHostFixture.WebhookDisableAfterFailedDeliveries)
            .And.OnlyContain(d => d.Status == "Failed" && d.Attempts == perDelivery && d.LastStatusCode == 500);
        receiver.Received.Should().HaveCount(perDelivery * AppHostFixture.WebhookDisableAfterFailedDeliveries);

        // While it is off, an event fans out to nobody.
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        var ignored = await PublishNewJobAsync(recruiter, "While disabled");
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(ignored.ToString(), Ct, ".FanOutWebhook") == 1, "the fan-out for the disabled endpoint", Ct);
        (await DeliveriesAsync(recruiter, endpointId)).Should().HaveCount(AppHostFixture.WebhookDisableAfterFailedDeliveries);
        (await recruiter.PostAsync($"/v1/webhooks/{endpointId}/deliveries/{failed[0].Id}/replay")).Expect(HttpStatusCode.Conflict);

        // Turning it back on clears the reason and the count; a replay of a failed delivery then goes through.
        var enabled = (await recruiter.PutAsync($"/v1/webhooks/{endpointId}", new JsonObject
        {
            ["url"] = receiver.Url.ToString(),
            ["events"] = new JsonArray("job.published"),
            ["enabled"] = true,
        })).Expect(HttpStatusCode.OK);
        enabled.At("enabled").GetBoolean().Should().BeTrue();
        enabled.At("disabledReason").ValueKind.Should().Be(JsonValueKind.Null);
        enabled.At("consecutiveFailures").GetInt32().Should().Be(0);
        (await recruiter.PostAsync($"/v1/webhooks/{endpointId}/deliveries/{failed[0].Id}/replay")).Expect(HttpStatusCode.Accepted);
        await WaitForDeliveriesAsync(recruiter, endpointId, d => d.Any(e => e.Status == "Succeeded"), "the replay to succeed");
    }

    [Fact]
    public async Task Endpoints_deliveries_and_events_never_cross_organizations()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        await using var firstReceiver = await WebhookReceiver.StartAsync(Ct);
        await using var secondReceiver = await WebhookReceiver.StartAsync(Ct);
        var firstEndpoint = (await CreateEndpointAsync(first, firstReceiver, "job.published")).Id("endpoint.id");
        var secondEndpoint = (await CreateEndpointAsync(second, secondReceiver, "job.published")).Id("endpoint.id");

        var firstJob = await PublishNewJobAsync(first, "First organization's job");
        await firstReceiver.WaitForEventAsync("job.published", firstJob, Ct);
        var delivery = (await WaitForDeliveriesAsync(first, firstEndpoint, d => d.Any(e => e.Status == "Succeeded"), "the first delivery")).Single();

        ApiResult[] attempts =
        [
            await second.GetAsync($"/v1/webhooks/{firstEndpoint}"),
            await second.PutAsync($"/v1/webhooks/{firstEndpoint}", new JsonObject
            {
                ["url"] = secondReceiver.Url.ToString(),
                ["events"] = new JsonArray("job.published"),
                ["enabled"] = true,
            }),
            await second.PostAsync($"/v1/webhooks/{firstEndpoint}/rotate-secret"),
            await second.GetAsync($"/v1/webhooks/{firstEndpoint}/deliveries"),
            await second.PostAsync($"/v1/webhooks/{firstEndpoint}/deliveries/{delivery.Id}/replay"),
            await second.PostAsync($"/v1/webhooks/{secondEndpoint}/deliveries/{delivery.Id}/replay"),
            await second.DeleteAsync($"/v1/webhooks/{firstEndpoint}"),
        ];
        attempts.Select(a => a.Status).Should().AllBeEquivalentTo(HttpStatusCode.NotFound);
        var listed = (await second.GetAsync("/v1/webhooks")).Expect(HttpStatusCode.OK);
        listed.Body.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Equal(secondEndpoint);

        var secondJob = await PublishNewJobAsync(second, "Second organization's job");
        await secondReceiver.WaitForEventAsync("job.published", secondJob, Ct);
        secondReceiver.Received.Should().ContainSingle().Which.Body.Should().NotContain(firstJob.ToString());
        firstReceiver.Received.Should().ContainSingle().Which.Body.Should().NotContain(secondJob.ToString());

        var untouched = (await first.GetAsync($"/v1/webhooks/{firstEndpoint}")).Expect(HttpStatusCode.OK);
        untouched.Text("url").Should().Be(firstReceiver.Url.ToString());
        untouched.At("secretRotatedAt").GetDateTimeOffset().Should().Be(untouched.At("createdAt").GetDateTimeOffset());
        (await first.DeleteAsync($"/v1/webhooks/{firstEndpoint}")).Expect(HttpStatusCode.NoContent);
        (await first.GetAsync($"/v1/webhooks/{firstEndpoint}")).Expect(HttpStatusCode.NotFound);
        (await first.GetAsync($"/v1/webhooks/{firstEndpoint}/deliveries")).Expect(HttpStatusCode.NotFound);
    }

    // The daily sweep fans out one message per organization; the test sends one organization's message with a cutoff in
    // the future, so its finished delivery is past retention, and the other organization's log is untouched.
    [Fact]
    public async Task The_retention_sweep_removes_finished_deliveries_of_its_organization_only()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var firstEndpoint = (await CreateEndpointAsync(first, receiver, "job.published")).Id("endpoint.id");
        var secondEndpoint = (await CreateEndpointAsync(second, receiver, "job.published")).Id("endpoint.id");
        await PublishNewJobAsync(first, "Purged");
        await PublishNewJobAsync(second, "Kept");
        await WaitForDeliveriesAsync(first, firstEndpoint, d => d.Any(e => e.Status == "Succeeded"), "the first organization's delivery");
        await WaitForDeliveriesAsync(second, secondEndpoint, d => d.Any(e => e.Status == "Succeeded"), "the second organization's delivery");

        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new PurgeOrganizationWebhookDeliveries(first.OrganizationId, DateTimeOffset.UtcNow.AddDays(1)));

        await WaitForDeliveriesAsync(first, firstEndpoint, d => d.Count == 0, "the purge");
        (await DeliveriesAsync(second, secondEndpoint)).Should().ContainSingle();
    }

    [Fact]
    public async Task An_endpoint_needs_a_reachable_url_and_known_events()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        async Task<IReadOnlyList<string>> ProblemsAsync(string url, params string[] events) =>
            (await recruiter.PostAsync("/v1/webhooks", new JsonObject { ["url"] = url, ["events"] = new JsonArray([.. events.Select(e => (JsonNode)e)]) }))
                .Expect(HttpStatusCode.BadRequest).ProblemKeys();

        (await ProblemsAsync("http://hooks.example.com/in", "job.published")).Should().Equal("url");
        (await ProblemsAsync("https://10.0.0.1/in", "job.published")).Should().Equal("url");
        (await ProblemsAsync("https://user:password@hooks.example.com/in", "job.published")).Should().Equal("url");
        (await ProblemsAsync("not a url", "job.published")).Should().Equal("url");
        (await ProblemsAsync("https://hooks.example.com/in", "candidate.created")).Should().Equal("events");
        (await ProblemsAsync("https://hooks.example.com/in")).Should().Equal("events");

        (await recruiter.PostAsync("/v1/webhooks", new JsonObject
        {
            ["url"] = "https://hooks.example.com/in",
            ["events"] = new JsonArray("job.closed", "job.closed"),
        })).Expect(HttpStatusCode.Created).Count("endpoint.events").Should().Be(1);
        (await recruiter.GetAsync("/v1/webhooks/event-types")).Expect(HttpStatusCode.OK).Count().Should().Be(6);
    }

    private static async Task<ApiResult> CreateEndpointAsync(Tenant tenant, WebhookReceiver receiver, params string[] events) =>
        (await tenant.PostAsync("/v1/webhooks", new JsonObject
        {
            ["url"] = receiver.Url.ToString(),
            ["events"] = new JsonArray([.. events.Select(e => (JsonNode)e)]),
            ["description"] = "Test receiver",
        })).Expect(HttpStatusCode.Created);

    private static async Task<Guid> PublishNewJobAsync(Tenant tenant, string title)
    {
        var job = await tenant.CreateJobAsync(Samples.RemoteJob(title));
        await tenant.PublishJobAsync(job.Id());
        return job.Id();
    }

    private static async Task<IReadOnlyList<Delivery>> DeliveriesAsync(Tenant tenant, Guid endpointId) =>
        (await tenant.GetAsync($"/v1/webhooks/{endpointId}/deliveries")).Expect(HttpStatusCode.OK).Body
            .Deserialize<List<Delivery>>(JsonOptions)!;

    private static async Task<IReadOnlyList<Delivery>> WaitForDeliveriesAsync(
        Tenant tenant, Guid endpointId, Func<IReadOnlyList<Delivery>, bool> condition, string because)
    {
        IReadOnlyList<Delivery> deliveries = [];
        await WolverineStore.WaitUntilAsync(async () => condition(deliveries = await DeliveriesAsync(tenant, endpointId)), because, Ct);
        return deliveries;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record Delivery(
        Guid Id, Guid EventId, string EventType, string Status, int Attempts, int? LastStatusCode, string? LastError, Guid? ReplayOfId, string Payload);
}
