using Aspire.Hosting.Azure;
using Aspire.Hosting.Publishing;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;
using Azure.Provisioning.Storage;

/// <summary>
/// The publish model: what <c>dotnet aspire publish</c> turns into the Bicep under infra/ (docs/operations/deploy.md).
/// Run mode never reaches this file; the local stack in AppHost.cs is unchanged.
///
/// The output must not depend on the machine or the configuration it is generated on, because CI regenerates it and
/// compares (the infra job). So every setting is a Bicep parameter whose value comes in at deployment, from configuration
/// (Parameters:{name}), and every provider's parameters exist whether or not the provider is used. An empty value means
/// the provider is not configured and stays hidden, as it does locally.
///
/// Left out of publish on purpose: Mailpit, Dev SSO and every sandbox (Development only), and the Caddy sites. The
/// marketing site and the dashboard are static builds on Cloudflare Pages. No private endpoint, virtual network or
/// dedicated workload profile: those switch Container Apps to Dedicated Plan Management (ADR-0007).
/// </summary>
#pragma warning disable ASPIREPROBES001 // Experimental, and the only way to put Container Apps probes in the generated Bicep.
internal static class AzurePublish
{
    public static void Build(IDistributedApplicationBuilder builder)
    {
        // The environment's dashboard is off: it is one more always-on container. Logs go to Log Analytics.
        builder.AddAzureContainerAppEnvironment("aca").WithDashboard(false);

        // Postgres. Password authentication, because the API and worker log in as screenhire_app with a password
        // (the app has no Entra token provider). The admin login is the table owner: only the migrator uses it.
        var adminLogin = builder.AddParameter("postgres-admin-login", "screenhire_owner", publishValueAsDefault: true);
        var adminPassword = builder.AddParameter("postgres-admin-password", secret: true);
        // Aspire writes the admin's connection strings into a Key Vault and gives every app that uses the server a role on
        // it, which would hand the API and worker the owner's password. So this vault is a separate one with no default
        // roles: no app reads it.
        var ownerVault = builder.AddAzureKeyVault("postgres-kv").ClearDefaultRoleAssignments();
        var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
            .WithPasswordAuthentication(ownerVault, adminLogin, adminPassword)
            .ConfigureInfrastructure(infrastructure =>
            {
                var server = infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
                // Local development runs pg17 (AppHost.cs). The provisioning enum stops at 16, so the version is a literal.
                server.Version = new BicepValue<PostgreSqlFlexibleServerVersion>(new StringLiteralExpression("17"));
                server.Sku = new PostgreSqlFlexibleServerSku
                {
                    Name = "Standard_B1ms",
                    Tier = PostgreSqlFlexibleServerSkuTier.Burstable,
                };
                server.Storage = new PostgreSqlFlexibleServerStorage { StorageSizeInGB = 32 };

                // The migrations run CREATE EXTENSION pg_trgm as the owner, and Azure refuses it for an extension that
                // is not on the server's allow-list.
                infrastructure.Add(new PostgreSqlFlexibleServerConfiguration("azure_extensions")
                {
                    Parent = server,
                    Name = "azure.extensions",
                    Value = "PG_TRGM",
                    Source = "user-override",
                });
            });
        postgres.AddDatabase("screenhire");

        // The password of screenhire_app, the role the API and worker connect as. The migrator sets it on the role.
        var appPassword = builder.AddParameter("screenhire-app-password", secret: true);
        var appDatabase = builder.AddConnectionString("screenhire-app", ReferenceExpression.Create(
            $"Host={postgres.Resource.HostName};Database=screenhire;Username=screenhire_app;Password={appPassword};SSL Mode=Require"));

        // Blob Storage. Applicant uploads, brand images and email attachments, all private. The API signs user-delegation
        // SAS links (Storage Blob Data Contributor includes that), so no account key is ever used.
        var storage = builder.AddAzureStorage("storage");
        var blobs = storage.AddBlobs("blobs");
        foreach (var container in new[] { "applicant-uploads", "brand-images", "email-attachments" })
        {
            storage.AddBlobContainer(container);
        }

        storage.ConfigureInfrastructure(infrastructure =>
        {
            // Locally redundant: the default is geo-redundant, which doubles the price for a copy ADR-0007 does not ask for.
            infrastructure.GetProvisionableResources().OfType<StorageAccount>().Single().Sku = new StorageSku { Name = StorageSkuName.StandardLrs };
            var service = infrastructure.GetProvisionableResources().OfType<BlobService>().Single();
            // The browser uploads straight to storage with a create-only SAS link, from any tenant's careers host
            // ({tenant}.{CareersDomain}), and the dashboard reads files with a read-only one. Storage CORS cannot
            // match a wildcard subdomain, and the link is the credential, so the origin is open and the methods are not.
            service.CorsRules.Add(new StorageCorsRule
            {
                AllowedOrigins = { "*" },
                AllowedMethods = { CorsRuleAllowedMethod.Get, CorsRuleAllowedMethod.Head, CorsRuleAllowedMethod.Put },
                AllowedHeaders = { "*" },
                ExposedHeaders = { "*" },
                MaxAgeInSeconds = 3600,
            });
        });

        // Key Vault: the secrets the app needs to start, and the key that protects the Data Protection key ring.
        var vault = builder.AddAzureKeyVault("vault");
        // The provisioning library has no resource for a vault key, so this one is a few lines of Bicep.
        var dataProtectionKey = builder.AddBicepTemplateString("data-protection-key", Lf("""
            param vaultName string

            resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
              name: vaultName
            }

            resource key 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
              parent: vault
              name: 'data-protection'
              properties: {
                kty: 'RSA'
                keySize: 2048
                keyOps: [
                  'wrapKey'
                  'unwrapKey'
                ]
              }
            }

            output keyUri string = key.properties.keyUri
            """))
            .WithParameter("vaultName", vault.Resource.NameOutputReference);

        // Settings. Every one is a parameter, so the generated Bicep is the same wherever it is generated. A parameter
        // with a default is optional. A provider's secrets default to empty, and an empty client id or key leaves that
        // provider unconfigured and hidden (every options class tests IsNullOrWhiteSpace).
        IResourceBuilder<ParameterResource> Required(string name) => builder.AddParameter(name);
        IResourceBuilder<ParameterResource> Optional(string name, string fallback = "") =>
            builder.AddParameter(name, new FallbackValue(fallback));
        IResourceBuilder<ParameterResource> Secret(string name) => builder.AddParameter(name, secret: true);

        // Secrets the apps cannot start without. They go into the vault under their configuration key, with two dashes for each
        // colon, and the apps load the whole vault into configuration at start (Aspire's AddAzureKeyVaultSecrets). The
        // optional providers' secrets (SendGrid, Slack, calendars, sign-in, Turnstile, Indexing, Anthropic) are not
        // here: the owner sets them in the vault by the same naming (docs/operations/deploy.md), and a provider whose
        // secret is absent stays hidden. That keeps this model, and so the Bicep, the same for every deployment.
        vault.AddSecret("webhook-signing-key-secret", "ScreenHire--Webhooks--SigningKey", Secret("webhook-signing-key"));
        vault.AddSecret("device-hash-key-secret", "ScreenHire--Verification--DeviceHashKey", Secret("verification-device-hash-key"));
        vault.AddSecret("email-reply-key-secret", "ScreenHire--Email--ReplyKey", Secret("email-reply-key"));
        vault.AddSecret("sendgrid-inbound-secret-secret", "ScreenHire--Email--SendGrid--InboundSecret", Secret("sendgrid-inbound-secret"));

        var api = builder.AddProject<Projects.ScreenHire_Api>("api")
            .WithReference(appDatabase)
            .WithReference(blobs)
            .WithRoleAssignments(storage, StorageRoles)
            .WithReference(vault)
            .WithRoleAssignments(vault, VaultRoles)
            // The API reads X-Forwarded-For once, itself, from the proxies it is told to trust (AuthenticationSetup). This switch
            // makes ASP.NET Core read forwarded headers a second time, from any sender, before that.
            .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false")
            .WithExternalHttpEndpoints()
            // Aspire's defaults (5 seconds' delay, 1 second's timeout) would restart a container still starting: it reads the
            // vault and compiles Wolverine's handlers on half a vCPU, and /health queries a Burstable server.
            .WithHttpProbe(ProbeType.Liveness, "/alive", initialDelaySeconds: 60, timeoutSeconds: 3)
            .WithHttpProbe(ProbeType.Readiness, "/health", timeoutSeconds: 5)
            .PublishAsAzureContainerApp((infrastructure, app) =>
            {
                // One replica: the SignalR hub is in-process and one consumer reads the real-time queue (realtime.md).
                Size(app, minReplicas: 1, maxReplicas: 1, cpu: 0.5, memory: "1Gi");
            });

        var worker = builder.AddProject<Projects.ScreenHire_Worker>("worker")
            .WithReference(appDatabase)
            .WithReference(blobs)
            .WithRoleAssignments(storage, StorageRoles)
            .WithReference(vault)
            .WithRoleAssignments(vault, VaultRoles)
            .PublishAsAzureContainerApp((infrastructure, app) =>
            {
                Size(app, minReplicas: 1, maxReplicas: 1, cpu: 0.25, memory: "0.5Gi");
            });

        // Careers is a pnpm workspace app, so its image builds from the repository root.
        var careers = builder.AddDockerfile("careers", "../..", "apps/careers/Dockerfile")
            .WithBuildArg("CAREERS_ALLOWED_DOMAINS", Optional("careers-allowed-domains"))
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithExternalHttpEndpoints()
            .WithEnvironment("PORT", "8080")
            .WithEnvironment("HOST", "0.0.0.0")
            .WithEnvironment("API_URL", api.GetEndpoint("http"))
            .WithHttpProbe(ProbeType.Readiness, "/")
            .PublishAsAzureContainerApp((infrastructure, app) =>
            {
                Size(app, minReplicas: 1, maxReplicas: 2, cpu: 0.25, memory: "0.5Gi");
            });

        // Domains (ADR-0003): the API, the worker and careers read the same ScreenHire:Domains values.
        var careersDomain = Required("domains-careers-domain");
        var appHost = Required("domains-app-host");
        foreach (IResourceBuilder<IResourceWithEnvironment> app in new IResourceBuilder<IResourceWithEnvironment>[] { api, worker, careers })
        {
            app.WithEnvironment("ScreenHire__Domains__CareersDomain", careersDomain)
                .WithEnvironment("ScreenHire__Domains__CareersHostPattern", "{tenant}.{CareersDomain}");
        }

        api.WithEnvironment("ScreenHire__Domains__Scheme", "https")
            .WithEnvironment("ScreenHire__Domains__BaseDomain", Required("domains-base-domain"))
            .WithEnvironment("ScreenHire__Domains__ApiHost", Required("domains-api-host"))
            .WithEnvironment("ScreenHire__Domains__AppHost", appHost);
        worker.WithEnvironment("ScreenHire__Domains__AppHost", appHost);

        // Email (docs/architecture/email.md). An empty SendGrid key leaves SendGrid off, and with no sender the worker
        // skips email. Local-only settings (Mailpit, the sandbox) are not set here.
        var emailFrom = Required("email-from");
        var replyDomain = Optional("email-reply-domain");
        foreach (IResourceBuilder<IResourceWithEnvironment> app in new IResourceBuilder<IResourceWithEnvironment>[] { api, worker })
        {
            app.WithEnvironment("ScreenHire__Email__From", emailFrom)
                .WithEnvironment("ScreenHire__Email__ReplyDomain", replyDomain);
        }

        api.WithEnvironment("ScreenHire__Email__SendGrid__EventWebhookPublicKey", Optional("sendgrid-event-webhook-public-key"));

        // The key that protects the Data Protection key ring (Data Protection keys stay in Postgres).
        foreach (IResourceBuilder<IResourceWithEnvironment> app in new IResourceBuilder<IResourceWithEnvironment>[] { api, worker })
        {
            app.WithEnvironment("ScreenHire__DataProtection__KeyVaultKeyUri", dataProtectionKey.GetOutput("keyUri"));
        }

        // Sign-in, the proxies in front of the API, and the optional providers. An empty client id or secret leaves a
        // provider out of the sign-in list.
        api.WithEnvironment("ScreenHire__Auth__Google__ClientId", Optional("auth-google-client-id"))
            .WithEnvironment("ScreenHire__Auth__Microsoft__ClientId", Optional("auth-microsoft-client-id"))
            .WithEnvironment("ScreenHire__Auth__TrustedProxies__0", Required("auth-trusted-proxies"))
            .WithEnvironment("ScreenHire__Auth__ForwardLimit", Optional("auth-forward-limit", "1"))
            .WithEnvironment("ScreenHire__Turnstile__SiteKey", Optional("turnstile-site-key"));

        var slackClientId = Optional("slack-client-id");
        var googleCalendarId = Optional("calendar-google-client-id");
        var microsoftCalendarId = Optional("calendar-microsoft-client-id");
        var indexingMode = Optional("google-indexing-mode", "Off");
        foreach (IResourceBuilder<IResourceWithEnvironment> app in new IResourceBuilder<IResourceWithEnvironment>[] { api, worker })
        {
            app.WithEnvironment("ScreenHire__Slack__ClientId", slackClientId)
                .WithEnvironment("ScreenHire__Calendar__Google__ClientId", googleCalendarId)
                .WithEnvironment("ScreenHire__Calendar__Microsoft__ClientId", microsoftCalendarId)
                .WithEnvironment("ScreenHire__Distribution__GoogleIndexing__Mode", indexingMode);
        }

        // The consistency check's model (verification-engine.md section 10.10). Provider None records "unavailable".
        // Model ids come from here and nowhere else.
        worker.WithEnvironment("ScreenHire__Verification__Llm__Provider", Optional("verification-llm-provider", "None"))
            .WithEnvironment("ScreenHire__Verification__Llm__ExtractionModel", Optional("verification-llm-extraction-model"))
            .WithEnvironment("ScreenHire__Verification__Llm__ConsistencyModel", Optional("verification-llm-consistency-model"));
    }

    // The value of a parameter when configuration has none. AddParameter's string overload would not do: it sets the value
    // and never reads Parameters:{name}, so a deployment could not set the parameter at all.
    private sealed class FallbackValue(string value) : ParameterDefault
    {
        public override string GetDefaultValue() => value;

        public override void WriteToManifest(ManifestPublishingContext context) => context.Writer.WriteString("value", value);
    }

    // A raw string literal keeps the line endings of the source file, and the Bicep must not depend on which checkout wrote it.
    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    // Read the vault's secrets into configuration at start, and use its key to protect the Data Protection key ring.
    private static readonly KeyVaultBuiltInRole[] VaultRoles = [KeyVaultBuiltInRole.KeyVaultSecretsUser, KeyVaultBuiltInRole.KeyVaultCryptoUser];

    // Blobs only, in place of Aspire's default of table and queue roles too. It also lets the API sign user-delegation SAS links.
    private static readonly StorageBuiltInRole[] StorageRoles = [StorageBuiltInRole.StorageBlobDataContributor];

    private static void Size(ContainerApp app, int minReplicas, int maxReplicas, double cpu, string memory)
    {
        app.Template.Scale.MinReplicas = minReplicas;
        app.Template.Scale.MaxReplicas = maxReplicas;
        foreach (var container in app.Template.Containers)
        {
            container.Value!.Resources = new AppContainerResources { Cpu = cpu, Memory = memory };
        }
    }
}
