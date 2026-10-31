---
type: Design
title: "Careers apply flow"
description: "The candidate apply page: the notice before any collection, steps that work without JavaScript with one section of questions a page, direct-to-storage resume uploads, the public submission endpoint, per-tenant branding stylesheets under the content security policy, tenant careers hosts, saving the form to finish later through an emailed link, and the follow-up questionnaire page a signed link opens."
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
  - resource: apps/careers/src/lib/drafts.ts
  - resource: apps/careers/src/components/StepIndicator.astro
  - resource: packages/ui/src/questions/PageProgress.svelte
  - resource: packages/ui/src/questions/model.ts
  - resource: apps/api/Applications/ApplicationDrafts.cs
  - resource: apps/api/Endpoints/PublicApplicationDraftEndpoints.cs
  - resource: src/ScreenHire.Domain/Applications/ApplicationDraft.cs
  - resource: src/ScreenHire.Application/Applications/DraftRetention.cs
---
# Careers apply flow

> Status: v1 (2026-10-20). Code: `apps/careers` (pages, `src/actions`, `src/lib/apply*.ts`), `apps/api`
> (`Endpoints/PublicApplicationEndpoints.cs`, `Applications/*`), `src/ScreenHire.Domain/Applications`. Decisions it
> follows: ADR-0003 (Astro, plain forms, iframe widget), ADR-0004 (evidence, not verdicts), `docs/research/legal.md`
> (notice, accommodation), design-system §2.4, §7 and §8.

A candidate goes from a job page to a sent application a page at a time, with or without JavaScript, and is told what
is checked before anything is collected. They can save the form and finish it later from a link in their email (§7). The careers server talks to the API on the candidate's behalf; the browser
never calls the API.

## 1. Pages and steps

| Path (under `/{organization}`, or `/` on a tenant host) | What it is |
|---|---|
| `/jobs/{job}` | The job, the "How we keep this fair" notice and an Apply link. 410 once the job closes, 404 if unknown. |
| `/jobs/{job}/apply` | The form: **details** (notice, assistive-technology switch, first name, last name (optional), email, phone, resume), one **page** per section of the job's questionnaire (none when it has no questions beyond the resume; §7), **review** (summary, AI-use declaration, the optional "Keep my details for future roles" box, send). `?draft={token}` fills it from a saved draft. |
| `/jobs/{job}/applied` | The confirmation, after a POST-redirect-GET. The same page whether the application is new or the email had already applied, so the form never reveals who applied. |
| `/privacy` | "What we collect and why", the full notice. Its version (`NOTICE_VERSION` in `src/lib/apply.ts`) is recorded on every application. |
| `/brand/{hash}.css` | The employer's brand stylesheet (section 5). |
| `/uploads` | JSON endpoint for the upload island (section 3). |
| `/telemetry?job={id}` | Relay for the telemetry module's beacons to the API (verification-engine.md §10.2, "Form telemetry"). |
| `/questionnaires/{token}` | A follow-up questionnaire, opened from the signed link a SendQuestionnaire automation emailed (section 8). |
| `/questionnaires/sent` | Where answered follow-up questions land, after a POST-redirect-GET, with no token in the address. |
| `/talent-pool/{token}` | Where a candidate stops the employer keeping their details for future roles, from the signed link in a talent pool invitation ([talent pool](talent-pool.md)). One native form, no JavaScript, `no-store`, `noindex` and `no-referrer`. |
| `/talent-pool/stopped` | Where stopping lands, after a POST-redirect-GET, with no token in the address. |

Each step is a native `<form method="POST" enctype="multipart/form-data">` posted to the `apply` Astro Action
(`src/actions/index.ts`), which re-renders the same page with the next step. What the candidate has entered travels
between steps in one hidden field (`state`); the server re-reads and re-checks it on every post, and checks every step
again before sending. Back, "Change" and "Save and finish later" keep what was typed without checking it. Field errors
show inline and in an error summary at the top that links to each field and takes focus.

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
AI-use declaration and details, `PrivacyNoticeVersion`, `Source = CareersSite` (with `SourceSite`, the organization's
own site, when the form was in the embeddable widget: [careers widget](careers-widget.md)), and two automation signals
for the verification engine:

- `HoneypotFilled` (`AUTO_HONEYPOT`): an off-screen field that only bots fill;
- `BotCheck` (`AUTO_TURNSTILE_FAIL`): Cloudflare Turnstile's result when `ScreenHire:Turnstile:SiteKey` and `SecretKey`
  are set, otherwise `NotChecked`. Off locally and in tests.

Neither refuses a submission (CLAUDE.md non-negotiable 2), and knockout answers are stored like any other.

With a careers-site application the API also stores a `SubmissionObservation` for the verification engine
(verification-engine.md §10): the candidate's address (from `X-Forwarded-For`), their browser's user agent, which the
careers server passes on as `userAgent` because the API only sees the server's, and `pageTimes`, the active seconds on
each page as the careers server measured them (§7; kept in the step state, so they round-trip through the browser and
are only a hint, and never sent with the switch on). The telemetry module fills `automationMarkers` and `deviceFingerprint` on the review step, and the careers
server passes them on with the session token and the module's last snapshot. The API hashes a fingerprint per
organization and never stores it as sent, and keeps it only on the job's device-check basis: where that is `Consent`
(the EU and EEA by default), the details step shows an optional "Check this device for repeat applications" box and the
fingerprint is kept only when it is ticked. The observation and the telemetry are deleted after 30 days. The notice and
the privacy page say all of this, and that the employer may email a follow-up questionnaire whose page keeps the
answers only, that a saved draft keeps the details and answers only, for 30 days, and that an application through
the form on the employer's own website notes which website (`NOTICE_VERSION` `2026-10-31.2`).

The review step also asks, optionally and unticked, "Keep my details for future roles", with one line saying the
employer keeps the application for its talent pool period (`talentPoolMonths` on the application form, 12 months by
default) and may invite them to apply for other roles, and that every invitation has a link to stop. The careers server
sends `keepForFutureRoles`; the API records the permission, with that period, on the application only, never on the
candidate ([talent pool](talent-pool.md)). The privacy page's "Future roles" section says the same
(notice version `2026-10-30.1`).

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

A recruiter adding a candidate by hand uploads their resume the same way from the dashboard:
`POST /v1/jobs/{jobId}/applications/resume-uploads` (Add candidates, antiforgery) checks the name and size against the
job's resume question and returns the same create-only link, the browser PUTs the bytes, and
`POST /v1/jobs/{jobId}/applications` with `resumeUploadId` runs the same check and keeps the file as the resume.

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

An organization's branding (colours, heading font, logo, favicon, sharing image) is
[branding.md](branding.md): colours are checked against WCAG 2.2 AA before they save, and images are served on the
careers origin at `/brand/images/{id}.{png|jpg}`. The public organization and board responses carry `branding`; the
careers server turns its palette, header colours and font into a stylesheet named by a hash of its content
(`src/lib/brand.ts`), cached for a year. Brand never restyles status colours, the privacy notice or the footer. The
Development seed gives Brightwater a brand colour.

Careers pages run under a strict policy: Astro's `security.csp` (hashes for what Astro emits, `default-src 'none'`,
no `unsafe-inline`), stylesheets never inlined, the apply page adding `connect-src` for the upload origin (and
Turnstile's host when configured), and a header from `src/middleware.ts` for `frame-ancestors` and the rest that a
`<meta>` policy cannot carry. `frame-ancestors` is `'self'` plus the sites the organization allows to embed its careers
pages, or only the one the page is embedded in ([careers widget](careers-widget.md) section 3).

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

## 7. Pages, progress and saved drafts

**Pages.** A questionnaire's named sections (`sections` on the hiring definition's questionnaire, workflow-engine.md §7)
are its pages, one each, in order. Without sections the questions split into pages of the organization's questions per
page (Settings > Organization, 1 to 50, default 8; `questionsPerPage` on the public form). One function,
`questionPages` in `packages/ui/src/questions/model.ts`, pages the apply form, the follow-up page and the dashboard's
preview, so the preview is the form. The resume stays on the details step, and a section left empty by that is
dropped. Steps are `details`, `page-1` to `page-N` and `review`; each page's heading is its section's name
("Questions" for an automatic page). Each page posts the ids it rendered (`pageQuestions`), and the server reads only
those, so an answer is never wiped when the pages change between showing a page and posting it.

**Progress.** `PageProgress` (`packages/ui/src/questions`, rendered on the server with no JavaScript) shows a bar of one
segment per step, then "Step 2 of 4" and "9 of 24 answered", counting the form's questions with the resume and not the
contact fields. The review lists the answers page by page, each with its own Change; a page opened from the review has
"Back to review" as its primary.

**Active time per page.** The page stamps `pageShownAt` into the state when it renders a step; any post of that step
(Next, Back, Change, Save) adds the time since, at most 30 minutes, to `pageSeconds` for that step. A break between
saving and coming back is never inside a page, so it never counts. Submission sends the totals as `pageTimes`, which
the API stores on the submission's observation (dropping anything out of range rather than refusing the application)
for the submit-velocity check (verification-engine.md §10.2). Nothing new runs in the browser: this is the careers server's
own measure.

**Save and finish later.** On the pages and the review, when the form says `saveDraftEnabled` (an `IEmailSender` is
registered and a careers host pattern is configured; otherwise the button is hidden), "Save and finish later" posts
the draft and keeps the candidate on the step with a notice: "Saved. We emailed a link to {email} to finish by
{date}.", and that files are attached again. Saving again says the emailed link opens these answers, since no new
email goes. Only a 404 or 410 on saving again makes a new draft; any other failure keeps the old one and says so, so a
busy moment never mails a second link. The API (`PublicApplicationDraftEndpoints`, `ApplicationDrafts`):

- `POST .../jobs/{jobId}/application-drafts` checks the answers' shape only (`ApplicationForm.ValidateDraft`: nothing
  required yet, no file answers), creates an `ApplicationDraft` and sends the link inside the request, as sign-in links
  are, because the token lets whoever holds it read the draft and so never sits in the outbox. The email is
  ScreenHire's own wording, not an organization template. An address gets at most 5 draft links a day in one
  organization (a per-address advisory lock, as for sign-in links); the sixth save is a 429. The answer is the same
  whether or not the address applied before. If the email cannot be sent, the draft is deleted and the request fails,
  so a draft never sits without its link or counts against the cap.
- Accepted limits. Saving a draft takes no bot check, so anyone can make the API email a link to an address they
  type: the per-address cap and the per-network rate limit bound it, and the email says to ignore it if you did not
  start the application. The sixth save's 429 tells the caller that address has saved several drafts in that
  organization today, which says nothing about whether it applied.
- The token is Data Protection under its own purpose (`ScreenHire.ApplicationDraft.Link.v1`), naming the organization
  and the draft, like follow-up links. A draft is found by its token, never by its email, so nobody who knows an
  address can replace the answers saved under it. The careers page keeps the token in its state, so saving again
  updates the same draft (`PUT .../application-drafts/{token}`, no second email) while the email is the one its link
  went to; a different address makes a new draft.
- A draft keeps the details, the answers to non-file questions, the assistive-technology switch (so a resumed form never
  starts telemetry on someone who switched it on) and the AI-use answer. Never files, telemetry, the device check, a
  fingerprint or the time on the pages.

**Resume.** The link is the job's apply page with `?draft={token}`. The page checks the token is base64url text, reads
the draft (`GET .../application-drafts/{token}`; another job's draft is "not found"), and opens the details step filled
in, with a fresh telemetry session, so the notice shows again before anything is collected. The page sends
`no-referrer`, and the first post drops the token from the address. An expired link says "This link has expired", an
unknown one "We could not find your saved answers", and both offer a fresh form.

**Deletion.** Sending the application deletes the job's drafts for that email in the same save, and the one saved last
tells the observation that the application was resumed and which answers it carried over (verification-engine.md §10.2).
`PurgeExpiredApplicationDrafts` runs daily at 04:07 UTC and deletes drafts 30 days after their first save; later saves
never move that date. A link stays readable for 30 days past its draft's expiry, but once the sweep has run the draft
is gone, so the page says "We could not find your saved answers" (whose text covers answers sent or deleted) rather
than "expired". Two sends of one resumed form at the same moment can fail the second on the draft delete; sending
again succeeds or reports the application as already sent.

**Knockouts.** A draft is never an application: saving one raises no event and runs no automation or verification check,
and the careers server never learns knockout settings. A knockout answer on an early page therefore ends nothing
before the candidate sends the application; sent, it is judged as on any application (workflow-engine.md §2).

## 8. Follow-up questionnaires

After someone applied, a SendQuestionnaire automation can email them a link to a follow-up questionnaire
(workflow-engine.md §8): `/questionnaires/{token}` on the organization's careers board. Each page is a native form
posted to the `questionnaire` Astro Action (`src/lib/questionnaire.ts`), so it works with JavaScript off and ships no
script at all. Its fields are the same shared question components as the apply form, rendered on the server; follow-up
questionnaires have no file questions, so there is no upload island. It pages like the apply form (§7): one section a
page, or pages of the organization's size, with the progress line when there is more than one page, Back, and "Send
answers" on the last page.

- **Save and finish later.** The answers are kept on the request (`PUT .../questionnaires/{token}/draft`, answers
  only, checked for shape), and the same link opens them again while it works; the page says so. No second email: the
  link the candidate holds is the signed link to finish later. Answering, or 30 days after the first save, deletes them.

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

- Storage account: create the applicant uploads container, a CORS rule allowing `PUT` from the careers hosts, and `GET`
  and `PUT` from the dashboard host (the file viewer, and a resume a recruiter adds by hand), and a lifecycle rule that deletes unclaimed uploads (blobs with no
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
