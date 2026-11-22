using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Verification;
using ScreenHire.IntegrationTests.Scheduling;
using static ScreenHire.IntegrationTests.CandidateTestSupport;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests.Webhooks;

/// <summary>
/// The events of the first batch after the original six (docs/architecture/workflow-engine.md section 5): each arrives
/// signed with its endpoint's secret, from the action a person or a candidate takes through the API, with ids and facts
/// only. The payloads' member names are pinned by the unit test <c>WebhookCatalogTests</c>; these prove the wiring.
/// </summary>
public sealed class WebhookEventCatalogueTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hire_an_erasure_and_a_scorecard_raise_their_events()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var secret = (await CreateEndpointAsync(owner, receiver, "application.hired", "candidate.erased", "scorecard.submitted")).Text("signingSecret");

        // A scorecard on the interview stage, then a move to Hired.
        var key = await CreateScorecardAsync(owner, "Panel review", Rating5("depth"));
        var job = await JobNamingAsync(owner, "Platform Engineer", "interview", key);
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(job, person.Email, person.FullName)).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        var scorecard = (await owner.PostAsync($"/v1/applications/{application}/scorecards",
            Submission("interview", "StrongYes", Answer("depth", rating: 4)))).Expect(HttpStatusCode.Created).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "hired" })).Expect(HttpStatusCode.OK);

        var submitted = await receiver.WaitForEventAsync("scorecard.submitted", scorecard, Ct);
        submitted.Json.GetProperty("data").GetProperty("applicationId").GetGuid().Should().Be(application);
        submitted.Json.GetProperty("data").GetProperty("stageId").GetString().Should().Be("interview");
        submitted.Json.GetProperty("data").GetProperty("recommendation").GetString().Should().Be("StrongYes");

        var hired = await receiver.WaitForEventAsync("application.hired", application, Ct);
        var candidateId = await CandidateOfAsync(owner, application);
        hired.Json.GetProperty("data").GetProperty("candidateId").GetGuid().Should().Be(candidateId);
        hired.Json.GetProperty("data").GetProperty("stageId").GetString().Should().Be("hired");

        (await owner.PostAsync($"/v1/candidates/{candidateId}/erase", new JsonObject { ["confirmation"] = person.FullName })).Expect(HttpStatusCode.NoContent);
        var erased = await receiver.WaitForEventAsync("candidate.erased", candidateId, Ct);
        erased.Json.GetProperty("data").EnumerateObject().Select(p => p.Name).Should().Equal("candidateId");

        foreach (var webhook in new[] { submitted, hired, erased })
        {
            webhook.IsSignedWith(secret).Should().BeTrue();
            webhook.Body.Should().NotContain(person.Email).And.NotContain(person.LastName);
        }

        // A move into a stage that is not Hired raises no hire: the move to Interview above is the proof, and only one hire arrived.
        receiver.Received.Count(r => r.Type == "application.hired").Should().Be(1);
    }

    [Fact]
    public async Task Booking_moving_and_cancelling_an_interview_each_raise_one_event_and_an_offer_raises_none()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var secret = (await CreateEndpointAsync(world.Owner, receiver, "interview.scheduled", "interview.rescheduled", "interview.cancelled"))
            .Text("signingSecret");

        var (interviewId, token) = await world.ScheduledAsync();
        var slots = InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK));
        var chosen = slots[^1];
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);
        var scheduled = await receiver.WaitForEventAsync("interview.scheduled", interviewId, Ct);
        scheduled.Json.GetProperty("data").GetProperty("startsAt").GetDateTimeOffset().Should().Be(chosen);
        scheduled.Json.GetProperty("data").GetProperty("durationMinutes").GetInt32().Should().Be(30);
        scheduled.Json.GetProperty("data").GetProperty("byCandidate").GetBoolean().Should().BeTrue();
        scheduled.Json.GetProperty("data").GetProperty("applicationId").GetGuid().Should().Be(world.ApplicationId);

        var moved = InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)).First(s => s != chosen);
        (await world.RescheduleAsync(token, moved)).Expect(HttpStatusCode.NoContent);
        var rescheduled = await receiver.WaitForEventAsync("interview.rescheduled", interviewId, Ct);
        rescheduled.Json.GetProperty("data").GetProperty("startsAt").GetDateTimeOffset().Should().Be(moved);

        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.OK);
        var cancelled = await receiver.WaitForEventAsync("interview.cancelled", interviewId, Ct);
        cancelled.Json.GetProperty("data").GetProperty("byCandidate").GetBoolean().Should().BeFalse();

        foreach (var webhook in new[] { scheduled, rescheduled, cancelled })
        {
            webhook.IsSignedWith(secret).Should().BeTrue();
            webhook.Body.Should().NotContain(world.CandidateEmail);
        }

        new[] { scheduled.EventId, rescheduled.EventId, cancelled.EventId }.Should().OnlyHaveUniqueItems();
        // The offer came first and raised nothing: every event that arrived is one of the three, once each.
        receiver.Received.Select(r => r.Type).Should().BeEquivalentTo(["interview.scheduled", "interview.rescheduled", "interview.cancelled"]);
        scheduled.Json.GetProperty("occurredAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static async Task<ApiResult> CreateEndpointAsync(Tenant tenant, WebhookReceiver receiver, params string[] events) =>
        (await tenant.PostAsync("/v1/webhooks", new JsonObject
        {
            ["url"] = receiver.Url.ToString(),
            ["events"] = new JsonArray([.. events.Select(e => (JsonNode)e)]),
            ["description"] = "Test receiver",
        })).Expect(HttpStatusCode.Created);
}
