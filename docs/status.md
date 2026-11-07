---
type: Reference
title: "MVP status"
description: "What is built and in which PR, the acceptance journey that walks it end to end, the local stand-in for each external integration, every owner action (accounts, keys, legal reviews) needed before real use, and the known gaps."
tags: [status, owner-actions, mvp]
sources:
  - resource: tests/ScreenHire.E2E/AcceptanceJourneyTests.cs
---
# MVP status

Snapshot 2026-11-05, after the MVP workflow's tasks (PRs #1 to #18 and the acceptance task), the "Design system v0.2"
workflow (PRs #22 to #33), the "Platform: team, API access and webhooks" workflow (PRs #36 to #38) and the "Team,
collaboration and communication" workflow (PRs #39 to #42, and the docs restructure in ADR-0012), the "Workflow
builder" workflow (PRs #44, #45 and the editable graph) and the "Design system v0.3, the review's features, and roles
with job access" workflow (PRs #47 to #58 and the remaining screens), the "After the owner's review" workflow (PRs #61
to #74) and the "Owner review of the second build" workflow (PRs #80 to #89). The whole product
runs on one machine with `pnpm dev` ([local demo](operations/local-demo.md)), and one Playwright journey in
`pnpm verify` (`tests/ScreenHire.E2E/AcceptanceJourneyTests.cs`) walks it end to end: sign in, a job from a template,
publish to the careers page and the sandbox board, a genuine candidate and a bot apply, both arrive live with their
levels and evidence, a move sends the stage-change email, and closing takes the job down everywhere. Nothing is hosted
yet; the [deploy runbook](operations/deploy.md) is the way there.

What comes next, in order, is the [roadmap](roadmap.md).

## Built

| Area | What works | PRs |
|---|---|---|
| Sign-in and tenancy | Email links, Google, Microsoft and generic OIDC, onboarding, organizations, Postgres row-level security under an app role | #7 |
| Team | Members with roles, email invitations that expire and work once, role changes and removal with the last Owner protected, a membership log, switching between organizations | #38 |
| Roles and permissions | The server checks permissions, not role names, on every route; custom roles copied from a built-in or custom role, edited on their own page with History, deleted only when nobody has them; nobody gives more access than their own role has; Owner-only permissions stay with Owners; API keys capped by their creator ([ADR-0014](adr/0014-permissions-and-custom-roles.md)) | the custom roles task |
| API access | Organization API keys on the hiring API (jobs, applications, candidates, templates): read or read and write, shown once, a per-key rate limit, revocable ([ADR-0011](adr/0011-api-keys-for-integrators.md)); a Verification read scope that only an Owner can give opens an application's verification to the key, with every read in the review log ([ADR-0021](adr/0021-verification-read-scope-for-api-keys.md)) | #37 |
| Slack | An Owner or Admin connects the workspace from Settings > Slack and picks a channel and the events posted there (a new application, a candidate moved, an automation that failed, a job published); each person links their own account from their menu for their mentions and tasks as direct messages; messages name the candidate and the job and link to them, and never carry verification output ([Slack](architecture/slack.md)) | the Slack task |
| Interview scheduling | A member connects their own Google or Outlook calendar from the account menu (sign-in keeps no tokens, and the scopes are free/busy and events only); a recruiter with the permission to email candidates offers an interview from the candidate's page, or a `ScheduleInterview` automation does; the candidate picks a time that is free for every interviewer on a careers page that needs no account and no script, and can move or cancel it from the same link; one calendar event per interviewer with a Meet or Teams link, an advisory-locked booking that never double-books an interviewer, confirmation and cancellation emails, an Interview panel and a booked time on the candidate's page, and an `InterviewScheduled` automation trigger. No verification output reaches a calendar, an email or the page ([interview scheduling](architecture/interview-scheduling.md), [ADR-0024](adr/0024-interview-scheduling-on-calendar-apis.md)) | the interview scheduling task |
| MCP server | A team's AI assistant at `/v1/mcp`, signed in with an API key or, for a client that signs in only with OAuth such as a claude.ai custom connector, with a connection an Owner or Admin approves in the dashboard and can disconnect on the MCP server page: find jobs and candidates, read an application with the candidate's words marked, add notes, tags and tasks, move a candidate to a stage, read a hiring report and the saved candidates; Read keys see only the reads; verification tools only for a key an Owner gave the Verification read scope, each read logged and a level never a reason to reject ([MCP server](architecture/mcp-server.md), [OAuth sign-in](architecture/oauth-server.md)) | the MCP server task |
| Hiring | Jobs with versions, hiring templates (system and your own), candidates, applications, the pipeline | #9 |
| Reports | A Reports page for every member, over a date range and chosen jobs: the hiring funnel by stage kind with the share that moved on, median time to hire per job and overall, applications and hires by source; each chart has a table view; job access narrows the numbers; no verification data ([Reports](architecture/reports.md)) | the reports task |
| Workflows | Automations with guardrails (no verification-driven rejection), simulate and validate, transactional email, signed webhooks | #10 |
| Webhook subscriptions | Organization endpoints for six events, a secret per endpoint (shown once, rotatable), signed deliveries with retries and backoff, a delivery log, replay, automatic disabling | #36 |
| Collaboration | Notes on an application and on a stage move, tags with a tag filter, My tasks for the tasks automations and people make (Mine, For the team and Everyone's, each row a link to where the task is done; a to-do or a rejection review is ticked on the candidate's Next steps with Undo for 10 seconds, and a review or a requested scorecard is done when the scorecard is submitted), a real reviewer in AssignReviewer, a Run button for manual automations, a notification tray, and teammates' changes as toasts that each person picks in a Notifications sheet | #40, the remaining screens, roadmap item 10 |
| Scorecards | A scorecard library (built-ins and your own, with history) whose questions take answer kinds, a stage picking one and each job keeping its copy, a required recommendation; submitting one raises the "reviewer decision" trigger. Fill in takes a reached stage's scorecard or any library scorecard, and Ask for one gives a teammate who sees the job a Scorecard task, done when they submit it. Rating questions make the candidate's one-to-five-star rating, shown after a reviewer gives their own and never read by an automation ([workflow engine](architecture/workflow-engine.md#8-the-engine-v1), ADR-0013) | #41, the scorecards v0.3 task, the scorecard requests task |
| Email | Organization email templates (Liquid subject, rich-text message with variables as chips, live preview, a test send), email to one candidate or a selection, the rejection email only with Reject ([email](architecture/email.md)) | #42, the rich text task |
| Replies and SendGrid | SendGrid sends candidate email (Mailpit locally); every email carries a signed reply address, so the candidate's answer lands in Messages and Activity with its attachments scanned and the sender told; bounces and spam complaints flag the address; an Owner or Admin can add the company's own sending domain, verified by SendGrid ([email](architecture/email.md), ADR-0022) | the replies task |
| Rich text | A small editor with a toolbar (bold, italic, headings, lists, links) for a job's description and email messages, stored as sanitized HTML limited to the toolbar's elements and rendered on careers pages, in Google for Jobs, in feeds and in email ([rich text](architecture/rich-text.md)) | the rich text task |
| Change history | Who changed a hiring template, an email template, a scorecard, a job, a custom role or an organization setting (its name, brand colour or verification settings), when and what: Last updated as the last header fact and a History sheet of every change in plain words, newest first by day, with before and after on request; the paged history on the API ([change history](architecture/change-history.md)) | the change history task |
| Follow-up questionnaires | Answers and the application's source as automation facts, so a knockout can match; follow-up questionnaires in a workflow, sent by a SendQuestionnaire automation through a signed, time-limited link to a careers page that works without JavaScript; answering one fires the "questionnaire answered" trigger ([workflow engine](architecture/workflow-engine.md#8-the-engine-v1)) | #45 |
| Stage time limits | A stage's time limit (SLA) passing fires the "time limit passes" trigger, which may only remind the team, and an application past it shows an Overdue badge | #44 |
| Workflow graph | The builder's graph edits the same draft as the Pipeline view: add an automation to a stage or the whole workflow, open its form at any node, remove it, and draw a "move to a stage" link; every node is a tab stop, the arrow keys move between nodes, and a validation problem shows on its node. On a phone the graph is for reading ([workflow engine](architecture/workflow-engine.md#4-builder-ux-dashboard)) | the editable graph task |
| Templates | One page with a tab each for hiring templates, scorecards and email templates: where each is used and when it was last used, filters for In use, Not used in 6 months and Archived, Duplicate, Archive and Restore, Reset to original for an edited built-in email, and an editor for each with the Edit / Preview switch; email templates moved here from Settings | the Templates page task |
| Settings | One sidebar item with its own menu: Organization, Team, Roles (what each role can do, from the API's permission catalogue), Data requests, Branding, Verification, API keys and Webhooks (each saying how to call it and linking the API reference, public in production), each for a role with its permission | #39, #42, the Settings menu task |
| Data requests and record retention | A candidate asks for a copy of their data or for erasure from the careers privacy page: an address in, one fixed answer, then a signed link that works for an hour and one choice. Settings, Data requests lists each request with its due date and offers Send a copy, Erase (the name typed to confirm) and Decline (the reason is emailed to the candidate), and the team gets a live card for each new one. A copy is one JSON file of the candidate's applications, answers, messages and notes, downloaded through an emailed link that expires. An off-by-default retention setting deletes ended applications every day, after a dry-run count before it saves and with a Last run line ([data requests](architecture/data-requests.md)) | the data requests task |
| Saved candidates | Save candidate beside Rejected, on a job's selection and when a job closes: the candidate leaves the job without a rejection and joins the saved candidates, only with their own "Keep my details for future roles" from the apply form, with no end date (they stay until the team removes them or they use the stop link); a Saved candidates page with search, tags, Rating, Last contacted, a Message button and a note the team can edit, with History; invitations to apply with a link to stop being kept; adding someone to a job, linked to their earlier applications ([saved candidates](architecture/talent-pool.md)) | the talent pool task, the saved candidates task |
| Careers site | Tenant boards and hosts, the apply flow with the notice first, steps that work without JavaScript with one questionnaire section a page and "Step 2 of 4", saving the form to finish later through an emailed link, direct-to-storage resume upload | #11 |
| Careers widget | The job list and the apply flow in a frame on a company's own site from one line of embed code; the sites allowed to frame the careers pages set `frame-ancestors`, the frame sizes itself over postMessage from the careers origin only, the flow stays cookie-free, and the application records the site ([careers widget](architecture/careers-widget.md)) | the careers widget task |
| Distribution | One-click publish and teardown, Google for Jobs JSON-LD and sitemaps, five XML feeds, a public job-board API, the Indexing API queue | #12 |
| Verification | Authenticity and AI-assistance assessments from a versioned ruleset: network, identity, automation, duplicates, document and answer forensics, the LLM consistency check, aggregate form telemetry | #13, #15, #18 |
| Dashboard | Every page on [design system v0.3](design/design-system.md) (ADR-0013, ADR-0016), with a design review of the whole dashboard at 390, 1024, 1440 and 1920px: the shell with a sidebar, an icon rail and a phone sheet, and saved views with their status icons and short names; jobs and their forms; the candidates list and the job pipeline, whose tables fill the window; the candidate page with fixed tabs, the details rail, the full-window file viewer and notes the team edits, deletes and @mentions in; templates and the workflow builder, whose stages reorder with a grip; live updates; verification badges and the evidence panel. A development-only style guide shows every component | #14, #16, #17, the acceptance task, #24 to #27, #29 to #33, #48 to #58, the remaining screens |
| Demo data | The local demo fills every screen, with a questionnaire on every seeded job and answers for every candidate, and a second Brightwater member (Jordan Ellis, Recruiter) | #20, #23, #38 |
| Hosting definition | An AppHost publish model for Azure Container Apps (API, worker, careers), a Burstable PostgreSQL Flexible Server, Blob Storage and Key Vault, and the Bicep it generates under `infra/` with `azure.yaml`; a migrator that applies the migrations and creates Wolverine's tables as the database owner; production hosts that stop at start naming each required setting that is missing; secrets read from Key Vault and the Data Protection key ring wrapped with a Key Vault key; health endpoints in every environment; CIDR proxy networks; a CI job that checks the definition still generates; Cloudflare Pages files for the dashboard. Nothing is deployed ([deploy](operations/deploy.md), ADR-0007) | the hosting readiness task |
| Platform | Wolverine messaging with the Postgres outbox, the SignalR hub, the test harness and gate, the marketing site with its SEO budgets, a visual design pass at three widths in `/task-review`, the docs as an Open Knowledge Format bundle (ADR-0012) | #4, #5, #6, #8, #22, #28 |

## Faked locally

Every external integration sits behind an interface with a local stand-in, and a provider with no configuration is
hidden rather than an error.

| Integration | Local stand-in |
|---|---|
| Identity providers | Dev SSO (navikt/mock-oauth2-server) with seeded users; Google, Microsoft and generic OIDC appear only with a client id and secret |
| Email | Mailpit, through the SMTP adapter behind `IEmailSender`; invitations, sign-in links and candidate email all land there. SendGrid is used only with its API key. Replies are delivered by the sandbox route `/v1/dev/inbound-email` and sending domains verify in the sandbox with no DNS (`ScreenHire:Email:Sandbox`, Development only) |
| LLM | A deterministic fake model for the consistency check; with no provider the check records "unavailable"; off per organization by default |
| Job boards | A sandbox push board (Development only) and the feeds; Google Indexing has a local fake and is off unless set; Indeed is flags only |
| Webhook receivers | `WebhookReceiver` in `tests/ScreenHire.Testing`, a local server, so no test needs the internet |
| Slack | The sandbox workspace behind `ISlackClient` (Development only, on in the test stacks); with no Slack app configured, Settings hides Slack |
| Calendars | The sandbox calendar behind `ICalendarProvider` (Development only, on in `pnpm dev` and the test stacks); with no Google or Microsoft app configured those buttons are hidden |
| IP data | Configured ranges for the RFC 5737 documentation addresses; an ip2asn file and a Tor exit list are optional and absent |
| Storage, bot challenge, malware scan | Azurite; Turnstile unconfigured (recorded as "not checked", the honeypot still works); `NoMalwareScanner`, which records every file as "not scanned" |
| Built, flagged off | The cross-employer Signal Network, off until legal review (ADR-0005) |
| Not built | Identity verification and liveness (phase 2, the control is disabled), interview verification (phase 3), an inbox across candidates |

## Owner actions

Everything below needs a person: an account, a key, a decision or a legal review. The PR that raised each is in
brackets.

- **Sign-in.** Register the Google and Microsoft apps with redirect URIs `{AppOrigin}/v1/auth/signin-google` and
  `/signin-microsoft`, request Microsoft's `xms_edov` optional claim, and put the client ids and secrets in the secret
  store (#7). The Data Protection key ring (sign-in cookies, telemetry tokens, questionnaire links) is kept in Postgres
  and shared by the API and the worker; the Azure definition wraps its keys with a Key Vault key
  (`ScreenHire:DataProtection:KeyVaultKeyUri`), and a production host will not start without it (#7, #18). List the reverse proxies and the careers
  servers in `ScreenHire:Auth:TrustedProxies` (#7, #11).
- **Email.** Subscribe to SendGrid in the Azure Marketplace (ADR-0022) and set `Parameters:sendgrid-api-key` and
  `Email:SendGrid:Enabled=true` in the AppHost, `ScreenHire:Email:From`, `ReplyDomain` (the host replies arrive on, with an MX
  record to SendGrid) and a production `sendgrid-inbound-secret` and `email-reply-key`. In SendGrid, add an Inbound Parse
  setting for that host that posts to `https://<api host>/v1/webhooks/email/inbound` with the secret as Basic credentials
  (user `sendgrid`), and turn on the signed Event Webhook to `/v1/webhooks/email/events` with its public key in
  `ScreenHire:Email:SendGrid:EventWebhookPublicKey`. Each customer's own domain is verified from Settings, Sending domain.
  Without a provider automations skip email, and invitations and candidate email cannot go out (#7, #10, #38, #42). Set a production
  `ScreenHire:Webhooks:SigningKey` for the API and the worker; without it creating or rotating a webhook endpoint
  returns 503 and deliveries are skipped (#10, #36).
- **Slack.** Create the Slack app at api.slack.com: bot scopes `chat:write`, `chat:write.public`, `channels:read`,
  `groups:read` and `im:write`; Sign in with Slack with `openid` and `profile`; the redirect URL
  `{AppOrigin}/v1/slack/oauth/callback`; public distribution, so other workspaces can add it; token rotation off; the App
  Home's Messages tab on. Put its client id in `ScreenHire:Slack:ClientId` and its secret in the `slack-client-secret`
  parameter. Before real tenants connect, add Slack to the sub-processor list, and list the app in the Slack
  Marketplace if it is to be found there (the Slack task).
- **Calendars.** Create a Google Cloud OAuth client with the sensitive scopes `calendar.freebusy` and `calendar.events`, redirect URI `{AppOrigin}/v1/calendar/oauth/callback/google`, and submit the app for Google's verification, which takes weeks (until then only test users can connect). Register a Microsoft Entra application for any organizational directory and personal accounts, with delegated `Calendars.ReadWrite` and `offline_access` and the redirect URI `{AppOrigin}/v1/calendar/oauth/callback/microsoft`. Put each client id in `ScreenHire:Calendar:<Provider>:ClientId` and each secret in the `google-calendar-client-secret` and `microsoft-calendar-client-secret` parameters. The Data Protection key ring protection above now also guards calendar tokens. Add Google and Microsoft calendar access to the sub-processor list before real tenants connect (the interview scheduling task).
- **Integrators.** Integrator docs and terms must carry the `verification.assessed` guidance on automated rejection
  (GDPR Art. 22, NYC LL144 and the other rules in [legal](research/legal.md)); no integrator docs exist yet (#36).
- **LLM.** Set `ScreenHire:Verification:Llm:Provider=Anthropic` and the `anthropic-api-key` secret, then replace the
  hand-written recordings with live ones; add the provider to the sub-processor list and DPA before a real tenant turns
  the check on (#15).
- **Job boards.** Create a Google service account that owns the careers domain in Search Console, set
  `GoogleIndexing:Mode=Google` and `google-indexing-service-account-key`, and ask for more than 200 calls a day; the real
  client has never run (#12). Onboard each organization's feed with Careerjet, Talent.com, Adzuna and Jooble, and check
  Jooble's date format and Adzuna's schema with them (#12).
- **IP data and identity checks.** Supply an ip2asn file and a Tor exit list, or wire a commercial source behind
  `IIpIntelligence` (#13). Set `ScreenHire:Verification:DeviceHashKey` (32+ characters) in production (#13). Refresh the
  embedded disposable-domain list from time to time (#13).
- **Database firewall.** The Azure definition lets every Azure address reach PostgreSQL, so its passwords are the only barrier. Before real
  candidates apply, choose between accepting that and a virtual network, which costs more ([deploy](operations/deploy.md), ADR-0007).
- **Storage and scanning.** The Azure definition creates the private containers (`applicant-uploads`, `brand-images`,
  `email-attachments`, `data-exports`) and the CORS rule. Do not add a one-day storage lifecycle rule for unclaimed uploads: it would delete submitted
  resumes, because only the database knows which files are claimed. The worker's daily sweep does it instead
  ([apply flow](architecture/apply-flow.md), section 3). Wire a real malware scanner behind
  `IMalwareScanner` (#11, #15). Optionally, Turnstile keys in `ScreenHire:Turnstile` (#11); with them, saving a draft application
  also needs the check to pass. The worker's identity lists and deletes in `brand-images` through its storage role (branding.md §4). Candidates' copies of their data go in a fourth private container, `data-exports`, which the worker writes and deletes in and the API reads through short SAS links; the Azure definition creates it, and the same storage roles cover it (data-requests.md).
- **Database.** The Azure definition puts `pg_trgm` on the server's `azure.extensions` list, which the migrations need. The trigram indexes on candidate names and emails and job titles do not
  serve the search as `screenhire_app` yet, because `ILIKE` is not leakproof under row-level security; marking a
  function leakproof needs a superuser, so decide whether to provision one before anyone expects a faster search (see
  [sign-in and tenancy](architecture/sign-in-and-tenancy.md), Row-level security and indexes). The follow-ups migration
  takes heavy locks while it runs (it lifts FORCE on four tables and builds the indexes in one transaction), so run it
  in a quiet window.
- **Deploy.** Follow the [deploy runbook](operations/deploy.md); it has the order, the commands and the settings table. In it the
  migrator runs the EF migrations and `ScreenHireMessaging.ProvisionStorageAsync` as the owner and gives `screenhire_app` its login,
  and the API and worker hold only that login (#7). Still yours: the Azure subscription and the Cloudflare account; set the
  `SITE_URL` and `PUBLIC_APP_URL` of the marketing build and give redirect URLs no trailing slash (#6); the `careers-allowed-domains`
  and `auth-trusted-proxies` parameters, which need the real ingress addresses (#11); keep access logs for 30 days or less (#11).
  Wolverine's static code generation was measured and is not needed at the planned size (the worker starts in 12 s and peaks at
  198 MB without it).
- **Legal and policy.** A legal review of the notice on the job and apply pages and the privacy page, which say that applications are checked and what is collected but no longer how each check works; a legal review of the law tracker before publishing (#6); the analytics decision, with a privacy
  page and the ePrivacy position per market (#6); a legal review before any typing-rhythm signal (#18); a check of the
  device-check defaults per jurisdiction (notice outside the EU and EEA, consent inside) (#18). A legal review of saved candidates:
  keeping their details until the candidate asks or the team removes them, with no expiry, including permissions given
  before this under a form that promised 6 to 24 months and still running when it shipped; the apply form's "Keep my
  details for future roles" wording and the privacy page's "Future roles" section, per market; and the note's History,
  whose free text outlives a saved entry in append-only `change_records` with no link back to the candidate until an
  erasure in that organization purges it (the saved candidates task).
- **Data requests and retention.** A legal review of the request flow, which answers every address the same way and then
  offers a copy or erasure behind an emailed link; of the copy file's contents (applications, answers, messages and
  notes; verification output and scorecards are left out); of the 30-day due date, which is the team's target and not a
  promise to the candidate; of the retention default (off) and its definition of ended (hired, rejected, withdrawn or a
  closed job, counted from when it ended; saved candidates are never removed); and of the careers privacy page wording and
  the decline email per market. Erasure sends no confirmation, because the address is erased with the candidate (the data
  requests task).
- **Product.** Subscribe to Svelte Flow Pro before charging anyone. The workflow graph hides Svelte Flow's attribution, which its MIT licence allows, and its makers ask a business earning from it to subscribe first. Set prices and job limits in `apps/marketing/src/lib/pricing.ts` (#6). Move jobs still on "Software
  engineer" version 1 to version 2 (#13).

## Known gaps

- The API and worker run as one replica each (the SignalR hub and the real-time queue consumer are in the API process); the Azure definition fixes them there ([realtime](architecture/realtime.md)).
- Interview scheduling sends no reminders, and a recruiter cannot move a booked interview for the candidate: they cancel it and offer a new one. Events made for interviewers on different providers are separate copies, not one shared event ([interview scheduling](architecture/interview-scheduling.md)).
- Reports count applicants from Google for Jobs and the public job-board API as the careers page, because those two keep
  their canonical URLs and carry no source tag; only the MCP server reads reports for a key, not the HTTP API; and stage
  kinds come from each job's current workflow, so a removed stage's history drops out of the funnel
  (`architecture/reports.md` section 6).
- A job board that reads our feed never says where its listing landed, so only the careers page has a View posting link.
- Data requests: a candidate can ask for a copy of their data or for erasure through the careers privacy page, and the team
  acts on it from Settings, Data requests ([data requests](architecture/data-requests.md)). Retention never touches a saved
  candidate: they stay until the team removes them or they use the stop link, which only takes them off the list
  ([saved candidates](architecture/talent-pool.md)); the profile then offers Erase candidate. Retention treats a Hired application and an application on a closed job as
  ended, as the setting says.

- With the demo seeding on, a live update can arrive late while the worker works through the demo's backlog just after
  startup (verification-engine.md §10.9). In the browser pane, `/preview` waits for a verification chip before it checks anything live.
