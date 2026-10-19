// Local orchestration for the whole stack. Prerequisites: Docker running, `pnpm install` at the repo root
// (JS apps share one pnpm workspace, so Aspire must not run per-app installs — hence WithPnpm(install: false)).
//
// Settings (env `ScreenHire__…` or command-line `--ScreenHire:…`):
//   PersistData (default true)  keep Postgres/Azurite data in volumes. Volume names come from Aspire and include a
//                               hash of this AppHost's path, so each git worktree gets its own data.
//   PinPorts    (default true)  stable local URLs; set false (as the integration tests do) for random ports, or
//                               override individual ports with Ports:Api / Ports:Marketing / Ports:Careers / Ports:Dashboard.
// Avoid :5173 — Kangentic's own dev server uses it on this machine.
var builder = DistributedApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection("ScreenHire");
var persistData = !bool.TryParse(settings["PersistData"], out var persist) || persist;
var pinPorts = !bool.TryParse(settings["PinPorts"], out var pin) || pin;
int? PortFor(string name, int fallback) =>
    !pinPorts ? null : int.TryParse(settings[$"Ports:{name}"], out var port) ? port : fallback;

// pgvector image: near-duplicate answer detection needs the `vector` extension (ADR-0008).
var postgres = builder.AddPostgres("postgres").WithImage("pgvector/pgvector", "pg17");
if (persistData)
{
    postgres.WithDataVolume();
}

var database = postgres.AddDatabase("screenhire");

// Azurite locally; Azure Blob Storage in production (ADR-0007).
var storage = builder.AddAzureStorage("storage").RunAsEmulator(emulator =>
{
    if (persistData)
    {
        emulator.WithDataVolume();
    }
});
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.ScreenHire_Api>("api")
    .WithEndpoint("http", endpoint => endpoint.Port = PortFor("Api", 5080))
    .WithReference(database).WaitFor(database)
    .WithReference(blobs).WaitFor(blobs);

builder.AddProject<Projects.ScreenHire_Worker>("worker")
    .WithReference(database)
    .WaitFor(api); // the API applies migrations in Development

var apiUrl = api.GetEndpoint("http");

builder.AddJavaScriptApp("careers", "../careers")
    .WithPnpm(install: false)
    .WithHttpEndpoint(port: PortFor("Careers", 4322), env: "PORT")
    .WithEnvironment("API_URL", apiUrl)
    .WaitFor(api);

builder.AddJavaScriptApp("marketing", "../marketing")
    .WithPnpm(install: false)
    .WithHttpEndpoint(port: PortFor("Marketing", 4321), env: "PORT");

builder.AddViteApp("dashboard", "../dashboard")
    .WithPnpm(install: false)
    .WithEndpoint("http", endpoint => endpoint.Port = PortFor("Dashboard", 4323))
    .WithEnvironment("API_URL", apiUrl)
    .WaitFor(api);

builder.Build().Run();
