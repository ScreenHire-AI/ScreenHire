using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.Application.Webhooks;

/// <summary>
/// One webhook event as integrators read about it: its type on the wire, what it means in a sentence, the record its
/// <c>data</c> holds and a sample of that data. The public OpenAPI document's <c>webhooks</c> section and the developer
/// docs on the marketing site are built from it (docs/architecture/api-reference.md).
/// </summary>
public sealed record WebhookCatalogEntry(string Type, string Summary, string Description, Type DataType, object Sample);

/// <summary>Every event in <see cref="WebhookEventTypes.All"/>, in that order. <c>WebhookCatalogTests</c> keeps the two in step.</summary>
public static class WebhookCatalog
{
    // Fixed ids, so the samples in the committed documents never change on their own.
    private static readonly Guid Application = new("0193a1b2-7c4d-7e8f-9a0b-1c2d3e4f5a6b");
    private static readonly Guid Job = new("0193a1b2-6b3c-7d4e-8f5a-6b7c8d9e0f1a");
    private static readonly Guid Candidate = new("0193a1b2-5a2b-7c3d-9e4f-5a6b7c8d9e0f");
    private static readonly Guid Interview = new("0193a1b2-8d5e-7f6a-8b1c-2d3e4f5a6b7c");
    private static readonly Guid Scorecard = new("0193a1b2-9e6f-7a1b-9c2d-3e4f5a6b7c8d");
    private static readonly Guid Run = new("0193a1b2-af7a-7b2c-8d3e-4f5a6b7c8d9e");
    private static readonly Guid JobVersion = new("0193a1b2-b08b-7c3d-9e4f-5a6b7c8d9e0f");
    private static readonly DateTimeOffset StartsAt = new(2026, 12, 3, 15, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<WebhookCatalogEntry> All { get; } =
    [
        new(WebhookEventTypes.ApplicationSubmitted, "An application was submitted",
            "Someone applied to a job, or an application was added through the API. `stageId` is the stage it starts in.",
            typeof(ApplicationSubmittedData), new ApplicationSubmittedData(Application, Job, "applied")),
        new(WebhookEventTypes.ApplicationMoved, "An application moved to another stage",
            "A person, an API key or an automation moved an application from one stage of its job to another.",
            typeof(ApplicationMovedData), new ApplicationMovedData(Application, Job, "screening", "interview")),
        new(WebhookEventTypes.ApplicationHired, "A candidate was hired",
            "An application entered a stage of kind Hired. The `application.moved` event for the same move comes too.",
            typeof(ApplicationHiredData), new ApplicationHiredData(Application, Job, Candidate, "hired")),
        new(WebhookEventTypes.VerificationAssessed, "Verification checks finished",
            "The checks on an application finished. The levels are categories for a person to review, never a score or a reason to reject; "
            + "the evidence stays behind the API.",
            typeof(VerificationAssessedData),
            new VerificationAssessedData(Application, Job, AuthenticityLevel.NoConcerns, AiAssistanceLevel.NotIndicated, "2026-11-09.1")),
        new(WebhookEventTypes.AutomationFailed, "An automation failed or was blocked",
            "An automation run on an application ended Failed or Blocked, the two outcomes the automation issues list shows.",
            typeof(AutomationFailedData), new AutomationFailedData(Application, Job, "send-welcome", Run, AutomationRunStatus.Failed)),
        new(WebhookEventTypes.JobPublished, "A job was published",
            "A job went live, or a live job was published again with changes. `version` counts its publications.",
            typeof(JobPublishedData), new JobPublishedData(Job, 2, JobVersion)),
        new(WebhookEventTypes.JobClosed, "A job was closed",
            "A job stopped taking applications.",
            typeof(JobClosedData), new JobClosedData(Job)),
        new(WebhookEventTypes.CandidateErased, "A candidate was erased",
            "A candidate and everything about them was erased, on their request or when the retention period ended. "
            + "Delete what you copied about them; after this event the id names no one.",
            typeof(CandidateErasedData), new CandidateErasedData(Candidate)),
        new(WebhookEventTypes.InterviewScheduled, "An interview was booked",
            "The candidate picked a time, or a person booked one for them. `startsAt` is the booked time.",
            typeof(InterviewEventData), new InterviewEventData(Interview, Application, Job, StartsAt, 45, ByCandidate: true)),
        new(WebhookEventTypes.InterviewRescheduled, "An interview moved to a new time",
            "A booked interview moved to a new time. `startsAt` is the new time.",
            typeof(InterviewEventData), new InterviewEventData(Interview, Application, Job, StartsAt.AddDays(1), 45, ByCandidate: false)),
        new(WebhookEventTypes.InterviewCancelled, "An interview was cancelled",
            "The team, the candidate or ScreenHire cancelled an interview: an offered one before a time was picked (no `startsAt`), "
            + "or a booked one (the time it had).",
            typeof(InterviewEventData), new InterviewEventData(Interview, Application, Job, StartsAt, 45, ByCandidate: true)),
        new(WebhookEventTypes.ScorecardSubmitted, "A scorecard was submitted",
            "A reviewer submitted a stage's scorecard. The event has the overall recommendation; the ratings, answers and note stay behind the API.",
            typeof(ScorecardSubmittedData), new ScorecardSubmittedData(Scorecard, Application, Job, "interview", Recommendation.Yes)),
    ];

    /// <summary>How the samples are written: exactly as a delivery body is.</summary>
    public static System.Text.Json.JsonSerializerOptions Json => WebhookEvents.SerializerOptions;

    /// <summary>The whole delivery body for an entry's sample, as an endpoint receives it.</summary>
    public static object SampleBody(WebhookCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = WebhookEvents.EventId(entry.Type, "sample");
        return new WebhookEventBody<object>(id, entry.Type, new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            new Guid("0193a1b2-1a2b-7c3d-8e4f-5a6b7c8d9e0f"), entry.Sample);
    }
}
