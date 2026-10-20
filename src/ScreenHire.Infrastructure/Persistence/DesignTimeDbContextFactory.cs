using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without the Aspire AppHost.
/// The connection string is never used to connect when generating migrations.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ScreenHireDbContext>
{
    public ScreenHireDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ScreenHireDbContext>()
            .UseNpgsql("Host=localhost;Database=screenhire")
            .UseScreenHireConventions()
            .Options;

        return new ScreenHireDbContext(options, new TenantContext());
    }
}
