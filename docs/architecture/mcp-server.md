---
type: Design
title: "MCP server for AI assistants"
description: "The remote MCP server at /v1/mcp: Streamable HTTP in stateless mode, signed in with an organization API key or an OAuth connection a person approved and nothing else, 36 tools on the hiring API's own handlers for jobs, the pipeline, interviews, scorecards, email, tasks, templates and views, the person's yes before an email, an interview invitation, publishing or closing a job or a rejection, two verification tools for a key an Owner gave the Verification read scope, candidate-written text marked as data, the tools reference, and the values any MCP client needs to connect."
tags: [mcp, api-keys, ai]
sources:
  - resource: apps/api/Mcp/McpSetup.cs
  - resource: apps/api/Mcp/HiringTools.cs
  - resource: apps/api/Mcp/McpContracts.cs
  - resource: apps/api/Mcp/McpResults.cs
  - resource: apps/api/Mcp/VerificationTools.cs
  - resource: apps/api/Mcp/McpVerificationContracts.cs
  - resource: apps/api/Mcp/JobTools.cs
  - resource: apps/api/Mcp/SchedulingTools.cs
  - resource: apps/api/Mcp/ScorecardTools.cs
  - resource: apps/api/Mcp/EmailTools.cs
  - resource: apps/api/Mcp/TaskTools.cs
  - resource: apps/api/Mcp/TemplateTools.cs
  - resource: apps/api/Mcp/TeamTools.cs
  - resource: apps/api/Mcp/McpToolContracts.cs
  - resource: apps/api/Mcp/McpConfirmation.cs
  - resource: apps/api/Mcp/McpToolCatalog.cs
  - resource: apps/api/Mcp/McpToolExamples.cs
  - resource: apps/api/Mcp/McpToolsPage.cs
  - resource: apps/dashboard/src/lib/mcp-tools.ts
  - resource: apps/api/Endpoints/VerificationEndpoints.cs
  - resource: apps/api/Applications/TalentPoolReads.cs
  - resource: apps/api/Reports/HiringReportReads.cs
  - resource: apps/dashboard/src/routes/(app)/settings/mcp-server/+page.svelte
  - resource: apps/api/Applications/ApplicationReads.cs
  - resource: apps/api/Hiring/JobPublishCheckReads.cs
  - resource: apps/api/Applications/ApplicationChanges.cs
  - resource: apps/api/Hiring/JobReads.cs
  - resource: tests/ScreenHire.IntegrationTests/McpServerTests.cs
  - resource: tests/ScreenHire.IntegrationTests/VerificationReadScopeTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Migrations/VerificationReadByKeyMigrationTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpToolsTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpMappingsTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpToolCatalogTests.cs
  - resource: tests/ScreenHire.UnitTests/Mcp/McpConfirmationStateTests.cs
  - resource: tests/ScreenHire.IntegrationTests/McpConfirmationTests.cs
  - resource: tests/ScreenHire.IntegrationTests/McpJobToolsTests.cs
  - resource: tests/ScreenHire.IntegrationTests/McpWorkToolsTests.cs
---
# MCP server for AI assistants

A team's AI assistant works with ScreenHire through a remote [Model Context Protocol](https://modelcontextprotocol.io)
server on the API (roadmap item 6). It finds, creates, edits, publishes and closes jobs and sets their questions and
stages; reads an application, a job's pipeline, scorecards, a hiring report, the saved candidates and the team's shared
views; adds notes, tags and tasks, completes tasks and asks a teammate for a scorecard; moves a candidate to a stage;
drafts, sends and reads email; and invites a candidate to schedule an interview. A key an Owner gave the Verification read scope
also reads an application's verification and lists applications by level (ADR-0021). It signs in with an organization
API key ([sign-in and tenancy](sign-in-and-tenancy.md), "API keys"; ADR-0011), or, for a client that signs in only with
OAuth, with an access token for a connection a person approved ([OAuth sign-in](oauth-server.md); ADR-0028). Either way
the key's organization, scope and rate limit apply to everything it does: a connection is a key row.

## The endpoint

`/v1/mcp`, versioned with the rest of the API, speaks Streamable HTTP through `ModelContextProtocol.AspNetCore`
(Apache-2.0, ADR-0008), in `apps/api/Mcp/McpSetup.cs`.

- **Stateless.** Every MCP request is one HTTP request in its own DI scope. `TenantResolutionMiddleware` has made the
  key's organization the tenant in that scope before the tool runs, so the tools read and write through the same
  `ScreenHireDbContext`, named query filters and row-level security as the routes. A key sees every job (ADR-0015).
  Nothing is kept between requests, so any API instance answers, and the server makes no requests of its own to the
  client (no sampling, no `elicitation/create`). It asks the person to confirm through a multi round-trip request
  instead, which needs no session ("Confirmation" below).
- **An API key or an OAuth connection, and nothing else.** The `mcp` policy authenticates the `McpCredentials` scheme,
  which hands an `sho_` access token to the OAuth handler and anything else to the `ApiKey` handler, and needs the Read
  scope, which every key and connection has. A request with no credential, a revoked or malformed key, an expired or
  revoked token, or only the dashboard's session cookie gets 401 with
  `WWW-Authenticate: Bearer resource_metadata="..."`, which is how an OAuth client finds the authorization server. The
  session is refused because an MCP client sends no antiforgery token, and because the tools run a key's checks, not a
  person's role.
- **The key's rate limit.** The `api-key` policy counts each HTTP request, so each tool call, against the key's
  permits per minute, with 429 beyond.
- **Not in the OpenAPI document.** MCP describes its own tools (`tools/list`); the route is `ExcludeFromDescription`.

## Tools

Small verbs on one thing, one class per area in `apps/api/Mcp`, all registered from `McpToolCatalog.ToolTypes`. Each
calls the code its route calls, never a copy of the rules: a read service (`JobReads`, `ApplicationReads`,
`HiringReportReads`, `TalentPoolReads`, `JobPublishCheckReads`, `VerificationReads`), a change service
(`ApplicationChanges`), or the route's own handler, made `internal` for it (`JobEndpoints.CreateJob`, `UpdateJob`,
`UpdateJobWorkflow`, `PublishJob`, `CloseJob`, `ApplyTemplateUpdate`; `EmailEndpoints.PreviewApplicationEmail`,
`SendEmails`, `ListCandidateEmails`; `InterviewEndpoints.ScheduleInterview`; `TaskEndpoints.ListTasks`, `CompleteTask`;
`ScorecardEndpoints.ListApplicationScorecards`; `TemplateEndpoints.ListTemplates`). Validation, the 404 for something the
key cannot see, the timeline entry that names the key and the outbox events that reach open dashboards are therefore the
route's own. Routes that were session-only stay so over HTTP; their handlers take a key as the actor for the tools: an
email a key sends, an interview it offers and a task it completes record the key (`ApplicationEventData.ApiKeyId`,
`ApplicationTask.CompletedByApiKeyId`).

| Tool | Mirrors | Needs |
|---|---|---|
| `find_jobs(text?, status?, limit?)` | `GET /v1/jobs` | Read |
| `check_job(jobId)` | `GET /v1/jobs/{id}/publish-check` | Read |
| `get_job(jobId)` | `GET /v1/jobs/{id}`: posting, stages, questions, `workflowVersion` | Read |
| `get_pipeline(jobId, perStage?)` | `GET /v1/jobs/{id}/applications` by stage, with counts | Read |
| `create_job(title, templateVersionId?, ...)` | `POST /v1/jobs` (a draft) | Write, Edit jobs |
| `edit_job(jobId, ...)` | `PUT /v1/jobs/{id}`, changing only what is passed | Write, Edit jobs |
| `set_job_questions(jobId, questions, expectedWorkflowVersion)` | `PUT /v1/jobs/{id}/workflow`, the questionnaire replaced | Write, Edit jobs |
| `set_job_pipeline(jobId, stages, expectedWorkflowVersion)` | `PUT /v1/jobs/{id}/workflow`, the stages replaced | Write, Edit jobs |
| `publish_job(jobId, confirm)` | `POST /v1/jobs/{id}/publish` | Write, Edit jobs, confirmed |
| `close_job(jobId, confirm)` | `POST /v1/jobs/{id}/close` | Write, Edit jobs, confirmed |
| `apply_template_update(jobId, expectedWorkflowVersion)` | `POST /v1/jobs/{id}/workflow/apply-template-update` with the newest version | Write, Edit jobs |
| `list_hiring_templates()` | `GET /v1/templates`, archived ones left out | Read |
| `find_candidates(text?, jobId?, limit?)` | `GET /v1/applications`, `GET /v1/jobs/{id}/applications` | Read |
| `get_application(applicationId)` | `GET /v1/applications/{id}` and its timeline | Read |
| `add_note(applicationId, note)` | `POST /v1/applications/{id}/notes` | Write, Add notes and tags |
| `add_tag(applicationId, tag)` | `POST /v1/applications/{id}/tags` | Write, Add notes and tags |
| `remove_tag(applicationId, tag)` | `DELETE /v1/applications/{id}/tags` | Write, Add notes and tags |
| `create_task(applicationId, note, assigneeUserId?)` | `POST /v1/applications/{id}/tasks` | Write, Create tasks |
| `move_to_stage(applicationId, stageId, note?, confirm?)` | `POST /v1/applications/{id}/move` | Write, Move candidates; confirmed for a Rejected stage |
| `list_tasks(status?, assigneeUserId?, unassignedOnly?, applicationId?, limit?)` | `GET /v1/tasks?scope=Everyone` (a session route) | Read |
| `complete_task(taskId)` | `POST /v1/tasks/{id}/complete` (a session route) | Write, Complete tasks |
| `get_scorecards(applicationId)` | `GET /v1/applications/{id}/scorecards` | Read |
| `request_scorecard(applicationId, reviewerUserId, scorecardTemplateId, note?)` | `POST /v1/applications/{id}/scorecard-requests` | Write, Create tasks |
| `invite_to_schedule(applicationId, timeZone, confirm, option?, interviewerUserIds?, durationMinutes?, windowDays?)` | `POST /v1/applications/{id}/interviews` (a session route), filled from the stage's defaults | Write, Email candidates, confirmed |
| `list_upcoming_interviews(jobId?, withinDays?, limit?)` | Booked interviews from now, soonest first | Read |
| `list_email_templates()` | `GET /v1/email-templates` | Read |
| `draft_email(applicationId, templateId?, subject?, body?)` | `POST /v1/emails/preview` (a session route) | Read |
| `send_email(applicationId, confirm, templateId?, subject?, body?)` | `POST /v1/emails` for one application (a session route) | Write, Email candidates, confirmed |
| `read_email_thread(applicationId, limit?)` | `GET /v1/candidates/{id}/emails` with each email's text (a session route) | Read |
| `list_views()`, `apply_view(viewId, limit?)` | The team's shared views (`/v1/views`), applied on the server | Read |
| `list_team_members()` | `HiringTeams.MembersAsync`: user ids, names and roles, no email addresses | Read |
| `get_hiring_report(from, to, jobIds?, timeZone?, previousFrom?, previousTo?)` | `GET /v1/reports/hiring` | Read |
| `list_saved_candidates(limit?)` | `GET /v1/talent-pool` | Read |
| `get_verification(applicationId)` | `GET /v1/applications/{id}/verification` | Verification read |
| `find_candidates_by_verification_level(authenticity?, aiAssistance?, jobId?, includeDismissed?, limit?)` | `GET /v1/verification/applications` | Verification read |

- `invite_to_schedule` builds the request the Schedule interview sheet opens with from the current stage's defaults
  (`StageScheduling`): how the candidate picks, the interviewers (a named team as it is now, keeping the people who see
  the job, at most five), the length, the days ahead (14 when the stage names none), and the stage's hours and blocks
  when the stage's own way of picking is used. Any of them can be passed instead. "We'll suggest a time" needs a person
  to choose the times, so it is refused with the reason; so is a stage with no length or no interviewers who see the job.
- `apply_view` applies a shared view's jobs, stages (names on a candidates view, ids on a job's) and tag. A view that
  filters on verification (an authenticity or AI assistance filter, or the To review and To verify base views) is
  refused, naming `find_candidates_by_verification_level`. A view that narrows another built-in view is refused too,
  because only the dashboard computes those (`apps/dashboard/src/lib/saved-views.ts`). A view is never applied in part.
- `set_job_questions` and `set_job_pipeline` replace only their part of the saved workflow and keep its automations, so
  no tool writes an automation, and the save goes through `DefinitionValidator` like any workflow edit.
- `read_email_thread` returns the candidate's replies, their subjects and attachment names as `candidateWritten`, and
  never a file's contents. `list_tasks`, `get_pipeline`, `apply_view` and `list_upcoming_interviews` mark candidate names
  the same way.
- `text` matches words in a job's title, or in a candidate's name or email, ignoring case, and is 100 characters at
  most. `limit` is 20 unless the assistant asks, and 50 at most. The HTTP lists take no search; the filter lives in
  the same service. The dashboard's Ctrl K has its own session-only `GET /v1/search`, which keys cannot call.
- `get_application` returns the candidate, each answer with its question as the job asks it now, the files (names
  only, not their contents), follow-up questionnaires (an equality monitoring form's with no answers, which only count
  toward totals in Reports), the job's stages with the current one, and the latest 20 activity entries, newest first. The stage ids are what `move_to_stage` takes.
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

## Confirmation

Five actions reach people outside the team or end a candidacy, so the person the assistant works for says yes to each
one first: `send_email`, `invite_to_schedule` (it emails the candidate), `publish_job` (the careers site and job boards),
`close_job`, and `move_to_stage` to a stage of kind Rejected. `McpConfirmation` does it in one place.

- **A client that can ask.** The server is stateless, so it cannot send `elicitation/create` mid-call (the SDK refuses
  it there). A client on the 2026-07-28 protocol revision that declares form elicitation gets a multi round-trip request
  instead (MRTR, SEP-2322; ModelContextProtocol 2.3.0's `InputRequiredException` with `InputRequest.ForElicitation`): the
  call returns an input-required result with one yes/no question whose message says exactly what will happen ("email the
  candidate for "Platform Engineer" with the subject ..."). The client asks the person and calls again with the answer.
  A no is a tool error, "The person did not agree ..., so nothing changed."
- **Any other client.** The tool takes `confirm`, required on the four that always ask and optional on `move_to_stage`.
  Without `confirm: true` the call is a tool error that says what would happen and tells the assistant to ask the person
  and only then call again with the same arguments. The descriptions and the server instructions say the same, and never
  to pass it on the assistant's own judgement. A client that can ask is asked even when `confirm` is true.
- **The echoed state is untrusted.** The client sends `requestState` back unchanged, so it is protected with ASP.NET Core
  data protection (purpose `ScreenHire.Mcp.Confirmation`) and holds the tool, a SHA-256 digest of the arguments other than
  `confirm`, the key and an expiry ten minutes on. A retry with other arguments, from another key, after the expiry, or
  with state the server did not write is refused, so a yes to one email never sends another. The tool runs all its
  checks again on the retry.
- **Hints.** All five carry `Destructive = true`, so a client that asks before destructive tools (claude.ai connectors
  do) asks too; `send_email`, `invite_to_schedule`, `publish_job` and `close_job` are `OpenWorld` and carry
  `_meta["anthropic/requiresUserInteraction"] = true`, which Claude Code honours by prompting on every call.
  `McpToolsTests` holds the five to the destructive hint, the Write policy and the word `confirm: true` in their
  descriptions.

**What clients do, as of 2026-11-22** (sources in the pull request that added this): Claude Code supports form
elicitation, and on its v2 runtime the 2026-07-28 revision, so it shows the question. claude.ai connectors and Claude
Desktop declare no elicitation; they prompt before destructive tools, and the assistant asks in the chat before passing
`confirm`. VS Code (Copilot) supports elicitation during a turn, its MRTR support is unverified. Cursor supports
elicitation but speaks only the 2025-11-25 revision and earlier, so it gets the `confirm` path. ChatGPT's developer mode
asks before tools without `readOnlyHint` and does not elicit. The `confirm` path is therefore the one most clients take
today, and the hints and the server's refusal are what hold it to the person's word.

## Rejection

`move_to_stage` follows the same rules as the HTTP move, Rejected included: moving a candidate to Rejected is a
person's choice made through their assistant, and the person confirms it ("Confirmation"). No tool acts on verification
output: the verification tools only read, `apply_view` refuses a view that filters on verification, and nothing in the
server turns a level into a move, a tag, an email or a rejection. The tool descriptions and the server instructions tell
the assistant to move a candidate to a Rejected stage only when the person it works for explicitly asks, and never
because of a level. The timeline records the key that moved them. The workflow guardrail that refuses a
`verification.*` condition driving a rejection is unchanged ([verification engine](verification-engine.md), ADR-0004),
and no tool writes an automation.

## Connecting a client

Integrations > MCP server shows what any MCP client needs, with this deployment's address filled in. An Owner or Admin
creates a key in Integrations > API keys: Read for a client that only looks, Write for one that also changes things. Only an
Owner can add Verification read, for a client that should read verification results too. The
server's address is the API's origin, which `GET /v1/meta` reports as `apiOrigin` (`ScreenHire:Domains:ApiHost`),
followed by `/v1/mcp`. The example uses `https://api.example.com`.

The page lists the tools by the access that shows them (Read, Write, Verification read), from the tools catalogue
(`apps/dashboard/src/lib/mcp-tools.ts` reads `packages/api-client/mcp-tools.json`), and each chip links to its entry in
the tools reference the API serves at `/mcp-tools`, beside `/scalar`. The reference gives each tool's description, its
inputs with types and which are required, the access it needs, whether the person confirms it, and an example call with
a shortened result. It, the committed file and the developer docs' MCP page all come from `McpToolCatalog.Describe`,
which builds each tool's definition through the SDK, so its inputs are exactly what `tools/list` returns
([API reference](api-reference.md)).

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

**A client that signs in only with OAuth**, such as a claude.ai custom connector, needs only the server URL. It finds the
authorization server from the 401, registers itself, and sends the person to a consent page in the dashboard, where an
Owner or Admin chooses what the assistant may do (Read always; Write and Verification read if they hold them
themselves). The connection then shows under Integrations > MCP server, where it can be disconnected at once. Until an assistant is connected, that page opens with a three-step How it works panel (copy the server URL, add a custom connector, sign in), because the work starts in another tool. The flow, the
tokens and the limits are in [OAuth sign-in](oauth-server.md) (ADR-0028, which closes ADR-0011 decision 6). It needs the
API on a public HTTPS host; locally the integration tests and the MCP client exercise it.

## Tests

| Test | Proves |
|---|---|
| `McpServerTests` (integration) | a Write key lists the hiring tools and a Read key the reads, neither a verification tool; a Verification read key also lists the two verification tools, whose descriptions and the server instructions say a level is never a reason to reject; the verification tools return evidence with the guidance and log each read under the key; a key without the scope is refused them and leaves no log row; the report and saved-candidate tools read; each tool's change lands in the timeline under the key; a confirmed move to Rejected works as over HTTP; a Read key's writes are refused and change nothing; another organization's jobs and applications are unreachable; no verification output from the hiring tools; only a valid key connects; the new tools read only the key's own organization |
| `VerificationReadScopeTests` (integration) | only an Owner gives the scope; a key without it is refused the verification routes; with it the reads work and each is in the review log with the key and its maker; the key still sees no verification in lists or the timeline and cannot dismiss or note; another organization's application is a 404; a list by level is newest first, never ranked |
| `VerificationReadByKeyMigrationTests` (integration) | undoing the migration removes the key reads and the scope, keeps people's actions and the keys, and restores FORCE; deleting an organization takes its keys and their reads with it |
| `McpConfirmationTests` (integration) | `send_email` without a yes is refused and sends nothing; with `confirm: true` it sends under the key's name; a client with an elicitation handler is asked once, in words naming the job and the subject, and sends on yes; a no is refused even with `confirm: true`; a state the server did not write is refused with an accepting answer; a move to Rejected needs the yes and any other move does not; `close_job` needs the yes |
| `McpJobToolsTests` (integration) | `create_job` from a template and `get_job`'s stages and questions; `edit_job` changes only what it is given; `set_job_questions` adds a question and a stale `workflowVersion` is refused; `set_job_pipeline` adds a stage; `apply_template_update` replaces the job's own edits with its template and refuses a stale version or a job with no template; `publish_job` opens the job only with the yes; `get_pipeline` counts an application in its stage with the name marked as the candidate's; another organization's job is not found; a Read key is not offered the write tools |
| `McpWorkToolsTests` (integration) | `list_team_members` carries no email address; `request_scorecard` and `get_scorecards`; `complete_task` records the key (`completed_by_api_key_id`); `draft_email` sends nothing and a confirmed email is in `read_email_thread` as sent; a shared view with a tag applies and one that filters on verification is listed but refused; `invite_to_schedule` asks, offers and names the key, and with a stage's defaults uses its length and interviewers, listed by `list_upcoming_interviews` once booked |
| `ApplicationTasksTests` (integration) | the task route the `create_task` tool mirrors |
| `McpToolsTests`, `McpMappingsTests` (unit) | every registered tool class served; the write policy on every write tool, the verification policy on every verification tool, exactly the five consequential tools taking `confirm` with the destructive hint (and the user-interaction meta on the four that always ask), the view refusal, no verification tool that rejects, moves or changes anything, the never-reject sentence in every verification description, snake_case names, and which words are wrapped as `candidateWritten` |
| `McpConfirmationStateTests` (unit) | the echoed state holds only for its tool, arguments (other than `confirm`, in any order), key and ten minutes, and a forged one never; the server registers every tool the catalogue lists |
| `McpToolCatalogTests` (unit) | `mcp-tools.json` matches the tools; every tool has an example that passes only inputs it takes and every required one; access and confirmation per tool; the reference page anchors every tool and encodes what it shows |
