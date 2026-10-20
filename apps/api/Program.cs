using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Demo;
using ScreenHire.Api.Endpoints;
using ScreenHire.Api.Hiring;
using ScreenHire.Api.Realtime;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.AddAzureBlobServiceClient("blobs");
builder.AddScreenHireAuthentication();
builder.AddScreenHireApply();

// Wolverine: endpoints write through IDbContextOutbox<ScreenHireDbContext>, so a message commits with the data and the
// worker handles it (docs/architecture/messaging.md). The API handles only tenant events from the real-time queue,
// which it pushes to connected dashboards over SignalR (docs/architecture/realtime.md).
builder.AddScreenHireApiMessaging(TenantEventPusher.For);
builder.AddScreenHireRealtime();

builder.Services.AddSingleton<TimeProvider>(DatabasePrecisionTimeProvider.Instance);
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("ScreenHire:Demo:Enabled"))
{
    // The local demo's data: Brightwater's template, a draft job, the synthetic applicants and candidates in every
    // stage, once the API has started (docs/operations/local-demo.md). The AppHost turns it on for `pnpm dev` only.
    builder.Services.AddHostedService<DevelopmentDemo>();
}

builder.Services.AddProblemDetails();
// A body the framework could not bind (malformed JSON, an unknown member where a contract refuses them, over a size
// limit) is the caller's mistake: answer with its own status. Minimal APIs throw these in Development rather than answer.
builder.Services.Configure<ExceptionHandlerOptions>(options => options.StatusCodeSelector = exception =>
    exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError);
builder.Services.AddOpenApi(options => options.AddSchemaTransformer(DefinitionSchemas.LinkFromOpenApi));
builder.Services.AddHiringValidation();
builder.AddJobDistribution();
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
app.MapPublicApplicationEndpoints();
app.MapPublicTelemetryEndpoints();
app.MapJobEndpoints();
app.MapTemplateEndpoints();
app.MapApplicationEndpoints();
app.MapIntegrityEndpoints();
app.MapDistributionEndpoints();
app.MapPublicDistributionEndpoints();
app.MapOrganizationEndpoints();
app.MapWorkflowEndpoints();
app.MapAutomationEndpoints();
app.MapSchemaEndpoints();
app.MapRealtimeEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference().AllowAnonymous();

    // As the owner, before Kestrel listens: the schema, the app role's login, Wolverine's tables, then the demo data.
    // The worker waits for a healthy API, so it starts with all of it in place. Outside Development the deploy step runs
    // the same two steps (docs/architecture/sign-in-and-tenancy.md, "Deploy prerequisites").
    var ownerConnectionString = builder.Configuration.GetConnectionString(DatabaseRegistration.OwnerConnectionName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{DatabaseRegistration.OwnerConnectionName} is not set.");
    await DevelopmentDataSeeder.MigrateAndSeedAsync(
        ownerConnectionString, builder.Configuration.GetConnectionString(DatabaseRegistration.AppConnectionName)!, DatabasePrecisionTimeProvider.Instance);
    await ScreenHireMessaging.ProvisionStorageAsync(ownerConnectionString);
    await app.Services.GetRequiredService<ApplicantUploads>().ProvisionDevelopmentStorageAsync();
}

await app.RunAsync();
