using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

public static class DatabaseRegistration
{
    /// <summary>
    /// The connection string for the <c>screenhire_app</c> role: not the table owner and without <c>BYPASSRLS</c>, so
    /// row-level security applies to everything the API and worker do.
    /// </summary>
    public const string AppConnectionName = "screenhire-app";

    /// <summary>
    /// The table owner's connection string. Only migrations, Wolverine's storage setup and the Development seed use it;
    /// outside Development the API and worker never see it.
    /// </summary>
    public const string OwnerConnectionName = "screenhire";

    /// <summary>
    /// The Postgres schema for Wolverine's envelope and queue tables. Migrations grant <c>screenhire_app</c> data access
    /// to it, and <c>MessagingSettings.Schema</c> defaults to it, so the name lives in one place.
    /// </summary>
    public const string MessagingSchema = "wolverine";

    /// <summary>
    /// The Postgres schema for the API's own Wolverine storage: its outbox, the inbox of the real-time queue and its node
    /// records. The API is a separate Wolverine cluster from the worker, so the two never assign agents to each other;
    /// both use the queue tables in <see cref="MessagingSchema"/> (docs/architecture/realtime.md).
    /// </summary>
    public const string ApiMessagingSchema = "wolverine_api";

    /// <summary>
    /// Registers <see cref="ScreenHireDbContext"/> on the application role with a scoped <see cref="TenantContext"/>,
    /// plus Aspire's health check and telemetry. The API and the worker both call this.
    /// </summary>
    /// <remarks>
    /// Not Aspire's <c>AddNpgsqlDbContext</c>: that registers a pooled context, and a pooled context cannot take the
    /// scoped tenant context in its constructor. <c>EnrichNpgsqlDbContext</c> adds the same Aspire behaviour to a plain
    /// registration. Without EF's retrying execution strategy (<c>DisableRetry</c>): it refuses the explicit transaction
    /// that Wolverine's outbox and handler middleware open, and Wolverine's error policy retries messages instead.
    /// </remarks>
    public static TBuilder AddScreenHireDatabase<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<TenantContext>());
        // Change history (ChangeHistoryInterceptor) needs ScreenHire's wording of the built-in email templates.
        builder.Services.TryAddSingleton<EmailTemplates>();
        builder.Services.TryAddSingleton<ChangeHistoryInterceptor>();
        // Read when the first context is built, not at registration: the build-time OpenAPI document generator starts
        // the API with no connection strings at all.
        builder.Services.AddDbContext<ScreenHireDbContext>((services, options) => options
            .UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString(AppConnectionName)
                ?? throw new InvalidOperationException(
                    $"ConnectionStrings:{AppConnectionName} is not set. The AppHost provides it; outside Aspire, set it to a " +
                    "connection string for the screenhire_app role."))
            .UseScreenHireConventions()
            .AddInterceptors(services.GetRequiredService<ChangeHistoryInterceptor>()));
        builder.EnrichNpgsqlDbContext<ScreenHireDbContext>(settings => settings.DisableRetry = true);
        return builder;
    }
}
