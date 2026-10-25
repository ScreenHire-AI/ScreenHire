using System.Text.Json.Serialization;
using FluentValidation;
using Scalar.AspNetCore;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Demo;
using ScreenHire.Api.Endpoints;
using ScreenHire.Api.Hiring;
using ScreenHire.Api.Mcp;
using ScreenHire.Api.Realtime;
using ScreenHire.Application.Automations;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.AddScreenHireDataProtection();
builder.AddAzureBlobServiceClient("blobs");
builder.AddScreenHireAuthentication();
builder.AddScreenHireApiKeys();
builder.AddScreenHireMcp();
builder.AddScreenHireApply();

// Wolverine: endpoints write through IDbContextOutbox<ScreenHireDbContext>, so a message commits with the data and the
// worker handles it (docs/architecture/messaging.md). The API handles only tenant events from the real-time queue,
// which it pushes to connected dashboards over SignalR (docs/architecture/realtime.md).
builder.AddScreenHireApiMessaging(TenantEventPusher.For);
builder.AddScreenHireRealtime();

builder.Services.AddSingleton<TimeProvider>(DatabasePrecisionTimeProvider.Instance);
// Email templates render here for previews and test sends; every send to a candidate renders in the worker.
builder.Services.AddSingleton<EmailTemplates>();
builder.Services.AddSingleton<EmailTemplateStore>();
builder.Services.AddSingleton<IValidator<CreateEmailTemplateRequest>, CreateEmailTemplateRequestValidator>();
builder.Services.AddSingleton<IValidator<UpdateEmailTemplateRequest>, UpdateEmailTemplateRequestValidator>();
builder.Services.AddSingleton<IValidator<EmailTemplateDraft>, EmailTemplateDraftValidator>();
builder.Services.AddSingleton<IValidator<ApplicationEmailDraft>, ApplicationEmailDraftValidator>();
builder.Services.AddSingleton<IValidator<SendEmailsRequest>, SendEmailsRequestValidator>();
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("ScreenHire:Demo:Enabled"))
{
    // The local demo's data: Brightwater's template, jobs on every board, a closed job, the synthetic applicants and
    // candidates in every stage, once the API has started (docs/operations/local-demo.md). The AppHost turns it on for
    // `pnpm dev` only. It is also a health check, so /health is healthy only once the demo data is in.
    builder.Services.AddSingleton<DevelopmentDemo>();
    builder.Services.AddHostedService(services => services.GetRequiredService<DevelopmentDemo>());
    builder.Services.AddHealthChecks().AddCheck<DevelopmentDemo>("development-demo");
}

builder.Services.AddProblemDetails();
// A body the framework could not bind (malformed JSON, an unknown member where a contract refuses them, over a size
// limit) is the caller's mistake: answer with its own status. Minimal APIs throw these in Development rather than answer.
builder.Services.Configure<ExceptionHandlerOptions>(options => options.StatusCodeSelector = exception =>
    exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError);
builder.Services.AddOpenApi(options => options.AddSchemaTransformer(DefinitionSchemas.LinkFromOpenApi).AddScreenHireSecuritySchemes());
builder.Services.AddHiringValidation();
builder.AddJobDistribution();
// Webhook subscriptions: the API shows each endpoint's secret once and checks URLs against the same address rule the
// worker applies (docs/architecture/workflow-engine.md section 5).
builder.Services.AddOptions<WebhookOptions>().BindConfiguration(WebhookOptions.Section);
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
app.UseAuthorization();
// After authorization: on the hiring API it authenticates an API key in place of the cookie (ApiKeyAuthentication), so
// only then is the request's user final.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapOpenApi().AllowAnonymous();
// The API reference (Scalar, /scalar) is public in every environment: it documents the public API and holds no data. The
// dashboard's API keys and Webhooks pages link to it. Being public, it calls no third party: Scalar's defaults would send
// usage telemetry, load fonts from Scalar's CDN and offer Scalar's hosted AI chat and MCP server on our API.
app.MapScalarApiReference(options => options.DisableTelemetry().DisableDefaultFonts().DisableAgent().DisableMcp()).AllowAnonymous();
app.MapMetaEndpoints();
app.MapAuthEndpoints();
app.MapPublicJobEndpoints();
app.MapPublicApplicationEndpoints();
app.MapPublicQuestionnaireEndpoints();
app.MapPublicTelemetryEndpoints();
app.MapJobEndpoints();
app.MapTemplateEndpoints();
app.MapApplicationEndpoints();
app.MapIntegrityEndpoints();
app.MapDistributionEndpoints();
app.MapPublicDistributionEndpoints();
app.MapOrganizationEndpoints();
app.MapTeamEndpoints();
app.MapHiringTeamEndpoints();
app.MapRoleEndpoints();
app.MapApiKeyEndpoints();
app.MapWorkflowEndpoints();
app.MapAutomationEndpoints();
app.MapTaskEndpoints();
app.MapScorecardEndpoints();
app.MapScorecardTemplateEndpoints();
app.MapTemplateUsageEndpoints();
app.MapWebhookEndpoints();
app.MapEmailEndpoints();
app.MapSchemaEndpoints();
app.MapRealtimeEndpoints();
app.MapScreenHireMcp();

if (app.Environment.IsDevelopment())
{
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
