using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ScreenHireDbContext>("screenhire", configureDbContextOptions: options => options.UseScreenHireConventions());

// Wolverine (durable jobs + transactional outbox, ADR-0008) replaces this heartbeat in the messaging work item.
builder.Services.AddHostedService<HeartbeatWorker>();

await builder.Build().RunAsync();
