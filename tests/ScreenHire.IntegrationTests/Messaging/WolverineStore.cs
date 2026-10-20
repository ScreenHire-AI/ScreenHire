using Npgsql;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// Reads Wolverine's tables to see where a message is: in the outbox, waiting in a Postgres queue, in the inbox
/// (handled or not) or dead-lettered. Messages are found by a value in their JSON body, such as a job id, so a test
/// only ever sees its own messages.
/// </summary>
internal sealed class WolverineStore(string connectionString, string schema = "wolverine")
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>The worker's store and the shared queues (schema <c>wolverine</c>).</summary>
    public static async Task<WolverineStore> ForAppHostAsync(AppHostFixture fixture, CancellationToken cancellationToken) =>
        new(await fixture.GetOwnerConnectionStringAsync(cancellationToken));

    /// <summary>The API's own store (schema <c>wolverine_api</c>): its outbox and the real-time queue's inbox.</summary>
    public static async Task<WolverineStore> ForAppHostApiAsync(AppHostFixture fixture, CancellationToken cancellationToken) =>
        new(await fixture.GetOwnerConnectionStringAsync(cancellationToken), DatabaseRegistration.ApiMessagingSchema);

    public Task<long> CountOutgoingAsync(string bodyContains, CancellationToken cancellationToken) =>
        CountAsync("wolverine_outgoing_envelopes", bodyContains, cancellationToken);

    public Task<long> CountQueuedAsync(string queue, string bodyContains, CancellationToken cancellationToken) =>
        CountAsync($"wolverine_queue_{queue}", bodyContains, cancellationToken);

    public Task<long> CountIncomingAsync(string bodyContains, CancellationToken cancellationToken) =>
        CountAsync("wolverine_incoming_envelopes", bodyContains, cancellationToken);

    public Task<long> CountHandledAsync(string bodyContains, CancellationToken cancellationToken) =>
        CountAsync("wolverine_incoming_envelopes", bodyContains, cancellationToken, "status = 'Handled'");

    /// <summary>Handled messages of one type. Only meaningful in a private schema, where every message is the test's own.</summary>
    public async Task<long> CountHandledOfTypeAsync(string messageTypeContains, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"select count(*) from {schema}.wolverine_incoming_envelopes where status = 'Handled' and position(@type in message_type) > 0",
            connection);
        command.Parameters.AddWithValue("type", messageTypeContains);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<DeadLetter>> GetDeadLettersAsync(string bodyContains, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"select message_type, exception_type, exception_message from {schema}.wolverine_dead_letters where {BodyMatches}",
            connection);
        command.Parameters.AddWithValue("needle", bodyContains);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var deadLetters = new List<DeadLetter>();
        while (await reader.ReadAsync(cancellationToken))
        {
            deadLetters.Add(new DeadLetter(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return deadLetters;
    }

    /// <summary>Polls <paramref name="probe"/> until it holds, failing with <paramref name="because"/> after the deadline.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> probe, string because, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        try
        {
            while (!await probe())
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Waited {Deadline.TotalSeconds:0} seconds for {because}.");
        }
    }

    public async Task DropSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"drop schema if exists {schema} cascade", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Bodies are bytea: the message's JSON, or (in queue tables) a binary envelope that contains it.
    private const string BodyMatches = "position(convert_to(@needle, 'UTF8') in body) > 0";

    private async Task<long> CountAsync(string table, string bodyContains, CancellationToken cancellationToken, string? filter = null)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"select count(*) from {schema}.{table} where {BodyMatches}{(filter is null ? "" : $" and {filter}")}",
            connection);
        command.Parameters.AddWithValue("needle", bodyContains);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

internal sealed record DeadLetter(string MessageType, string ExceptionType, string ExceptionMessage);
