// Local orchestration for the whole stack. Prerequisites: Docker running, `pnpm install` at the repo root
// (JS apps share one pnpm workspace, so Aspire must not run per-app installs — hence WithPnpm(install: false)).
//
// Settings (env `ScreenHire__…` or command-line `--ScreenHire:…`):
//   PersistData (default true)  keep Postgres, Azurite and Mailpit data in volumes. Volume names come from Aspire and
//                               include a hash of this AppHost's path, so each git worktree gets its own data.
//   PinPorts    (default true)  stable local URLs; set false (as the integration tests do) for random ports, or
//                               override individual ports with Ports:Api / Ports:Marketing / Ports:Careers / Ports:Dashboard
//                               / Ports:Mailpit (its web UI) / Ports:DevSso. `aspire start --isolated` (what /preview
//                               uses in a worktree) randomises every port, these and the Aspire dashboard's included.
//   Frontends   (default Dev)   Dev: dev servers with hot reload. Built: the output of `pnpm -r build` served as it
//                               ships (the end-to-end tests use it): the dashboard and marketing sites from Caddy
//                               containers (Caddy/*/Caddyfile; the dashboard's sends /v1 to the API), careers from its
//                               own Node server, dist/server/entry.mjs. None: no JS apps at all (the integration tests
//                               use it; only the API, worker and infrastructure start).
//   Demo        (on with Dev)   the local demo's data on Brightwater (the API's DevelopmentDemo): on with Frontends Dev,
//                               off with Built and None, which the test stacks use. The integration tests'
//                               DevelopmentDemoTests turn it on for a stack of their own. With it on, the API is healthy
//                               only once the demo data is in.
//   Distribution:*              job distribution settings, forwarded as they are to the API and the worker so both see
//                               the same boards (ScreenHire.Application DistributionOptions). appsettings.Development.json
//                               turns on the local sandbox board, a fake that must never reach real tenants;
//                               GoogleIndexing:Mode is Off unless set (Local is the fake); in Google mode the key comes
//                               from the secret parameter google-indexing-service-account-key, never from these keys.
//   Verification:Llm:*             the consistency check's model (ScreenHire.Infrastructure LlmOptions), forwarded to the
//                               worker. The worker's Development settings use the deterministic fake model; Provider
//                               None turns the check into "unavailable"; in Anthropic mode the key comes from the secret
//                               parameter anthropic-api-key, never from these keys.
//   Webhooks:*                  webhook settings (ScreenHire.Application WebhookOptions), forwarded to the API and the
//                               worker: AllowLoopback (appsettings.Development.json turns it on, so a receiver on this
//                               machine works; never in production), RetryDelays:0..n and DisableAfterFailedDeliveries
//                               (the tests shorten both). The signing key comes from the webhook-signing-key parameter,
//                               never from these keys.
//   CareersEmbedding:*          the sites allowed to embed the careers pages (the API's CareersEmbeddingOptions,
//                               docs/architecture/careers-widget.md), forwarded to the API: AllowLoopback
//                               (appsettings.Development.json and the test stacks turn it on, so a site on this machine,
//                               http://localhost or http://*.localhost, can be added; never in production, where sites
//                               use https).
//   Slack:*                     Slack (ScreenHire.Application SlackOptions, docs/architecture/slack.md), forwarded to the
//                               API and the worker. Off unless configured: real Slack once ClientId is set and the
//                               slack-client-secret parameter has the app's secret; Sandbox:Enabled=true is the local
//                               sandbox workspace instead (Development only; the test stacks turn it on). The secret
//                               comes from that parameter, never from these keys.
// Avoid :5173 — Kangentic's own dev server uses it on this machine.
using Aspire.Hosting.Utils;
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
var demo = bool.TryParse(settings["Demo"], out var demoSetting) ? demoSetting : frontends == FrontendMode.Dev;
int? PortFor(string name, int fallback) =>
    !pinPorts ? null : int.TryParse(settings[$"Ports:{name}"], out var port) ? port : fallback;

// pgvector image: near-duplicate answer detection needs the `vector` extension (ADR-0008). The API, the worker, their
// Wolverine agents and the tests each keep their own connection pool, so a burst (the worker assessing every applicant
// at start) went past Postgres's default 100 connections and refused clients; 300 leaves room for all of them.
var postgres = builder.AddPostgres("postgres").WithImage("pgvector/pgvector", "pg17").WithArgs("-c", "max_connections=300");
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

// Mailpit catches every email the stack sends (sign-in links, automation emails); its web UI and HTTP API show them. The
// web UI is pinned like the apps (Ports:Mailpit, default 8025, Mailpit's usual port) so the local guide can link it; SMTP
// stays random, since only the API and worker use it, through Aspire's reference.
var mailpit = builder.AddMailPit("mailpit", httpPort: PortFor("Mailpit", 8025))
    // Mailpit looks up each SMTP client's reverse DNS by default, which takes ~10 s per connection under Docker Desktop.
    .WithEnvironment("MP_SMTP_DISABLE_RDNS", "true");
if (persistData)
{
    // The demo sends its emails once, on the start that adds its candidates, so they stay with the data they belong to.
    mailpit.WithDataVolume(VolumeNameGenerator.Generate(mailpit, "data"));
}

// The key the API hashes candidates' device fingerprints with, per organization (ScreenHire:Verification:DeviceHashKey).
// Generated locally and kept in the AppHost's user secrets like other generated parameters; production sets its own.
var deviceHashKey = builder.AddParameter(
    "verification-device-hash-key", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);

// The API's `http` launch profile only: its `https` profile would add a fixed https://localhost:7080 that nothing uses
// and that collides when two stacks run side by side.
var api = builder.AddProject<Projects.ScreenHire_Api>("api", launchProfileName: "http")
    .WithEndpoint("http", endpoint => endpoint.Port = PortFor("Api", 5080))
    .WaitFor(database)
    .WithReference(appDatabase)
    .WithReference(mailpit).WaitFor(mailpit)
    .WithReference(blobs).WaitFor(blobs)
    .WithEnvironment("ScreenHire__Verification__DeviceHashKey", deviceHashKey)
    // Kestrel only listens after the Development migrate-and-seed, and with Demo on /health stays unhealthy until the demo
    // data is in, so a healthy API has its schema and its data. Everything that waits for it (worker, frontends, test
    // fixtures) starts after that, not when the process starts.
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
        .WithHttpEndpoint(port: PortFor("DevSso", 5090), targetPort: 8080)
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

// The platform key webhooks are signed with (ScreenHire:Webhooks:SigningKey): CallWebhook automations derive each
// organization's key from it, subscriptions each endpoint's secret. The worker signs; the API derives an endpoint's
// secret to show it once. Generated and kept in the AppHost's user secrets locally; production sets its own secret.
var webhookSigningKey = builder.AddParameter(
    "webhook-signing-key", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);
api.WithEnvironment("ScreenHire__Webhooks__SigningKey", webhookSigningKey);

var worker = builder.AddProject<Projects.ScreenHire_Worker>("worker")
    .WithReference(appDatabase)
    .WithEnvironment("ScreenHire__Webhooks__SigningKey", webhookSigningKey)
    // Automations send transactional email (application received, stage change) from the worker.
    .WithReference(mailpit)
    // Content forensics read candidates' uploads (read only).
    .WithReference(blobs).WaitFor(blobs)
    .WaitFor(api); // the API applies migrations and sets the app role's password in Development

foreach (var (key, value) in settings.GetSection("Distribution").AsEnumerable(makePathsRelative: true))
{
    // The service account key is a secret: it goes through the parameter below, never as plain configuration.
    if (value is not null && !key.Equals("GoogleIndexing:ServiceAccountKeyJson", StringComparison.OrdinalIgnoreCase))
    {
        var name = $"ScreenHire__Distribution__{key.Replace(":", "__", StringComparison.Ordinal)}";
        api.WithEnvironment(name, value);
        worker.WithEnvironment(name, value);
    }
}

foreach (var (key, value) in settings.GetSection("Webhooks").AsEnumerable(makePathsRelative: true))
{
    // The signing key is a secret: it goes through the parameter above, never as plain configuration.
    if (value is not null && !key.Equals("SigningKey", StringComparison.OrdinalIgnoreCase))
    {
        var name = $"ScreenHire__Webhooks__{key.Replace(":", "__", StringComparison.Ordinal)}";
        api.WithEnvironment(name, value);
        worker.WithEnvironment(name, value);
    }
}

foreach (var (key, value) in settings.GetSection("CareersEmbedding").AsEnumerable(makePathsRelative: true))
{
    if (value is not null)
    {
        api.WithEnvironment($"ScreenHire__CareersEmbedding__{key.Replace(":", "__", StringComparison.Ordinal)}", value);
    }
}

foreach (var (key, value) in settings.GetSection("Slack").AsEnumerable(makePathsRelative: true))
{
    // The client secret is a secret: it goes through the parameter below, never as plain configuration.
    if (value is not null && !key.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase))
    {
        var name = $"ScreenHire__Slack__{key.Replace(":", "__", StringComparison.Ordinal)}";
        api.WithEnvironment(name, value);
        worker.WithEnvironment(name, value);
    }
}

// The Slack app's client secret (docs/architecture/slack.md), only once a client id is set: a secret parameter, set with
// `dotnet user-secrets set Parameters:slack-client-secret "<secret>"` in apps/apphost or by the deployment's secret store.
// Both hosts get it, so both work out the same mode from the same settings. With no client id nothing asks for it.
if (!string.IsNullOrWhiteSpace(settings["Slack:ClientId"]))
{
    var slackSecret = builder.AddParameter("slack-client-secret", secret: true);
    api.WithEnvironment("ScreenHire__Slack__ClientSecret", slackSecret);
    worker.WithEnvironment("ScreenHire__Slack__ClientSecret", slackSecret);
}

// The real Indexing API's service account key (job-distribution.md section 5), only in Google mode: a secret parameter,
// set with `dotnet user-secrets set Parameters:google-indexing-service-account-key "<json>"` in apps/apphost or by the
// deployment's secret store. In Off or Local mode there is no parameter, so nothing asks for a key.
if (string.Equals(settings["Distribution:GoogleIndexing:Mode"], "Google", StringComparison.OrdinalIgnoreCase))
{
    var googleKey = builder.AddParameter("google-indexing-service-account-key", secret: true);
    api.WithEnvironment("ScreenHire__Distribution__GoogleIndexing__ServiceAccountKeyJson", googleKey);
    worker.WithEnvironment("ScreenHire__Distribution__GoogleIndexing__ServiceAccountKeyJson", googleKey);
}

foreach (var (key, value) in settings.GetSection("Verification:Llm").AsEnumerable(makePathsRelative: true))
{
    // The API key is a secret: it goes through the parameter below, never as plain configuration.
    if (value is not null && !key.StartsWith("Anthropic", StringComparison.OrdinalIgnoreCase))
    {
        worker.WithEnvironment($"ScreenHire__Verification__Llm__{key.Replace(":", "__", StringComparison.Ordinal)}", value);
    }
}

// Anthropic's API key for the consistency check (verification-engine.md §10.10), only in Anthropic mode: a secret parameter,
// set with `dotnet user-secrets set Parameters:anthropic-api-key "<key>"` in apps/apphost or by the deployment's secret
// store. In the default modes there is no parameter, so nothing asks for a key.
if (string.Equals(settings["Verification:Llm:Provider"], "Anthropic", StringComparison.OrdinalIgnoreCase))
{
    var anthropicKey = builder.AddParameter("anthropic-api-key", secret: true);
    worker.WithEnvironment("ScreenHire__Verification__Llm__Anthropic__ApiKey", anthropicKey);
}

// The local demo's data (the API's DevelopmentDemo, docs/operations/local-demo.md): Brightwater's template, jobs on every
// board, a draft and a closed job, the synthetic applicants and candidates in every stage, once the API starts. Only for
// `pnpm dev` by default: the test stacks make their own data and treat Brightwater as the seeder leaves it, and the
// startup burst of submissions delayed a later live update in the end-to-end tests (verification-engine.md §10.9).
if (demo)
{
    api.WithEnvironment("ScreenHire__Demo__Enabled", "true");
}

if (frontends != FrontendMode.None)
{
    var apiUrl = api.GetEndpoint("http");
    var built = frontends == FrontendMode.Built;

    // Built serves what ships, with no Vite or Astro preview server: their Node processes died mid-run in the end-to-end
    // tests with a native abort (0xC0000409) and no message (docs/process/testing.md, "Flake history"). The static apps
    // go to Caddy, a real static file server, with Caddyfiles in Caddy/; careers runs its own Node server, the one that
    // ships, with no pnpm or Astro CLI around it. Caddy is the official caddy image (Apache-2.0), pinned.
    IResourceBuilder<ContainerResource> StaticSite(string name, string site, int? port) =>
        builder.AddContainer(name, "caddy", "2.10.2-alpine")
            .WithBindMount(Path.Combine(builder.AppHostDirectory, "Caddy", name), "/etc/caddy", isReadOnly: true)
            .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", site)), "/srv", isReadOnly: true)
            .WithHttpEndpoint(port: port, targetPort: 8080)
            .WithHttpHealthCheck("/");

    // Health checks make WaitFor and the test fixtures wait until each server actually answers, not just until the
    // process starts. Careers' "/" redirects to the demo board, which also proves the API round trip.
    IResourceBuilder<Aspire.Hosting.JavaScript.JavaScriptAppResource> careers = built
        ? builder.AddNodeApp("careers", "../careers", "dist/server/entry.mjs")
        : builder.AddJavaScriptApp("careers", "../careers", "dev");
    careers.WithPnpm(install: false)
        .WithHttpEndpoint(port: PortFor("Careers", 4322), env: "PORT")
        .WithEnvironment("API_URL", apiUrl)
        .WithHttpHealthCheck("/")
        .WaitFor(api);

    // Tenant careers hosts (ADR-0003): {tenant}.{CareersDomain}, the careers endpoint locally, so a board is also at
    // http://brightwater.localhost:<port>/ (browsers resolve *.localhost to this machine). Path-based URLs keep working.
    // The careers server, the API and the worker read the same ScreenHire:Domains settings, so the canonical, sitemap,
    // feed, job-board API and Google URLs the API builds, and the follow-up questionnaire links the worker emails, are
    // these tenant hosts.
    var careersDomain = careers.GetEndpoint("http").Property(EndpointProperty.HostAndPort);
    foreach (var resource in new IResourceBuilder<IResourceWithEnvironment>[] { careers, api, worker })
    {
        resource.WithEnvironment("ScreenHire__Domains__CareersDomain", careersDomain)
            .WithEnvironment("ScreenHire__Domains__CareersHostPattern", "{tenant}.{CareersDomain}");
    }

    if (built)
    {
        StaticSite("marketing", "marketing/dist", PortFor("Marketing", 4321));
    }
    else
    {
        builder.AddJavaScriptApp("marketing", "../marketing", "dev")
            .WithPnpm(install: false)
            .WithHttpEndpoint(port: PortFor("Marketing", 4321), env: "PORT")
            .WithHttpHealthCheck("/");
    }

    // The dashboard reaches the API through its own /v1 path (cookies stay same-origin): the Vite dev server's proxy, or
    // in Built mode the Caddyfile's, which also carries the real-time hub's WebSocket.
    IResourceBuilder<IResourceWithEndpoints> dashboard = built
        ? StaticSite("dashboard", "dashboard/build", PortFor("Dashboard", 4323)).WithEnvironment("API_URL", apiUrl).WaitFor(api)
        : builder.AddViteApp("dashboard", "../dashboard", "dev")
            .WithPnpm(install: false)
            .WithEndpoint("http", endpoint => endpoint.Port = PortFor("Dashboard", 4323))
            .WithEnvironment("API_URL", apiUrl)
            .WithHttpHealthCheck("/")
            .WaitFor(api);

    // Sign-in cookies, OIDC redirect URIs and emailed links point at the dashboard, which reaches the API through its
    // /v1 proxy. Its real endpoint, because ports are random in tests. The worker links to it from mention emails.
    var appHost = dashboard.GetEndpoint("http").Property(EndpointProperty.HostAndPort);
    api.WithEnvironment("ScreenHire__Domains__AppHost", appHost);
    worker.WithEnvironment("ScreenHire__Domains__AppHost", appHost);
}
else
{
    // No dashboard: the API serves its own sign-in redirects, and mention emails link to the same host.
    var appHost = api.GetEndpoint("http").Property(EndpointProperty.HostAndPort);
    api.WithEnvironment("ScreenHire__Domains__AppHost", appHost);
    worker.WithEnvironment("ScreenHire__Domains__AppHost", appHost);
}

api.WithEnvironment("ScreenHire__Domains__ApiHost", api.GetEndpoint("http").Property(EndpointProperty.HostAndPort));

builder.Build().Run();

internal enum FrontendMode
{
    Dev,
    Built,
    None,
}
