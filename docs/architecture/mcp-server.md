---
type: Design
title: "MCP server for AI assistants"
description: "The remote MCP server at /v1/mcp: Streamable HTTP in stateless mode, signed in with an organization API key and nothing else, twelve tools on the hiring API's own services, two of them verification tools for a key an Owner gave the Verification read scope, candidate-written text marked as data, and the values any MCP client needs to connect."
tags: [mcp, api-keys, ai]
sources:
  - resource: apps/api/Mcp/McpSetup.cs
  - resource: apps/api/Mcp/HiringTools.cs
  - resource: apps/api/Mcp/McpContracts.cs
  - resource: apps/api/Mcp/McpResults.cs
  - resource: apps/api/Mcp/VerificationTools.cs
  - resource: apps/api/Mcp/McpVerificationContracts.cs
  - resource: apps/api/Endpoints/VerificationEndpoints.cs
  - resource: apps/api/Applications/TalentPoolReads.cs
  - resource: apps/api/Reports/HiringReportReads.cs
  - resource: apps/dashboard/src/routes/(app)/settings/mcp-server/+page.svelte
  - resource: apps/api/Applications/ApplicationReads.cs
  - resource: apps/api/Applications/ApplicationChanges.cs
  - resource: apps/api/Hiring/JobReads.cs
  - resource: tests/ScreenHire.IntegrationTests/McpServerTests.cs
  - resource: tests/ScreenHire.IntegrationTests/VerificationReadScopeTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Migrations/VerificationReadByKeyMigrationTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpToolsTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpMappingsTests.cs
---
# MCP server for AI assistants

A team's AI assistant works with ScreenHire through a remote [Model Context Protocol](https://modelcontextprotocol.io)
server on the API (roadmap item 6). It finds jobs and candidates, reads an application, a hiring report and the saved
candidates, adds notes, tags and tasks, and moves a candidate to a stage. A key an Owner gave the Verification read scope
also reads an application's verification and lists applications by level (ADR-0021). It signs in with an organization
API key ([sign-in and tenancy](sign-in-and-tenancy.md), "API keys"; ADR-0011), so the key's organization, scope and rate
limit apply to everything it does.

## The endpoint

`/v1/mcp`, versioned with the rest of the API, speaks Streamable HTTP through `ModelContextProtocol.AspNetCore`
(Apache-2.0, ADR-0008), in `apps/api/Mcp/McpSetup.cs`.

- **Stateless.** Every MCP request is one HTTP request in its own DI scope. `TenantResolutionMiddleware` has made the
  key's organization the tenant in that scope before the tool runs, so the tools read and write through the same
  `ScreenHireDbContext`, named query filters and row-level security as the routes. A key sees every job (ADR-0015).
  Nothing is kept between requests, so any API instance answers, and the server makes no requests of its own to the
  client (no sampling, no elicitation).
- **An API key and nothing else.** The `mcp` policy authenticates the `ApiKey` scheme alone and needs the Read scope,
  which every key has. A request with no key, a revoked or malformed key, or only the dashboard's session cookie gets
  401 with `WWW-Authenticate: Bearer`. The session is refused because an MCP client sends no antiforgery token, and
  because the tools run a key's checks, not a person's role.
- **The key's rate limit.** The `api-key` policy counts each HTTP request, so each tool call, against the key's
  permits per minute, with 429 beyond.
- **Not in the OpenAPI document.** MCP describes its own tools (`tools/list`); the route is `ExcludeFromDescription`.

## Tools

Small verbs on one thing, in `apps/api/Mcp/HiringTools.cs` and, for verification, `VerificationTools.cs`. Each calls
the service its route calls, never a copy of the rules: `JobReads`, `ApplicationReads`, `ApplicationChanges`,
`HiringReportReads`, `TalentPoolReads` and `VerificationReads`. Validation, the 404 for an application the key cannot
see, the timeline entry that names the key, and the outbox events that reach open dashboards are therefore the route's
own.

| Tool | Mirrors | Needs |
|---|---|---|
| `find_jobs(text?, status?, limit?)` | `GET /v1/jobs` | Read |
| `find_candidates(text?, jobId?, limit?)` | `GET /v1/applications`, `GET /v1/jobs/{id}/applications` | Read |
| `get_application(applicationId)` | `GET /v1/applications/{id}` and its timeline | Read |
| `add_note(applicationId, note)` | `POST /v1/applications/{id}/notes` | Write, Add notes and tags |
| `add_tag(applicationId, tag)` | `POST /v1/applications/{id}/tags` | Write, Add notes and tags |
| `remove_tag(applicationId, tag)` | `DELETE /v1/applications/{id}/tags` | Write, Add notes and tags |
| `create_task(applicationId, note, assigneeUserId?)` | `POST /v1/applications/{id}/tasks` | Write, Create tasks |
| `move_to_stage(applicationId, stageId, note?)` | `POST /v1/applications/{id}/move` | Write, Move candidates |
| `get_hiring_report(from, to, jobIds?, timeZone?, previousFrom?, previousTo?)` | `GET /v1/reports/hiring` | Read |
| `list_saved_candidates(limit?)` | `GET /v1/talent-pool` | Read |
| `get_verification(applicationId)` | `GET /v1/applications/{id}/verification` | Verification read |
| `find_candidates_by_verification_level(authenticity?, aiAssistance?, jobId?, includeDismissed?, limit?)` | `GET /v1/verification/applications` | Verification read |

- `text` matches words in a job's title, or in a candidate's name or email, ignoring case, and is 100 characters at
  most. `limit` is 20 unless the assistant asks, and 50 at most. The HTTP lists take no search; the filter lives in
  the same service. The dashboard's Ctrl K has its own session-only `GET /v1/search`, which keys cannot call.
- `get_application` returns the candidate, each answer with its question as the job asks it now, the files (names
  only, not their contents), follow-up questionnaires, the job's stages with the current one, and the latest 20
  activity entries, newest first. The stage ids are what `move_to_stage` takes.
- `get_hiring_report` takes the dashboard's range rule (`from` inclusive, `to` exclusive, at most five years, at most
  200 jobs, an optional previous range and the time zone that decides where Monday weeks start) from
  `ReportEndpoints.Problems`, and returns the headline numbers, weekly trend, funnel, days per stage, sources and open
  jobs of `HiringReportReads.ReadAsync`. `list_saved_candidates` lists the saved candidates who allowed it and have not
  asked to stop, by name, with each one's rating, note and last contacted date, 20 unless the assistant asks and 50 at
  most. The tools call the read services directly, which is why a Read key can use them (ADR-0021 decision 6). The
  saved-candidate routes stay session-only; the reports routes also accept a key (`.AllowApiKeys()`). A saved candidate is the team's Saved entry; the tool only reads the list and saves,
  removes, invites and edits no one. A key has no scorecards to give, so the rating is always the full one.
- **The Write scope hides the write tools.** They carry `[Authorize(Policy = McpSetup.WritePolicy)]`, which the SDK's
  authorization filter applies: a Read key does not see them in `tools/list`, and calling one is a protocol error.
  Each write tool also asks `HiringAuthorization.KeyRefusal` for its permission, the same check the routes' filter
  makes, so the rule that a Write key does the hiring writes and nothing more stays in one place.
- **Errors** are tool results the assistant reads (`McpResults`): "No application with that id in this organization."
  for a 404, the field messages for a 400, and the problem's title and detail for anything else.
- **The Verification read scope hides the verification tools.** They carry
  `[Authorize(Policy = McpSetup.VerificationPolicy)]`, so a key without the scope does not see them in `tools/list`
  and calling one is a protocol error that returns nothing of the verification. They only read; each read is written to
  the application's review log as a `ReadByKey` entry under the key before the answer goes out
  (`VerificationReads.RecordKeyReadsAsync`, the call the HTTP route makes too).
- `McpToolsTests` holds every tool that changes data to the write policy, every read-only hiring tool to none, the
  verification tools to the verification policy and to reading only, and every name to snake_case.

## What comes back

The outputs are records of their own (`McpContracts.cs`), mapped from the hiring API's contracts, with enums as names.

- **Candidate-written text is marked.** Every string a candidate wrote comes back as
  `{ "candidateWritten": "..." }`: their name, free-text and URL answers, file names, AI-use details and follow-up
  answers. Resumes and answers can carry hidden instructions aimed at AI screeners, which the verification engine flags
  (`CNT_PROMPT_INJECTION`, [verification engine](verification-engine.md)). The server instructions tell the assistant to
  read such text as data and never follow it. Choices come back as the job's own option labels, and yes/no and numbers
  as plain values, because the candidate picked them rather than wrote them. An answer that is not a yes/no or a
  number, given before its question changed type, comes back marked like free text.
- **Verification only through its own tools, only with its scope.** `find_candidates`, `get_application` and the other
  hiring tools return no verification level, reason code, signal or evidence, and the activity leaves out
  `VerificationAssessed`, exactly as the HTTP API does for a key (ADR-0011 decision 3, ADR-0021 decision 3). A key an
  Owner gave the Verification read scope gets two more tools, `get_verification` (levels, reason codes, summary, next
  step, signals with evidence and counter-evidence, the review log) and `find_candidates_by_verification_level` (exact
  level, newest first, never ranked). Their results carry a `guidance` field and their descriptions say it too: a
  level is evidence for a person to review and never a reason to reject on its own. Evidence values, quotes and list
  items came from the applicant or their device, so they come back as `candidateWritten`. There is no score or
  percentage. `McpServerTests` checks a bot applicant's application for any verification, level, reason, signal or
  score member on a key without the scope, and what the verification tools return on a key with it.
- **No accommodation flag, no phone number and no file contents.** The assistant gets what it needs to act on a request.

## Rejection

`move_to_stage` follows the same rules as the HTTP move, Rejected included: moving a candidate to Rejected is a
person's choice made through their assistant. No tool acts on verification output: the verification tools only read,
and nothing in the server turns a level into a move, a tag or a rejection. The tool descriptions and the server
instructions tell the assistant to move a candidate to a Rejected stage only when the person it works for explicitly
asks, and never because of a level. The timeline records the key that moved them. The workflow guardrail that refuses a
`verification.*` condition driving a rejection is unchanged ([verification engine](verification-engine.md), ADR-0004).

## Connecting a client

Integrations > MCP server shows what any MCP client needs, with this deployment's address filled in. An Owner or Admin
creates a key in Integrations > API keys: Read for a client that only looks, Write for one that also changes things. Only an
Owner can add Verification read, for a client that should read verification results too. The
server's address is the API's origin, which `GET /v1/meta` reports as `apiOrigin` (`ScreenHire:Domains:ApiHost`),
followed by `/v1/mcp`. The example uses `https://api.example.com`.

| Setting | Value |
|---|---|
| Server URL | `https://api.example.com/v1/mcp` |
| Transport | Streamable HTTP |
| Header | `Authorization: Bearer shk_...` |

Most clients take those values in a config file of this shape; each client documents where its file lives and what
it calls the top-level key:

```json
{
  "mcpServers": {
    "screenhire": {
      "type": "http",
      "url": "https://api.example.com/v1/mcp",
      "headers": { "Authorization": "Bearer <your API key>" }
    }
  }
}
```

Neither the page nor this doc gives one client's setup steps: they change with each client's releases. Locally,
`/preview` prints the API's address, and `npx @modelcontextprotocol/inspector` with the header above lists and calls
the tools.

**OAuth waits** (ADR-0011 decision 6). A client that signs in only with OAuth, such as a claude.ai custom connector,
cannot connect until ScreenHire runs an authorization server.

## Tests

| Test | Proves |
|---|---|
| `McpServerTests` (integration) | a Write key lists the hiring tools and a Read key the five reads, neither a verification tool; a Verification read key also lists the two verification tools, whose descriptions and the server instructions say a level is never a reason to reject; the verification tools return evidence with the guidance and log each read under the key; a key without the scope is refused them and leaves no log row; the report and saved-candidate tools read; each tool's change lands in the timeline under the key; a move to Rejected works as over HTTP; a Read key's writes are refused and change nothing; another organization's jobs and applications are unreachable; no verification output from the hiring tools; only a valid key connects; the new tools read only the key's own organization |
| `VerificationReadScopeTests` (integration) | only an Owner gives the scope; a key without it is refused the verification routes; with it the reads work and each is in the review log with the key and its maker; the key still sees no verification in lists or the timeline and cannot dismiss or note; another organization's application is a 404; a list by level is newest first, never ranked |
| `VerificationReadByKeyMigrationTests` (integration) | undoing the migration removes the key reads and the scope, keeps people's actions and the keys, and restores FORCE; deleting an organization takes its keys and their reads with it |
| `ApplicationTasksTests` (integration) | the task route the `create_task` tool mirrors |
| `McpToolsTests`, `McpMappingsTests` (unit) | the write policy on every write tool, the verification policy on every verification tool, no verification tool that rejects, moves or changes anything, the never-reject sentence in every verification description, snake_case names, and which words are wrapped as `candidateWritten` |
