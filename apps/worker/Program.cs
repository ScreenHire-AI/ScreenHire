using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
// No EF retrying execution strategy: it refuses the explicit transaction that Wolverine's outbox and handler
// middleware open. Failed messages are retried by Wolverine's error policy instead (ScreenHireMessaging).
builder.AddNpgsqlDbContext<ScreenHireDbContext>(
    ScreenHireMessaging.ConnectionName,
    settings => settings.DisableRetry = true,
    options => options.UseScreenHireConventions());

// Wolverine: the worker queue's durable inbox, every handler in ScreenHire.Application, the durability agent and the
// cron schedules (ADR-0008, docs/architecture/messaging.md).
builder.AddScreenHireMessageHandling();

await builder.Build().RunAsync();
