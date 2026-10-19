using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context. Tenant isolation (named query filters + Postgres row-level security,
/// ADR-0008) is added by the multi-tenancy work item; until then queries must filter by organization explicitly.
/// </summary>
public sealed class ScreenHireDbContext(DbContextOptions<ScreenHireDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Job> Jobs => Set<Job>();

    // No OnConfiguring: Aspire registers this context with pooling, which forbids it.
    // Shared options live in UseScreenHireConventions().
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScreenHireDbContext).Assembly);
}
