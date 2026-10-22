using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context, including the ASP.NET Core Identity user store. Tenant isolation has two layers
/// (ADR-0008): the named query filter <see cref="TenantFilter"/> on every <see cref="ITenantOwned"/> entity, and
/// Postgres row-level security, whose session settings <see cref="TenantSessionInterceptor"/> sets from
/// <see cref="Tenant"/>.
/// </summary>
public sealed class ScreenHireDbContext : IdentityUserContext<ApplicationUser, Guid>
{
    /// <summary>The name of the query filter that scopes tenant-owned rows to <see cref="ITenantContext.OrganizationId"/>.</summary>
    public const string TenantFilter = "Tenant";

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

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobVersion> JobVersions => Set<JobVersion>();

    public DbSet<HiringTemplate> HiringTemplates => Set<HiringTemplate>();

    public DbSet<HiringTemplateVersion> HiringTemplateVersions => Set<HiringTemplateVersion>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<JobApplication> Applications => Set<JobApplication>();

    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();

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

    /// <summary>
    /// The cross-employer Signal Network (ADR-0005). Not tenant-owned; the application role has no access to the table,
    /// and nothing uses it while the network's feature flag is off.
    /// </summary>
    public DbSet<SignalNetworkEntry> SignalNetworkEntries => Set<SignalNetworkEntry>();

    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();

    // Read by the query filters. EF evaluates context members per query, so each query sees the current tenant.
    private Guid? CurrentOrganizationId => Tenant.OrganizationId;

    private Guid? CurrentUserId => Tenant.UserId;

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
    }

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
