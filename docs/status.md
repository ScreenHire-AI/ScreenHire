---
type: Reference
title: "MVP status"
description: "What is built and in which PR, the acceptance journey that walks it end to end, the local stand-in for each external integration, every owner action (accounts, keys, legal reviews) needed before real use, and the known gaps."
tags: [status, owner-actions, mvp]
sources:
  - resource: tests/ScreenHire.E2E/AcceptanceJourneyTests.cs
---
# MVP status

Snapshot 2026-10-25, after the MVP workflow's tasks (PRs #1 to #18 and the acceptance task), the "Design system v0.2"
workflow (PRs #22 to #33), the "Platform: team, API access and webhooks" workflow (PRs #36 to #38) and the "Team,
collaboration and communication" workflow (PRs #39 to #42, and the docs restructure in ADR-0012), the "Workflow
builder" workflow (PRs #44, #45 and the editable graph) and the "Design system v0.3, the review's features, and roles
with job access" workflow (PRs #47 to #58 and the remaining screens). The whole product
runs on one machine with `pnpm dev` ([local demo](operations/local-demo.md)), and one Playwright journey in
`pnpm verify` (`tests/ScreenHire.E2E/AcceptanceJourneyTests.cs`) walks it end to end: sign in, a job from a template,
publish to the careers page and the sandbox board, a genuine candidate and a bot apply, both arrive live with their
levels and evidence, a move sends the stage-change email, and closing takes the job down everywhere. Nothing is hosted
yet.

What comes next, in order, is the [roadmap](roadmap.md).

## Built

| Area | What works | PRs |
|---|---|---|
| Sign-in and tenancy | Email links, Google, Microsoft and generic OIDC, onboarding, organizations, Postgres row-level security under an app role | #7 |
| Team | Members with roles, email invitations that expire and work once, role changes and removal with the last Owner protected, a membership log, switching between organizations | #38 |
| Roles and permissions | The server checks permissions, not role names, on every route; custom roles copied from a built-in or custom role, edited on their own page with History, deleted only when nobody has them; nobody gives more access than their own role has; Owner-only permissions stay with Owners; API keys capped by their creator ([ADR-0014](adr/0014-permissions-and-custom-roles.md)) | the custom roles task |
| API access | Organization API keys on the hiring API (jobs, applications, candidates, templates): read or read and write, shown once, a per-key rate limit, revocable ([ADR-0011](adr/0011-api-keys-for-integrators.md)) | #37 |
| Slack | An Owner or Admin connects the workspace from Settings > Slack and picks a channel and the events posted there (a new application, a candidate moved, an automation that failed, a job published); each person links their own account from their menu for their mentions and tasks as direct messages; messages name the candidate and the job and link to them, and never carry verification output ([Slack](architecture/slack.md)) | the Slack task |
| MCP server | A team's AI assistant at `/v1/mcp`, signed in with an API key: find jobs and candidates, read an application with the candidate's words marked, add notes, tags and tasks, move a candidate to a stage; Read keys see only the reads; no integrity output ([MCP server](architecture/mcp-server.md)) | the MCP server task |
| Hiring | Jobs with versions, hiring templates (system and your own), candidates, applications, the pipeline | #9 |
| Reports | A Reports page for every member, over a date range and chosen jobs: the hiring funnel by stage kind with the share that moved on, median time to hire per job and overall, applications and hires by source; each chart has a table view; job access narrows the numbers; no verification data ([Reports](architecture/reports.md)) | the reports task |
| Workflows | Automations with guardrails (no integrity-driven rejection), simulate and validate, transactional email, signed webhooks | #10 |
| Webhook subscriptions | Organization endpoints for six events, a secret per endpoint (shown once, rotatable), signed deliveries with retries and backoff, a delivery log, replay, automatic disabling | #36 |
| Collaboration | Notes on an application and on a stage move, tags with a tag filter, My tasks for the tasks automations and people make (Mine, For the team and Everyone's, each row a link to where the task is done; a to-do or a rejection review is ticked on the candidate's Next steps with Undo for 10 seconds, and a review or a requested scorecard is done when the scorecard is submitted), a real reviewer in AssignReviewer, a Run button for manual automations, a notification tray | #40, the remaining screens |
| Scorecards | A scorecard library (built-ins and your own, with history) whose questions take answer kinds, a stage picking one and each job keeping its copy, a required recommendation; submitting one raises the "reviewer decision" trigger. Fill in takes a reached stage's scorecard or any library scorecard, and Ask for one gives a teammate who sees the job a Scorecard task, done when they submit it. Rating questions make the candidate's one-to-five-star rating, shown after a reviewer gives their own and never read by an automation ([workflow engine](architecture/workflow-engine.md#8-the-engine-v1), ADR-0013) | #41, the scorecards v0.3 task, the scorecard requests task |
| Email | Organization email templates (Liquid subject, rich-text message with variables as chips, live preview, a test send), email to one candidate or a selection, the rejection email only with Reject ([email](architecture/email.md)) | #42, the rich text task |
| Rich text | A small editor with a toolbar (bold, italic, headings, lists, links) for a job's description and email messages, stored as sanitized HTML limited to the toolbar's elements and rendered on careers pages, in Google for Jobs, in feeds and in email ([rich text](architecture/rich-text.md)) | the rich text task |
| Change history | Who changed a hiring template, an email template, a scorecard, a job, a custom role or an organization setting (its name, brand colour or integrity settings), when and what: Last updated as the last header fact and a History sheet of every change in plain words, newest first by day, with before and after on request; the paged history on the API ([change history](architecture/change-history.md)) | the change history task |
| Follow-up questionnaires | Answers and the application's source as automation facts, so a knockout can match; follow-up questionnaires in a workflow, sent by a SendQuestionnaire automation through a signed, time-limited link to a careers page that works without JavaScript; answering one fires the "questionnaire answered" trigger ([workflow engine](architecture/workflow-engine.md#8-the-engine-v1)) | #45 |
| Stage time limits | A stage's time limit (SLA) passing fires the "time limit passes" trigger, which may only remind the team, and an application past it shows an Overdue badge | #44 |
| Workflow graph | The builder's graph edits the same draft as the Pipeline view: add an automation to a stage or the whole workflow, open its form at any node, remove it, and draw a "move to a stage" link; every node is a tab stop, the arrow keys move between nodes, and a validation problem shows on its node. On a phone the graph is for reading ([workflow engine](architecture/workflow-engine.md#4-builder-ux-dashboard)) | the editable graph task |
| Templates | One page with a tab each for hiring templates, scorecards and email templates: where each is used and when it was last used, filters for In use, Not used in 6 months and Archived, Duplicate, Archive and Restore, Reset to original for an edited built-in email, and an editor for each with the Edit / Preview switch; email templates moved here from Settings | the Templates page task |
| Settings | One sidebar item with its own menu: Organization, Team, Roles (what each role can do, from the API's permission catalogue), Branding, Integrity, API keys and Webhooks (each saying how to call it and linking the API reference, public in production), each for a role with its permission | #39, #42, the Settings menu task |
| Talent pool | Keep in mind beside Rejected, on a job's selection and when a job closes: the candidate leaves the job without a rejection and joins the pool, only with their own "Keep my details for future roles" from the apply form, until the organization's period (12 months by default, 6 to 24) runs out; a Talent pool page with search, tags and the note; invitations to apply with a link to stop being kept; adding someone to a job, linked to their earlier applications ([talent pool](architecture/talent-pool.md)) | the talent pool task |
| Careers site | Tenant boards and hosts, the apply flow with the notice first, steps that work without JavaScript with one questionnaire section a page and "Step 2 of 4", saving the form to finish later through an emailed link, direct-to-storage resume upload | #11 |
| Careers widget | The job list and the apply flow in a frame on a company's own site from one line of embed code; the sites allowed to frame the careers pages set `frame-ancestors`, the frame sizes itself over postMessage from the careers origin only, the flow stays cookie-free, and the application records the site ([careers widget](architecture/careers-widget.md)) | the careers widget task |
| Distribution | One-click publish and teardown, Google for Jobs JSON-LD and sitemaps, five XML feeds, a public job-board API, the Indexing API queue | #12 |
| Integrity | Authenticity and AI-assistance assessments from a versioned ruleset: network, identity, automation, duplicates, document and answer forensics, the LLM consistency check, aggregate form telemetry | #13, #15, #18 |
| Dashboard | Every page on [design system v0.3](design/design-system.md) (ADR-0013, ADR-0016), with a design review of the whole dashboard at 390, 1024, 1440 and 1920px: the shell with a sidebar, an icon rail and a phone sheet, and saved views with their status icons and short names; jobs and their forms; the candidates list and the job pipeline, whose tables fill the window; the candidate page with fixed tabs, the details rail, the full-window file viewer and notes the team edits, deletes and @mentions in; templates and the workflow builder, whose stages reorder with a grip; live updates; integrity badges and the evidence panel. A development-only style guide shows every component | #14, #16, #17, the acceptance task, #24 to #27, #29 to #33, #48 to #58, the remaining screens |
| Demo data | The local demo fills every screen, with a questionnaire on every seeded job and answers for every candidate, and a second Brightwater member (Jordan Ellis, Recruiter) | #20, #23, #38 |
| Platform | Wolverine messaging with the Postgres outbox, the SignalR hub, the test harness and gate, the marketing site with its SEO budgets, a visual design pass at three widths in `/task-review`, the docs as an Open Knowledge Format bundle (ADR-0012) | #4, #5, #6, #8, #22, #28 |

## Faked locally

Every external integration sits behind an interface with a local stand-in, and a provider with no configuration is
hidden rather than an error.

| Integration | Local stand-in |
|---|---|
| Identity providers | Dev SSO (navikt/mock-oauth2-server) with seeded users; Google, Microsoft and generic OIDC appear only with a client id and secret |
| Email | Mailpit, through the SMTP adapter behind `IEmailSender`; invitations, sign-in links and candidate email all land there |
| LLM | A deterministic fake model for the consistency check; with no provider the check records "unavailable"; off per organization by default |
| Job boards | A sandbox push board (Development only) and the feeds; Google Indexing has a local fake and is off unless set; Indeed is flags only |
| Webhook receivers | `WebhookReceiver` in `tests/ScreenHire.Testing`, a local server, so no test needs the internet |
| Slack | The sandbox workspace behind `ISlackClient` (Development only, on in the test stacks); with no Slack app configured, Settings > Slack says it is not set up |
| IP data | Configured ranges for the RFC 5737 documentation addresses; an ip2asn file and a Tor exit list are optional and absent |
| Storage, bot challenge, malware scan | Azurite; Turnstile unconfigured (recorded as "not checked", the honeypot still works); `NoMalwareScanner`, which records every file as "not scanned" |
| Built, flagged off | The cross-employer Signal Network, off until legal review (ADR-0005) |
| Not built | Identity verification and liveness (phase 2, the control is disabled), interview integrity (phase 3), candidate replies and an inbox |

## Owner actions

Everything below needs a person: an account, a key, a decision or a legal review. The PR that raised each is in
brackets.

- **Sign-in.** Register the Google and Microsoft apps with redirect URIs `{AppOrigin}/v1/auth/signin-google` and
  `/signin-microsoft`, request Microsoft's `xms_edov` optional claim, and put the client ids and secrets in the secret
  store (#7). The Data Protection key ring (sign-in cookies, telemetry tokens, questionnaire links) is kept in Postgres
  and shared by the API and the worker; protect its keys at rest with a key vault or certificate
  (`ProtectKeysWith...`) in production (#7, #18). List the reverse proxies and the careers
  servers in `ScreenHire:Auth:TrustedProxies` (#7, #11).
- **Email.** Choose the production provider behind `IEmailSender`, and set `ScreenHire:Email:From` and the worker's
  SMTP connection; without them automations skip email, and invitations and candidate email cannot go out (#7, #10,
  #38, #42). Candidate replies and an inbox need that provider to route inbound mail (#42). Set a production
  `ScreenHire:Webhooks:SigningKey` for the API and the worker; without it creating or rotating a webhook endpoint
  returns 503 and deliveries are skipped (#10, #36).
- **Slack.** Create the Slack app at api.slack.com: bot scopes `chat:write`, `chat:write.public`, `channels:read`,
  `groups:read` and `im:write`; Sign in with Slack with `openid` and `profile`; the redirect URL
  `{AppOrigin}/v1/slack/oauth/callback`; public distribution, so other workspaces can add it; token rotation off; the App
  Home's Messages tab on. Put its client id in `ScreenHire:Slack:ClientId` and its secret in the `slack-client-secret`
  parameter. Before real tenants connect, add Slack to the sub-processor list, and list the app in the Slack
  Marketplace if it is to be found there (the Slack task).
- **Integrators.** Integrator docs and terms must carry the `integrity.assessed` guidance on automated rejection
  (GDPR Art. 22, NYC LL144 and the other rules in [legal](research/legal.md)); no integrator docs exist yet (#36).
- **LLM.** Set `ScreenHire:Integrity:Llm:Provider=Anthropic` and the `anthropic-api-key` secret, then replace the
  hand-written recordings with live ones; add the provider to the sub-processor list and DPA before a real tenant turns
  the check on (#15).
- **Job boards.** Create a Google service account that owns the careers domain in Search Console, set
  `GoogleIndexing:Mode=Google` and `google-indexing-service-account-key`, and ask for more than 200 calls a day; the real
  client has never run (#12). Onboard each organization's feed with Careerjet, Talent.com, Adzuna and Jooble, and check
  Jooble's date format and Adzuna's schema with them (#12).
- **IP data and identity checks.** Supply an ip2asn file and a Tor exit list, or wire a commercial source behind
  `IIpIntelligence` (#13). Set `ScreenHire:Integrity:DeviceHashKey` (32+ characters) in production (#13). Refresh the
  embedded disposable-domain list from time to time (#13).
- **Storage and scanning.** Create the uploads container with a CORS rule for PUT from the careers hosts and GET from the
  dashboard host (the file viewer, #25), and a one-day lifecycle rule for unclaimed uploads (#11). Wire a real
  malware scanner behind `IMalwareScanner` (#11, #15). Optionally, Turnstile keys in `ScreenHire:Turnstile` (#11).
- **Deploy.** Run the EF migrations and `ScreenHireMessaging.ProvisionStorageAsync` as the owner, give `screenhire_app`
  its login, and give the API and worker only that connection (#7). Wolverine's static code generation is deferred
  until hosting (#5). Set `SITE_URL` for marketing builds and redirect URLs without a trailing slash (#6); set Astro's
  `security.allowedDomains` for careers behind a CDN (#11); keep access logs for 30 days or less (#11). Set
  `PUBLIC_APP_URL` so the marketing site's "Start free" reaches sign-in (#6). Give the worker
  `ScreenHire:Domains:AppHost` as well as the API, or mention emails are skipped with a warning (#55).
- **Legal and policy.** A legal review of the law tracker before publishing (#6); the analytics decision, with a privacy
  page and the ePrivacy position per market (#6); a legal review before any typing-rhythm signal (#18); a check of the
  device-check defaults per jurisdiction (notice outside the EU and EEA, consent inside) (#18). A legal review of the
  talent pool: the 12-month default permission period, the 6 to 24 month range an organization may set, the apply
  form's "Keep my details for future roles" wording and the privacy page's "Future roles" section, per market (the
  talent pool task).
- **Product.** Set prices and job limits in `apps/marketing/src/lib/pricing.ts` (#6). Move jobs still on "Software
  engineer" version 1 to version 2 (#13).

## Known gaps

- Reports know two sources, the careers page and a recruiter, because nothing records which job board sent a careers
  applicant, and they count an application through the careers widget under the careers page although it records its
  site; the MCP server and API keys have no report yet; and stage kinds come from each job's current workflow, so a
  removed stage's history drops out of the funnel (`architecture/reports.md` section 6).
- Erasure requests: nothing deletes a candidate yet. A talent pool entry goes with its candidate when one is deleted,
  and until erasure exists a person removes someone from the pool with "They asked to be removed", which withdraws their
  permission for future roles ([talent pool](architecture/talent-pool.md)).

- The MCP server gives an assistant no integrity output, as ADR-0011 refuses it to every API key, although roadmap item 6
  asked for levels, reasons and evidence. Opening it to `/v1/mcp` is an owner decision: an amendment to ADR-0011, then
  one `IntegrityReads.LoadAsync` call in `get_application` and a view that marks quoted evidence as candidate-written.
- MCP clients that sign in only with OAuth, such as claude.ai custom connectors, cannot connect until OAuth comes
  (ADR-0011 decision 6).
- With the demo seeding on, a live update can arrive late while the worker works through the demo's backlog just after
  startup (integrity-engine.md §10.9). In the browser pane, `/preview` waits for an integrity chip before it checks anything live.
