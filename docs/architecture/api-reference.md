---
type: Design
title: "API reference and developer docs"
description: "What integrators read: the public OpenAPI document Scalar shows (only what a key can call, a summary on every operation, tag groups, an introduction and the webhook events), the full document that feeds the generated client, the MCP tools catalogue and its reference page beside Scalar, and the developer docs under /developers on the marketing site, all generated from the code."
tags: [api, openapi, webhooks, mcp, marketing]
sources:
  - resource: apps/api/OpenApi/ApiDocuments.cs
  - resource: apps/api/Auth/SecuritySchemes.cs
  - resource: apps/api/Program.cs
  - resource: src/ScreenHire.Application/Webhooks/WebhookCatalog.cs
  - resource: apps/api/Mcp/McpToolCatalog.cs
  - resource: apps/api/Mcp/McpToolExamples.cs
  - resource: apps/api/Mcp/McpToolsPage.cs
  - resource: packages/api-client/package.json
  - resource: apps/marketing/src/lib/developers.ts
  - resource: apps/marketing/src/components/DeveloperPage.astro
  - resource: apps/marketing/src/pages/developers/index.astro
  - resource: apps/marketing/src/pages/developers/authentication.astro
  - resource: apps/marketing/src/pages/developers/api.astro
  - resource: apps/marketing/src/pages/developers/webhooks.astro
  - resource: apps/marketing/src/pages/developers/mcp.astro
  - resource: apps/marketing/src/developers/samples/verify-signature.mjs
  - resource: apps/marketing/src/developers/samples/verify_signature.py
  - resource: apps/marketing/src/developers/samples/ScreenHireSignature.cs
  - resource: apps/marketing/src/developers/samples/signature-vector.json
  - resource: tests/ScreenHire.UnitTests/Tooling/PublicApiDocumentTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Production/ApiReferenceTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpToolCatalogTests.cs
  - resource: tests/ScreenHire.UnitTests/Webhooks/SignatureSampleTests.cs
  - resource: apps/marketing/src/lib/developers.test.ts
---
# API reference and developer docs

The owner's fourth walk-through (2026-11-21) found the API reference unreadable: `/scalar` showed every route the
dashboard uses, 331 operations with no names. Integrators now read three things, each generated from the code so none
can describe a route, an event or a tool the API lacks.

## Two OpenAPI documents

`apps/api/OpenApi/ApiDocuments.cs` registers both, with the same schema and security transformers and
`UseUnixLineEndings` last. The API build writes both into `packages/api-client` (`OpenApiGenerateDocuments`), and
`pnpm verify:contract` fails when either differs from the code.

| Document | Served at | File | Holds | Read by |
|---|---|---|---|---|
| `v1` | `/openapi/v1.json` | `openapi.json` | Every route | Orval, which generates `packages/api-client/src/generated` |
| `public` | `/openapi/public.json` | `openapi_public.json` | Only what an organization API key can call | Scalar at `/scalar`, the developer docs, integrators' generators |

- **What is public.** `ShouldInclude` keeps an operation whose `IAuthorizeData.Policy` is `hiring-api` or
  `verification-api` (`SecuritySchemes.AcceptsApiKey`, the check that also adds the `apiKey` requirement). The public
  document declares the `apiKey` scheme only. Opening a route to keys is an authorization change of its own (ADR-0011),
  never a documentation one.
- **Summaries.** Every public operation has `.WithSummary(...)` beside its `.WithName(...)`: a short sentence-case
  phrase with no full stop, such as "Move an application to a stage". `PublicApiDocumentTests` reads the committed file
  and fails on an operation without one, and on one the full document does not mark as taking a key.
  `ApiReferenceTests` (integration, the API run in Production) checks that the served `/openapi/public.json` has the
  committed file's paths and webhooks, and that `/scalar` shows it.
- **Tags and groups.** Tags are set at the source with `.WithTags(...)`, one per route group: scorecard routes are
  `Scorecards`, and hiring templates, the scorecard library and the form library are `Templates`. The public document
  adds `x-tagGroups` from `ApiDocuments.TagGroups` (Hiring: Jobs, Applications, Candidates, Interviews, Scorecards, Tasks,
  Reports; Verification; Organization: Team, Templates, Tags; Webhooks), keeping only the tags an operation uses, and a
  description for each tag. Interviews, Tasks and Team have no key routes today, so they are not shown; an assistant
  reaches them through MCP tools. Scalar shows only grouped tags, so the test holds every used tag to one group.
- **Introduction.** `info.title` is "ScreenHire API" and `info.description` the introduction: authentication (the
  bearer key, the three scopes, OAuth only for MCP), errors (problem details, 400 with `errors`, 401, 403, 404 for another
  organization's row, 409), paging (lists are not paged yet), rate limits (600 a minute unless the key sets fewer, fixed
  windows, 429) and versioning (`/v1`, additive changes only). Each sentence is checked against the code it describes.
- **Webhooks.** The public document's `webhooks` section has one POST per event from `WebhookCatalog`: its summary, its
  description with the delivery note, the five headers, the body as an envelope whose `data` references the event's
  record as a component, and the catalogue's sample as the example
  ([workflow engine](workflow-engine.md#webhook-subscriptions)).
- **Scalar** (`Program.cs`) shows the public document, titled "ScreenHire API", with this deployment's API origin
  (`DomainOptions.GetApiOrigin`) as its server, so the committed file holds no host. Its telemetry, fonts, agent and
  MCP features stay off.

## The MCP tools catalogue

`McpToolCatalog.Describe` lists every tool of `McpToolCatalog.ToolTypes` ([MCP server](mcp-server.md)): its name, title
and description, its input schema built through the SDK's own `McpServerTool.Create`, so it is exactly what
`tools/list` returns, the access its `[Authorize]` policy needs, whether the person confirms it, its hints, and an example
call with a shortened result from `McpToolExamples`. It goes three places:

- `packages/api-client/mcp-tools.json`, written by
  `UPDATE_MCP_TOOLS=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*McpToolCatalogTests"`. Without the
  variable the test fails when a tool changed, when a tool has no example, or when an example leaves out a required input
  or passes one the tool does not take. The dashboard's MCP server page and the developer docs read the file.
- `/mcp-tools` on the API, beside `/scalar`: a page rendered on the server (`McpToolsPage`), one section per tool
  anchored by its name, every value HTML-encoded, nothing loaded from anywhere. Public like `/scalar`: it documents the
  server and holds no data.
- The dashboard's Integrations > MCP server page, whose tool names link to `/mcp-tools#<name>`.

## Developer docs on the marketing site

Five static pages under `/developers` (`apps/marketing/src/pages/developers`), in the page registry with their own
"Developers" section in the site index, so they get Open Graph images, a sitemap entry and an llms.txt section like
every page. They ship no JavaScript.

| Page | Built from |
|---|---|
| `/developers/` (getting started) | Counts from the public document, the webhooks section and the catalogue; a first call |
| `/developers/authentication/` | Keys, OAuth for assistants, the scopes, the rate limit, as the introduction says them |
| `/developers/api/` | Every public operation by tag group with its summary, and links to `/scalar` and `/openapi/public.json` |
| `/developers/webhooks/` | Every event with its sample, the headers, signature checking in Node, Python and C#, retries and replays |
| `/developers/mcp/` | Connecting, what the assistant asks first and never does, and the tools by access, each linked to `/mcp-tools` |

- **Data.** `src/lib/developers.ts` reads `@screenhire/api-client/openapi-public.json` and `@screenhire/api-client/mcp-tools.json`
  at build time. Its Vitest checks every operation, event and tool reaches a page.
- **Hosts.** The pages show the API at `PUBLIC_API_URL`, an `astro:env` field set at build time from the `domains-api`
  host ([deploy](../operations/deploy.md)), and `https://api.example.com` when it is unset. The AppHost passes the local
  API's address in `pnpm dev`. No host is written into a page (ADR-0003).
- **Signature samples.** `src/developers/samples` holds the three samples the webhooks page shows with `?raw`. The Node
  sample is run by `developers.test.ts` and the C# sample is compiled into `ScreenHire.UnitTests` and run by
  `SignatureSampleTests`, both against one vector that `WebhookSignature.Create`, the API's own signer, produces. The
  vector is `signature-vector.json` beside the samples, which the webhooks page also reads for the header it shows. Its
  secret is `shws_fictional_test_vector`: a vector never looks like a real secret, or secret scanning reports it. The
  Python sample follows the same steps and is not run.
- CI's report-only Lighthouse job runs for a change under `packages/api-client` too, since the pages are built from it.
