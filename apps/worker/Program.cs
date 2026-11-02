using ScreenHire.Application.Distribution;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.AddScreenHireDataProtection();
builder.Services.AddSingleton<TimeProvider>(DatabasePrecisionTimeProvider.Instance);
builder.AddJobDistribution();

// Transactional email from automations and people's sends: SendGrid when its API key is set (ADR-0022), Mailpit locally.
// It needs a From address (ScreenHire:Email:From) and either the key or an SMTP server from the AppHost; without them no
// sender is registered and automations skip their emails instead of failing. The reply settings are read in every case.
if (builder.Configuration["ScreenHire:Email:From"] is { Length: > 0 } from)
{
    builder.AddScreenHireEmail(from);
}

// Content forensics read candidates' uploads from the applicant uploads container (Azurite locally). Registered before
// the messaging setup, whose local adapter (no storage) then stays out of the way.
builder.AddAzureBlobServiceClient("blobs");
builder.Services.AddSingleton<IApplicantFileStore, BlobApplicantFileStore>();

// Wolverine: the worker queue's durable inbox, every handler in ScreenHire.Application, the durability agent and the
// cron schedules (ADR-0008, docs/architecture/messaging.md).
builder.AddScreenHireMessageHandling();

await builder.Build().RunAsync();
