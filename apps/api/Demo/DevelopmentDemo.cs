using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Integrity;
using ScreenHire.Application.Jobs;
using ScreenHire.Application.Templates;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Demo;

/// <summary>
/// The local demo's data on top of <see cref="DevelopmentDataSeeder"/>: Development only, and only when
/// <c>ScreenHire:Demo:Enabled</c> is on (the AppHost sets it for <c>pnpm dev</c>; the test stacks never do, so they keep
/// Brightwater as the seeder leaves it). Once the API has started, in order:
/// <list type="number">
/// <item>Brightwater's own template, "Brightwater engineering": the "Software engineer" system template plus emails to the
/// candidate when they apply and when they reach Screening or Interview.</item>
/// <item>The same emails on the three open jobs' workflows, as a recruiter would add them, so moving a candidate there
/// sends an email to Mailpit.</item>
/// <item>A draft job from the template, "Backend Engineer (Payments)", left on no board for a person to publish during the demo.</item>
/// <item>The open jobs posted to boards through the same publish as the dashboard's: the .NET and designer jobs to every
/// board, the frontend job to the careers page and the sandbox board only.</item>
/// <item>"Support Engineer", from the template, published to every board, for the closed job below.</item>
/// <item>The synthetic applicants (<see cref="SyntheticApplicants"/>) apply to "Senior .NET Engineer" through the same
/// submission path as the careers form, with their network and session details, so the worker's assessments show every
/// integrity level.</item>
/// <item>The people in <see cref="DemoCandidates"/> apply to their jobs and are moved through their stages, with notes.</item>
/// <item>"Support Engineer" is closed and taken down everywhere, so a closed job, its 410 page and removed postings show.
/// It stays open until all its candidates have applied, so a start that failed one leaves it for the next start.</item>
/// </list>
/// Each step first looks for what it adds (a template, automation, job, posting or candidate that exists is left alone),
/// so later starts add nothing, and a data volume from before a step existed gets that step. A step does not repair
/// itself: a candidate whose move failed stays where they got to, a job someone took off a board stays off it, and a demo
/// email someone removed from a job comes back on the next start.
/// </summary>
/// <remarks>
/// A one-shot startup step, not a loop. It runs after startup rather than in <see cref="DevelopmentDataSeeder"/>
/// because submissions and moves publish through Wolverine's outbox, which needs the started host. It is also the API's
/// <c>development-demo</c> health check: <c>/health</c> answers Unhealthy until every step has run, so the worker, the
/// frontends and <c>/preview</c>, which wait for a healthy API, start with the demo data in place. No step may wait for
/// the worker, which only starts once the API is healthy.
/// </remarks>
internal sealed partial class DevelopmentDemo(
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<DevelopmentDemo> logger)
    : BackgroundService, IHealthCheck
{
    public const string TemplateName = "Brightwater engineering";

    public const string SyntheticApplicantsJobSlug = DemoCandidates.SeniorDotnetEngineer;

    public const string DraftJobSlug = "backend-engineer-payments";

    public const string ClosedJobSlug = DemoCandidates.SupportEngineer;

    /// <summary>The careers notice version the demo applicants "saw" (NOTICE_VERSION in apps/careers/src/lib/apply.ts).</summary>
    public const string PrivacyNoticeVersion = "2026-10-20.4";

    /// <summary>The seeded open jobs that get the candidate emails, and the boards each is posted to (null: every board).</summary>
    public static IReadOnlyList<(string Slug, IReadOnlyList<string>? Boards)> OpenJobs { get; } =
    [
        (SyntheticApplicantsJobSlug, null),
        (DemoCandidates.ProductDesigner, null),
        (DemoCandidates.FrontendEngineer, [Boards.CareersPage, Boards.Sandbox]),
    ];

    /// <summary>The emails the template and the open jobs send. Ids are stable, so a later start finds them.</summary>
    public static IReadOnlyList<Automation> CandidateEmails { get; } =
    [
        new("demo_application_received", "Confirm the application to the candidate", new(TriggerType.ApplicationSubmitted), null,
            [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.ApplicationReceived)]),
        new("demo_screening_email", "Tell the candidate they reached Screening", new(TriggerType.StageEntered, "screening"), null,
            [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.StageChange)]),
        new("demo_interview_email", "Tell the candidate they reached Interview", new(TriggerType.StageEntered, "interview"), null,
            [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.StageChange)]),
    ];

    private volatile bool finished;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(finished
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The local demo's data is still being added."));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider")
            {
                return; // the build's OpenAPI generation starts the host without a database
            }

            var started = new TaskCompletionSource();
            await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
            {
                await started.Task.WaitAsync(stoppingToken);
            }

            await SeedAsync(stoppingToken);
        }
        finally
        {
            // A failed step is logged and the API turns healthy anyway: demo data never holds up the stack.
            finished = true;
        }
    }

    private async Task SeedAsync(CancellationToken stoppingToken)
    {
        // Without Brightwater every step finds nothing to add to.
        var templateVersion = await TryAsync(() => SeedTemplateAsync(stoppingToken), stoppingToken);
        foreach (var (slug, _) in OpenJobs)
        {
            await TryAsync(() => SeedJobEmailsAsync(slug, stoppingToken), stoppingToken);
        }

        if (templateVersion is { } published)
        {
            await TryAsync(() => SeedJobFromTemplateAsync(published, DraftJobSlug, DraftJob, stoppingToken), stoppingToken);
            await TryAsync(() => SeedJobFromTemplateAsync(published, ClosedJobSlug, ClosedJob, stoppingToken), stoppingToken);
        }

        foreach (var (slug, boards) in OpenJobs)
        {
            await TryAsync(() => PostAsync(slug, boards, stoppingToken), stoppingToken);
        }

        await TryAsync(() => PostAsync(ClosedJobSlug, boards: null, stoppingToken), stoppingToken);

        var applicants = 0;
        foreach (var applicant in SyntheticApplicants.All)
        {
            applicants += await TryAsync(() => ApplyAsync(SyntheticApplicantsJobSlug, applicant, moves: [], stoppingToken), stoppingToken) ? 1 : 0;
        }

        var candidates = 0;
        foreach (var candidate in DemoCandidates.All)
        {
            candidates += await TryAsync(() => ApplyAsync(candidate.JobSlug, candidate.Applicant, candidate.Moves, stoppingToken), stoppingToken) ? 1 : 0;
        }

        await TryAsync(() => CloseAsync(ClosedJobSlug, stoppingToken), stoppingToken);
        LogSeeded(logger, applicants, candidates);
    }

    // Demo data is a convenience: a failed step is logged, never stops the API, and does not keep the next step from running.
    private async Task<T?> TryAsync<T>(Func<Task<T>> step, CancellationToken stoppingToken)
    {
        try
        {
            return await step();
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
            return default;
        }
    }

    private async Task TryAsync(Func<Task> step, CancellationToken stoppingToken)
    {
        try
        {
            await step();
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
        }
    }

    // Each step gets its own scope, signed in as Brightwater's Owner: one transaction and outbox flush, like a request.
    private async Task<Step?> BeginAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        var services = scope.ServiceProvider;
        var outbox = services.GetRequiredService<IDbContextOutbox<ScreenHireDbContext>>();
        var db = outbox.DbContext;
        var organization = await db.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Slug == DevelopmentDataSeeder.DemoOrganizationSlug, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        // Identity's normalized form (UpperInvariantLookupNormalizer), as the seeder stores it.
        var ownerEmail = DevelopmentDataSeeder.DemoOwnerEmail.ToUpperInvariant();
        var owner = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == ownerEmail)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
        services.GetRequiredService<TenantContext>().SetOrganization(organization.Id);
        return new Step(services, outbox, organization.Id, owner, time.GetUtcNow());
    }

    // The id of the template's newest published version, which the demo's jobs are created from.
    private async Task<Guid?> SeedTemplateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return null;
        }

        var db = step.Outbox.DbContext;
        var existing = await db.HiringTemplates.AsNoTracking().Include(t => t.Versions)
            .Where(t => t.OrganizationId == step.OrganizationId && t.Name == TemplateName)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Never a second template of the name: the demo's jobs use the newest published version, or are skipped.
            return existing.Versions.OrderByDescending(v => v.Version).FirstOrDefault(v => v.Status == TemplateVersionStatus.Published) is { } published
                ? published.Id
                : null;
        }

        var source = await db.HiringTemplateVersions.AsNoTracking()
            .SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion2, cancellationToken);
        var definition = source.Definition with
        {
            Workflow = source.Definition.Workflow with { Automations = [.. source.Definition.Workflow.Automations, .. CandidateEmails] },
        };
        var problems = DefinitionValidator.Validate(definition);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The demo template is not valid: {string.Join("; ", problems.Select(p => p.Message))}");
        }

        var template = HiringTemplate.Create(
            step.OrganizationId, TemplateName,
            "How Brightwater hires engineers: the Software engineer pipeline, with an email to the candidate at each step.",
            definition, step.Now, step.Owner);
        var version = template.Versions.Single();
        template.Publish(version, step.Now, step.Owner);
        db.HiringTemplates.Add(template);
        await step.Outbox.PublishAsync(new HiringTemplateVersionPublished(step.OrganizationId, template.Id, version.Id, version.Version, step.Now));
        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return version.Id;
    }

    private async Task SeedJobEmailsAsync(string slug, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var job = await step.Outbox.DbContext.Jobs.SingleOrDefaultAsync(j => j.Slug == slug, cancellationToken);
        if (job is null || job.Status == JobStatus.Closed)
        {
            return;
        }

        var missing = CandidateEmails.Where(email => job.Workflow.Automations.All(a => a.Id != email.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        // The same stages, plus automations: no application can sit in a stage the edit drops.
        job.ReviseWorkflow(job.Workflow with { Automations = [.. job.Workflow.Automations, .. missing] }, step.Now);
        await step.Outbox.PublishAsync(new JobWorkflowChanged(job.OrganizationId, job.Id, job.WorkflowVersion, step.Now));
        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    // A job from Brightwater's template, as a recruiter creates it on the dashboard: a draft until something publishes it.
    private async Task SeedJobFromTemplateAsync(
        Guid templateVersionId, string slug, Func<Guid, DateTimeOffset, Job> create, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        if (await db.Jobs.AnyAsync(j => j.Slug == slug, cancellationToken))
        {
            return;
        }

        var version = await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == templateVersionId, cancellationToken);
        var job = create(step.OrganizationId, step.Now);
        job.UseTemplate(version);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Job DraftJob(Guid organizationId, DateTimeOffset now)
    {
        var job = new Job
        {
            OrganizationId = organizationId,
            CreatedAt = now,
            Title = "Backend Engineer (Payments)",
            Slug = DraftJobSlug,
            Department = "Engineering",
            Description =
                "<p>Brightwater Studio builds booking and payment software for independent venues across the UK and Ireland. " +
                "Our payments team is four engineers who own the service end to end, from the card provider's webhooks to the " +
                "reports our customers close their month with.</p>" +
                "<h2>What you will do</h2><ul><li>Design and ship features in C# and Postgres.</li>" +
                "<li>Keep payouts correct and on time, and know when they are not.</li>" +
                "<li>Review code and help the team improve how it works.</li></ul>" +
                "<h2>What we are looking for</h2><ul><li>Several years of building and running services in production.</li>" +
                "<li>Care for money, idempotency and audit trails.</li></ul>",
            WorkplaceType = WorkplaceType.Remote,
            EmploymentType = EmploymentType.FullTime,
            ApplicantLocationRequirements = ["GB", "IE"],
        };
        job.SetSalary(new JobSalary(70_000, 90_000, "GBP", SalaryPeriod.Year));
        return job;
    }

    private static Job ClosedJob(Guid organizationId, DateTimeOffset now)
    {
        var job = new Job
        {
            OrganizationId = organizationId,
            CreatedAt = now,
            Title = "Support Engineer",
            Slug = ClosedJobSlug,
            Department = "Engineering",
            Description =
                "<p>Our support engineers are the second line for venues whose bookings or payouts look wrong. You read logs " +
                "and queries, fix what you can, and hand the rest to the product teams with everything they need.</p>" +
                "<h2>What you will do</h2><ul><li>Investigate payment and booking issues across our services.</li>" +
                "<li>Write runbooks so first-line support can solve more on their own.</li></ul>",
            WorkplaceType = WorkplaceType.Remote,
            EmploymentType = EmploymentType.FullTime,
            ApplicantLocationRequirements = ["GB", "IE"],
        };
        job.SetSalary(new JobSalary(45_000, 55_000, "GBP", SalaryPeriod.Year));
        return job;
    }

    // Posts the job through the dashboard's publish: opens a draft, or posts an open job to boards. Only a job on no board
    // at all, so a board someone took it off stays off.
    private async Task PostAsync(string slug, IReadOnlyList<string>? boards, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Slug == slug, cancellationToken);
        if (job is null || job.Status == JobStatus.Closed || await db.JobPostings.AnyAsync(p => p.JobId == job.Id, cancellationToken))
        {
            return;
        }

        var services = step.Services;
        var refused = await JobEndpoints.PublishAsync(
            step.Outbox,
            services.GetRequiredService<JobPublishValidator>(),
            services.GetRequiredService<DistributionPlanner>(),
            services.GetRequiredService<BoardRegistry>(),
            services.GetRequiredService<IOptions<DomainOptions>>().Value,
            job, boards, step.Now, cancellationToken);
        if (refused is not null)
        {
            var reason = refused.Result switch
            {
                ValidationProblem invalid => string.Join("; ", invalid.ProblemDetails.Errors.SelectMany(e => e.Value)),
                ProblemHttpResult problem => problem.ProblemDetails.Detail,
                _ => null,
            };
            throw new InvalidOperationException($"The demo could not publish '{slug}': {reason}");
        }
    }

    // Closes the job and takes it down everywhere through the dashboard's close, once its candidates have applied.
    private async Task CloseAsync(string slug, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Slug == slug, cancellationToken);
        if (job is not { Status: JobStatus.Open })
        {
            return;
        }

        // Only once all its candidates are in: a closed job takes no more applications, so one that failed to apply
        // could never apply on a later start.
        var expected = DemoCandidates.All.Where(c => c.JobSlug == slug).Select(c => Domain.Candidates.Candidate.Normalize(c.Applicant.Email)).ToList();
        var applied = await db.Candidates.CountAsync(c => expected.Contains(c.NormalizedEmail), cancellationToken);
        if (applied < expected.Count)
        {
            throw new InvalidOperationException($"The demo leaves '{slug}' open: {applied} of its {expected.Count} candidates applied.");
        }

        if (await JobEndpoints.CloseAsync(step.Outbox, step.Services.GetRequiredService<DistributionPlanner>(), job, step.Now, cancellationToken) is { } conflict)
        {
            throw new InvalidOperationException($"The demo could not close '{slug}': {conflict.ProblemDetails.Detail}");
        }
    }

    // Submits one applicant through the careers submission path, then makes `moves` in order as Brightwater's Owner.
    // False when they had already applied or the job is not open.
    private async Task<bool> ApplyAsync(string jobSlug, SyntheticApplicant applicant, IReadOnlyList<DemoMove> moves, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return false;
        }

        var db = step.Outbox.DbContext;
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Slug == jobSlug && j.Status == JobStatus.Open, cancellationToken);
        var email = Domain.Candidates.Candidate.Normalize(applicant.Email);
        if (job is null || await db.Candidates.AnyAsync(c => c.NormalizedEmail == email, cancellationToken))
        {
            return false;
        }

        var resumeQuestion = ApplicationForm.QuestionsFor(job.Workflow).First(q => q.Id == ApplicationForm.ResumeQuestionId);
        var upload = await step.Services.GetRequiredService<ApplicantUploads>().StoreDevelopmentFileAsync(
            step.OrganizationId, job.Id, resumeQuestion, applicant.ResumeFileName, applicant.ResumeBytes(), cancellationToken);
        var intake = new ApplicationIntake(
            ApplicationSource.CareersSite,
            [new ApplicationAnswer(resumeQuestion.Id, upload.UploadId.ToString()), .. applicant.Answers],
            PrivacyNoticeVersion,
            applicant.HoneypotFilled,
            BotCheckResult.NotChecked);
        var created = await ApplicationSubmission.SubmitAsync(
            step.Outbox, job, new Contracts.CandidateInput(applicant.FullName, applicant.Email, applicant.Phone),
            new ApplicationDeclarations(false, applicant.AiUseDeclaration, applicant.AiUseDetails),
            intake, [upload], step.Now, actorUserId: null, cancellationToken,
            new SubmissionSession(applicant.IpAddress, applicant.UserAgent, applicant.DeviceFingerprint, step.Now - applicant.TimeToComplete, applicant.AutomationMarkers),
            step.Services.GetRequiredService<IOptions<IntegrityOptions>>().Value,
            Telemetry(applicant));
        if (created is not (var application, _))
        {
            return false;
        }

        if (moves.Count > 0)
        {
            await MoveAsync(application.Id, moves, cancellationToken);
        }

        return true;
    }

    // Makes `moves` in order as Brightwater's Owner, in a scope of its own like the dashboard's move request: an outbox
    // that has already flushed once (the submission's) keeps later messages in its table without sending them. One commit
    // for all the moves; a move to a stage the job lacks stops the rest.
    private async Task MoveAsync(Guid applicationId, IReadOnlyList<DemoMove> moves, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId, cancellationToken);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        foreach (var move in moves)
        {
            if (job.Workflow.FindStage(move.StageId) is not { } target)
            {
                throw new InvalidOperationException($"The demo moves an applicant to '{move.StageId}', which '{job.Slug}' has no stage for.");
            }

            var from = application.CurrentStageId;
            var now = time.GetUtcNow();
            if (application.MoveTo(target, now, step.Owner, move.Note) is { } moved)
            {
                db.ApplicationEvents.Add(moved);
                await step.Outbox.PublishAsync(new ApplicationMoved(application.OrganizationId, application.Id, application.JobId, from, target.Id, moved.Id, now));
            }
        }

        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    // What the applicant's browser would have sent, as the last page's snapshot of a session of its own.
    private static TelemetryBinding? Telemetry(SyntheticApplicant applicant) => applicant.Telemetry is not { } telemetry
        ? null
        : new TelemetryBinding(
            Guid.CreateVersion7(),
            new Contracts.TelemetrySnapshotInput(
                SessionToken: "",
                PageViewId: Guid.CreateVersion7(),
                Sequence: 1,
                [.. telemetry.Fields.Select(f => new Contracts.FieldTelemetryInput(
                    f.FieldId, f.PasteCount, f.PastedCharacters, f.TypedCharacters, f.FocusCount, f.BlurCount, f.ActiveMilliseconds, f.Autofilled))],
                new Contracts.PageTelemetryInput(telemetry.Page.VisibilityChanges, telemetry.Page.AutomationMarkers)));

    private sealed record Step(IServiceProvider Services, IDbContextOutbox<ScreenHireDbContext> Outbox, Guid OrganizationId, Guid? Owner, DateTimeOffset Now);

    [LoggerMessage(Level = LogLevel.Information, Message = "Development demo: {Applicants} synthetic applicants and {Candidates} demo candidates applied to Brightwater")]
    private static partial void LogSeeded(ILogger logger, int applicants, int candidates);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development demo: a step of the demo data could not be seeded")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
