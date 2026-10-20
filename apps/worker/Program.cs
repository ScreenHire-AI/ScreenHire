using ScreenHire.Application.Distribution;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.Services.AddSingleton<TimeProvider>(DatabasePrecisionTimeProvider.Instance);
builder.AddJobDistribution();

// Transactional email from automations (Mailpit locally). It needs an SMTP server from the AppHost and a From address
// (ScreenHire:Email:From); without either, no sender is registered and automations skip their emails instead of failing.
if (builder.Configuration["ScreenHire:Email:From"] is { Length: > 0 } from)
{
    builder.AddScreenHireEmail(from);
}

// Wolverine: the worker queue's durable inbox, every handler in ScreenHire.Application, the durability agent and the
// cron schedules (ADR-0008, docs/architecture/messaging.md).
builder.AddScreenHireMessageHandling();

await builder.Build().RunAsync();
