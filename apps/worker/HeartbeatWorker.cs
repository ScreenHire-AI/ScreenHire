using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Worker;

/// <summary>Proves the worker can reach the database; logs the number of open jobs once a minute.</summary>
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
            var openJobs = await db.Jobs.CountAsync(j => j.Status == JobStatus.Open, stoppingToken);
            LogHeartbeat(logger, openJobs);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker heartbeat: {OpenJobs} open jobs")]
    private static partial void LogHeartbeat(ILogger logger, int openJobs);
}
