using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Options every <see cref="ScreenHireDbContext"/> registration must apply (API, worker, design-time factory, tests):
    /// snake_case names and the row-level security session settings.
    /// </summary>
    public static TBuilder UseScreenHireConventions<TBuilder>(this TBuilder builder)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseSnakeCaseNamingConvention();
        builder.AddInterceptors(TenantSessionInterceptor.Instance);
        return builder;
    }
}
