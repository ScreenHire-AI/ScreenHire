using Npgsql;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Testing;

/// <summary>
/// The worker's durable scheduled messages: Wolverine's incoming envelopes waiting with status <c>Scheduled</c>. A timer
/// that waits days, such as a stage's time limit, is released early by moving its due time to now. The worker then
/// delivers the message it scheduled, unchanged, as it would at the due time; nothing sleeps.
/// </summary>
public static class ScheduledMessages
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Waits until a scheduled message whose body contains <paramref name="bodyContains"/> (an id) exists, then makes
    /// every such message due now. Returns how many it released.
    /// </summary>
    public static async Task<int> ReleaseAsync(this AppHostFixture fixture, string bodyContains, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        try
        {
            while (true)
            {
                await using var command = new NpgsqlCommand(
                    $"update {DatabaseRegistration.MessagingSchema}.wolverine_incoming_envelopes set execution_time = now() " +
                    "where status = 'Scheduled' and position(convert_to(@needle, 'UTF8') in body) > 0",
                    connection);
                command.Parameters.AddWithValue("needle", bodyContains);
                var released = await command.ExecuteNonQueryAsync(cancellationToken);
                if (released > 0)
                {
                    return released;
                }

                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Waited {Deadline.TotalSeconds:0} seconds for a scheduled message containing {bodyContains}.");
        }
    }
}
