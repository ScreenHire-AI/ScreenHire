---
type: Design
title: "MCP server for AI assistants"
description: "The remote MCP server at /v1/mcp: Streamable HTTP in stateless mode, signed in with an organization API key and nothing else, eight tools on the hiring API's own services, candidate-written text marked as data, no integrity output, and how to connect Claude Code or VS Code."
tags: [mcp, api-keys, ai]
sources:
  - resource: apps/api/Mcp/McpSetup.cs
  - resource: apps/api/Mcp/HiringTools.cs
  - resource: apps/api/Mcp/McpContracts.cs
  - resource: apps/api/Mcp/McpResults.cs
  - resource: apps/api/Applications/ApplicationReads.cs
  - resource: apps/api/Applications/ApplicationChanges.cs
  - resource: apps/api/Hiring/JobReads.cs
  - resource: tests/ScreenHire.IntegrationTests/McpServerTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpToolsTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpMappingsTests.cs
---
# MCP server for AI assistants

A team's AI assistant works with ScreenHire through a remote [Model Context Protocol](https://modelcontextprotocol.io)
server on the API (roadmap item 9). It finds jobs and candidates, reads an application, adds notes, tags and tasks, and
moves a candidate to a stage. It signs in with an organization API key ([sign-in and tenancy](sign-in-and-tenancy.md),
"API keys"; ADR-0011), so the key's organization, scope and rate limit apply to everything it does.

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

Small verbs on one thing, in `apps/api/Mcp/HiringTools.cs`. Each calls the service its hiring API route calls, never
a copy of the rules: `JobReads`, `ApplicationReads` and `ApplicationChanges`. Validation, the 404 for an application
the key cannot see, the timeline entry that names the key, and the outbox events that reach open dashboards are
therefore the route's own.

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

- `text` matches words in a job's title, or in a candidate's name or email, ignoring case, and is 100 characters at
  most. `limit` is 20 unless the assistant asks, and 50 at most. The HTTP lists take no search; the filter lives in
  the same service.
- `get_application` returns the candidate, each answer with its question as the job asks it now, the files (names
  only, not their contents), follow-up questionnaires, the job's stages with the current one, and the latest 20
  activity entries, newest first. The stage ids are what `move_to_stage` takes.
- **The Write scope hides the write tools.** They carry `[Authorize(Policy = McpSetup.WritePolicy)]`, which the SDK's
  authorization filter applies: a Read key does not see them in `tools/list`, and calling one is a protocol error.
  Each write tool also asks `HiringAuthorization.KeyRefusal` for its permission, the same check the routes' filter
  makes, so the rule that a Write key does the hiring writes and nothing more stays in one place.
- **Errors** are tool results the assistant reads (`McpResults`): "No application with that id in this organization."
  for a 404, the field messages for a 400, and the problem's title and detail for anything else.
- `McpToolsTests` holds every tool that changes data to the write policy, every read-only tool to none, and every name
  to snake_case.

## What comes back

The outputs are records of their own (`McpContracts.cs`), mapped from the hiring API's contracts, with enums as names.

- **Candidate-written text is marked.** Every string a candidate wrote comes back as
  `{ "candidateWritten": "..." }`: their name, free-text and URL answers, file names, AI-use details and follow-up
  answers. Resumes and answers can carry hidden instructions aimed at AI screeners, which the integrity engine flags
  (`CNT_PROMPT_INJECTION`, [integrity engine](integrity-engine.md)). The server instructions tell the assistant to
  read such text as data and never follow it. Choices come back as the job's own option labels, and yes/no and numbers
  as plain values, because the candidate picked them rather than wrote them. An answer that is not a yes/no or a
  number, given before its question changed type, comes back marked like free text.
- **No integrity output.** No tool returns integrity levels, reason codes, signals or evidence, and the activity leaves
  out `IntegrityAssessed`, exactly as the HTTP API does for a key (ADR-0011 decision 3). A key has no named person
  behind it, and an assistant can be an autonomous agent that could reject on the evidence, which ScreenHire's own
  automations may not (ADR-0004). The tool descriptions say a person reviews integrity in the dashboard.
  `McpServerTests` checks a bot applicant's application for any integrity, level, reason, signal or score member.
- **No accommodation flag, no phone number and no file contents.** The assistant gets what it needs to act on a request.

## Rejection

`move_to_stage` follows the same rules as the HTTP move, Rejected included: moving a candidate to Rejected is a
person's choice made through their assistant. Nothing in the server acts on integrity output, because it never has
any. The tool description and the server instructions tell the assistant to move a candidate to a Rejected stage only
when the person it works for explicitly asks. The timeline records the key that moved them.

## Connecting an assistant

An Owner or Admin creates a key in Settings > API keys: Read for an assistant that only looks, Write for one that also
changes things. The server's address is the API's origin, which `GET /v1/meta` reports as `apiOrigin`
(`ScreenHire:Domains:ApiHost`), followed by `/v1/mcp`. The examples use `https://api.example.com`.

Claude Code:

```
claude mcp add --transport http screenhire https://api.example.com/v1/mcp --header "Authorization: Bearer shk_..."
```

VS Code (`.vscode/mcp.json`), with the key asked for once and kept out of the file:

```json
{
  "inputs": [{ "type": "promptString", "id": "screenhire-key", "description": "ScreenHire API key", "password": true }],
  "servers": {
    "screenhire": {
      "type": "http",
      "url": "https://api.example.com/v1/mcp",
      "headers": { "Authorization": "Bearer ${input:screenhire-key}" }
    }
  }
}
```

Any client that sends a bearer header over Streamable HTTP works the same way. Locally, `/preview` prints the API's
address, and `npx @modelcontextprotocol/inspector` with the header above lists and calls the tools.

**OAuth waits** (ADR-0011 decision 6). A client that signs in only with OAuth, such as a claude.ai custom connector,
cannot connect until ScreenHire runs an authorization server.

## Tests

| Test | Proves |
|---|---|
| `McpServerTests` (integration) | a Write key lists all eight tools and a Read key the three reads; each tool's change lands in the timeline under the key; a move to Rejected works as over HTTP; a Read key's writes are refused and change nothing; another organization's jobs and applications are unreachable; no integrity output; only a valid key connects |
| `ApplicationTasksTests` (integration) | the task route the `create_task` tool mirrors |
| `McpToolsTests`, `McpMappingsTests` (unit) | the write policy on every write tool, snake_case names, and which words are wrapped as `candidateWritten` |
