using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using ScreenHire.Api.Endpoints;
using ScreenHire.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ScreenHireDbContext>("screenhire", configureDbContextOptions: options => options.UseScreenHireConventions());
builder.AddAzureBlobServiceClient("blobs");

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
