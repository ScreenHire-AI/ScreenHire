using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Candidates;
using ScreenHire.Application.Scheduling;
using ScreenHire.Application.Webhooks;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.UnitTests.Webhooks;

/// <summary>
/// The webhook payloads are a contract integrators code against (docs/architecture/workflow-engine.md section 5): each
/// event's <c>data</c> members are pinned here by name and order, so a rename or a removal fails before it ships.
/// </summary>
public sealed class WebhookCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static TheoryData<string, string[]> Payloads => new()
    {
        { WebhookEventTypes.ApplicationSubmitted, ["applicationId", "jobId", "stageId"] },
        { WebhookEventTypes.ApplicationMoved, ["applicationId", "jobId", "fromStageId", "toStageId"] },
        { WebhookEventTypes.ApplicationHired, ["applicationId", "jobId", "candidateId", "stageId"] },
        { WebhookEventTypes.VerificationAssessed, ["applicationId", "jobId", "authenticity", "aiAssistance", "rulesetVersion"] },
        { WebhookEventTypes.AutomationFailed, ["applicationId", "jobId", "automationId", "runId", "status"] },
        { WebhookEventTypes.JobPublished, ["jobId", "version", "jobVersionId"] },
        { WebhookEventTypes.JobClosed, ["jobId"] },
        { WebhookEventTypes.CandidateErased, ["candidateId"] },
        { WebhookEventTypes.InterviewScheduled, ["interviewId", "applicationId", "jobId", "startsAt", "durationMinutes", "byCandidate"] },
        { WebhookEventTypes.InterviewRescheduled, ["interviewId", "applicationId", "jobId", "startsAt", "durationMinutes", "byCandidate"] },
        { WebhookEventTypes.InterviewCancelled, ["interviewId", "applicationId", "jobId", "startsAt", "durationMinutes", "byCandidate"] },
        { WebhookEventTypes.ScorecardSubmitted, ["scorecardId", "applicationId", "jobId", "stageId", "recommendation"] },
    };

    [Fact]
    public void The_catalogue_describes_every_event_type_in_order() =>
        WebhookCatalog.All.Select(e => e.Type).Should().Equal(WebhookEventTypes.All);

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Each_payload_keeps_its_members(string type, string[] members)
    {
        var entry = WebhookCatalog.All.Single(e => e.Type == type);
        entry.Sample.Should().BeOfType(entry.DataType);
        entry.Summary.Should().NotBeNullOrWhiteSpace();
        entry.Description.Should().NotBeNullOrWhiteSpace();

        using var data = JsonDocument.Parse(JsonSerializer.Serialize(entry.Sample, entry.DataType, Json));
        data.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(members);
    }

    [Fact]
    public void Every_payload_type_is_in_the_contract_list() =>
        Payloads.Select(row => row.Data.Item1).Should().BeEquivalentTo(WebhookEventTypes.All);

    [Fact]
    public void A_hire_carries_the_candidate_and_the_stage_and_keys_on_the_move()
    {
        var moved = new ApplicationMoved(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "offer", "hired", Guid.CreateVersion7(), Now);
        var candidateId = Guid.CreateVersion7();

        var raised = WebhookEvents.Hired(moved, candidateId);

        raised.EventType.Should().Be(WebhookEventTypes.ApplicationHired);
        raised.EventId.Should().Be(WebhookEvents.Hired(moved, candidateId).EventId).And.NotBe(WebhookEvents.From(moved).EventId);
        using var body = JsonDocument.Parse(raised.Body);
        body.RootElement.GetProperty("data").GetProperty("candidateId").GetGuid().Should().Be(candidateId);
        body.RootElement.GetProperty("data").GetProperty("stageId").GetString().Should().Be("hired");
    }

    [Fact]
    public void An_erasure_carries_only_the_candidate_id()
    {
        var erased = new CandidateErased(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        using var body = JsonDocument.Parse(WebhookEvents.From(erased).Body);

        body.RootElement.GetProperty("data").EnumerateObject().Select(p => p.Name).Should().Equal("candidateId");
        body.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
    }

    [Theory]
    [InlineData(InterviewChanges.Booked, WebhookEventTypes.InterviewScheduled)]
    [InlineData(InterviewChanges.Rescheduled, WebhookEventTypes.InterviewRescheduled)]
    [InlineData(InterviewChanges.Cancelled, WebhookEventTypes.InterviewCancelled)]
    [InlineData(InterviewChanges.Invited, null)]
    [InlineData(InterviewChanges.NewTimesOffered, null)]
    [InlineData(null, null)]
    public void Only_a_booking_a_new_time_or_a_cancellation_raises_an_interview_event(string? change, string? type)
    {
        var changed = Interview(change, revision: 1);

        WebhookEvents.From(changed, 30, Now)?.EventType.Should().Be(type);
        if (type is null)
        {
            WebhookEvents.From(changed, 30, Now).Should().BeNull();
        }
    }

    [Fact]
    public void Each_revision_of_an_interview_is_its_own_event_and_a_redelivery_is_the_same_one()
    {
        var first = Interview(InterviewChanges.Rescheduled, revision: 2);
        var second = first with { Revision = 3, StartsAt = Now.AddDays(2) };

        WebhookEvents.From(first, 30, Now)!.EventId.Should().Be(WebhookEvents.From(first, 30, Now.AddMinutes(5))!.EventId);
        WebhookEvents.From(second, 30, Now)!.EventId.Should().NotBe(WebhookEvents.From(first, 30, Now)!.EventId);
    }

    [Fact]
    public void An_interview_event_happens_when_the_change_did()
    {
        var changed = Interview(InterviewChanges.Booked, revision: 1) with { ChangedAt = Now.AddMinutes(-3) };

        using var body = JsonDocument.Parse(WebhookEvents.From(changed, 45, Now)!.Body);

        body.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now.AddMinutes(-3));
        body.RootElement.GetProperty("data").GetProperty("durationMinutes").GetInt32().Should().Be(45);
        body.RootElement.GetProperty("data").GetProperty("byCandidate").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void A_scorecard_event_has_the_recommendation_and_no_note_or_ratings()
    {
        var submitted = new ScorecardSubmitted(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        using var body = JsonDocument.Parse(WebhookEvents.From(submitted, "interview", Recommendation.StrongYes).Body);

        var data = body.RootElement.GetProperty("data");
        data.GetProperty("recommendation").GetString().Should().Be(nameof(Recommendation.StrongYes));
        data.EnumerateObject().Select(p => p.Name).Should().NotContain(["note", "ratings", "answers", "reviewerUserId"]);
    }

    private static InterviewChanged Interview(string? change, int revision) => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Booked", change,
        ByCandidate: true, StartsAt: Now.AddDays(1), Revision: revision, ChangedAt: Now);
}
