using ScreenHire.Application.Distribution;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.Services.AddSingleton<TimeProvider>(DatabasePrecisionTimeProvider.Instance);
builder.AddJobDistribution();

// Wolverine: the worker queue's durable inbox, every handler in ScreenHire.Application, the durability agent and the
// cron schedules (ADR-0008, docs/architecture/messaging.md).
builder.AddScreenHireMessageHandling();

await builder.Build().RunAsync();
