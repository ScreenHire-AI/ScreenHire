using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Application.Applications;
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
/// <item>The same emails on the "Senior .NET Engineer" job's workflow, as a recruiter would add them, so moving a candidate
/// there sends an email to Mailpit.</item>
/// <item>A draft job from the template, "Backend Engineer (Payments)", for the demo to publish to every board.</item>
/// <item>The synthetic applicants (<see cref="SyntheticApplicants"/>) apply to "Senior .NET Engineer" through the same
/// submission path as the careers form, with their network and session details, so the worker's assessments show every
/// integrity level.</item>
/// <item>The people in <see cref="DemoCandidates"/> apply to the other open jobs and are moved to their stages.</item>
/// </list>
/// Every step is idempotent (a template, automation, job or candidate that exists is left alone), so later starts cost
/// nothing, and a data volume from before a step existed gets that step.
/// </summary>
/// <remarks>
/// A one-shot startup step, not a loop. It runs after startup rather than in <see cref="DevelopmentDataSeeder"/>
/// because submissions and moves publish through Wolverine's outbox, which needs the started host.
/// </remarks>
internal sealed partial class DevelopmentDemo(
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<DevelopmentDemo> logger) : BackgroundService
{
    public const string TemplateName = "Brightwater engineering";

    public const string SyntheticApplicantsJobSlug = "senior-dotnet-engineer";

    public const string DraftJobSlug = "backend-engineer-payments";

    /// <summary>The careers notice version the demo applicants "saw" (NOTICE_VERSION in apps/careers/src/lib/apply.ts).</summary>
    public const string PrivacyNoticeVersion = "2026-10-20.4";

    /// <summary>The emails the template and the .NET job send. Ids are stable, so a later start finds them.</summary>
    public static IReadOnlyList<Automation> CandidateEmails { get; } =
    [
        new("demo_application_received", "Confirm the application to the candidate", new(TriggerType.ApplicationSubmitted), null,
            [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.ApplicationReceived)]),
        new("demo_screening_email", "Tell the candidate they reached Screening", new(TriggerType.StageEntered, "screening"), null,
            [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.StageChange)]),
        new("demo_interview_email", "Tell the candidate they reached Interview", new(TriggerType.StageEntered, "interview"), null,
            [new(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.StageChange)]),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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

        try
        {
            var template = await SeedTemplateAsync(stoppingToken);
            if (template is null)
            {
                return; // no Brightwater: nothing to add to
            }

            await SeedJobEmailsAsync(stoppingToken);
            await SeedDraftJobAsync(template.Value, stoppingToken);
            var applicants = 0;
            foreach (var applicant in SyntheticApplicants.All)
            {
                applicants += await ApplyAsync(SyntheticApplicantsJobSlug, applicant, stageId: null, stoppingToken) ? 1 : 0;
            }

            var candidates = 0;
            foreach (var candidate in DemoCandidates.All)
            {
                candidates += await ApplyAsync(candidate.JobSlug, candidate.Applicant, candidate.StageId, stoppingToken) ? 1 : 0;
            }

            LogSeeded(logger, applicants, candidates);
        }
        // Demo data is a convenience: a failure is logged and never stops the API.
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

    private async Task<(Guid TemplateId, Guid VersionId)?> SeedTemplateAsync(CancellationToken cancellationToken)
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
        if (existing?.Versions.FirstOrDefault(v => v.Status == TemplateVersionStatus.Published) is { } published)
        {
            return (existing.Id, published.Id);
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
        return (template.Id, version.Id);
    }

    private async Task SeedJobEmailsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var job = await step.Outbox.DbContext.Jobs.SingleOrDefaultAsync(j => j.Slug == SyntheticApplicantsJobSlug, cancellationToken);
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

    private async Task SeedDraftJobAsync((Guid TemplateId, Guid VersionId) template, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        if (await db.Jobs.AnyAsync(j => j.Slug == DraftJobSlug, cancellationToken))
        {
            return;
        }

        var version = await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == template.VersionId, cancellationToken);
        var job = new Job
        {
            OrganizationId = step.OrganizationId,
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
            CreatedAt = step.Now,
        };
        job.SetSalary(new JobSalary(70_000, 90_000, "GBP", SalaryPeriod.Year));
        job.UseTemplate(version);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Submits one applicant through the careers submission path, then moves them to `stageId` as Brightwater's Owner.
    // False when they had already applied or the job is not there.
    private async Task<bool> ApplyAsync(string jobSlug, SyntheticApplicant applicant, string? stageId, CancellationToken cancellationToken)
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

        if (stageId is not null && job.Workflow.FindStage(stageId) is { } target && application.CurrentStageId != target.Id)
        {
            var from = application.CurrentStageId;
            var now = time.GetUtcNow();
            if (application.MoveTo(target, now, step.Owner, note: null) is { } moved)
            {
                db.ApplicationEvents.Add(moved);
                await step.Outbox.PublishAsync(new ApplicationMoved(application.OrganizationId, application.Id, application.JobId, from, target.Id, moved.Id, now));
                await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
        }

        return true;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development demo: the demo data could not be seeded")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
