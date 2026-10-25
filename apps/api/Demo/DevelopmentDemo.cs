using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
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
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
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
/// <item>A questionnaire on each seeded job that has none (<see cref="DemoQuestionnaires"/>): the designer, frontend and
/// Customer Success Lead jobs. The .NET job asks the "Software engineer" questions already.</item>
/// <item>A draft job from the template, "Backend Engineer (Payments)", left on no board for a person to publish during the demo.</item>
/// <item>The open jobs posted to boards through the same publish as the dashboard's: the .NET and designer jobs to every
/// board, the frontend job to the careers page and the sandbox board only.</item>
/// <item>"Support Engineer", from the template, published to every board, for the closed job below.</item>
/// <item>Change history (<see cref="SeedHistoryAsync"/>): Jordan Ellis starts version 2 of the template (a question and a
/// longer time limit on Interview) and changes the
/// draft job's salary, and Avery Quinn adds an "Interview invitation" email and edits its subject, on top of the steps above, which Avery
/// makes. Applying and moving candidates are not tracked changes.</item>
/// <item>A custom role (<see cref="SeedInterviewerAsync"/>): Avery Quinn makes "Interviewer" from Viewer, with notes, tags and
/// scorecards, and gives it to Robin Hale, whom the seeder adds as a Viewer, so Roles, the role's History and the
/// membership log show a custom role.</item>
/// <item>Hiring teams (<see cref="SeedHiringTeamsAsync"/>): Jordan Ellis is on every job's hiring team, and Robin Hale sees
/// only the jobs he is on, which is "Senior .NET Engineer" alone, so signing in as Robin shows job access at work.</item>
/// <item>The synthetic applicants (<see cref="SyntheticApplicants"/>) apply to "Senior .NET Engineer" through the same
/// submission path as the careers form, with their network and session details, so the worker's assessments show every
/// integrity level.</item>
/// <item>The people in <see cref="DemoCandidates"/> apply to their jobs, answering every question of their job, and are
/// moved through their stages, with notes.</item>
/// <item>Notes on one .NET candidate (<see cref="SeedNotesAsync"/>): Jordan Ellis mentions Avery Quinn, and Avery mentions Jordan and
/// edits the note, so the candidate's Activity shows mentions and an edited note and Mailpit has the mention emails.</item>
/// <item>Scorecards (<see cref="SeedScorecardsAsync"/>): Brightwater's own "Technical interview" scorecard, with every answer
/// kind, on the .NET job's Interview stage; scorecards from Avery Quinn and Jordan Ellis on three candidates, so the job
/// tables show ratings and "Not rated yet"; and a review for Avery on Priya Natarajan, whose rating Avery then reads as
/// "Rating after your scorecard". Seeded history: it fires no ReviewerDecision automation.</item>
/// <item>One email changed for one send (<see cref="SeedOneOffEmailAsync"/>): Avery emails the same candidate the "Interview
/// invitation" template with an extra sentence, so Messages shows the email as it went while the template stays as it was.</item>
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
    public const string PrivacyNoticeVersion = "2026-10-23.1";

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

        foreach (var (slug, questionnaire) in DemoQuestionnaires.All)
        {
            await TryAsync(() => SeedQuestionnaireAsync(slug, questionnaire, stoppingToken), stoppingToken);
        }

        if (templateVersion is { } published)
        {
            await TryAsync(() => SeedJobFromTemplateAsync(published, DraftJobSlug, DraftJob, stoppingToken), stoppingToken);
            await TryAsync(() => SeedJobFromTemplateAsync(published, ClosedJobSlug, ClosedJob, stoppingToken), stoppingToken);
        }

        await TryAsync(() => SeedHistoryAsync(stoppingToken), stoppingToken);
        await TryAsync(() => SeedInterviewerAsync(stoppingToken), stoppingToken);
        await TryAsync(() => SeedHiringTeamsAsync(stoppingToken), stoppingToken);

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

        await TryAsync(() => SeedNotesAsync(stoppingToken), stoppingToken);
        await TryAsync(() => SeedScorecardsAsync(stoppingToken), stoppingToken);
        await TryAsync(() => SeedOneOffEmailAsync(stoppingToken), stoppingToken);
        await TryAsync(() => CloseAsync(ClosedJobSlug, stoppingToken), stoppingToken);
        LogSeeded(logger, applicants, candidates);
    }

    /// <summary>The demo candidate whose Activity shows notes with mentions, one of them edited.</summary>
    public const string NotedCandidateEmail = "joel.mensah@example.org";

    // Notes with mentions on one .NET candidate, through the same domain methods and outbox as the notes endpoints, so the
    // mentions reach Mailpit as emails: Jordan mentions Avery, and Avery mentions Jordan and then edits the note, so it
    // says Edited. A candidate who already has notes is left alone.
    private async Task SeedNotesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken) is not { Owner: { } avery } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var email = Domain.Candidates.Candidate.Normalize(NotedCandidateEmail);
        var application = await (
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates on a.CandidateId equals c.Id
            where c.NormalizedEmail == email
            select a).SingleOrDefaultAsync(cancellationToken);
        if (application is null
            || await db.ApplicationNotes.AnyAsync(n => n.ApplicationId == application.Id, cancellationToken)
            || await UserIdAsync(db, DevelopmentDataSeeder.DemoRecruiterEmail, cancellationToken) is not { } jordan)
        {
            return;
        }

        // After everything the candidate's timeline already holds, which the demo wrote moments ago, so the notes are the
        // newest entries and Overview's last five show them.
        var last = await db.ApplicationEvents.Where(e => e.ApplicationId == application.Id)
            .MaxAsync(e => (DateTimeOffset?)e.OccurredAt, cancellationToken) ?? step.Now;
        var gap = (step.Now - last) / 3;
        var (jordansNote, jordansEntry) = application.AddNote(
            $"{NoteMentions.Token(avery)}, the take-home is due Friday. Can you review it with me on Monday?", last + gap, jordan);
        var (averysNote, averysEntry) = application.AddNote(
            $"Good detail on the billing ledger. {NoteMentions.Token(jordan)}, ask in the technical round how they tested the migration.",
            last + (gap * 2), avery);
        db.ApplicationNotes.AddRange(jordansNote, averysNote);
        db.ApplicationEvents.AddRange(jordansEntry, averysEntry);
        foreach (var (note, entry, mentioned) in new[] { (jordansNote, jordansEntry, avery), (averysNote, averysEntry, jordan) })
        {
            await step.Outbox.PublishAsync(new ApplicationNoteAdded(application.OrganizationId, application.Id, application.JobId, entry.Id, entry.OccurredAt));
            await step.Outbox.PublishAsync(new TeammateMentioned(
                application.OrganizationId, application.Id, application.JobId, note.Id, note.Mentions.Single(m => m.UserId == mentioned).Id,
                mentioned, note.AuthorUserId, entry.OccurredAt));
        }

        if (application.EditNote(
                averysNote,
                $"{averysNote.Text} And how long the move to Postgres took.",
                step.Now, avery) is { Entry: var edited })
        {
            db.ApplicationEvents.Add(edited);
            await step.Outbox.PublishAsync(new ApplicationNoteEdited(application.OrganizationId, application.Id, application.JobId, averysNote.Id, step.Now));
        }

        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    // The demo's scorecards (DemoScorecards), through the same domain methods as the scorecard endpoint. Seeded history
    // publishes no RunAutomations: a ReviewerDecision automation would move demo candidates on after every start. A
    // candidate who already has a scorecard is left alone, and so is a library scorecard or a job stage already set up.
    private async Task SeedScorecardsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, signedIn: DevelopmentDataSeeder.DemoOwnerEmail) is not { Owner: { } avery } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var library = DemoScorecards.TechnicalInterview(step.OrganizationId, step.Now);
        var own = await db.ScorecardTemplates.SingleOrDefaultAsync(t => t.Name == library.Name, cancellationToken);
        if (own is null)
        {
            own = library;
            db.ScorecardTemplates.Add(own);
        }

        // The .NET job's Interview stage takes the team's scorecard, as picking it in the workflow builder would.
        var dotnet = await db.Jobs.SingleOrDefaultAsync(j => j.Slug == SyntheticApplicantsJobSlug, cancellationToken);
        if (dotnet is { Status: not JobStatus.Closed } && dotnet.Workflow.FindStage("interview") is { } interview && interview.ScorecardTemplateId != own.Key)
        {
            dotnet.ReviseWorkflow(dotnet.Workflow with
            {
                Stages = [.. dotnet.Workflow.Stages.Select(s => s.Id == interview.Id ? s with { ScorecardTemplateId = own.Key, Scorecard = own.ToScorecard() } : s)],
            }, step.Now);
            await step.Outbox.PublishAsync(new JobWorkflowChanged(dotnet.OrganizationId, dotnet.Id, dotnet.WorkflowVersion, step.Now));
        }

        var reviewers = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var email in DemoScorecards.All.Select(s => s.ReviewerEmail).Distinct(StringComparer.Ordinal))
        {
            if (await UserIdAsync(db, email, cancellationToken) is { } id)
            {
                reviewers[email] = id;
            }
        }

        var given = 0;
        foreach (var group in DemoScorecards.All.GroupBy(s => s.CandidateEmail, StringComparer.Ordinal))
        {
            var normalized = Domain.Candidates.Candidate.Normalize(group.Key);
            var found = await (
                from a in db.Applications
                join c in db.Candidates on a.CandidateId equals c.Id
                join j in db.Jobs on a.JobId equals j.Id
                where c.NormalizedEmail == normalized
                select new { Application = a, Job = j }).SingleOrDefaultAsync(cancellationToken);
            if (found is null || await db.ApplicationScorecards.AnyAsync(s => s.ApplicationId == found.Application.Id, cancellationToken))
            {
                continue;
            }

            foreach (var (seeded, index) in group.Select((s, i) => (s, i)))
            {
                if (found.Job.Workflow.FindStage(seeded.StageId) is not { Scorecard: not null } stage || !reviewers.TryGetValue(seeded.ReviewerEmail, out var reviewer))
                {
                    continue;
                }

                // A little apart and before now, oldest first, so Activity reads in the order they were given.
                var at = step.Now.AddMinutes(-10 * (group.Count() - index));
                var (scorecard, entry) = found.Application.SubmitScorecard(
                    stage, reviewer, seeded.Answers, seeded.Recommendation, seeded.Note, found.Job.WorkflowVersion, at);
                db.ApplicationScorecards.Add(scorecard);
                db.ApplicationEvents.Add(entry);
                await step.Outbox.PublishAsync(new ScorecardSubmitted(
                    found.Application.OrganizationId, found.Application.Id, found.Application.JobId, scorecard.Id, entry.Id, at));
                given++;
            }

            if (group.Key == DemoScorecards.ReviewForOwner)
            {
                // As AssignReviewer makes it: Avery has her own review to give, so the others' scorecards wait for it.
                db.ApplicationTasks.Add(new Domain.Automations.ApplicationTask
                {
                    OrganizationId = found.Application.OrganizationId,
                    ApplicationId = found.Application.Id,
                    Kind = Domain.Automations.ApplicationTaskKind.Review,
                    Note = "Review this application.",
                    AssigneeUserId = avery,
                    StageId = DemoScorecards.ReviewForOwnerStage,
                    CreatedAt = step.Now,
                });
            }
        }

        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        LogScorecards(logger, given);
    }

    /// <summary>The sentence the demo's one-off email adds to the "Interview invitation" template for one send.</summary>
    public const string OneOffEmailSentence = "Your answer about the billing ledger stood out, and we would like to hear more about it.";

    // One email changed for one send (the email composer), on the noted candidate: Avery starts from the "Interview
    // invitation" template and adds a sentence, through the same rendering and outbox as POST /v1/emails, so Messages shows
    // the changed email once the worker sends it, the template stays as it was, and Mailpit has it. A candidate who already
    // has a copy of that template is left alone.
    private async Task SeedOneOffEmailAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { Owner: { } avery } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var email = Domain.Candidates.Candidate.Normalize(NotedCandidateEmail);
        var row = await (
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            join j in db.Jobs.AsNoTracking() on a.JobId equals j.Id
            where c.NormalizedEmail == email
            select new { Application = a, Candidate = c, Job = j }).SingleOrDefaultAsync(cancellationToken);
        var template = await db.EmailTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Name == InterviewEmailName, cancellationToken);
        if (row is null || template is null)
        {
            return;
        }

        var templateId = template.Key;
        if (await db.ApplicationEmails.AnyAsync(e => e.ApplicationId == row.Application.Id && e.TemplateId == templateId, cancellationToken))
        {
            return;
        }

        var organization = await db.Organizations.AsNoTracking().Where(o => o.Id == step.OrganizationId).Select(o => o.Name).SingleAsync(cancellationToken);
        var body = template.Body.Replace(
            "We would like to invite you to an interview.", $"{OneOffEmailSentence} We would like to invite you to an interview.", StringComparison.Ordinal);
        var copy = ApplicationEmails.Prepare(
            step.Services.GetRequiredService<EmailTemplates>(), row.Application, row.Candidate, row.Job.Title, organization,
            row.Job.Workflow.FindStage(row.Application.CurrentStageId)?.Name, templateId, new EmailTemplateContent(template.Subject, body),
            Guid.CreateVersion7(), step.Now);
        db.ApplicationEmails.Add(copy);
        await step.Outbox.PublishAsync(new SendApplicationEmail(row.Application.OrganizationId, row.Application.Id, copy.Id, avery));
        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
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

    // Each step gets its own scope, acting for Brightwater's Owner: one transaction and outbox flush, like a request. A
    // step that changes a template or a job also signs in as `signedIn` (an email of the seeder's members), so the change
    // history names the person, as it would for the dashboard's edit. Applying and moving candidates sign nobody in.
    private async Task<Step?> BeginAsync(AsyncServiceScope scope, CancellationToken cancellationToken, string? signedIn = null)
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

        var owner = await UserIdAsync(db, DevelopmentDataSeeder.DemoOwnerEmail, cancellationToken);
        var tenant = services.GetRequiredService<TenantContext>();
        tenant.SetOrganization(organization.Id);
        if (signedIn is not null && await UserIdAsync(db, signedIn, cancellationToken) is { } user)
        {
            tenant.SetUser(user);
            tenant.SetOrganization(organization.Id);
            await Memberships.ResolveMemberScopeAsync(tenant, db, cancellationToken);
        }

        return new Step(services, outbox, organization.Id, owner, time.GetUtcNow());
    }

    // Identity's normalized form (UpperInvariantLookupNormalizer), as the seeder stores it.
    private static Task<Guid?> UserIdAsync(ScreenHireDbContext db, string email, CancellationToken cancellationToken)
    {
        var normalized = email.ToUpperInvariant();
        return db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
    }

    // The id of the template's newest published version, which the demo's jobs are created from.
    private async Task<Guid?> SeedTemplateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { } step)
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
            .SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion4, cancellationToken);
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
            definition, step.Now, step.Owner, copiedFrom: SystemTemplates.SoftwareEngineer);
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
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { } step)
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

    // The questions the job lacks, added as a recruiter's edit of its workflow, before anyone applies to it.
    private async Task SeedQuestionnaireAsync(string slug, Questionnaire questionnaire, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { } step)
        {
            return;
        }

        var job = await step.Outbox.DbContext.Jobs.SingleOrDefaultAsync(j => j.Slug == slug, cancellationToken);
        if (job is null || job.Status == JobStatus.Closed)
        {
            return;
        }

        var questions = job.Workflow.Questionnaire.Questions;
        var missing = questionnaire.Questions.Where(question => questions.All(q => q.Id != question.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        // The same stages, plus questions: the dashboard's workflow edit validates the same way.
        var workflow = job.Workflow with { Questionnaire = new Questionnaire([.. questions, .. missing]) };
        var problems = DefinitionValidator.Validate(workflow);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The demo questionnaire for '{slug}' is not valid: {string.Join("; ", problems.Select(p => p.Message))}");
        }

        job.ReviseWorkflow(workflow, step.Now);
        await step.Outbox.PublishAsync(new JobWorkflowChanged(job.OrganizationId, job.Id, job.WorkflowVersion, step.Now));
        await step.Outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    // A job from Brightwater's template, as a recruiter creates it on the dashboard: a draft until something publishes it.
    private async Task SeedJobFromTemplateAsync(
        Guid templateVersionId, string slug, Func<Guid, DateTimeOffset, Job> create, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { } step)
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

    /// <summary>The question Jordan Ellis adds to version 2 of Brightwater's template, so its History has a story to tell.</summary>
    public static Question NoticePeriodQuestion { get; } = new("demo_notice_period", QuestionType.ShortText, "What is your notice period?");

    /// <summary>The email template of Brightwater's own that Avery Quinn adds and then edits.</summary>
    public const string InterviewEmailName = "Interview invitation";

    // Edits a team would make in its first days, made through the same tracked saves as the dashboard's, so the History
    // sheets of the template, an email template and a job have entries by two people. Each edit is made once.
    private async Task SeedHistoryAsync(CancellationToken cancellationToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoRecruiterEmail) is { } step)
            {
                var db = step.Outbox.DbContext;
                var template = await db.HiringTemplates.Include(t => t.Versions)
                    .SingleOrDefaultAsync(t => t.OrganizationId == step.OrganizationId && t.Name == TemplateName, cancellationToken);
                if (template is { Versions: [{ Status: TemplateVersionStatus.Published } published] })
                {
                    // Version 2 as a draft: one more question and two more days for Interview.
                    var workflow = published.Definition.Workflow;
                    var draft = template.CreateDraft(
                        published.Definition with
                        {
                            Workflow = workflow with
                            {
                                Stages = [.. workflow.Stages.Select(s => s.Id == "interview" ? s with { SlaDays = (s.SlaDays ?? 3) + 2 } : s)],
                                Questionnaire = new Questionnaire([.. workflow.Questionnaire.Questions, NoticePeriodQuestion]),
                            },
                        },
                        step.Now, step.Services.GetRequiredService<TenantContext>().UserId);
                    db.HiringTemplateVersions.Add(draft);
                    await db.SaveChangesAsync(cancellationToken);

                    // A second save a little later: the new question becomes required.
                    var revised = draft.Definition.Workflow;
                    draft.Revise(draft.Definition with
                    {
                        Workflow = revised with
                        {
                            Questionnaire = new Questionnaire([.. revised.Questionnaire.Questions.Select(q => q.Id == NoticePeriodQuestion.Id ? q with { Required = true } : q)]),
                        },
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is { } step)
            {
                // An email of Brightwater's own, then a second edit of its subject. The built-ins keep ScreenHire's
                // wording, so every email the demo sends reads as the seeded tests expect.
                var db = step.Outbox.DbContext;
                if (!await db.EmailTemplates.AnyAsync(t => t.Name == InterviewEmailName, cancellationToken))
                {
                    var template = EmailTemplate.CreateCustom(
                        step.OrganizationId, InterviewEmailName, "Interview for {{ jobTitle }}",
                        "Hi {{ firstName }},\n\nThank you for applying for {{ jobTitle }}. We would like to invite you to an interview. " +
                        "Someone from our team will contact you within two working days to find a time that suits you.\n\n{{ organizationName }}",
                        step.Now);
                    db.EmailTemplates.Add(template);
                    await db.SaveChangesAsync(cancellationToken);

                    template.Subject = "Interview for {{ jobTitle }} at {{ organizationName }}";
                    template.UpdatedAt = time.GetUtcNow();
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoRecruiterEmail) is { } step)
            {
                var db = step.Outbox.DbContext;
                var job = await db.Jobs.SingleOrDefaultAsync(j => j.Slug == DraftJobSlug && j.Status == JobStatus.Draft, cancellationToken);
                if (job is { SalaryMin: 70_000m })
                {
                    job.Revise(job.Snapshot() with { Salary = new JobSalary(75_000, 95_000, "GBP", SalaryPeriod.Year) }, step.Now);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }

    /// <summary>The custom role the demo makes, and what it adds to Viewer.</summary>
    public const string InterviewerRoleName = "Interviewer";

    public static IReadOnlyList<Permission> InterviewerPermissions { get; } =
        [Permission.ViewHiring, Permission.AddNotesAndTags, Permission.FillInScorecards];

    // Made once, as Avery would on the Roles page: the role, then Robin moved to it from Viewer. Both land in the
    // membership log; the role's change history comes from the tracked save. A Robin someone has given another role
    // keeps it.
    private async Task SeedInterviewerAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { Owner: { } owner } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var normalized = CustomRole.Normalize(InterviewerRoleName);
        if (await db.CustomRoles.AnyAsync(r => r.NormalizedName == normalized, cancellationToken))
        {
            return;
        }

        var role = CustomRole.Copy(step.OrganizationId, InterviewerRoleName, nameof(OrganizationRole.Viewer), InterviewerPermissions, step.Now);
        db.CustomRoles.Add(role);
        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = step.OrganizationId,
            Kind = MembershipEventKind.RoleCreated,
            ActorUserId = owner,
            Role = OrganizationRole.Custom,
            CustomRoleId = role.Id,
            CustomRoleName = role.Name,
            OccurredAt = step.Now,
        });

        var robin = await UserIdAsync(db, DevelopmentDataSeeder.DemoInterviewerEmail, cancellationToken);
        var member = await db.OrganizationMembers.SingleOrDefaultAsync(
            m => m.OrganizationId == step.OrganizationId && m.UserId == robin && m.Role == OrganizationRole.Viewer, cancellationToken);
        if (member is not null)
        {
            db.MembershipEvents.Add(new MembershipEvent
            {
                OrganizationId = step.OrganizationId,
                Kind = MembershipEventKind.RoleChanged,
                ActorUserId = owner,
                MemberUserId = member.UserId,
                PreviousRole = OrganizationRole.Viewer,
                Role = OrganizationRole.Custom,
                CustomRoleId = role.Id,
                CustomRoleName = role.Name,
                OccurredAt = step.Now,
            });
            member.AssignRole(OrganizationRole.Custom, role.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The one job Robin Hale's hiring teams give him: he sees it and nothing else.</summary>
    public const string InterviewerJobSlug = DemoCandidates.SeniorDotnetEngineer;

    // As Avery would set it up: Jordan on every job's hiring team, Robin on the .NET job's, then Robin moved to Only jobs
    // they're on, each change in the membership log. Done once: an organization whose jobs have a team, or whose Robin
    // is on Only jobs they're on already, is left as it is.
    private async Task SeedHiringTeamsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { Owner: { } owner } step)
        {
            return;
        }

        var db = step.Outbox.DbContext;
        var jordan = await UserIdAsync(db, DevelopmentDataSeeder.DemoRecruiterEmail, cancellationToken);
        var robin = await UserIdAsync(db, DevelopmentDataSeeder.DemoInterviewerEmail, cancellationToken);
        var members = await db.OrganizationMembers
            .Where(m => m.OrganizationId == step.OrganizationId && (m.UserId == jordan || m.UserId == robin))
            .ToListAsync(cancellationToken);
        var recruiter = members.SingleOrDefault(m => m.UserId == jordan);
        var interviewer = members.SingleOrDefault(m => m.UserId == robin);
        if (recruiter is null || interviewer is null || interviewer.JobAccess == JobAccess.HiringTeamOnly
            || await db.Jobs.AnyAsync(j => j.HiringTeam.Count > 0, cancellationToken))
        {
            return;
        }

        foreach (var job in await db.Jobs.ToListAsync(cancellationToken))
        {
            var change = job.SetHiringTeam(job.Slug == InterviewerJobSlug ? [recruiter.Id, interviewer.Id] : [recruiter.Id]);
            foreach (var member in members.Where(m => change.Added.Contains(m.Id)))
            {
                db.MembershipEvents.Add(new MembershipEvent
                {
                    OrganizationId = step.OrganizationId,
                    Kind = MembershipEventKind.AddedToHiringTeam,
                    ActorUserId = owner,
                    MemberUserId = member.UserId,
                    JobId = job.Id,
                    OccurredAt = step.Now,
                });
            }
        }

        interviewer.SetJobAccess(JobAccess.HiringTeamOnly);
        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = step.OrganizationId,
            Kind = MembershipEventKind.JobAccessChanged,
            ActorUserId = owner,
            MemberUserId = interviewer.UserId,
            PreviousJobAccess = JobAccess.All,
            JobAccess = JobAccess.HiringTeamOnly,
            OccurredAt = step.Now,
        });
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
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { } step)
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
        if (await BeginAsync(scope, cancellationToken, DevelopmentDataSeeder.DemoOwnerEmail) is not { } step)
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
            step.Outbox, job, new ApplicationSubmission.CandidateDetails(applicant.FirstName, applicant.LastName, applicant.Email, applicant.Phone),
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Development demo: {Scorecards} scorecards given on Brightwater candidates")]
    private static partial void LogScorecards(ILogger logger, int scorecards);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development demo: a step of the demo data could not be seeded")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
