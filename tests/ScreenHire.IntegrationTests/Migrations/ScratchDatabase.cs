using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// A database of its own on the test stack's Postgres, migrated to a chosen migration, so a test can write the rows an
/// older release left and run the migration after it. Dropped on dispose.
/// </summary>
internal sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly string _ownerConnectionString;
    private readonly string _name;

    private ScratchDatabase(string ownerConnectionString, string name, string connectionString)
    {
        _ownerConnectionString = ownerConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The scratch database, as the stack's owner (a superuser, which row-level security does not bind).</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// The scratch database as <paramref name="role"/>: every statement runs with that role as the current user (the
    /// <c>role</c> setting at connection start, which is what SET ROLE does), so a role that is not a superuser is bound
    /// by row-level security even though the login is the stack's owner.
    /// </summary>
    public string ConnectionStringAs(string role) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Options = $"-c role={role}" }.ConnectionString;

    public static async Task<ScratchDatabase> CreateAsync(AppHostFixture fixture, CancellationToken ct)
    {
        var owner = await fixture.GetOwnerConnectionStringAsync(ct);
        var name = $"scratch_{Guid.NewGuid():N}"[..24];
        await ExecuteAsync(owner, $"CREATE DATABASE {name}", ct);
        var connectionString = new NpgsqlConnectionStringBuilder(owner) { Database = name, Pooling = false }.ConnectionString;
        return new ScratchDatabase(owner, name, connectionString);
    }

    /// <summary>Migrates up or down to <paramref name="migration"/> (a migration's name, without its timestamp).</summary>
    public Task MigrateAsync(string migration, CancellationToken ct) => MigrateAsync(migration, ConnectionString, ct);

    /// <summary>Migrates as the connection <paramref name="connectionString"/> describes, such as <see cref="ConnectionStringAs"/>.</summary>
    public async Task MigrateAsync(string migration, string connectionString, CancellationToken ct)
    {
        await using var db = Context(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migration, ct);
    }

    public Task ExecuteAsync(string sql, CancellationToken ct) => ExecuteAsync(ConnectionString, sql, ct);

    public Task<T> ScalarAsync<T>(string sql, CancellationToken ct) => ScalarAsync<T>(ConnectionString, sql, ct);

    /// <summary>The names of every table with an organization_id that does not have row-level security forced.</summary>
    public async Task<List<string>> UnforcedTablesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organization_id' AND NOT a.attisdropped)
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(_ownerConnectionString, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)", CancellationToken.None);
    }

    private static ScreenHireDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(connectionString).UseScreenHireConventions().Options, new TenantContext());

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
