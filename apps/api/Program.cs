using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// No EF retrying execution strategy: it refuses the explicit transaction that Wolverine's outbox and handler
// middleware open. Failed messages are retried by Wolverine's error policy instead (ScreenHireMessaging).
builder.AddNpgsqlDbContext<ScreenHireDbContext>(
    ScreenHireMessaging.ConnectionName,
    settings => settings.DisableRetry = true,
    options => options.UseScreenHireConventions());
builder.AddAzureBlobServiceClient("blobs");

// Wolverine publishing only: endpoints write through IDbContextOutbox<ScreenHireDbContext>, so a message commits with
// the data and the worker handles it (docs/architecture/messaging.md).
builder.AddScreenHireMessagePublishing();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Strict numbers keep OpenAPI integer/number types clean for the generated TypeScript client.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapMetaEndpoints();
app.MapPublicJobEndpoints();
app.MapJobEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ScreenHireDbContext>();
    await DevelopmentDataSeeder.MigrateAndSeedAsync(db, TimeProvider.System);
}

await app.RunAsync();
