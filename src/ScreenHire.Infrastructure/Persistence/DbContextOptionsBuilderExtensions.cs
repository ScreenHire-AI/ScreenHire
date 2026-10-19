using Microsoft.EntityFrameworkCore;

namespace ScreenHire.Infrastructure.Persistence;

public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Options every <see cref="ScreenHireDbContext"/> registration must apply (API, worker, design-time factory).
    /// </summary>
    public static TBuilder UseScreenHireConventions<TBuilder>(this TBuilder builder)
        where TBuilder : DbContextOptionsBuilder
    {
        builder.UseSnakeCaseNamingConvention();
        return builder;
    }
}
