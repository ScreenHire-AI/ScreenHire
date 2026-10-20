// Local orchestration for the whole stack. Prerequisites: Docker running, `pnpm install` at the repo root
// (JS apps share one pnpm workspace, so Aspire must not run per-app installs — hence WithPnpm(install: false)).
//
// Settings (env `ScreenHire__…` or command-line `--ScreenHire:…`):
//   PersistData (default true)  keep Postgres/Azurite data in volumes. Volume names come from Aspire and include a
//                               hash of this AppHost's path, so each git worktree gets its own data.
//   PinPorts    (default true)  stable local URLs; set false (as the integration tests do) for random ports, or
//                               override individual ports with Ports:Api / Ports:Marketing / Ports:Careers / Ports:Dashboard.
//   Frontends   (default Dev)   Dev: dev servers with hot reload. Built: each app's `preview` script over the output of
//                               `pnpm -r build`, i.e. what ships (the end-to-end tests use it). None: no JS apps at all
//                               (the integration tests use it; only the API, worker and infrastructure start).
//   Distribution:*              job distribution settings, forwarded as they are to the API and the worker so both see
//                               the same boards (ScreenHire.Application DistributionOptions). appsettings.json turns
//                               on the local sandbox board; GoogleIndexing:Mode is Off unless set (Local is the fake).
// Avoid :5173 — Kangentic's own dev server uses it on this machine.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection("ScreenHire");
var persistData = !bool.TryParse(settings["PersistData"], out var persist) || persist;
var pinPorts = !bool.TryParse(settings["PinPorts"], out var pin) || pin;
var frontends = settings["Frontends"] is not { } frontendsSetting
    ? FrontendMode.Dev
    : Enum.TryParse<FrontendMode>(frontendsSetting, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
        ? mode
        : throw new InvalidOperationException($"ScreenHire:Frontends must be Dev, Built or None, not '{frontendsSetting}'.");
int? PortFor(string name, int fallback) =>
    !pinPorts ? null : int.TryParse(settings[$"Ports:{name}"], out var port) ? port : fallback;

// pgvector image: near-duplicate answer detection needs the `vector` extension (ADR-0008).
var postgres = builder.AddPostgres("postgres").WithImage("pgvector/pgvector", "pg17");
if (persistData)
{
    postgres.WithDataVolume();
}

// The table owner's connection: the API uses it only to migrate and seed in Development.
var database = postgres.AddDatabase("screenhire");

// The API and worker connect as screenhire_app, a role the tenancy migration creates without table ownership or
// BYPASSRLS, so Postgres row-level security applies to everything they do. The API's Development seed sets this
// password on every start, so a fresh value per run is fine and nothing is persisted.
var appRolePassword = builder.AddParameter(
    "screenhire-app-password", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);
var appDatabase = builder.AddConnectionString("screenhire-app", ReferenceExpression.Create(
    $"Host={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)};Port={postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)};" +
    $"Database={database.Resource.DatabaseName};Username=screenhire_app;Password={appRolePassword}"));

// Azurite locally; Azure Blob Storage in production (ADR-0007).
var storage = builder.AddAzureStorage("storage").RunAsEmulator(emulator =>
{
    if (persistData)
    {
        emulator.WithDataVolume();
    }
});
var blobs = storage.AddBlobs("blobs");

// Mailpit catches every email the stack sends (sign-in links); its web UI and HTTP API show them. Random ports, like
// the other containers, so worktrees never collide; the Aspire dashboard shows the URL.
var mailpit = builder.AddMailPit("mailpit")
    // Mailpit looks up each SMTP client's reverse DNS by default, which takes ~10 s per connection under Docker Desktop.
    .WithEnvironment("MP_SMTP_DISABLE_RDNS", "true");

var api = builder.AddProject<Projects.ScreenHire_Api>("api")
    .WithEndpoint("http", endpoint => endpoint.Port = PortFor("Api", 5080))
    .WaitFor(database)
    .WithReference(appDatabase)
    .WithReference(mailpit).WaitFor(mailpit)
    .WithReference(blobs).WaitFor(blobs)
    // Kestrel only listens after the Development migrate-and-seed, so a healthy API has its schema and demo data.
    // Everything that waits for it (worker, frontends, test fixtures) starts after that, not when the process starts.
    .WithHttpHealthCheck("/health");

// Dev SSO: a local mock OpenID Connect provider (navikt/mock-oauth2-server, MIT) with seeded users, so sign-in runs
// with no real provider account. Development only; DevSso/config.json maps each user to the claims it signs in with,
// and DevSso/login.html lists them. The API sees it as any other generic provider (ScreenHire:Auth:Oidc:dev-sso).
if (builder.Environment.IsDevelopment())
{
    // The owner's credentials reach the API only in Development, where it migrates and seeds at startup. Outside
    // Development it holds only the row-level-security-bound application role.
    api.WithReference(database);

    var devSsoSecret = builder.AddParameter(
        "dev-sso-client-secret", new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true);
    var devSso = builder.AddContainer("dev-sso", "navikt/mock-oauth2-server", "6.1.2")
        .WithImageRegistry("ghcr.io")
        .WithHttpEndpoint(targetPort: 8080)
        .WithEnvironment("SERVER_PORT", "8080")
        .WithEnvironment("JSON_CONFIG", File.ReadAllText(Path.Combine(builder.AppHostDirectory, "DevSso", "config.json")))
        .WithContainerFiles("/dev-sso", Path.Combine(builder.AppHostDirectory, "DevSso"))
        .WithHttpHealthCheck("/isalive");

    api.WithEnvironment("ScreenHire__Auth__Oidc__dev-sso__Authority", ReferenceExpression.Create($"{devSso.GetEndpoint("http")}/default"))
        .WithEnvironment("ScreenHire__Auth__Oidc__dev-sso__ClientId", "screenhire-dev")
        .WithEnvironment("ScreenHire__Auth__Oidc__dev-sso__ClientSecret", devSsoSecret)
        .WithEnvironment("ScreenHire__Auth__Oidc__dev-sso__DisplayName", "Dev SSO")
        .WaitFor(devSso);
}

var worker = builder.AddProject<Projects.ScreenHire_Worker>("worker")
    .WithReference(appDatabase)
    .WaitFor(api); // the API applies migrations and sets the app role's password in Development

foreach (var (key, value) in settings.GetSection("Distribution").AsEnumerable(makePathsRelative: true))
{
    if (value is not null)
    {
        var name = $"ScreenHire__Distribution__{key.Replace(":", "__", StringComparison.Ordinal)}";
        api.WithEnvironment(name, value);
        worker.WithEnvironment(name, value);
    }
}

if (frontends != FrontendMode.None)
{
    var apiUrl = api.GetEndpoint("http");
    var script = frontends == FrontendMode.Built ? "preview" : "dev";

    // Health checks make WaitFor and the test fixtures wait until each server actually answers, not just until the
    // process starts. Careers' "/" redirects to the demo board, which also proves the API round trip.
    var careers = builder.AddJavaScriptApp("careers", "../careers", script)
        .WithPnpm(install: false)
        .WithHttpEndpoint(port: PortFor("Careers", 4322), env: "PORT")
        .WithEnvironment("API_URL", apiUrl)
        .WithHttpHealthCheck("/")
        .WaitFor(api);

    builder.AddJavaScriptApp("marketing", "../marketing", script)
        .WithPnpm(install: false)
        .WithHttpEndpoint(port: PortFor("Marketing", 4321), env: "PORT")
        .WithHttpHealthCheck("/");

    var dashboard = builder.AddViteApp("dashboard", "../dashboard", script)
        .WithPnpm(install: false)
        .WithEndpoint("http", endpoint => endpoint.Port = PortFor("Dashboard", 4323))
        .WithEnvironment("API_URL", apiUrl)
        .WithHttpHealthCheck("/")
        .WaitFor(api);

    // Feeds, the job-board API and Google notifications link to job pages on the careers app's real endpoint.
    api.WithEnvironment("ScreenHire__Domains__CareersDomain", careers.GetEndpoint("http").Property(EndpointProperty.HostAndPort));

    // Sign-in cookies, OIDC redirect URIs and emailed links point at the dashboard, which reaches the API through its
    // /v1 proxy. Its real endpoint, because ports are random in tests.
    api.WithEnvironment("ScreenHire__Domains__AppHost", dashboard.GetEndpoint("http").Property(EndpointProperty.HostAndPort));
}
else
{
    // No dashboard: the API serves its own sign-in redirects.
    api.WithEnvironment("ScreenHire__Domains__AppHost", api.GetEndpoint("http").Property(EndpointProperty.HostAndPort));
}

api.WithEnvironment("ScreenHire__Domains__ApiHost", api.GetEndpoint("http").Property(EndpointProperty.HostAndPort));

builder.Build().Run();

internal enum FrontendMode
{
    Dev,
    Built,
    None,
}
