using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Verification.Llm;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// An application's verification assessments and the reviewer actions on them (docs/architecture/verification-engine.md §7).
/// Tenant-scoped like every dashboard route: another organization's application is a 404. Reviewers can dismiss an
/// assessment with a reason, mark each of its reasons explained (and undo their own), and add notes, and each action goes
/// to the append-only audit log. Explaining a reason decides nothing: only a dismissal sets an assessment aside, and
/// nothing outside this view reads explanations. Requesting verification is listed but refused until phase 2. There is
/// no reject action here, and there never will be (ADR-0004).
/// </summary>
internal static class VerificationEndpoints
{
    public const string VerificationUnavailable = "Identity verification arrives in phase 2.";

    public static IEndpointRouteBuilder MapVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        // The reads accept an API key with the Verification read scope (ADR-0021); every other route here is the session's.
        var reads = app.MapGroup("/v1").WithTags("Verification").AllowVerificationApiKeys();
        reads.MapGet("/applications/{applicationId:guid}/verification", GetVerification).WithName("GetApplicationVerification")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        reads.MapGet("/verification/applications", ListByLevel).WithName("ListVerificationApplications")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        var group = app.MapGroup("/v1/applications/{applicationId:guid}/verification").WithTags("Verification").RequireAuthorization();
        group.MapPost("/assessments/{assessmentId:guid}/dismiss", Dismiss).WithName("DismissVerificationAssessment")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ReviewVerification)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/assessments/{assessmentId:guid}/signals/{signalId:guid}/explain", Explain).WithName("ExplainVerificationReason")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ReviewVerification)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/assessments/{assessmentId:guid}/signals/{signalId:guid}/undo-explanation", UndoExplanation)
            .WithName("UndoVerificationReasonExplanation")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ReviewVerification)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/notes", AddNote).WithName("AddVerificationNote")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ReviewVerification)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/request-verification", RequestVerification).WithName("RequestIdentityVerification")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.ReviewVerification)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // A read through a key is written to the review log in the same save that precedes the answer, so a read that cannot
    // be recorded is not answered (ADR-0021).
    private static async Task<Results<Ok<ApplicationVerification>, NotFound>> GetVerification(
        Guid applicationId, ClaimsPrincipal user, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var verification = await VerificationReads.LoadAsync(db, applicationId, cancellationToken);
        await VerificationReads.RecordKeyReadsAsync(user, db, tenant, time, [applicationId], cancellationToken);
        return TypedResults.Ok(verification);
    }

    /// <summary>The most applications one list returns.</summary>
    public const int MaxMatches = 50;

    /// <summary>The applications whose current verification is at a level, newest first, for a key that can read verification.</summary>
    /// <remarks>
    /// A level is evidence for a person to review, never a reason to reject on its own (ADR-0004): the list is in the order
    /// applications arrived, not ranked by severity.
    /// </remarks>
    private static async Task<Results<Ok<VerificationMatches>, ValidationProblem>> ListByLevel(
        AuthenticityLevel? authenticity,
        AiAssistanceLevel? aiAssistance,
        Guid? jobId,
        bool? includeDismissed,
        int? limit,
        ClaimsPrincipal user,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (authenticity is null && aiAssistance is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("authenticity", "Name an authenticity level, an AI assistance level or both."));
        }

        if (limit is < 1 or > MaxMatches)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("limit", $"The limit is 1 to {MaxMatches}."));
        }

        var matches = await VerificationReads.ByLevelAsync(
            db, authenticity, aiAssistance, jobId, includeDismissed ?? true, limit ?? 20, cancellationToken);
        await VerificationReads.RecordKeyReadsAsync(user, db, tenant, time, [.. matches.Items.Select(m => m.ApplicationId)], cancellationToken);
        return TypedResults.Ok(matches);
    }

    // Sets the current assessment of one kind aside, with a reason. Only an assessment above the lowest level, only the
    // current one (a reassessment replaces what was dismissed), and only once.
    private static async Task<Results<Ok<ApplicationVerification>, NotFound, ValidationProblem, ProblemHttpResult>> Dismiss(
        Guid applicationId,
        Guid assessmentId,
        DismissVerificationAssessmentRequest request,
        IValidator<DismissVerificationAssessmentRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var target = await ReviewableAsync(db, applicationId, assessmentId, null, "dismiss", cancellationToken);
        if (target.Assessment is not { } assessment)
        {
            return target.Refusal is { } refusal ? refusal : TypedResults.NotFound();
        }

        db.VerificationReviewActions.Add(VerificationReviewAction.Dismiss(assessment, request.Reason, request.Note, tenant.UserId!.Value, time.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        // Another reviewer's dismissal committed between the check above and this insert.
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return Conflict("Already dismissed", "Someone already dismissed this assessment.");
        }

        return TypedResults.Ok(await VerificationReads.LoadAsync(db, applicationId, cancellationToken));
    }

    // Marks one reason of the current assessment explained, with a reason. It records a person's reading in the review
    // log and changes nothing else: the level, the list chips, filters, saved views, automations and webhooks never read
    // it. Once per reason until its author undoes it, and only while the assessment can still be dismissed.
    private static async Task<Results<Ok<ApplicationVerification>, NotFound, ValidationProblem, ProblemHttpResult>> Explain(
        Guid applicationId,
        Guid assessmentId,
        Guid signalId,
        ExplainVerificationReasonRequest request,
        IValidator<ExplainVerificationReasonRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var target = await ReviewableAsync(db, applicationId, assessmentId, signalId, "explain", cancellationToken);
        if (target.Assessment is not { } assessment)
        {
            return target.Refusal is { } refusal ? refusal : TypedResults.NotFound();
        }

        // A check, not a unique index: an undo is its own row, so a reason can be explained again. Two explanations that
        // pass it at the same moment both go in the log and the later one stands (CurrentExplanations), which is harmless
        // because an explanation decides nothing.
        if (await CurrentExplanationAsync(db, assessment.Id, signalId, cancellationToken) is not null)
        {
            return Conflict("Already explained", "Someone already marked this reason explained.");
        }

        db.VerificationReviewActions.Add(VerificationReviewAction.Explain(assessment, signalId, request.Reason, request.Note, tenant.UserId!.Value, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await VerificationReads.LoadAsync(db, applicationId, cancellationToken));
    }

    // Takes an explanation back. Only its author can, and only while the assessment can still be dismissed.
    private static async Task<Results<Ok<ApplicationVerification>, NotFound, ProblemHttpResult>> UndoExplanation(
        Guid applicationId,
        Guid assessmentId,
        Guid signalId,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var target = await ReviewableAsync(db, applicationId, assessmentId, signalId, "undo", cancellationToken);
        if (target.Assessment is not { } assessment)
        {
            return target.Refusal is { } refusal ? refusal : TypedResults.NotFound();
        }

        if (await CurrentExplanationAsync(db, assessment.Id, signalId, cancellationToken) is not { } explanation)
        {
            return Conflict("Not explained", "This reason is not marked explained.");
        }

        if (explanation.ActorUserId != tenant.UserId)
        {
            return TypedResults.Problem(
                title: "Author only", detail: "Only the person who marked it explained can undo it.", statusCode: StatusCodes.Status403Forbidden);
        }

        db.VerificationReviewActions.Add(VerificationReviewAction.UndoExplanation(explanation, tenant.UserId!.Value, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await VerificationReads.LoadAsync(db, applicationId, cancellationToken));
    }

    private static async Task<Results<Ok<ApplicationVerification>, NotFound, ValidationProblem>> AddNote(
        Guid applicationId,
        AddVerificationNoteRequest request,
        IValidator<AddVerificationNoteRequest> validator,
        ScreenHireDbContext db,
        ITenantContext tenant,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        if (request.AssessmentId is { } assessmentId
            && !await db.VerificationAssessments.AnyAsync(a => a.Id == assessmentId && a.ApplicationId == applicationId, cancellationToken))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("assessmentId", "This application has no assessment with this id."));
        }

        db.VerificationReviewActions.Add(VerificationReviewAction.AddNote(
            application.OrganizationId, applicationId, request.AssessmentId, request.Note, tenant.UserId!.Value, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await VerificationReads.LoadAsync(db, applicationId, cancellationToken));
    }

    // Present so the dashboard and the API describe the full set of review actions, refused until phase 2 brings the
    // verification step (verification-engine.md §9).
    private static async Task<Results<NotFound, ProblemHttpResult>> RequestVerification(
        Guid applicationId, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Applications.AnyAsync(a => a.Id == applicationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return Conflict("Not available yet", VerificationUnavailable);
    }

    /// <summary>The assessment a reviewer may act on, or why not: nothing found (both null) or a 409.</summary>
    private sealed record ReviewTarget(VerificationAssessment? Assessment, ProblemHttpResult? Refusal);

    // An assessment of this application (and, given a signal, one of its reasons) that is the current one of its kind,
    // above the lowest level and not dismissed: what a dismissal and an explanation both need.
    private static async Task<ReviewTarget> ReviewableAsync(
        ScreenHireDbContext db, Guid applicationId, Guid assessmentId, Guid? signalId, string verb, CancellationToken cancellationToken)
    {
        var assessment = await db.VerificationAssessments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == assessmentId && a.ApplicationId == applicationId, cancellationToken);
        if (assessment is null || (signalId is { } id && !assessment.SignalIds.Contains(id)))
        {
            return new(null, null);
        }

        var current = await db.VerificationAssessments.AsNoTracking()
            .Where(a => a.ApplicationId == applicationId && a.Kind == assessment.Kind)
            .OrderByDescending(a => a.AssessedAt).ThenByDescending(a => a.Id)
            .Select(a => a.Id)
            .FirstAsync(cancellationToken);
        if (current != assessment.Id)
        {
            return new(null, Conflict("A newer assessment replaced this one", "Refresh to see the current assessment."));
        }

        if (!assessment.IsRaised)
        {
            return new(null, Conflict($"Nothing to {verb}", "This assessment is at its lowest level."));
        }

        if (await db.VerificationReviewActions.AnyAsync(a => a.AssessmentId == assessment.Id && a.Type == VerificationReviewActionType.Dismissed, cancellationToken))
        {
            return new(null, Conflict("Already dismissed", "Someone already dismissed this assessment."));
        }

        return new(assessment, null);
    }

    private static async Task<VerificationReviewAction?> CurrentExplanationAsync(
        ScreenHireDbContext db, Guid assessmentId, Guid signalId, CancellationToken cancellationToken)
    {
        var actions = await db.VerificationReviewActions.AsNoTracking()
            .Where(a => a.AssessmentId == assessmentId && a.SignalId == signalId
                && (a.Type == VerificationReviewActionType.ReasonExplained || a.Type == VerificationReviewActionType.ExplanationUndone))
            .ToListAsync(cancellationToken);
        return VerificationReviewAction.CurrentExplanations(actions).GetValueOrDefault((assessmentId, signalId));
    }

    private static ProblemHttpResult Conflict(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status409Conflict);
}

/// <summary>Reads verification assessments for the API: one application in full, or the chips for a list.</summary>
internal static class VerificationReads
{
    public static async Task<ApplicationVerification> LoadAsync(ScreenHireDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var assessments = await db.VerificationAssessments.AsNoTracking()
            .Where(a => a.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        var authenticity = Current(assessments, VerificationAssessmentKind.Authenticity);
        var aiAssistance = Current(assessments, VerificationAssessmentKind.AiAssistance);
        var actions = await db.VerificationReviewActions.AsNoTracking()
            .Where(a => a.ApplicationId == applicationId)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        // The current assessments' signals, and the ones the log's explanations name, for their titles.
        var signalIds = new[] { authenticity, aiAssistance }.Where(a => a is not null).SelectMany(a => a!.SignalIds)
            .Concat(actions.Where(a => a.SignalId is not null).Select(a => a.SignalId!.Value))
            .Distinct().ToList();
        var signals = await db.VerificationSignals.AsNoTracking()
            .Where(s => s.ApplicationId == applicationId && signalIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var explanations = VerificationReviewAction.CurrentExplanations(actions);
        // A key's read names the key and, as who is behind it, the person who made the key (a revoked key keeps its name).
        var keyIds = actions.Select(a => a.ApiKeyId).OfType<Guid>().Distinct().ToList();
        var keys = new Dictionary<Guid, KeyOrigin>();
        if (keyIds.Count > 0)
        {
            keys = await db.ApiKeys.AsNoTracking().Where(k => keyIds.Contains(k.Id))
                .ToDictionaryAsync(k => k.Id, k => new KeyOrigin(k.Name, k.CreatedByUserId), cancellationToken);
        }

        var actorIds = actions.Select(a => a.ActorUserId).Concat(keys.Values.Select(k => k.CreatedByUserId)).OfType<Guid>().Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, cancellationToken);
        string? NameOf(Guid? userId) => userId is { } id ? names.GetValueOrDefault(id) : null;
        var kinds = assessments.ToDictionary(a => a.Id, a => a.Kind);
        var check = await db.ConsistencyChecks.AsNoTracking()
            .Where(c => c.ApplicationId == applicationId && c.PromptVersion == ConsistencyPrompts.Version)
            .SingleOrDefaultAsync(cancellationToken);

        // What the organization's settings kept the checks from doing (a group turned off, a trusted domain or network), from each
        // analyzer's latest run, so a result without a signal is never read as a clean one.
        var runs = await db.VerificationAnalyzerRuns.AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .Select(r => new { r.Analyzer, r.AnalyzerVersion, r.Revision, r.Note })
            .ToListAsync(cancellationToken);
        var notRun = runs
            .GroupBy(r => r.Analyzer)
            .Select(g => g.OrderByDescending(r => r.AnalyzerVersion).ThenByDescending(r => r.Revision).First())
            .Select(r => (Run: r, Kind: RunNotes.KindOf(r.Analyzer, r.Note)))
            .Where(r => r.Kind is not null && CheckGroups.Of(r.Run.Analyzer) is not null)
            .OrderBy(r => CheckGroups.Of(r.Run.Analyzer))
            .Select(r => new CheckNotRun(
                CheckGroups.Of(r.Run.Analyzer)!.Value,
                // A paused AI check is named for what paused, not for the group of checks its analyzer belongs to.
                r.Kind == CheckNotRunKind.Paused ? RunNotes.AiChecksLabel : CheckGroups.Label(CheckGroups.Of(r.Run.Analyzer)!.Value),
                r.Kind!.Value,
                r.Run.Note!))
            .DistinctBy(n => (n.Group, n.Note)) // two analyzers of one group (answers, consistency) say the same thing once
            .ToList();

        VerificationDismissal? DismissalOf(VerificationAssessment assessment) =>
            actions.LastOrDefault(a => a.AssessmentId == assessment.Id && a.Type == VerificationReviewActionType.Dismissed) is { } action
                ? new VerificationDismissal(action.Id, action.DismissReason!.Value, action.Note, action.ActorUserId!.Value, NameOf(action.ActorUserId), action.OccurredAt)
                : null;

        static string TitleOf(VerificationSignal signal) => SignalCatalogue.All.TryGetValue(signal.Code, out var definition) ? definition.Title : signal.Code;

        VerificationReasonExplanation? ExplanationOf(VerificationAssessment assessment, Guid signalId) =>
            explanations.TryGetValue((assessment.Id, signalId), out var action)
                ? new VerificationReasonExplanation(
                    action.Id, action.ExplainReason!.Value, action.Note, action.ActorUserId!.Value, NameOf(action.ActorUserId), action.OccurredAt)
                : null;

        List<VerificationSignalView> SignalsOf(VerificationAssessment assessment) =>
        [
            .. assessment.SignalIds.Where(signals.ContainsKey).Select(id => signals[id]).Select(s => new VerificationSignalView(
                s.Id, s.Code, TitleOf(s), s.Category, s.Weight,
                s.Confidence, assessment.CountedSignalIds.Contains(s.Id), s.ContextOnly, s.AccessibilitySensitive, s.Explanation, s.Evidence,
                s.CounterEvidence, s.Source, s.SourceVersion, s.ObservedAt, s.Informational, s.Llm, ExplanationOf(assessment, s.Id),
                CheckGroups.OfSignal(s))),
        ];

        var authenticitySignals = authenticity is null ? [] : SignalsOf(authenticity);
        var aiSignals = aiAssistance is null ? [] : SignalsOf(aiAssistance);
        // A hand-added candidate came through no careers form, so some groups had nothing to read. It goes by the source alone.
        var source = await db.Applications.AsNoTracking().Where(a => a.Id == applicationId).Select(a => (ApplicationSource?)a.Source).SingleOrDefaultAsync(cancellationToken);

        return new ApplicationVerification(
            applicationId,
            authenticity is null && aiAssistance is null ? VerificationStatus.Pending : VerificationStatus.Assessed,
            authenticity is null
                ? null
                : new AuthenticityAssessmentView(
                    authenticity.Id, Enum.Parse<AuthenticityLevel>(authenticity.Level), authenticity.ReasonCodes, authenticity.Summary,
                    authenticity.NextStep, authenticitySignals, authenticity.RulesetVersion, authenticity.AssessedAt, DismissalOf(authenticity)),
            aiAssistance is null
                ? null
                : new AiAssistanceAssessmentView(
                    aiAssistance.Id, Enum.Parse<AiAssistanceLevel>(aiAssistance.Level), aiAssistance.ReasonCodes, aiAssistance.Summary,
                    aiAssistance.NextStep, aiSignals, aiAssistance.RulesetVersion, aiAssistance.AssessedAt, DismissalOf(aiAssistance),
                    aiAssistance.AiUsePolicy ?? AiUsePolicy.AllowedWithDisclosure, aiAssistance.AiUseDeclaration ?? AiUseDeclaration.NotProvided,
                    aiAssistance.AiPolicyContext ?? AiPolicyContext.NotDisclosed),
            [
                .. actions.Select(a => new VerificationReviewEntry(
                    a.Id, a.Type, a.AssessmentId is { } id && kinds.TryGetValue(id, out var kind) ? kind : null, a.AssessmentId, a.DismissReason,
                    a.Note, a.ActorUserId, NameOf(a.ActorUserId), a.OccurredAt, a.SignalId,
                    a.SignalId is { } signalId && signals.TryGetValue(signalId, out var signal) ? TitleOf(signal) : null, a.ExplainReason,
                    a.ApiKeyId, a.ApiKeyId is { } keyId && keys.TryGetValue(keyId, out var key) ? key.Name : null,
                    a.ApiKeyId is { } createdKeyId && keys.TryGetValue(createdKeyId, out var created) ? NameOf(created.CreatedByUserId) : null)),
            ],
            new VerificationActionAvailability(false, VerificationEndpoints.VerificationUnavailable),
            check is null || check.Status == ConsistencyCheckStatus.Running
                ? null
                : new ConsistencyCheckView(
                    check.Status, check.Note, check.PromptVersion, check.ExtractionModel, check.ConsistencyModel, check.Contradictions.Count, check.CompletedAt),
            notRun,
            VerificationGroups.Of(authenticitySignals, aiSignals, notRun, source));
    }

    /// <summary>The chips for each application in <paramref name="applicationIds"/> that has been assessed.</summary>
    /// <remarks>
    /// Only a dismissal reaches the chips, and through them the filters and saved views. Explained reasons never do:
    /// explaining decides nothing (ADR-0004).
    /// </remarks>
    public static async Task<Dictionary<Guid, VerificationSummary>> SummariesAsync(
        ScreenHireDbContext db, IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken)
    {
        var assessments = await db.VerificationAssessments.AsNoTracking()
            .Where(a => applicationIds.Contains(a.ApplicationId))
            .ToListAsync(cancellationToken);
        var assessmentIds = assessments.Select(a => a.Id).ToList();
        var dismissed = (await db.VerificationReviewActions.AsNoTracking()
            .Where(a => a.Type == VerificationReviewActionType.Dismissed && a.AssessmentId != null && assessmentIds.Contains(a.AssessmentId.Value))
            .Select(a => a.AssessmentId!.Value)
            .ToListAsync(cancellationToken)).ToHashSet();

        var summaries = new Dictionary<Guid, VerificationSummary>();
        foreach (var group in assessments.GroupBy(a => a.ApplicationId))
        {
            var list = group.ToList();
            if (Current(list, VerificationAssessmentKind.Authenticity) is { } authenticity && Current(list, VerificationAssessmentKind.AiAssistance) is { } ai)
            {
                summaries[group.Key] = new VerificationSummary(
                    Enum.Parse<AuthenticityLevel>(authenticity.Level), authenticity.ReasonCodes, dismissed.Contains(authenticity.Id),
                    Enum.Parse<AiAssistanceLevel>(ai.Level), ai.ReasonCodes, dismissed.Contains(ai.Id));
            }
        }

        return summaries;
    }

    private static VerificationAssessment? Current(IEnumerable<VerificationAssessment> assessments, VerificationAssessmentKind kind) =>
        assessments.Where(a => a.Kind == kind).OrderByDescending(a => a.AssessedAt).ThenByDescending(a => a.Id).FirstOrDefault();

    private sealed record KeyOrigin(string Name, Guid? CreatedByUserId);

    /// <summary>
    /// Records, for a request made with an API key, that the key read the verification of each application in
    /// <paramref name="applicationIds"/>: the key and when, one row per application (ADR-0021). Does nothing for a session.
    /// The rows are saved before the caller answers, so a read that cannot be recorded is not answered.
    /// </summary>
    public static async Task RecordKeyReadsAsync(
        ClaimsPrincipal user, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time,
        IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken)
    {
        if (user.ApiKeyId() is not { } keyId || applicationIds.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        db.VerificationReviewActions.AddRange(applicationIds.Select(id => VerificationReviewAction.ReadByKey(tenant.OrganizationId!.Value, id, keyId, now)));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The applications whose current assessment of each named kind is at the named level, newest first, with their chips.
    /// One more row than <paramref name="limit"/> is read to know whether there are more.
    /// </summary>
    /// <remarks>
    /// "Current" is the newest assessment of its kind. With <paramref name="includeDismissed"/> false, an assessment a
    /// reviewer set aside does not match. The chips are read the same way the lists read them and the match is checked
    /// again against them, so a tie in assessment time can never list an application under a level it no longer has.
    /// </remarks>
    public static async Task<VerificationMatches> ByLevelAsync(
        ScreenHireDbContext db, AuthenticityLevel? authenticity, AiAssistanceLevel? aiAssistance, Guid? jobId, bool includeDismissed, int limit,
        CancellationToken cancellationToken)
    {
        var current = db.VerificationAssessments.AsNoTracking().Where(a => !db.VerificationAssessments.Any(newer =>
            newer.ApplicationId == a.ApplicationId && newer.Kind == a.Kind && newer.AssessedAt > a.AssessedAt));

        IQueryable<Guid> Matching(VerificationAssessmentKind kind, string? level)
        {
            var ofKind = current.Where(a => a.Kind == kind);
            if (level is not null)
            {
                ofKind = ofKind.Where(a => a.Level == level);
                if (!includeDismissed)
                {
                    ofKind = ofKind.Where(a => !db.VerificationReviewActions.Any(r => r.AssessmentId == a.Id && r.Type == VerificationReviewActionType.Dismissed));
                }
            }

            return ofKind.Select(a => a.ApplicationId);
        }

        var authenticityIds = Matching(VerificationAssessmentKind.Authenticity, authenticity?.ToString());
        var aiAssistanceIds = Matching(VerificationAssessmentKind.AiAssistance, aiAssistance?.ToString());
        var applications = db.Applications.AsNoTracking().Where(a => authenticityIds.Contains(a.Id) && aiAssistanceIds.Contains(a.Id));
        if (jobId is { } id)
        {
            applications = applications.Where(a => a.JobId == id);
        }

        var rows = await applications
            .Join(db.Jobs, a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .Join(db.Candidates, x => x.Application.CandidateId, c => c.Id, (x, c) => new
            {
                ApplicationId = x.Application.Id,
                x.Application.JobId,
                JobTitle = x.Job.Title,
                CandidateId = c.Id,
                CandidateName = c.FullName,
                x.Application.SubmittedAt,
            })
            .OrderByDescending(r => r.SubmittedAt).ThenBy(r => r.ApplicationId)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var summaries = await SummariesAsync(db, [.. rows.Take(limit).Select(r => r.ApplicationId)], cancellationToken);
        var items = rows.Take(limit)
            .Where(r => summaries.TryGetValue(r.ApplicationId, out var s)
                && (authenticity is null || s.Authenticity == authenticity) && (aiAssistance is null || s.AiAssistance == aiAssistance))
            .Select(r => new VerificationMatch(r.ApplicationId, r.JobId, r.JobTitle, r.CandidateId, r.CandidateName, r.SubmittedAt, summaries[r.ApplicationId]))
            .ToList();
        return new VerificationMatches(items, rows.Count > limit);
    }
}
