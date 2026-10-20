using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

public static class DatabaseRegistration
{
    /// <summary>
    /// The connection string for the <c>screenhire_app</c> role: not the table owner and without <c>BYPASSRLS</c>, so
    /// row-level security applies to everything the API and worker do.
    /// </summary>
    public const string AppConnectionName = "screenhire-app";

    /// <summary>The table owner's connection string. Only migrations (and the Development seed) use it.</summary>
    public const string OwnerConnectionName = "screenhire";

    /// <summary>
    /// Registers <see cref="ScreenHireDbContext"/> on the application role with a scoped <see cref="TenantContext"/>,
    /// plus Aspire's health check, telemetry and retries.
    /// </summary>
    /// <remarks>
    /// Not Aspire's <c>AddNpgsqlDbContext</c>: that registers a pooled context, and a pooled context cannot take the
    /// scoped tenant context in its constructor. <c>EnrichNpgsqlDbContext</c> adds the same Aspire behaviour to a plain
    /// registration.
    /// </remarks>
    public static TBuilder AddScreenHireDatabase<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<TenantContext>());
        // Read when the first context is built, not at registration: the build-time OpenAPI document generator starts
        // the API with no connection strings at all.
        builder.Services.AddDbContext<ScreenHireDbContext>((services, options) => options
            .UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString(AppConnectionName)
                ?? throw new InvalidOperationException(
                    $"ConnectionStrings:{AppConnectionName} is not set. The AppHost provides it; outside Aspire, set it to a " +
                    "connection string for the screenhire_app role."))
            .UseScreenHireConventions());
        builder.EnrichNpgsqlDbContext<ScreenHireDbContext>();
        return builder;
    }
}
