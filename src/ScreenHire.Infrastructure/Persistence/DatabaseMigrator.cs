using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// The schema steps that run as the table owner: the EF migrations, then the application role's login. The API and the
/// worker never do these; they connect as <c>screenhire_app</c>, which is bound by row-level security. The development
/// seeder runs them at every local start, and the migrator (apps/migrator) runs them for a deployment.
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>Migrates as the owner, then lets <c>screenhire_app</c> log in with <paramref name="appRolePassword"/>. Idempotent.</summary>
    public static async Task MigrateAsync(string ownerConnectionString, string appRolePassword, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<ScreenHireDbContext>()
            .UseNpgsql(ownerConnectionString)
            .UseScreenHireConventions()
            .Options;
        await using (var db = new ScreenHireDbContext(options, new TenantContext()))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await GrantAppRoleLoginAsync(ownerConnectionString, appRolePassword, cancellationToken);
    }

    /// <summary>The password in an application connection string, which the role is given.</summary>
    public static string AppRolePasswordFrom(string appConnectionString) =>
        new NpgsqlConnectionStringBuilder(appConnectionString).Password
            ?? throw new InvalidOperationException($"ConnectionStrings:{DatabaseRegistration.AppConnectionName} has no password.");

    // A raw connection, not EF: EF logs command text, and this command carries the password. ALTER ROLE is a utility
    // statement and cannot take parameters, so the literal is quoted here.
    private static async Task GrantAppRoleLoginAsync(string ownerConnectionString, string password, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER ROLE {TenantSecurity.AppRole} WITH LOGIN PASSWORD '{password.Replace("'", "''", StringComparison.Ordinal)}'";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
