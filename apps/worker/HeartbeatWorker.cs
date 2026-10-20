using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Worker;

/// <summary>
/// Proves the worker can reach the database; logs the number of organizations once a minute. Organizations are the
/// tenant registry, readable without a tenant. Jobs would always count 0 here: the worker has no tenant, and row-level
/// security hides every tenant's rows from it.
/// </summary>
internal sealed partial class HeartbeatWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ScreenHireDbContext>();
            var organizations = await db.Organizations.CountAsync(stoppingToken);
            LogHeartbeat(logger, organizations);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker heartbeat: {Organizations} organizations")]
    private static partial void LogHeartbeat(ILogger logger, int organizations);
}
