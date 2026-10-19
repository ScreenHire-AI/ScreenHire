// Local orchestration for the whole stack. Prerequisites: Docker running, `pnpm install` at the repo root
// (JS apps share one pnpm workspace, so Aspire must not run per-app installs — hence WithPnpm(install: false)).
var builder = DistributedApplication.CreateBuilder(args);

// pgvector image: near-duplicate answer detection needs the `vector` extension (ADR-0008).
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume("screenhire-postgres-data");
var database = postgres.AddDatabase("screenhire");

// Azurite locally; Azure Blob Storage in production (ADR-0007).
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume("screenhire-azurite-data"));
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.ScreenHire_Api>("api")
    .WithReference(database).WaitFor(database)
    .WithReference(blobs).WaitFor(blobs);

builder.AddProject<Projects.ScreenHire_Worker>("worker")
    .WithReference(database)
    .WaitFor(api); // the API applies migrations in Development

var apiUrl = api.GetEndpoint("http");

// Fixed proxy ports give stable local URLs: marketing :4321, careers :4322, dashboard :4323 (API :5080).
// Avoid 5173: Kangentic's own dev server uses it on this machine.
builder.AddJavaScriptApp("careers", "../careers")
    .WithPnpm(install: false)
    .WithHttpEndpoint(port: 4322, env: "PORT")
    .WithEnvironment("API_URL", apiUrl)
    .WaitFor(api);

builder.AddJavaScriptApp("marketing", "../marketing")
    .WithPnpm(install: false)
    .WithHttpEndpoint(port: 4321, env: "PORT");

builder.AddViteApp("dashboard", "../dashboard")
    .WithPnpm(install: false)
    .WithEndpoint("http", endpoint => endpoint.Port = 4323)
    .WithEnvironment("API_URL", apiUrl)
    .WaitFor(api);

builder.Build().Run();
