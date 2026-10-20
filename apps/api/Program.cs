using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.AddAzureBlobServiceClient("blobs");
builder.AddScreenHireAuthentication();

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

app.UseForwardedHeaders(); // first: the rate limiter partitions on the client address it restores
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapOpenApi().AllowAnonymous();
app.MapMetaEndpoints();
app.MapAuthEndpoints();
app.MapPublicJobEndpoints();
app.MapJobEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference().AllowAnonymous();

    // As the owner, before Kestrel listens: the schema, the app role's login, Wolverine's tables, then the demo data.
    // The worker waits for a healthy API, so it starts with all of it in place. Outside Development the deploy step runs
    // the same two steps (docs/architecture/sign-in-and-tenancy.md, "Deploy prerequisites").
    var ownerConnectionString = builder.Configuration.GetConnectionString(DatabaseRegistration.OwnerConnectionName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{DatabaseRegistration.OwnerConnectionName} is not set.");
    await DevelopmentDataSeeder.MigrateAndSeedAsync(
        ownerConnectionString, builder.Configuration.GetConnectionString(DatabaseRegistration.AppConnectionName)!, TimeProvider.System);
    await ScreenHireMessaging.ProvisionStorageAsync(ownerConnectionString);
}

await app.RunAsync();
