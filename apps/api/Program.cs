using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Endpoints;
using ScreenHire.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddScreenHireDatabase();
builder.AddAzureBlobServiceClient("blobs");
builder.AddScreenHireAuthentication();

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

    await DevelopmentDataSeeder.MigrateAndSeedAsync(
        builder.Configuration.GetConnectionString(DatabaseRegistration.OwnerConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{DatabaseRegistration.OwnerConnectionName} is not set."),
        builder.Configuration.GetConnectionString(DatabaseRegistration.AppConnectionName)!,
        TimeProvider.System);
}

await app.RunAsync();
