using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Slack;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context, including the ASP.NET Core Identity user store. Tenant isolation has two layers
/// (ADR-0008): the named query filter <see cref="TenantFilter"/> on every <see cref="ITenantOwned"/> entity, and
/// Postgres row-level security, whose session settings <see cref="TenantSessionInterceptor"/> sets from
/// <see cref="Tenant"/>. Job access (ADR-0015) narrows the same two layers: the named filter <see cref="JobScopeFilter"/>
/// on jobs and everything under them, and the restrictive <c>job_scope</c> policies of migration <c>AddJobAccess</c>.
/// </summary>
public sealed class ScreenHireDbContext : IdentityUserContext<ApplicationUser, Guid>, IDataProtectionKeyContext
{
    /// <summary>The name of the query filter that scopes tenant-owned rows to <see cref="ITenantContext.OrganizationId"/>.</summary>
    public const string TenantFilter = "Tenant";

    /// <summary>
    /// The name of the query filter that shows a member on Only jobs they're on just the jobs whose hiring team they are
    /// on, and the rows under those jobs (ADR-0015). It is true for everyone who sees all jobs.
    /// </summary>
    public const string JobScopeFilter = "JobScope";

    public ScreenHireDbContext(DbContextOptions<ScreenHireDbContext> options, ITenantContext tenant)
        : base(options)
    {
        Tenant = tenant;
        // Even a one-row SaveChanges runs in a transaction, so TenantSessionInterceptor can set the row-level security
        // settings as a separate command ahead of the batch.
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    public ITenantContext Tenant { get; }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    public DbSet<MembershipEvent> MembershipEvents => Set<MembershipEvent>();

    public DbSet<CustomRole> CustomRoles => Set<CustomRole>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobVersion> JobVersions => Set<JobVersion>();

    public DbSet<HiringTemplate> HiringTemplates => Set<HiringTemplate>();

    public DbSet<HiringTemplateVersion> HiringTemplateVersions => Set<HiringTemplateVersion>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<JobApplication> Applications => Set<JobApplication>();

    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();

    public DbSet<ApplicationScorecard> ApplicationScorecards => Set<ApplicationScorecard>();

    public DbSet<ApplicationNote> ApplicationNotes => Set<ApplicationNote>();

    public DbSet<ApplicationEmail> ApplicationEmails => Set<ApplicationEmail>();

    public DbSet<ApplicationNoteMention> ApplicationNoteMentions => Set<ApplicationNoteMention>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();

    public DbSet<IndexingNotification> IndexingNotifications => Set<IndexingNotification>();

    /// <summary>Indexing API calls used per UTC day, for the whole deployment: no tenant data, no tenant filter.</summary>
    public DbSet<IndexingQuotaDay> IndexingQuotaDays => Set<IndexingQuotaDay>();

    /// <summary>The local sandbox board's own listings (see <see cref="SandboxListing"/>).</summary>
    public DbSet<SandboxListing> SandboxListings => Set<SandboxListing>();

    public DbSet<ApplicationFile> ApplicationFiles => Set<ApplicationFile>();

    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();

    public DbSet<AutomationActionRun> AutomationActionRuns => Set<AutomationActionRun>();

    public DbSet<ApplicationTask> ApplicationTasks => Set<ApplicationTask>();

    public DbSet<IntegrityAnalyzerRun> IntegrityAnalyzerRuns => Set<IntegrityAnalyzerRun>();

    public DbSet<IntegritySignal> IntegritySignals => Set<IntegritySignal>();

    public DbSet<IntegrityAssessment> IntegrityAssessments => Set<IntegrityAssessment>();

    public DbSet<IntegrityReviewAction> IntegrityReviewActions => Set<IntegrityReviewAction>();

    public DbSet<SubmissionObservation> SubmissionObservations => Set<SubmissionObservation>();

    public DbSet<TelemetrySnapshot> TelemetrySnapshots => Set<TelemetrySnapshot>();

    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();

    public DbSet<AnswerFingerprint> AnswerFingerprints => Set<AnswerFingerprint>();

    public DbSet<ConsistencyCheck> ConsistencyChecks => Set<ConsistencyCheck>();

    public DbSet<IntegritySettings> IntegritySettings => Set<IntegritySettings>();

    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    public DbSet<ScorecardTemplate> ScorecardTemplates => Set<ScorecardTemplate>();

    /// <summary>Every change to the configuration people edit (<c>ChangeHistoryInterceptor</c>); append-only.</summary>
    public DbSet<ChangeRecord> ChangeRecords => Set<ChangeRecord>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <summary>An organization's Slack workspace, one at most (docs/architecture/slack.md).</summary>
    public DbSet<SlackInstallation> SlackInstallations => Set<SlackInstallation>();

    public DbSet<SlackAccountLink> SlackAccountLinks => Set<SlackAccountLink>();

    /// <summary>The Slack handlers' effect check: one row per message posted or refused, no text.</summary>
    public DbSet<SlackMessageRecord> SlackMessages => Set<SlackMessageRecord>();

    /// <summary>What the local sandbox workspace received (see <see cref="SlackSandboxMessage"/>).</summary>
    public DbSet<SlackSandboxMessage> SlackSandboxMessages => Set<SlackSandboxMessage>();

    /// <summary>
    /// The cross-employer Signal Network (ADR-0005). Not tenant-owned; the application role has no access to the table,
    /// and nothing uses it while the network's feature flag is off.
    /// </summary>
    public DbSet<SignalNetworkEntry> SignalNetworkEntries => Set<SignalNetworkEntry>();

    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();

    public DbSet<QuestionnaireRequest> QuestionnaireRequests => Set<QuestionnaireRequest>();

    /// <summary>
    /// The ASP.NET Core Data Protection key ring the API and the worker share (<see cref="DataProtectionRegistration"/>).
    /// Not tenant-owned: the keys protect every organization's cookies and links alike.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Read by the query filters. EF evaluates context members per query, so each query sees the current tenant.
    private Guid? CurrentOrganizationId => Tenant.OrganizationId;

    private Guid? CurrentUserId => Tenant.UserId;

    private bool SeesAllJobs => Tenant.SeesAllJobs;

    // Guid.Empty is never a member's id, so an unresolved member matches no hiring team.
    private Guid CurrentMemberId => Tenant.MemberId ?? Guid.Empty;

    // No OnConfiguring: shared options live in UseScreenHireConventions().
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(ScreenHireDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes().Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)))
        {
            builder.Entity(entityType.ClrType).HasQueryFilter(TenantFilter, OwnOrganizationOnly(entityType.ClrType));
        }

        // Mirrors the organization_members row-level security policy: a user also sees their own memberships in other
        // organizations, which is how the API finds a user's organization before it sets the tenant.
        builder.Entity<OrganizationMember>()
            .HasQueryFilter(TenantFilter, m => m.OrganizationId == CurrentOrganizationId || m.UserId == CurrentUserId);

        // Mirror the hiring templates' row-level security read policies: an organization sees its own templates and the
        // system templates, which have no organization. Writes to system rows are refused by the policies.
        builder.Entity<HiringTemplate>()
            .HasQueryFilter(TenantFilter, t => t.OrganizationId == null || t.OrganizationId == CurrentOrganizationId);
        builder.Entity<HiringTemplateVersion>()
            .HasQueryFilter(TenantFilter, v => v.OrganizationId == null || v.OrganizationId == CurrentOrganizationId);

        ApplyJobScope(builder);
    }

    // Mirrors the job_scope row-level security policies (migration AddJobAccess). Each filter names its whole path to the
    // job's hiring team instead of leaning on the parent's own filter, so it reads the same as its policy. Candidates are
    // filtered here only: their table keeps tenant-only row-level security, because one person who applied to two jobs
    // is one candidate row (ADR-0015).
    private void ApplyJobScope(ModelBuilder builder)
    {
        builder.Entity<Job>().HasQueryFilter(JobScopeFilter, j => SeesAllJobs || j.HiringTeam.Contains(CurrentMemberId));

        var jobScoped = typeof(ScreenHireDbContext).GetMethod(nameof(ScopeToJob), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var applicationScoped = typeof(ScreenHireDbContext).GetMethod(nameof(ScopeToApplication), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(IJobScoped).IsAssignableFrom(entityType.ClrType))
            {
                jobScoped.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
            }
            else if (typeof(IApplicationScoped).IsAssignableFrom(entityType.ClrType))
            {
                applicationScoped.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
            }
        }

        builder.Entity<Candidate>().HasQueryFilter(JobScopeFilter, c => SeesAllJobs
            || Applications.Any(a => a.CandidateId == c.Id && Jobs.Any(j => j.Id == a.JobId && j.HiringTeam.Contains(CurrentMemberId))));
        builder.Entity<ApplicationNoteMention>().HasQueryFilter(JobScopeFilter, m => SeesAllJobs
            || ApplicationNotes.Any(n => n.Id == m.NoteId
                && Applications.Any(a => a.Id == n.ApplicationId && Jobs.Any(j => j.Id == a.JobId && j.HiringTeam.Contains(CurrentMemberId)))));
        builder.Entity<AutomationActionRun>().HasQueryFilter(JobScopeFilter, r => SeesAllJobs
            || AutomationRuns.Any(run => run.Id == r.RunId && Jobs.Any(j => j.Id == run.JobId && j.HiringTeam.Contains(CurrentMemberId))));
        builder.Entity<IndexingNotification>().HasQueryFilter(JobScopeFilter, n => SeesAllJobs
            || JobPostings.Any(p => p.Id == n.PostingId && Jobs.Any(j => j.Id == p.JobId && j.HiringTeam.Contains(CurrentMemberId))));
    }

    private void ScopeToJob<T>(ModelBuilder builder)
        where T : class, IJobScoped =>
        builder.Entity<T>().HasQueryFilter(JobScopeFilter, e => SeesAllJobs
            || Jobs.Any(j => j.Id == e.JobId && j.HiringTeam.Contains(CurrentMemberId)));

    private void ScopeToApplication<T>(ModelBuilder builder)
        where T : class, IApplicationScoped =>
        builder.Entity<T>().HasQueryFilter(JobScopeFilter, e => SeesAllJobs
            || Applications.Any(a => a.Id == e.ApplicationId && Jobs.Any(j => j.Id == a.JobId && j.HiringTeam.Contains(CurrentMemberId))));

    // entity => entity.OrganizationId == this.CurrentOrganizationId
    private LambdaExpression OwnOrganizationOnly(Type entityType)
    {
        var entity = Expression.Parameter(entityType, "entity");
        var organizationId = Expression.Convert(
            Expression.Property(entity, nameof(ITenantOwned.OrganizationId)), typeof(Guid?));
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId));
        return Expression.Lambda(Expression.Equal(organizationId, current), entity);
    }
}
