---
type: Guide
title: "Deploy ScreenHire to Azure"
description: "The runbook for the first deployment and every release: what the Azure definition builds, the order of the steps with exact commands, which setting is a parameter and which a Key Vault secret, the owner steps for SendGrid, domains, sign-in and calendars, budget alerts, rollback, and the limits to know. Nothing in it has been run."
tags: [deploy, azure, hosting, operations]
sources:
  - resource: apps/apphost/AzurePublish.cs
  - resource: apps/apphost/AppHost.cs
  - resource: azure.yaml
  - resource: infra/main.bicep
  - resource: apps/migrator/Program.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/DatabaseMigrator.cs
  - resource: src/ScreenHire.Infrastructure/Hosting/KeyVaultConfiguration.cs
  - resource: apps/careers/astro.config.mjs
  - resource: apps/dashboard/static/_redirects
  - resource: apps/dashboard/static/_headers
  - resource: apps/dashboard/static/_routes.json
  - resource: apps/careers/Dockerfile
  - resource: apps/dashboard/functions/v1/[[path]].js
  - resource: src/ScreenHire.Infrastructure/Hosting/ProductionSettings.cs
  - resource: .github/workflows/ci.yml
---
# Deploy ScreenHire to Azure

This is the plan of [ADR-0007](../adr/0007-hosting-azure.md) made runnable. A task wrote it and a task cannot deploy, so
no command below has been run against Azure. Each one is checked only against its tool's `--help` where that is said.
Expect to correct a flag or two on the first pass and fix this page when you do.

## What gets built

| Piece | Where | Size |
|---|---|---|
| API | Container Apps, external ingress, 1 replica fixed | 0.5 vCPU, 1 GiB |
| Worker | Container Apps, no ingress, 1 replica fixed | 0.25 vCPU, 0.5 GiB |
| Careers site | Container Apps, external ingress, 1 to 2 replicas | 0.25 vCPU, 0.5 GiB |
| Database | PostgreSQL Flexible Server 17, Burstable B1ms, 32 GB, 7-day backups, password login, `pg_trgm` allowed | |
| Files | Blob Storage, locally redundant, four private containers, CORS for the browser's direct uploads | |
| Secrets and keys | Key Vault: required secrets, the Data Protection key. A second vault (`postgreskv-...`) holds the Postgres owner's connection strings, which Aspire writes there. No app has a role on it. | |
| Images | Azure Container Registry (Basic), created by the Container Apps environment | |
| Marketing and dashboard | Cloudflare Pages, built from this repository | |
| Edge | Cloudflare in front of everything | |

The API is pinned to one replica because the SignalR hub is in the API process and one consumer reads the real-time queue
([realtime](../architecture/realtime.md)). Nothing here uses a private endpoint, a virtual network or a dedicated workload
profile, which are what switch Container Apps to Dedicated Plan Management (about $104 a month, ADR-0007). Do not add them
until a customer requires it.

Where this differs from ADR-0007:
- Images live in Azure Container Registry (about $5 a month), not GitHub Container Registry.
- The database has a password login, not Entra ID, because the API and worker sign in as `screenhire_app` with a password.
- Log Analytics ingestion is a cost line. The Aspire dashboard on the Container Apps environment is off.
- The server's firewall allows all Azure addresses (Aspire's default), not a restricted list. Any resource in any Azure subscription can
  reach the port, so the two passwords are all that stands in the way: generate them as below and keep them long. An app on Consumption
  has no fixed outbound address to allow, and a virtual network is what ADR-0007 warns against. Decide this before real candidates
  apply: the choice is this risk or the virtual network's cost.

## Files that matter

- `apps/apphost/AzurePublish.cs` is the model. `AppHost.cs` is the local stack and is not used for publishing.
- `infra/` is the Bicep that `dotnet aspire publish` writes from that model. Never edit it by hand: the `infra` CI job
  regenerates it and fails when it differs. To regenerate after a change to the model:

```bash
rm -rf infra
dotnet aspire publish --apphost apps/apphost/ScreenHire.AppHost.csproj --output-path infra --non-interactive
```

- `azure.yaml` is the `azd` project file the ADR names. The steps below use the Aspire CLI instead, because its pipeline has a
  step that provisions infrastructure alone, which the migration needs, and because `infra/` is the Aspire CLI's own layout
  (`main.bicep` with one module per resource), not the templates `azd` generates. Nothing has shown that `azd` can deploy from this
  `infra/` folder. If you want `azd`, move `infra/` aside and let it generate its own from the AppHost, then keep the same order:
  provision, run the migrator, deploy.
- `apps/migrator` creates the schema as the database owner. It is the only thing that holds that login.
- `apps/careers/Dockerfile` builds the careers image from the repository root.
- `apps/dashboard/functions/v1/[[path]].js` forwards the dashboard's `/v1` calls to the API on Cloudflare.

## Settings

An app started outside Development stops at once, with one message naming every required setting it lacks
(`ProductionSettings`). A provider with no configuration stays hidden: that is how SendGrid, Slack, the calendars, Google and
Microsoft sign-in, Turnstile, Google Indexing and the language model are optional.

Every parameter goes in one file, `apps/apphost/appsettings.Production.json`, which git ignores. The Aspire CLI runs the AppHost
as Production, and a Production host does not read user secrets, so that file (or an environment variable named
`Parameters__<name>`, set with `env 'Parameters__domains-app-host=app.example.org' dotnet aspire ...` because a shell cannot export
a name with a hyphen) is where they come from. This was tried: a value in the file is read, and one in user secrets is not.

```json
{
  "Parameters": {
    "domains-app-host": "app.example.org",
    "postgres-admin-password": "<generated>"
  }
}
```

The file holds passwords. Keep it out of backups and delete it when the deployment is done, or keep the values in a password manager.

| Parameter | What it is | Required |
|---|---|---|
| `postgres-admin-password` | The password of the server's owner login. The login is fixed at `screenhire_owner` (setting `postgres-admin-login` does nothing). Only the migrator uses it. | yes |
| `screenhire-app-password` | The password `screenhire_app` gets. The API and worker use it. | yes |
| `domains-base-domain` | The company's own domain, for default From addresses | yes |
| `domains-app-host` | The dashboard host, e.g. `app.example.org` | yes |
| `domains-api-host` | The API host, e.g. `api.example.org` | yes |
| `domains-careers-domain` | The careers domain; boards are `{tenant}.{domain}` | yes |
| `careers-allowed-domains` | Hosts the careers server trusts forwarded headers from, comma separated, e.g. `careers.example.org,*.careers.example.org`. Read when the image builds. | for correct rate limits |
| `auth-trusted-proxies` | Addresses or CIDR networks of the proxies in front of the API, comma separated | yes |
| `auth-forward-limit` | How many forwarded hops to read (default 1) | no |
| `email-from` | The default From address | yes |
| `email-reply-domain` | The host replies arrive on, e.g. `reply.example.org` | for replies |
| `sendgrid-event-webhook-public-key` | SendGrid's signed event webhook key (not a secret) | for bounces |
| `auth-google-client-id`, `auth-microsoft-client-id` | Sign-in apps | no |
| `slack-client-id` | The Slack app | no |
| `calendar-google-client-id`, `calendar-microsoft-client-id` | Calendar apps | no |
| `turnstile-site-key` | Cloudflare Turnstile | no |
| `google-indexing-mode` | `Off` (default) or `Google` | no |
| `verification-llm-provider`, `verification-llm-extraction-model`, `verification-llm-consistency-model` | `None` (default) or `Anthropic` and the model ids. Model ids live here and nowhere else. | no |

These secrets are required, and the deployment writes them into the Key Vault under the name shown:

| Parameter | Key Vault secret | What it is |
|---|---|---|
| `webhook-signing-key` | `ScreenHire--Webhooks--SigningKey` | Signs webhooks. 32 or more random characters. |
| `verification-device-hash-key` | `ScreenHire--Verification--DeviceHashKey` | Hashes device fingerprints. 32 or more characters. |
| `email-reply-key` | `ScreenHire--Email--ReplyKey` | Signs reply addresses |
| `sendgrid-inbound-secret` | `ScreenHire--Email--SendGrid--InboundSecret` | The password of the Inbound Parse URL |

The optional providers' secrets are not parameters. You set them in the vault, by hand, and a provider whose secret is absent
stays hidden. The name is the setting with `--` for each colon:

| Key Vault secret | Provider |
|---|---|
| `ScreenHire--Email--SendGrid--ApiKey` | SendGrid sends email. Without it nothing is sent. |
| `ScreenHire--Auth--Google--ClientSecret`, `ScreenHire--Auth--Microsoft--ClientSecret` | Sign-in |
| `ScreenHire--Slack--ClientSecret` | Slack |
| `ScreenHire--Billing--SecretKey`, `ScreenHire--Billing--WebhookSecret` | Billing. Stripe is on only when both are set; with neither, billing is hidden and nothing is limited |
| `ScreenHire--Calendar--Google--ClientSecret`, `ScreenHire--Calendar--Microsoft--ClientSecret` | Calendars |
| `ScreenHire--Turnstile--SecretKey` | Bot challenge |
| `ScreenHire--Distribution--GoogleIndexing--ServiceAccountKeyJson` | Google Indexing |
| `ScreenHire--Verification--Llm--Anthropic--ApiKey` | The consistency check |

Apps read the vault when they start, so restart them after changing a secret (step 8).

Generate a random secret with `openssl rand -base64 48`. Use only letters and digits where a value goes into a connection
string (`screenhire-app-password`): `openssl rand -hex 24`.

## Before you start (owner)

1. An Azure subscription, and Microsoft for Startups credits if you have them (ADR-0007).
2. Domains. Pick three hosts and one careers domain. Cloudflare's free certificate covers one level of wildcard, so
   `{tenant}.careers.example.org` is not covered. Use a separate registrable domain for careers (for example
   `example-jobs.org`), or buy Cloudflare's Advanced Certificate Manager.
3. A Cloudflare account with the domains added.
4. A SendGrid subscription from the Azure Marketplace (ADR-0022).
5. Google and Microsoft OAuth apps, a Slack app, calendar apps, a Turnstile widget, and an Anthropic key, for each provider you
   want on. Every one is optional for a first deployment.
6. Tools: the Azure CLI, Docker, `dotnet tool restore` (the Aspire CLI is repository-local, `dotnet aspire`), and Node with
   `pnpm install` for the static builds.

## Steps

### 1. Sign in and set the budget alerts

```bash
az login
az account set --subscription <subscription id>
export Azure__SubscriptionId=<subscription id> Azure__Location=westeurope Azure__ResourceGroup=screenhire-prod
```

Budget alerts at $50, $100 and $200 (ADR-0007): one monthly budget of $200 that mails at 25%, 50% and 100% of actual spend.
`az consumption budget create` has no option for notifications, so use the REST API. Save this as `budget.json`, with the
first day of the current month as the start date and your own address:

```json
{
  "properties": {
    "category": "Cost",
    "amount": 200,
    "timeGrain": "Monthly",
    "timePeriod": { "startDate": "2026-12-01T00:00:00Z", "endDate": "2030-12-01T00:00:00Z" },
    "notifications": {
      "fifty": { "enabled": true, "operator": "GreaterThanOrEqualTo", "threshold": 25, "thresholdType": "Actual", "contactEmails": ["owner@example.com"] },
      "hundred": { "enabled": true, "operator": "GreaterThanOrEqualTo", "threshold": 50, "thresholdType": "Actual", "contactEmails": ["owner@example.com"] },
      "twohundred": { "enabled": true, "operator": "GreaterThanOrEqualTo", "threshold": 100, "thresholdType": "Actual", "contactEmails": ["owner@example.com"] }
    }
  }
}
```

```bash
az rest --method put \
  --url "https://management.azure.com/subscriptions/$Azure__SubscriptionId/providers/Microsoft.Consumption/budgets/screenhire-monthly?api-version=2023-05-01" \
  --body @budget.json
```

Do not commit `budget.json` with a real address.

### 2. Set the parameters

Put every required parameter from the tables above in `apps/apphost/appsettings.Production.json`. Include
`auth-trusted-proxies`. You do not know the ingress addresses yet, so set it to a placeholder network now and correct it in
step 9, then redeploy.

### 3. Provision the infrastructure, and only that

```bash
dotnet aspire do provision-azure-bicep-resources --apphost apps/apphost/ScreenHire.AppHost.csproj
```

This creates the resource group, the registry, the Container Apps environment, the database server, storage and the vault.
It builds and deploys no app. `dotnet aspire deploy --list-steps` shows the step order.

### 4. Let yourself write secrets, and add the optional ones

```bash
VAULT=$(az keyvault list -g $Azure__ResourceGroup --query "[?starts_with(name,'vault')].name | [0]" -o tsv)
az role assignment create --role "Key Vault Secrets Officer" --assignee-object-id <your object id> \
  --assignee-principal-type User --scope $(az keyvault show -n $VAULT --query id -o tsv)
az keyvault secret set --vault-name $VAULT --name ScreenHire--Email--SendGrid--ApiKey --value "<key>"
```

Repeat the last command for each optional provider you want on.

### 5. Create the schema

The database's firewall lets Azure's own addresses in. Let your address in for the length of the step, then close it:

```bash
PG=$(az postgres flexible-server list -g $Azure__ResourceGroup --query "[0].name" -o tsv)
PG_HOST=$(az postgres flexible-server list -g $Azure__ResourceGroup --query "[0].fullyQualifiedDomainName" -o tsv)
MY_IP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create -g $Azure__ResourceGroup -n $PG -r migrate-temp \
  --start-ip-address $MY_IP --end-ip-address $MY_IP

ScreenHire__Migrator__OwnerConnectionString="Host=$PG_HOST;Database=screenhire;Username=screenhire_owner;Password=<owner password>;SSL Mode=Require" \
ScreenHire__Migrator__AppRolePassword="<screenhire-app-password>" \
  dotnet run --project apps/migrator -c Release

az postgres flexible-server firewall-rule delete -g $Azure__ResourceGroup -n $PG -r migrate-temp -y
```

The migrator applies the EF migrations, gives `screenhire_app` its login, and creates Wolverine's tables
(`ScreenHireMessaging.ProvisionStorageAsync`). Every step is idempotent. It ran against a local PostgreSQL 17 while this was
written: 49 migrations, a second run changed nothing. The follow-ups migration takes heavy locks, so on a database with data
run this step in a quiet window.

### 6. Build and deploy the apps

```bash
dotnet aspire deploy --apphost apps/apphost/ScreenHire.AppHost.csproj
```

This builds the API, worker and careers images, pushes them to the registry and creates the three container apps. The API
and worker start knowing only `screenhire_app`.

### 7. Cloudflare and DNS

1. Find the addresses:
   ```bash
   az containerapp show -g $Azure__ResourceGroup -n api --query properties.configuration.ingress.fqdn -o tsv
   az containerapp show -g $Azure__ResourceGroup -n careers --query properties.configuration.ingress.fqdn -o tsv
   ```
2. The careers hosts need the original `Host` header, because the careers server reads the tenant from it. So Cloudflare must
   not rewrite it, and the Container Apps environment must accept the host. Create a Cloudflare Origin CA certificate for
   the careers domain and `*.` of it, then:
   ```bash
   ENV=$(az containerapp env list -g $Azure__ResourceGroup --query "[0].name" -o tsv)
   az containerapp env certificate upload -g $Azure__ResourceGroup -n $ENV -c careers-wildcard -f careers.pfx -p "<password>"
   az containerapp hostname add -g $Azure__ResourceGroup -n careers --hostname "*.example-jobs.org"
   az containerapp hostname bind -g $Azure__ResourceGroup -n careers --hostname "*.example-jobs.org" -e $ENV -c careers-wildcard
   ```
   Repeat for the bare careers domain. Wildcard custom domains with an uploaded certificate are what Container Apps supports; if
   `hostname add` refuses the wildcard, put the certificate on each tenant host as it is created, or front careers with a
   Cloudflare Worker that forwards the host in `X-Forwarded-Host` and trust that header in `careers-allowed-domains`.
3. DNS in Cloudflare, proxied: `api` to the API's address, the careers domain and `*` to the careers address, `reply` per the
   SendGrid step. Set the SSL mode to Full (strict).
4. Pages. Build and publish the two static sites. `SITE_URL` and `PUBLIC_APP_URL` are read at build time:
   ```bash
   SITE_URL=https://www.example.org PUBLIC_APP_URL=https://app.example.org pnpm --filter @screenhire/marketing build
   pnpm dlx wrangler pages deploy apps/marketing/dist --project-name screenhire-marketing

   pnpm --filter @screenhire/dashboard build
   cd apps/dashboard && pnpm dlx wrangler pages deploy build --project-name screenhire-dashboard
   ```
   In the dashboard project's settings set the variable `API_ORIGIN` to the API's own address from step 7.1 (a function
   fetches by URL, so the host is right without a custom domain), and give the project the custom domain
   `app.example.org`. The function forwards `/v1`, the real-time WebSocket and the visitor's address. Nothing could run it, so
   test sign-in and the live updates first (step 10). Pages evaluates `_redirects` and functions in an order this page could
   not confirm; if `/v1` returns the app shell, remove the `_redirects` line and use `_routes.json` alone.

### 8. Providers

Each is optional. A changed parameter reaches the apps through a deploy, so after setting one run step 6 again. A secret in the vault
(step 4) is read when an app starts, so if you changed only secrets, restart the apps:

```bash
for app in api worker; do
  az containerapp revision restart -g $Azure__ResourceGroup -n $app \
    --revision $(az containerapp revision list -g $Azure__ResourceGroup -n $app --query "[0].name" -o tsv)
done
```

- **SendGrid** (ADR-0022). Subscribe in the Azure Marketplace and create an API key. Put it in the vault. Give `email-from` and
  `email-reply-domain`. At the DNS host add an MX record for the reply host pointing at SendGrid's inbound service. In
  SendGrid add an Inbound Parse for that host posting to `https://<api host>/v1/webhooks/email/inbound` with the
  `sendgrid-inbound-secret` as the Basic password (user `sendgrid`). Turn on the signed Event Webhook to
  `https://<api host>/v1/webhooks/email/events` and set `sendgrid-event-webhook-public-key`. Each customer verifies their own
  sending domain from Settings.
- **Google and Microsoft sign-in.** Register apps with the redirect URIs `https://<app host>/v1/auth/signin-google` and
  `/signin-microsoft`. Request Microsoft's `xms_edov` optional claim. Parameters `auth-google-client-id` and
  `auth-microsoft-client-id`, secrets in the vault.
- **Calendars.** A Google Cloud OAuth client with the scopes `calendar.freebusy` and `calendar.events`, redirect
  `https://<app host>/v1/calendar/oauth/callback/google`, and Google's verification review, which takes weeks (until then only
  test users connect). A Microsoft Entra app for any directory and personal accounts with delegated `Calendars.ReadWrite` and
  `offline_access`, redirect `https://<app host>/v1/calendar/oauth/callback/microsoft`. Parameters
  `calendar-google-client-id` and `calendar-microsoft-client-id`, secrets in the vault. Add both to the sub-processor list.
- **Slack.** The app's settings are in [Slack](../architecture/slack.md): redirect `https://<app host>/v1/slack/oauth/callback`.
  Parameter `slack-client-id`, secret in the vault. Add Slack to the sub-processor list.
- **Billing.** The steps are in [Billing](../architecture/billing.md) and the owner actions in [status](../status.md#owner-actions).
  In Stripe, on an account only ScreenHire uses: a product with a recurring monthly price per plan, the customer portal turned
  on, and a webhook endpoint at `https://<api host>/v1/webhooks/stripe` for `checkout.session.completed`,
  `customer.subscription.created`, `customer.subscription.updated` and `customer.subscription.deleted`. Create the endpoint
  through Stripe's API with `api_version=2024-06-20`, the version the code is written against: a webhook otherwise arrives at
  the account's default version, the Dashboard offers only that or the latest, and an endpoint's version cannot be changed
  later. In the vault: the two secrets above, and each plan's price
  id, price and limit as secrets named like the setting (for example `ScreenHire--Billing--Plans--growth--StripePriceId`,
  `ScreenHire--Billing--Plans--growth--MonthlyPrice`, `ScreenHire--Billing--Plans--growth--ActiveJobs`). Leave
  `ScreenHire:Billing:Sandbox:Enabled` off: the sandbox's checkout charges nobody, and an app started outside Development with it on
  stops at start. Add Stripe to the sub-processor list.
- **Turnstile.** `turnstile-site-key` and the vault secret `ScreenHire--Turnstile--SecretKey`. Without them the honeypot still works.
- **Google Indexing.** A Google service account that owns the careers domain in Search Console, `google-indexing-mode` set to
  `Google`, its key JSON in the vault. The real client has never run.
- **The consistency check.** `verification-llm-provider` to `Anthropic`, the two model ids, and the key in the vault. Add the
  provider to the sub-processor list and the DPA first.

### 9. Proxies and client addresses

Behind Cloudflare and the Container Apps ingress the API sees the ingress, not the visitor. Rate limits and the device checks key
on the visitor's address, so set `auth-trusted-proxies` to the networks the API's requests arrive from and `auth-forward-limit`
to the number of trusted hops, then redeploy (step 6 again). Read what arrives:

```bash
az containerapp logs show -g $Azure__ResourceGroup -n api --tail 50
```

Do the same for careers: `careers-allowed-domains` is read when the image builds, so a change needs `dotnet aspire deploy`. Until
both are right every visitor shares one rate-limit bucket. This is the least certain step in this page.

### 10. Check it works

- `https://<api host>/health` answers `Healthy`. `/alive` too.
- Sign in with email or a provider at `https://app.example.org`; the dashboard's live updates connect (the WebSocket).
- A careers board at `https://<tenant>.<careers domain>/`, the apply form, a resume upload (a browser PUT to storage), and the
  applicant arriving in the dashboard with a verification level.
- A webhook endpoint can be created (it returns 503 without the signing key).
- The worker log shows handlers running: `az containerapp logs show -g $Azure__ResourceGroup -n worker --tail 50`.

## Releasing an update

```bash
git pull
# if the model changed: regenerate infra/ and commit it (see above)
# run step 5 again (the migrator)
dotnet aspire deploy --apphost apps/apphost/ScreenHire.AppHost.csproj
```

Run the migrator before the deploy, always. A migration that only adds is safe while the old apps still run.

## Rolling back

Container Apps keeps each revision. The apps run in single-revision mode, so a rollback is a new revision that uses the previous
image. Images are tagged `aspire-deploy-<timestamp>` in the registry:

```bash
ACR=$(az acr list -g $Azure__ResourceGroup --query "[0].name" -o tsv)
az acr repository show-tags --name $ACR --repository api --orderby time_desc --top 5
az containerapp update -g $Azure__ResourceGroup -n api --image $ACR.azurecr.io/api:<previous tag>
```

Do the same for `worker` and `careers`. Migrations only go forward. An app rollback is safe only when the migration since the
good release was additive (a new table or column). If one dropped or changed something, restore instead: a point-in-time
restore makes a new server, which you then point the apps at:

```bash
az postgres flexible-server restore -g $Azure__ResourceGroup -n <new server name> --source-server $PG --restore-time "<UTC time>"
```

That is the last resort. It loses everything written since that time.

## Limits to know

- One API replica and one worker replica, on purpose. More than one API needs Azure SignalR Service, a shared real-time queue
  consumer, and `ScreenHire:ApiKeys:PermitsPerMinute` divided by the count ([realtime](../architecture/realtime.md)).
- Handlers compile when the worker starts (Wolverine's dynamic mode). Measured against a real database on a development machine: ready in
  12 seconds, 198 MB peak, under the 0.5 GiB limit. At 0.25 vCPU expect a slower start. If it ever runs out of memory, give the
  worker 1 GiB before looking at static code generation.
- Container Apps also sets its own ASP.NET Core Data Protection (`autoConfigureDataProtection` in the generated API and worker
  modules). The apps register their own key ring, in Postgres and wrapped by the Key Vault key, and that registration should win, but
  it could not be run here. After the first deploy, check that a questionnaire link the worker emails opens on the API.
- `ScreenHire:DataProtection:AllowUnprotectedKeys=true` lets a Production host start without the Key Vault key. It exists for the
  integration test stack that runs the API as Production. Never set it in a deployment.
- The Azure definition does not map every setting in the owner-action list, on purpose: `ScreenHire:Verification:Network:Ip2AsnFile`
  and `TorExitListFile` need a file mounted into the worker, generic `ScreenHire:Auth:Oidc:*` providers are not parameters (add
  them as environment variables of the API in `AzurePublish.cs` when a customer needs one), and
  `ScreenHire:ApiKeys:PermitsPerMinute` keeps its default, which is right for one API replica.
- The owner login on Azure is not a superuser, unlike the local one, so `FORCE ROW LEVEL SECURITY` binds it. A migration that changes
  rows has to lift it (`/add-migration` says how). One that forgets passes locally and updates no rows here, with no error.
- Do not add a one-day lifecycle rule to the uploads container. An age rule cannot tell a claimed file from an unclaimed one and
  would delete every submitted resume. The worker's daily sweep (`PurgeExpiredApplicantUploads`, 03:47 UTC) removes uploads no
  application claimed, a day after they were made; its storage role is the one the worker already has.
- No malware scanner runs (`NoMalwareScanner`). Wire one behind `IMalwareScanner` before real candidates apply.
- Keep access logs for 30 days or less. Log Analytics retention is a setting on the workspace.
- Cloudflare Pages and the dashboard function, the wildcard host binding, and the proxy settings could not be tested here.

## Commands that were not run

Every `az`, `dotnet aspire do` (provisioning) and `dotnet aspire deploy` command, `azd`, `wrangler`, and the Pages function. Only
`dotnet aspire publish` ran, and `az ... --help` for flags. The migrator ran against a local PostgreSQL 17 container and the
careers image was built and started in Docker.
