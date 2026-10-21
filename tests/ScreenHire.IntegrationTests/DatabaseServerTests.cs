using Npgsql;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The AppHost's Postgres has room for every connection pool in the stack. At the default of 100 connections, a burst of
/// worker handlers refused clients ("too many clients already") and a workflow run timed out.
/// </summary>
public sealed class DatabaseServerTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Postgres_allows_300_connections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SHOW max_connections", connection);

        var maxConnections = (string?)await command.ExecuteScalarAsync(cancellationToken);

        maxConnections.Should().Be("300");
    }
}
