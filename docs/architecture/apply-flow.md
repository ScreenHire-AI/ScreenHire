---
type: Design
title: "Careers apply flow"
description: "The candidate apply page: the notice before any collection, three steps that work without JavaScript, direct-to-storage resume uploads, the public submission endpoint, per-tenant branding stylesheets under the content security policy, tenant careers hosts, and the follow-up questionnaire page a signed link opens."
tags: [careers, apply, astro]
sources:
  - resource: apps/careers/src/pages/[organization]/jobs/[job]/apply.astro
  - resource: apps/careers/src/pages/[organization]/uploads.ts
  - resource: apps/careers/src/lib/apply.ts
  - resource: apps/careers/src/lib/apply-server.ts
  - resource: apps/careers/src/lib/brand.ts
  - resource: apps/careers/src/lib/host.ts
  - resource: apps/careers/src/middleware.ts
  - resource: apps/careers/src/components/PrivacyNotice.astro
  - resource: apps/careers/src/pages/[organization]/questionnaires/[token].astro
  - resource: apps/careers/src/pages/[organization]/questionnaires/sent.astro
  - resource: apps/careers/src/lib/questionnaire.ts
  - resource: apps/api/Endpoints/PublicApplicationEndpoints.cs
  - resource: apps/api/Applications/ApplicantUploads.cs
  - resource: src/ScreenHire.Domain/Organizations/BrandPalette.cs
  - resource: apps/dashboard/src/lib/components/file-frame.svelte
---
# Careers apply flow

> Status: v1 (2026-10-20). Code: `apps/careers` (pages, `src/actions`, `src/lib/apply*.ts`), `apps/api`
> (`Endpoints/PublicApplicationEndpoints.cs`, `Applications/*`), `src/ScreenHire.Domain/Applications`. Decisions it
> follows: ADR-0003 (Astro, plain forms, iframe widget), ADR-0004 (evidence, not verdicts), `docs/research/legal.md`
> (notice, accommodation), design-system §2.4, §7 and §8.

A candidate goes from a job page to a sent application in three steps, with or without JavaScript, and is told what
is checked before anything is collected. The careers server talks to the API on the candidate's behalf; the browser
never calls the API.

## 1. Pages and steps

| Path (under `/{organization}`, or `/` on a tenant host) | What it is |
|---|---|
| `/jobs/{job}` | The job, the "How we keep this fair" notice and an Apply link. 410 once the job closes, 404 if unknown. |
| `/jobs/{job}/apply` | The form: **details** (notice, assistive-technology switch, name, email, phone, resume), **questions** (the job's questionnaire; skipped when it has none), **review** (summary, AI-use declaration, send). |
| `/jobs/{job}/applied` | The confirmation, after a POST-redirect-GET. The same page whether the application is new or the email had already applied, so the form never reveals who applied. |
| `/privacy` | "What we collect and why", the full notice. Its version (`NOTICE_VERSION` in `src/lib/apply.ts`) is recorded on every application. |
| `/brand/{hash}.css` | The employer's brand stylesheet (section 5). |
| `/uploads` | JSON endpoint for the upload island (section 3). |
| `/telemetry?job={id}` | Relay for the telemetry module's beacons to the API (integrity-engine.md §10.2, "Form telemetry"). |
| `/questionnaires/{token}` | A follow-up questionnaire, opened from the signed link a SendQuestionnaire automation emailed (section 8). |
| `/questionnaires/sent` | Where answered follow-up questions land, after a POST-redirect-GET, with no token in the address. |

Each step is a native `<form method="POST" enctype="multipart/form-data">` posted to the `apply` Astro Action
(`src/actions/index.ts`), which re-renders the same page with the next step. What the candidate has entered travels
between steps in one hidden field (`state`); the server re-reads and re-checks it on every post, and checks every step
again before sending. Back and "Change" buttons keep what was typed without checking it. Field errors show inline and in
an error summary at the top that links to each field and takes focus.

The form is built from the API's `GET /v1/public/organizations/{slug}/jobs/{job}/application-form`: the job, its
questions (resume first; knockouts and automations stay on the server), the AI-use policy, upload limits and the bot
challenge's site key. Every form asks for a resume: the questionnaire's own `resume` file question, or an optional one
(`ApplicationForm.DefaultResumeQuestion`).

The fields are the shared question components in `packages/ui/src/questions` (`TextField`, `ChoiceField`,
`QuestionField`, `FileField`), the same Svelte components the dashboard's form builder previews, so the
preview is the form (workflow-engine.md §4). The apply page renders them on the server with no client directive, so
they ship no JavaScript; only file questions hydrate, as `FileField` islands. Their field names (`q:{id}`,
`file:{id}`, `upload:{id}`) and element ids (`field-...`) come from `packages/ui/src/questions/model.ts`, which
`src/lib/apply.ts` re-exports, so the error summary's links and the posted names cannot drift apart.

## 2. Notice before collection

Browsing sets no cookies and runs no scripts beyond JSON-LD. The apply page shows the notice and the
assistive-technology switch above the first field. The telemetry module (`packages/telemetry`) records nothing on that
step until the candidate first uses the form, and the server never starts it with the switch on. Its counts are stored
unbound and deleted after a day unless the application is sent; files uploaded along the way sit unclaimed in storage
until then (section 3).

What this build records with an application (`JobApplication`): the answers, the files, the switch
(`AccommodationRequested`, which later turns telemetry and typing signals off and is never a signal itself), the
AI-use declaration and details, `PrivacyNoticeVersion`, `Source = CareersSite`, and two automation signals for the
integrity engine:

- `HoneypotFilled` (`AUTO_HONEYPOT`): an off-screen field that only bots fill;
- `BotCheck` (`AUTO_TURNSTILE_FAIL`): Cloudflare Turnstile's result when `ScreenHire:Turnstile:SiteKey` and `SecretKey`
  are set, otherwise `NotChecked`. Off locally and in tests.

Neither refuses a submission (CLAUDE.md non-negotiable 2), and knockout answers are stored like any other.

With a careers-site application the API also stores a `SubmissionObservation` for the integrity engine
(integrity-engine.md §10): the candidate's address (from `X-Forwarded-For`), their browser's user agent, which the
careers server passes on as `userAgent` because the API only sees the server's, and `formStartedAt`, the moment the
careers server first rendered the form (kept in the step state, so it round-trips through the browser and is only a
hint). The telemetry module fills `automationMarkers` and `deviceFingerprint` on the review step, and the careers
server passes them on with the session token and the module's last snapshot. The API hashes a fingerprint per
organization and never stores it as sent, and keeps it only on the job's device-check basis: where that is `Consent`
(the EU and EEA by default), the details step shows an optional "Check this device for repeat applications" box and the
fingerprint is kept only when it is ticked. The observation and the telemetry are deleted after 30 days. The notice and
the privacy page say all of this, and that the employer may email a follow-up questionnaire whose page keeps the
answers only (`NOTICE_VERSION` `2026-10-23.1`).

## 3. Uploads

Files go straight to blob storage (Azurite locally) through a create-only link the API issues, so bytes never pass
through the API. The link writes its blob once and cannot replace it, so the file the API checked is the file it keeps:

1. `POST .../jobs/{jobId}/questions/{questionId}/uploads` with the file name and size: the API checks the extension
   against the question's accepted types (within PDF, DOCX, DOC, JPG and PNG) and the size against
   `ScreenHire:Apply:MaxUploadBytes` (10 MB), then returns a SAS URL valid for `UploadLinkLifetime` (15 minutes) for the
   blob `{organizationId}/{jobId}/{questionId}/{uploadId}/{fileName}`.
2. The bytes are PUT to that URL.
3. `POST .../uploads/{uploadId}/verify`: the API finds the blob under the route's organization and job, checks its real
   size and that its first bytes match its extension, hashes it (SHA-256), and deletes it if it fails.

With JavaScript, the `FileField` Svelte island (`packages/ui/src/questions/FileField.svelte`) does this from the
browser through the careers `/uploads` endpoint (a bare `fetch`, which keeps the page under its ~20 KB JavaScript
budget) and leaves only the upload id in the form.
Without JavaScript, the file posts with the step and the careers server does the same three calls. Either way the
candidate hears about a wrong or oversized file at the step where they chose it. Submission checks every file again,
stores an `ApplicationFile` per file and publishes one `ScanApplicationFile` message each; the worker's
`IMalwareScanner` is a placeholder (`NoMalwareScanner`, which records `NotScanned`) until a real scanner is wired in.

Because an upload id can only be claimed under the organization and job in the route, another tenant's upload, or
another job's, is simply not found.

Reviewers download a file the same way, without the bytes passing through the API.
`GET /v1/applications/{applicationId}/files/{fileId}` redirects to a read-only SAS URL for that one blob, valid for
`ScreenHire:Apply:DownloadLinkLifetime` (5 minutes). The link sets `Content-Disposition: attachment` with the
candidate's file name, so storage serves the document as a download and never renders it on the storage origin. A
file whose scan is `Pending` or `Infected` gets a 409 and no link. `Clean` and `NotScanned` files are offered.

The dashboard's file viewer (a file row's View, in a full-window dialog; a page never embeds a preview) reads a PDF
through the same route, in one component, `FileFrame`: it fetches the redirect target (storage's CORS rule allows
`GET`) and hands the bytes to a viewer in an `<iframe sandbox="allow-scripts" srcdoc>`. The frame has an opaque
origin, so nothing in it can reach the dashboard's cookies, DOM or API, and its document allows only its own script and
stylesheet by hash (`default-src 'none'`). The viewer is pdf.js, bundled into that one script by
`apps/dashboard/file-viewer-plugin.ts`; it draws pages to canvases, keeps their text for screen readers, and sends back
only its height and page count. Other file types, images included, are offered as downloads only.

## 4. Submission

`POST /v1/public/organizations/{slug}/jobs/{jobId}/applications` (anonymous, rate-limited per candidate address by the
`apply` policy) sets the slug's organization as the tenant, finds the job by id under it (another organization's job
is 404, a closed one 410), validates the answers against the job's questions (`ApplicationForm.Validate`) and the
AI-use policy (a declaration is required unless the policy is `Allowed`), checks the files, verifies the bot challenge,
and then, through the outbox in one transaction: finds or creates the candidate by email (an existing candidate's
details are never changed by a public form), creates the application in the first stage with its `Submitted` timeline
event, stores the files, and publishes `ApplicationSubmitted` (a tenant event, so open dashboards update) plus the scan
messages. A second application from the same email is 409, which the careers page shows as the normal confirmation.
The API's own 409 does say that an email applied to a job; it is rate limited, and the form, which is what
candidates and the public use, never shows it. A clash on the candidates index (a new email applying to two jobs at the
same moment) is an error the candidate can retry, never the "already applied" answer.

The careers server forwards the candidate's address in `X-Forwarded-For`. The API believes it only from
`ScreenHire:Auth:TrustedProxies` (loopback by default), so in production the careers servers' addresses go there.

## 5. Employer branding and the content security policy

An organization's `BrandColor` becomes a `BrandPalette` on the server (`src/ScreenHire.Domain/Organizations`):
`brand` and `onBrand` (white or ink, 4.5:1), `brandText` (4.5:1 on the page and on cards) and `brandUi` (3:1). A
colour that fails is darkened in OKLCH lightness, keeping its hue, until it passes. The public organization and board
responses carry the palette; the careers server turns it into a stylesheet named by a hash of its content
(`src/lib/brand.ts`), cached for a year. Brand never restyles status colours, the privacy notice or the footer. Owners
and admins set the colour with `PUT /v1/organization/branding`, which returns the derived palette, from the
dashboard's Settings > Branding page; the Development seed gives Brightwater one.

Careers pages run under a strict policy: Astro's `security.csp` (hashes for what Astro emits, `default-src 'none'`,
no `unsafe-inline`), stylesheets never inlined, the apply page adding `connect-src` for the upload origin (and
Turnstile's host when configured), and a header from `src/middleware.ts` for `frame-ancestors` and the rest that a
`<meta>` policy cannot carry.

## 6. Tenant hosts

`ScreenHire:Domains:CareersHostPattern` (read by the API and, as `ScreenHire__Domains__*` environment variables, by
the careers server) decides where an organization's pages live. `{tenant}.{CareersDomain}` puts each on its own host:
the middleware reads the organization from the request's Host header and renders `/jobs/x` as `/{tenant}/jobs/x`, and
pages build links without the prefix (`basePath(Astro.request, organization)`). Astro's own `Astro.url` cannot be used
for this: Astro builds it from the Host header only for hosts in `security.allowedDomains`, a build-time list, and
uses `localhost` otherwise. Endpoints (`/uploads`, the brand stylesheet) take the organization from the host as well
(`organizationOf`): Astro hands an endpoint the parameters of the route the path matched before the rewrite. On a
tenant host, a path that already starts with `/{tenant}` (a path-style link) gets a 301 to the same path without it. On the careers domain itself, the organization is in the path. Locally the AppHost sets
`CareersDomain` to the careers endpoint, so `http://brightwater.localhost:<port>/` serves Brightwater's board.

## 8. Follow-up questionnaires

After someone applied, a SendQuestionnaire automation can email them a link to a follow-up questionnaire
(workflow-engine.md §8): `/questionnaires/{token}` on the organization's careers board. The page is one native form
posted to the `questionnaire` Astro Action (`src/lib/questionnaire.ts`), so it works with JavaScript off and ships no
script at all. Its fields are the same shared question components as the apply form, rendered on the server; follow-up
questionnaires have no file questions, so there is no upload island.

- **Answers only.** No device check, no form telemetry and no bot challenge: the candidate saw the notice when they
  applied, and this page collects what they type into its fields and nothing about their browser (CLAUDE.md
  non-negotiable 4). The page says so above the button.
- **The link stays private.** `Cache-Control: no-store`, `noindex`, and `<meta name="referrer" content="no-referrer">`,
  so the token is never cached or sent on. The careers server refuses a token that is not Data Protection's base64url
  text before it builds an API path from it.
- **Plain endings.** An answered link says "You have answered these questions" (200), an expired one "This link has
  expired" with what to do (410), one whose questionnaire, job or application has ended "These questions are no longer
  open" (410), and an unknown or forged one "We could not find these questions" (404). A problem with an answer comes
  back on the same page with what was typed, as on the apply form.

## 9. Owner actions before production

- Storage account: create the applicant uploads container, a CORS rule allowing `PUT` from the careers hosts and `GET`
  from the dashboard host (the file viewer), and a lifecycle rule that deletes unclaimed uploads (blobs with no
  application) after a day. Development does the first two at startup
  (`ApplicantUploads.ProvisionDevelopmentStorageAsync`).
- Add the careers servers to `ScreenHire:Auth:TrustedProxies`.
- Behind a CDN or load balancer, set Astro's `security.allowedDomains` to the careers hosts when building
  `apps/careers`. Astro trusts `X-Forwarded-For` (the candidate's address, which the API rate-limits on) only for those
  hosts; without it every candidate shares the proxy's address. Until then Astro also sees its own origin as
  `localhost`, so a browser that sends `Origin` without `Sec-Fetch-Site` (older Safari) gets 403 from Astro's form
  origin check on every step.
- The lifecycle rule above is what makes the privacy page's "deleted after a day" true for files never sent with an
  application; nothing in the application deletes them.
- Keep server and CDN access logs for no more than 30 days: the privacy page promises it, and nothing in the
  application enforces it.
- Optionally configure Turnstile keys, and a malware scanner behind `IMalwareScanner`.
