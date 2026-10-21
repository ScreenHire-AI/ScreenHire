# Integrity Engine — Applicant Authenticity & AI-Assistance Signals

> Status: **v1 application stage built (2026-10-20)**, content forensics included (§10.10); §10 describes the implementation. Design draft v0.1
> (2026-10-18), grounded in `docs/research/legal.md` and `docs/research/competitors.md`.
> This is ScreenHire's core differentiator: an integrity layer from first click to first day that recruiters and
> candidates can both understand.

## 1. Goals and non-goals

**Goals**
- Give recruiters **explainable, evidence-backed** indicators of whether an applicant is a real, unique, present person,
  and how much of their application appears machine-generated.
- Cover the **whole funnel** (careers page → application → post-submission analysis → verification → interview), not a
  single point-in-time score.
- Be **lawful by design** in the US + UK/EU so compliance becomes a selling point instead of a liability.

**Non-goals (hard lines — see ADR-0004)**
- No opaque "% AI-written" or "fraud score" number. No automatic rejection driven by integrity signals.
- No inference of emotion, stress, honesty, intent or personality from text, typing, face or voice.
- No biometric templates; no identifying a person from behaviour.
- No scraping LinkedIn or any third-party site.

## 2. Two separate assessments

Competitor research shows most tools blur "used AI" and "is a fraud". ~74% of genuine US job seekers use AI. So the
engine produces **two independent assessments**, each with a level and reason codes:

| Assessment | Question | Levels | Typical evidence |
|---|---|---|---|
| **Authenticity** | Is this a real, unique, present person who is who they claim to be? | `No concerns` · `Review suggested` · `Verification recommended` | Data-centre IP + mismatched claimed location; disposable or 2-day-old email; VoIP phone; automation markers; same device across many applications; stand-in at interview |
| **AI Assistance** | How much of the content appears generated or templated? | `Not indicated` · `Some indicators` · `Strong indicators` | Whole answers pasted in seconds; answers near-identical to other applicants; resume built by a generator; hidden prompt-injection text |

AI Assistance is **informational and policy-relative**: each employer sets whether AI help is allowed, allowed with
disclosure, or not allowed for a given job. Candidates can **declare** AI use (the "declared-AI lane"), which changes how
indicators are presented.

### Level rules
- A level above the lowest requires **≥ 2 independent signal categories** (e.g., network + identity). **Telemetry alone
  can never raise a level** (assistive tech, dictation and autofill look like paste/tab-switching).
- Each signal carries a weight. Levels are derived by documented, versioned rules (not an ML black box) so we can
  explain, audit and bias-test them. ML models may produce *signals*; rules produce *levels*.
- Every level shows its **reason codes**, the **evidence**, and any **counter-evidence** (e.g., "VPN detected — but
  claimed location matches VPN exit country").
- Every level also carries a **plain-language summary and a suggested next step** ("Ask for a quick video call before
  the technical screen", "Request ID verification", "No action needed"). Market research (Kyle & Co landscape report,
  60+ vendors) names the gap as *small teams who can't interpret raw fraud signals* — the explanation + next step is
  ScreenHire's answer to it.

## 3. Collection timeline (what, when, on which legal footing)

| Phase | Collected | Not collected | Basis / notes |
|---|---|---|---|
| **Browse** (careers pages, job pages) | Server access logs (IP, UA, path, timestamp), CDN/WAF bot signals, rate-limit counters | No fingerprint, no telemetry, no tracking cookies | Security / legitimate interest. Short retention (≤ 30 days). |
| **Application start** (candidate clicks Apply) | **Notice shown first**: privacy notice + plain-language telemetry explanation + **accommodation toggle**. Then: session cookie; device fingerprint (per-employer salted hash); behavioural telemetry *aggregates* | Raw keystrokes, key content, mouse paths, screen recording | EU: ePrivacy consent or "strictly necessary for fraud prevention" per jurisdiction config. UK: PECR Sch. A1 fraud exemption. |
| **Submission** | Answers, resume file (+ file metadata), contact details, IP intelligence lookup, email/phone checks | Browser geolocation API | Employer = controller, ScreenHire = processor (Art. 28). |
| **Post-submission analysis** | Content forensics, within-employer duplicate checks, optional network lookups (§6) | Training data for generalized models | Async, results attached as signals. |
| **Step-up verification** (only when level or template requires) | ID + liveness via vendor → **pass/fail only** stored | Images/templates beyond vendor's few-day deletion window | GDPR Art. 9 explicit consent; BIPA written release; human-check alternative always offered. |
| **Interview** (later phase) | Media-artifact signals: virtual camera, injected audio, face-swap artifacts, second speaker, continuity with verified ID | Emotion, affect, gaze-based "cheating intent" | AI Act Art. 5(1)(f) red line. IL AI Video Interview Act / MD consent. |
| **Correspondence** (later phase) | Candidate threads in the employer's connected recruiting mailbox, post-delivery | Tracking pixels; accommodation/health emails (routed out) | Employer-authorized API only. No training on content. |

## 4. Signal catalogue (MVP subset in **bold**)

Each signal: `code`, `category`, `weight`, `evidence` (structured JSON), `source`, `observedAt`, `confidence`,
`explanationTemplate`, `counterEvidence[]`, `accessibilitySensitive` (suppressed when accommodation is on).

**Network**
- **`NET_DATACENTER_IP`** — request from hosting/cloud ASN.
- **`NET_ANON_PROXY`** — VPN / residential proxy / Tor (context only; never a sole reason).
- **`NET_GEO_CLAIM_MISMATCH`** — claimed location vs coarse IP-geo country/region mismatch.
- `NET_TZ_MISMATCH` — browser timezone vs IP region.

**Identity & contact**
- **`ID_EMAIL_DISPOSABLE`**, **`ID_EMAIL_NO_MX`**, `ID_EMAIL_NEW` (where an age signal is lawfully available).
- **`ID_PHONE_VOIP`** / `ID_PHONE_INVALID`.
- **`ID_DUPLICATE_IN_ORG`** — same email/phone/device/resume hash across applications in the same employer.
- `ID_VERIFICATION_FAILED` / `ID_VERIFICATION_PASSED` (counter-evidence).

**Automation & session**
- **`AUTO_WEBDRIVER`** — automation framework markers (navigator.webdriver, headless traits, CDP artefacts).
- **`AUTO_SUBMIT_VELOCITY`** — application completed faster than humanly plausible for its length.
- **`AUTO_HONEYPOT`** — hidden honeypot field filled.
- `AUTO_TURNSTILE_FAIL` — CDN bot challenge failed/suspicious.

**Behavioural telemetry (aggregates only; `accessibilitySensitive = true`)**
- **`BEH_PASTE_DOMINANT`** — ≥ X% of an answer's characters arrived via paste.
- **`BEH_FOCUS_SWITCHING`** — high focus/tab-switch count during free-text answers.
- `BEH_CADENCE_ANOMALY` — session-level typing-rhythm summary outside human ranges (never stored per person, never
  compared across applicants).

**Content forensics**
- **`CNT_PROMPT_INJECTION`** — hidden instructions in resume (white/tiny text, off-page text, "ignore previous
  instructions"-style payloads). High-confidence, high-value signal.
- **`CNT_NEAR_DUPLICATE_ANSWER`** — answer near-identical to other applicants' answers (SimHash/embedding) within the
  employer; across employers only via the network (§6).
- **`CNT_RESUME_GENERATOR`** — file metadata shows generator tooling / creation moments before submission.
- `CNT_INCONSISTENCY` — LLM-checked contradictions between resume, answers and candidate-supplied LinkedIn export,
  **each contradiction cited with both source spans**.
- `CNT_TEMPLATED_ANSWER` — answer matches known generic LLM response patterns for that question (informational).

**Interview (later)** — `INT_VIRTUAL_CAMERA`, `INT_INJECTED_AUDIO`, `INT_FACE_SWAP_ARTIFACT`, `INT_SECOND_SPEAKER`,
`INT_IDENTITY_DISCONTINUITY`.

## 5. Architecture

```
 Candidate browser                      ScreenHire
 ─────────────────                      ─────────────────────────────────────────────
 Astro apply page ──(HTML form post)──▶ API: Applications ──▶ Postgres (application,
   └ telemetry.ts (framework-free)          │                   signals, evidence)
       computes aggregates in-browser       │ outbox event: ApplicationSubmitted
       └─(beacon: aggregates only)──▶ API: Telemetry ingest
                                            ▼
                                     Worker: Integrity pipeline
                                       1. Collectors (sync, cheap): network intel,
                                          email/phone checks, automation markers
                                       2. Analyzers (async): resume parse + forensics,
                                          near-dup (pgvector / SimHash), LLM consistency
                                       3. Network lookup (opt-in tenants, §6)
                                       4. Level rules engine (versioned) → assessments
                                            ▼
                                     Domain events: IntegrityAssessed
                                       → Workflow engine (route to review / step-up)
                                       → Dashboard (SignalR push)
```

- **Telemetry module** (`packages/telemetry`): framework-free TS, < 5 KB br. Computes aggregates client-side; raw
  timings never leave the browser. Disabled entirely when the candidate turns on the accommodation toggle (the toggle
  itself is never a signal). Served from ScreenHire's origin; embedded widget is an **iframe** so host pages cannot
  forge events or read PII.
- **Pipeline** stages are idempotent jobs keyed by `(applicationId, analyzer, analyzerVersion)` so re-runs after rule
  changes are safe and auditable.
- **LLM usage**: extraction and consistency checks with structured outputs and *quoted spans*; never asked "is this AI
  written?". Prompts and model versions are recorded with each signal for audit.
- **Rule engine**: levels computed from signals by a versioned ruleset; every assessment stores `rulesetVersion`.

## 6. Cross-employer Signal Network (opt-in, pseudonymised)

Owner decision (2026-10-18): build cross-tenant detection **if it can be de-identified**. Design:

- **What is shared**: keyed HMAC-SHA256 of normalised hard identifiers — email (lower-cased, plus-tag stripped), phone
  (E.164), device fingerprint hash, resume file hash, and SimHash buckets of free-text answers. **Never** names,
  content, outcomes, or which employers.
- **Key management**: network HMAC key held in a KMS/secret store, never exported, rotated on a schedule (old-key
  lookups expire with retention).
- **Query result**: counts only — "this identifier appeared in *N* applications across *M* organisations in the last
  30 days". Shown only when **M ≥ 3** (k-anonymity floor) so a result can't reveal a specific other employer.
- **Retention**: 90 days rolling (TTL), then purged.
- **Participation**: per-tenant opt-in; a tenant must opt in to *contribute* in order to *query*.
- **Legal reality**: hashed identifiers remain **personal data for ScreenHire** (we hold the key), so for the network
  ScreenHire is a **controller** (own legitimate-interest basis — fraud prevention, Recital 47; own Art. 14 notice; DPIA).
  US: assess FCRA "consumer report" risk — mitigate by using network results only to trigger **step-up verification**,
  never as a decision input.
- **Gate**: built behind a feature flag in the MVP architecture; **enabled in production only after legal review +
  DPIA sign-off**.

## 7. Recruiter & candidate experience

- **Recruiter**: assessment chips on every candidate card (`Authenticity: Review suggested · 3 reasons`), a detail
  panel with evidence + counter-evidence, and actions: *Dismiss (reason required)*, *Request verification*, *Add note*.
  Every action is audit-logged. No "reject" button sits in the integrity panel.

  Built in v1 (`apps/dashboard`): Authenticity and AI assistance chip columns in every candidate table, sortable by
  level and filterable from an "Integrity" menu, with calm levels quiet and the reason count as a pill; a job overview
  with a numeric legend (each entry filters the table) and an `aria-hidden` stacked bar; the "Needs review" and "Verification recommended" saved views (authenticity above its
  lowest level, not dismissed; AI assistance is information, not a queue); and the evidence panel on the application
  page. The panel shows each assessment's summary, suggested next step (one quiet line for a calm result), reasons
  (signals that count toward the level) and context signals with their evidence drawn by kind (§10.1) and
  counter-evidence, the job's AI policy and any dismissal. The candidate's AI declaration is in the page's Declarations
  panel, not repeated here. Its action is *Dismiss with a reason* (per assessment above the lowest level); the Review
  log panel beside it lists every dismissal and note and holds *Add note*. *Request verification* is hidden until
  phase 2 builds it. The footer reads "No automated decision has been made. Every action is logged."
  `IntegrityAssessed` refetches the chips, overview and panel live. AI chips use one colour and no mark, so they read
  as information; "Not indicated" is neutral. While an integrity filter is set, the job page's bulk move offers no
  Rejected or Withdrawn stage, and a
  new filter clears the selection (`bulkMoveStages` in `#lib/integrity.ts`): a filtered "select all" must not become
  rejection driven by integrity signals. Moving one candidate stays a person's decision about that candidate.
- **Candidate**: notice before collection, accommodation toggle, AI-use declaration, a "why we asked you to verify"
  page, and an **appeal / human-review request** flow (UK Art. 22C, CCPA ADMT, Colorado notices).

## 8. Governance

- Bias monitoring: LL144-style impact ratios on level distributions per protected-category proxy where lawfully
  available; quarterly review; published false-positive estimates (a market gap — no vendor does this).
- Documentation pack per customer: DPIA template, LIA, CPPA risk-assessment template, candidate notice templates.
- Build toward AI Act provider obligations (risk management, logging, transparency, human oversight, post-market
  monitoring) ahead of **2 Dec 2027**.

## 9. MVP scope (proposed)

> Owner's original v1 definition (2026-09-23): an **application-stage** human-vs-AI assessment from three inputs —
> (1) where the application came from (source, channel, network), (2) the documents submitted, (3) an evaluation of the
> answers. ID/background checks "might come later".
>
> **Decision (owner, 2026-10-18): v1 is application-stage only.** No biometrics in v1.

**Phase 1 (MVP) — application stage:** notice + accommodation toggle; telemetry aggregates; network/identity/automation
signals; prompt-injection + generator-metadata + within-employer near-duplicate detection; LLM consistency (resume ↔
answers ↔ uploaded LinkedIn export); two assessments with reason codes, plain-language summary and suggested next step;
review actions + audit log; Signal Network built but **flagged off**.

**Phase 2 — verification:** step-up ID + liveness via one wrapped vendor (Persona / Veriff / Incode), BIPA release and
GDPR Art. 9 consent flows, human-check alternative.

**Phase 3 — interview integrity:** ScreenHire interview links with media-artifact checks (virtual camera, injected audio,
face-swap, second speaker, continuity with verified ID).

**Later:** mailbox correspondence analysis, Signal Network enablement after legal review + DPIA, published accuracy
reports.

## 10. Implementation (v1, application stage)

What is built, as of 2026-10-20. Code: `src/ScreenHire.Domain/Integrity` (signal model, catalogue, ruleset),
`src/ScreenHire.Application/Integrity` (analyzers, pipeline, retention, synthetic applicants; content forensics in
`Content/`), `src/ScreenHire.Infrastructure/Integrity` (local data sources; document extraction in `Documents/`, the
model access in `Llm/`), `apps/api/Endpoints/IntegrityEndpoints.cs` (API), `packages/telemetry` and
`apps/api/Endpoints/PublicTelemetryEndpoints.cs` (form telemetry),
`apps/dashboard/src/lib/integrity.ts` and `apps/dashboard/src/lib/components/integrity-*.svelte` (dashboard, §7).

### 10.1 Signals and evidence

An `IntegritySignal` row has the fields of §4: `code`, `category`, `assessment` (which of the two it feeds), `weight`
(`Low`, `Medium`, `High`), `confidence` (same three), `source` and `sourceVersion` (the analyzer), `observedAt`,
`accessibilitySensitive`, `contextOnly`, `explanation` (the catalogue's template with this observation's values filled
in), `evidence[]` and `counterEvidence[]`. `SignalCatalogue` defines every code once: category, assessment, default
weight, title and explanation template. An analyzer only says what it saw.

Evidence is typed, so the dashboard renders any signal, including later content and telemetry signals, without code
per signal. One flat record (`SignalEvidence`) with `kind` and the members that kind uses:

| `kind` | Members | Example |
|---|---|---|
| `Fact` | `label`, `value` | Network operator: Example Cloud Hosting (AS64497) |
| `Quote` | `label`, `text`, `source` | a hidden instruction quoted from page 2 of the resume |
| `TextComparison` | `label`, `left`, `right` (each `label` and `text`) | two near-identical answers side by side |
| `Metric` | `label`, `amount`, `unit`, `reference`, `referenceLabel` | 6 seconds; plausible minimum 32 seconds |
| `List` | `label`, `items` | automation markers: webdriver, headless |

A metric is a measurement, never a likelihood. Counter-evidence is a plain-language `explanation` with optional
evidence of its own.

Categories: `Network`, `Identity`, `Automation`, `Behavioural` (telemetry), `Document` (file metadata and hidden text)
and `Content` (answer and resume text). `Document` and `Content` are separate so the content analyzers can corroborate
each other: one is the file, the other the words.

### 10.2 Pipeline

`ApplicationSubmitted`'s worker handler cascades `AnalyzeApplicationIntegrity(organizationId, applicationId)`, a
separate message so the pipeline retries and fails apart from the workflow engine. Its handler (`IntegrityPipeline`):

1. runs each registered `IIntegrityAnalyzer` whose `(application, name, version)` has no `integrity_analyzer_runs`
   row, and stores the run (with a `note` when it could not look, such as "No IP intelligence source is configured")
   and its signals. A unique index on the run is the idempotency key;
2. derives both assessments with the ruleset. An assessment row is written only when its inputs changed: its
   `fingerprint` (ruleset version, signal ids, policy, declaration, accommodation) has a unique index;
3. when a level changed (or on the first assessment), appends an `IntegrityAssessed` timeline entry with both level
   names, and publishes the `IntegrityAssessed` tenant event and `RunAutomations(IntegrityAssessed)` for the workflow
   engine. New evidence at the same levels is stored as a new assessment row and not announced: content forensics
   reassess as each stage lands, and an automation that assigns a reviewer must run once, not once per stage.

A later analyzer (content forensics) registers as another `IIntegrityAnalyzer`, or publishes
`AnalyzeApplicationIntegrity` again when its own asynchronous work lands. Bumping an analyzer's version re-runs it.

| Analyzer | Signals | Reads | When it sees nothing |
|---|---|---|---|
| `network` | `NET_DATACENTER_IP`, `NET_ANON_PROXY` (context only), `NET_GEO_CLAIM_MISMATCH` | the submission's IP address; IP intelligence | no address (recruiter-added), no source configured, nothing known |
| `identity` | `ID_EMAIL_DISPOSABLE`, `ID_EMAIL_NO_MX`, `ID_PHONE_INVALID`, `ID_PHONE_VOIP` | email and phone | MX not checked, number without a country code |
| `automation` | `AUTO_HONEYPOT`, `AUTO_WEBDRIVER`, `AUTO_SUBMIT_VELOCITY`, `AUTO_TURNSTILE_FAIL` (context only) | honeypot, bot check, markers, form start time | |
| `duplicates` | `ID_DUPLICATE_IN_ORG` | other candidates' email (plus tag ignored), phone, device hash, resume hash, same organization only | |
| `behavioural` | `BEH_PASTE_DOMINANT`, `BEH_FOCUS_SWITCHING` | the form telemetry bound to the application (`DatabaseTelemetrySource`, rules in `TelemetryRules`) | no telemetry: JavaScript off, the assistive-technology switch on, or nothing measured |
| `documents` | `CNT_RESUME_GENERATOR`, `CNT_PROMPT_INJECTION` | the extracted documents (§10.10) | no files; waits until every file is read |
| `answers` | `CNT_NEAR_DUPLICATE_ANSWER`, `CNT_TEMPLATED_ANSWER` (informational) | free-text answers, other candidates' answer fingerprints | no free-text answer of 20 words or more |
| `consistency` | `CNT_INCONSISTENCY` | the consistency check (§10.10) | waits until the check has an outcome; one that could not run leaves a note |

An analyzer can answer "not ready" (`AnalyzerResult.NotReady`): its input is still being prepared. The pipeline records
no run for it, so it runs again when the stage that prepares its input publishes `AnalyzeApplicationIntegrity`. The
content analyzers use it; the others never do.

Applications assessed before the telemetry module have a `behavioural` version 1 run that saw nothing; version 2
reads telemetry. There is no typing-rhythm field and no `BEH_CADENCE_ANOMALY` rule: timing between keys is keystroke
dynamics, a behavioural biometric, and v1 has none (CLAUDE.md non-negotiable 3). Adding it needs its own legal review.

#### Form telemetry

The careers apply page runs `packages/telemetry`, a framework-free module of about 2.2 KB brotli (the package's build
fails over 5 KB). Per form field it counts typed characters, pasted characters, pastes, focus and blur, time focused
and whether the browser autofilled it; per page, how often the page was hidden and the automation markers the browser
reports. It reads only a field's `name`, the length of its value and the kind of edit the browser reports
(`InputEvent.inputType`). It has no keyboard listener and never reads `data`, the clipboard, the value or the time
between keys, so key values and content cannot leave the browser.

- **When.** The notice and the assistive-technology switch sit above the first field. On that step the module records
  nothing until the candidate first uses the form; later steps start at once. With the switch on, the careers server
  does not start it, and switching it on mid-step discards what was counted. A page view that measured nothing sends
  nothing.
- **Session.** `GET .../application-form` carries `telemetry.sessionToken`, a Data Protection token valid for 12 hours
  that names the organization, the job and a new session id. The careers server keeps the first one in the form's
  state.
- **Delivery.** Each time the page is hidden or left, the module sends a cumulative snapshot of that page view with
  `navigator.sendBeacon` to the careers server's `/{organization}/telemetry?job=<id>`. The relay takes only
  same-origin `text/plain` bodies that state their length, checks them with a strict schema, and forwards them to
  `POST /v1/public/organizations/{slug}/jobs/{jobId}/telemetry`. The API refuses unknown members at every level, field
  ids that are not the job's questions or the form's own fields, unknown automation markers, out-of-range counts, a
  token for another job, and bodies over 32 KB (413). Its own rate limit (`ScreenHire:Apply:TelemetryPermitsPerMinute`,
  120) keeps beacons from using up the candidate's apply allowance. There is one row per page view: a snapshot with a
  higher `sequence` replaces the stored one (an upsert), and nothing changes once the session is bound.
- **Binding.** The last step's snapshot also travels in the form post (`telemetry`), so it is stored before analysis
  starts. Submission names the session (`telemetrySessionToken`) and, in the application's own transaction, binds the
  session's snapshots to the application before `ApplicationSubmitted` is published. With the switch on, it deletes
  them instead. A bad token, or a snapshot that fails the ingest checks, never refuses an application; it is dropped.
  The snapshot's shape is part of the request contract, so an unknown member in it is a 400, which the careers server's
  strict parsing never sends.
- **Rules** (`TelemetryRules`, free-text answers only, autofilled fields left out). `BEH_PASTE_DOMINANT`: an answer of
  at least 40 entered characters of which 80% or more were pasted; the evidence is the pasted and entered character
  counts and the number of pastes. Entered characters are the growth in the field's length, so a paste over selected
  text counts only what it added. That undercounts a full replacement, which errs toward no signal.
  `BEH_FOCUS_SWITCHING`: the page hidden at least 5 times and one answer returned to
  at least 5 times. Both feed AI assistance and are accessibility-sensitive. Alone they are not shown on the
  assessment at all (`telemetry-only-alongside-another-category`); next to another category they are context, and
  reasons only when a category other than telemetry corroborates (`telemetry-never-alone`). They are never the second
  category for the top level. Content forensics now gives AI assistance other categories, so a behavioural signal
  shows only when one of them corroborates it.
- **Device check.** On the last step the module also fills `deviceFingerprint`, a SHA-256 of coarse browser traits
  (user agent, languages, screen, time zone, hardware concurrency; no canvas, audio or font probing), and
  `automationMarkers`. The API keyed-hashes the fingerprint per organization, as before. It keeps the fingerprint only
  on the footing `ScreenHire:Integrity:DeviceCheck` gives where the job is: `Notice`, `Consent` (only with the
  candidate's tick on the details step) or `Off`, taking the most protective of the job's countries.

Rules of thumb in the analyzers:

- **Claimed location** is the country of the candidate's phone number: the form asks for no location, and the phone
  is the only place a candidate says where they are. A VPN or proxy exit says where the service is, so no mismatch
  is reported for one, and a VPN on a hosting network is reported once, as the VPN. A VPN exit in the claimed country
  is counter-evidence.
- **Submit velocity** compares the time from the careers server first showing the form to the submission with
  15 seconds, plus 4 per answer, plus 1 per 50 characters of text. Not measured with an accommodation.
- **Duplicates** count only other candidates. One person applying to two jobs is normal. A shared device or file is
  `High`; a shared email base or phone alone is `Medium`. Earlier applications are not revisited when a later one
  matches them, so the first member of a farm carries no duplicate signal of its own.
- **Phone numbers** use libphonenumber. A number without a country code is checked only when the job is in exactly one
  country; otherwise it is not a signal.
- **Accommodation** (the assistive-technology switch): the API stores no form start time, device hash, automation markers or telemetry, accessibility-sensitive
  signals (timing, telemetry, the honeypot) are never stored, the behavioural analyzer never asks for telemetry, and the
  run's note reads the same either way, so nothing records the switch.

### 10.3 Ruleset (`IntegrityRuleset`, version `2026-10-21.1`)

Weights count `Low` 1, `Medium` 2, `High` 3. A category **corroborates** when its weights add up to at least 2;
context-only signals (`NET_ANON_PROXY`, `AUTO_TURNSTILE_FAIL`) add their weight only next to a direct signal of the same
category. Informational signals (`CNT_TEMPLATED_ANSWER`) add nothing at all; version `2026-10-20.3` added that rule. Version `2026-10-20.4` added `telemetry-only-alongside-another-category`. Version `2026-10-21.1` took the declaration out of AI assistance summaries. The honeypot is `Medium` and accessibility-sensitive: autofill and assistive tools can fill a hidden field.
For each assessment, in order:

| Rule id | Rule |
|---|---|
| `accommodation-suppression` | With an accommodation, accessibility-sensitive signals are dropped. |
| `telemetry-only-alongside-another-category` | Behavioural signals are shown only next to a signal of another category on the same assessment. Alone, they are dropped from the assessment: never a level, a reason or a line the recruiter reads. |
| `telemetry-never-alone` | If no category other than `Behavioural` corroborates, the level is the lowest. |
| `two-independent-categories` | If fewer than two categories corroborate, the level is the lowest. |
| `top-level-needs-two-non-telemetry-categories` | The highest level needs two corroborating categories besides telemetry and a combined weight of at least 6; otherwise the middle level. |
| `ai-within-policy-cap` | AI assistance that fits the job's policy (the job allows AI, or allows it with disclosure and the candidate disclosed) is capped at the middle level. |

Levels: Authenticity `NoConcerns`, `ReviewSuggested`, `VerificationRecommended`; AI assistance `NotIndicated`,
`SomeIndicators`, `StrongIndicators`. These names are what automation conditions compare
(`integrity.authenticity`, `integrity.aiAssistance`). Reason codes are the codes of the signals in corroborating
categories, and only when the level is raised; other signals are still shown, as context.

The AI-use declaration never raises a level or adds a reason. With the policy it sets `aiPolicyContext`
(`AllowedByPolicy`, `Disclosed`, `NotDisclosed`, `DeclaredUseNotAllowed`, `NotAllowed`), which changes the summary
and the next step only. The summary states the job's policy and not the declaration, which the application page shows
in its own Declarations panel (design system section 16). The next step is the same whatever the candidate declared,
so a declaration is never set against the evidence. Summaries and next steps are generated from the outcome in plain language; none contains a
number that grades the candidate.

Changing a weight, threshold or rule means a new `IntegrityRuleset.Version`. Assessments store the version they were
made with.

### 10.4 Data and retention

| Table | Holds | Application role |
|---|---|---|
| `submission_observations` | raw network and session data of a careers submission: IP address, user agent, device hash, form start time, automation markers, `expires_at` | insert, read, delete (retention) |
| `telemetry_snapshots` | one page view's form telemetry (counts only), its session, the application once bound, `expires_at` | insert, read, update (upsert, binding), delete (retention, accommodation) |
| `integrity_analyzer_runs` | one row per analyzer version per application | insert, read |
| `integrity_signals` | signals with their evidence (JSONB) | insert, read |
| `integrity_assessments` | each assessment, newest per kind is current | insert, read |
| `integrity_review_actions` | the reviewers' audit log | insert, read |
| `application_documents` | what each uploaded file says: visible text, metadata, hidden text (§10.10) | insert, read |
| `answer_fingerprints` | a SimHash per free-text answer, for near-duplicate lookups | insert, read |
| `consistency_checks` | the consistency check per application and prompt version, with its contradictions and token use | insert, read, update |
| `integrity_settings` | the organization's switch for the consistency check and its monthly token budget | insert, read, update |
| `signal_network_entries` | the Signal Network (§6), cross-tenant | none |

All but the last carry `organization_id` with the usual `tenant_isolation` policy. Only careers-site submissions get
an observation; a recruiter's own address is never recorded for a candidate. The device fingerprint is stored as an
HMAC with a per-organization salt (`ScreenHire:Integrity:DeviceHashKey`, generated by the AppHost locally), so it never
matches across employers. Evidence keeps the network operator and country, never the address. Extracted documents and
answer fingerprints cannot be deleted by the application role on their own: they go when their application or file is
deleted (the foreign keys cascade), so a future erasure job deletes the parent row.

`PurgeExpiredIntegrityData` runs daily at 03:17 UTC and fans out one `PurgeOrganizationIntegrityData` per organization
(the worker sees no tenant rows without a tenant). It deletes observations past `expires_at`
(`ScreenHire:Integrity:Retention:RawNetworkDays`, 30, as the privacy page promises; the API and the worker refuse
to start with more than 30) and asks `ITelemetrySource` to purge expired telemetry: a bound snapshot lives as long as
the observation, an unbound one (an application never sent) a day. Signals, assessments and the audit log stay for the
employer's record period.

### 10.5 Sources (local or operator-supplied; none needs an account)

| Setting (`ScreenHire:Integrity:...`) | Source | Default |
|---|---|---|
| `Network:Ranges` | address ranges with operator, country, hosting and anonymizer flags | Development: the RFC 5737 documentation ranges the synthetic applicants use |
| `Network:Ip2AsnFile` | an iptoasn.com `ip2asn` TSV the operator downloads under iptoasn.com's terms (published under the PDDL 1.0): ASN, operator, country | unset |
| `Network:TorExitListFile` | an exit list the operator fetches from the Tor Project, following its terms and rate guidance | unset |
| `Network:HostingAsns` | extra hosting-provider ASNs, on top of a built-in list of large clouds | empty |
| `Identity:MxLookup` | MX lookups with DnsClient; reserved test domains are never looked up, and a failed lookup is "not checked" | on |
| `Identity:ExtraDisposableDomains` | more disposable domains, on top of the embedded CC0-1.0 list (`Integrity/Data/README.md`, with its licence beside it) | Development: `tempinbox.example.net` |
| `SignalNetwork:Enabled` | the Signal Network flag | off; a host refuses to start with it on |
| `DeviceCheck:Default`, `DeviceCheck:Countries:{CC}` | the device-check basis (`Off`, `Notice`, `Consent`) for a job in no listed country, and per ISO country code | `Notice`; the API's `appsettings.json` lists the EU and EEA countries as `Consent` |
| `Llm:Provider` | the consistency check's model: `None`, `Fake` (Development only; a host refuses to start with it elsewhere) or `Anthropic` | `None`; the worker's Development settings use `Fake` |
| `Llm:ExtractionModel`, `Llm:ConsistencyModel` | model ids for the two steps of the check (§10.10) | `claude-haiku-5-5`, `claude-opus-5-5` (worker `appsettings.json`) |
| `Llm:Anthropic:ApiKey` | Anthropic's API key, from the AppHost's secret parameter `anthropic-api-key` | unset: the provider is hidden and checks are unavailable |
| `Llm:DefaultMonthlyTokenBudget`, `Llm:Timeout`, `Llm:MaxOutputTokens` | the budget for an organization with none of its own, and per-call limits | 2,000,000 tokens, 120 s, 8,000 tokens |

The worker refuses to start when `Network:Ip2AsnFile` or `Network:TorExitListFile` names a file that does not
exist, because the files are read on the first lookup and a wrong path would fail every analysis. A commercial
source such as MaxMind GeoIP2 needs an owner account; none is wired in. Adding one is another
`IIpIntelligence` behind configuration.

### 10.6 API

Every route is tenant-scoped (another organization's application is 404). No response carries a score or a percentage.

| Route | What |
|---|---|
| `GET /v1/applications/{id}/integrity` | `status` (`Pending` until assessed), both current assessments with level, reason codes, summary, next step, signals with evidence and counter-evidence, any dismissal, AI policy context, the review log, and whether verification can be requested (not until phase 2) |
| `POST .../integrity/assessments/{assessmentId}/dismiss` | a reason (`VerifiedOutsideScreenHire`, `ExplainedByCandidate`, `FalsePositive`, or `Other` with a note); only the current assessment, above the lowest level, once |
| `POST .../integrity/notes` | a note on the application's integrity or one assessment |
| `POST .../integrity/request-verification` | always 409 until phase 2 |
| `GET /v1/organization/integrity-settings` | whether the consistency check is on, its monthly token budget, and the tokens used this month |
| `PUT /v1/organization/integrity-settings` | turn the check on or off and set the budget (organization admins) |

The integrity view also carries `consistencyCheck` (status, note, prompt version, models) once the check is decided, and
each signal's `informational` flag and, for `CNT_INCONSISTENCY`, its `llm` provenance.

Writes need a hiring role and the antiforgery header. Dismissing records a decision and never changes the assessment.
A unique index (`ix_integrity_review_actions_one_dismissal`) keeps one dismissal per assessment when two reviewers
dismiss at once; the second gets the same 409 as a later attempt.
`ApplicationSummary.integrity` carries the chips for lists: both levels, their reason codes and whether each was
dismissed. The `IntegrityAssessed` tenant event carries ids, the two level names and the ruleset version, never
evidence.

### 10.7 Signal Network

The table, the normalisation (`Identifiers`) and the keyed hashing (`KeyedHashing.NetworkHash`, `ContributorHash`) exist,
with `Organization.SignalNetworkOptIn` defaulting to false. Nothing writes or reads the table: the application role has
no grant on it, and the worker and API refuse to start with `SignalNetwork:Enabled` on. Enabling it needs legal
review and DPIA sign-off (ADR-0005), then grants, a security-definer query that returns counts only with the M ≥ 3
floor, and an audit log of each query.

### 10.8 Synthetic applicants

`SyntheticApplicants` defines a genuine person, a bot, an honest AI-assisted candidate and a three-member application
farm, all fictional. They are used three ways:

- golden files in `tests/ScreenHire.UnitTests/Integrity/Golden`, run through the real analyzers and ruleset in memory.
  Rewrite them with `UPDATE_INTEGRITY_GOLDEN=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*IntegrityGoldenTests"`
  and review the diff;
- `IntegrityTests` (Aspire) submits them over HTTP and reads the assessments back through the API, and
  `IntegrityPanelTests` (Playwright) submits them while a recruiter watches the job, then checks the chips, the panel
  and a dismissal. Both submit through `SyntheticApplications.ApplyAsync` in `tests/ScreenHire.Testing`, which answers
  only the resume question, so the plausible minimum time is 19 seconds and the farm members (20 seconds) carry no
  `AUTO_SUBMIT_VELOCITY`, unlike their golden files;
- with `pnpm dev` the API submits them to Brightwater's "Senior .NET Engineer" job once it has started
  (`DevelopmentDemo`, on when `ScreenHire:Demo:Enabled` is true, which the AppHost sets only for
  the dev frontends), so the local demo shows every level. Skipped for applicants already there. The test stacks do
  not run it.

Their resumes are real PDFs (`SyntheticDocuments`, byte for byte the same for the same text, so the farm still shares a
file). Each applicant but the farm also has the telemetry its browser would have sent (`SyntheticTelemetry`). The demo
binds it to the application, and the golden files run it through `TelemetryRules`.

Expected results: genuine and honest AI-assisted `NoConcerns` (the VPN is context, with counter-evidence); the
bot and every farm member `VerificationRecommended`, the farm also with `CNT_INCONSISTENCY` (its resume claims fifteen
years, its answer says 6 to 9). AI assistance is `NotIndicated` for all: the bot's hidden instruction and the resume
builders are document signals with no second category behind them. The honest AI-assisted candidate went back and
forth to their assistant, so `BEH_FOCUS_SWITCHING` is stored and, because a document signal is now on the same
assessment, shown next to it as context; it is never a reason on its own.

### 10.9 Known gaps

- Version 1 of the "Software engineer" system template tests `integrity.aiAssistance` for `ReviewSuggested`, an
  authenticity level, so that half of its integrity automation never matches. Version 2 (migration
  `AddSoftwareEngineerTemplateVersion2`) corrects it: a reviewer is asked when either assessment is above its lowest
  level. New jobs and the Brightwater seed use version 2; jobs copied from version 1 keep their copy and show "update
  available". Level names in conditions are still not validated, because validation would block those version 1 jobs
  until someone adopts version 2. Turning it on is a small change once no job uses version 1.
- Duplicates do not revisit earlier applications (§10.2).
- With the demo seeding on in the end-to-end stack, `LiveUpdatesTests` failed in 3 of 9 runs: a live `JobClosed` took
  more than 5 seconds to reach the second browser. With it off, 10 of 10 runs passed. The demo itself finished well
  before the test ran. The trigger is the burst of
  submissions the API makes just after startup; how it delays later real-time delivery is not isolated yet. The demo
  now runs only for `pnpm dev`. Worth a look before real traffic bursts matter. One lead: until October 2026 the demo
  published its candidates' moves on the submission's outbox after that outbox had flushed, and those
  `ApplicationMoved` envelopes stayed in the API's outgoing table unsent. The demo now moves candidates in a scope of
  its own; whether the stuck envelopes were behind the late `JobClosed` was not measured. The API now reports healthy
  only once the demo's steps have run, and `/preview` waits for an integrity chip in the browser pane before it checks anything live.
- The assistive-technology switch turns the whole telemetry module off, automation markers and the device check
  included, as its brief asked. A script can tick the switch too, so a bot that does loses `AUTO_WEBDRIVER` and the
  device duplicate check; the network, identity, honeypot and duplicate checks still run.
- The device-check basis follows where the job is, not where the candidate is, because there is no other footing
  before submission. A remote job with no country takes `DeviceCheck:Default`.
- Session tokens need the API's Data Protection keys to be kept and shared across instances in production (an owner
  action); locally the default key ring is enough.
- The apply page's JavaScript budget rose from about 20 KB to about 22 KB brotli for the module.

- Content forensics read only `.pdf` and `.docx` files. Word 97 (`.doc`), `.rtf` and images are recorded as unsupported,
  with no OCR.
- Duplicate answers have the same blind spot as other duplicates, and one more: two applications analyzed at the same
  moment may miss each other, because each keeps its fingerprints only when its pipeline commits.
- Turning the consistency check on does not reach back: applications already checked, skipped or switched off keep that
  outcome.
- The dashboard's integrity panel (§7) and questionnaire editor both exist now. A job asks for a LinkedIn export only
  through a file question with the id `linkedin_export`.
- PdfPig has no limit on how far a crafted PDF may expand, and the 20-second deadline abandons a slow parse without
  stopping its thread. Word files are capped per part. A real malware scanner (task 15's `IMalwareScanner` hook) should
  come first in production; extraction trusts its result.

### 10.10 Content forensics

Four stages, each idempotent on its own:

1. **Reading files.** The malware scan (`ScanApplicationFile`) cascades `ExtractApplicationDocument` per file. The
   worker reads the blob (read only, `BlobApplicantFileStore`) and `DocumentExtraction` turns it into an
   `application_documents` row: the visible text in reading order, the producer, creator and creation time the file
   claims (never the author's name), and hidden text with its page and reason. PDFs go through PdfPig, Word 2007+
   through DocumentFormat.OpenXml. A letter is hidden when it is drawn invisibly, sits off the page, is under 2 points,
   or is white with nothing darker drawn behind it, so a template's white text on a dark sidebar stays visible. In a
   Word file, shading on the run, the paragraph, its table cell or its table counts as something behind it. A file
   the scan flagged is skipped; a damaged or encrypted one is unreadable; a broken XMP block loses only the XMP
   metadata; a file that takes over 20 seconds to parse is unreadable. None of these throws. At most two files are
   parsed at once per worker, and a parse abandoned at its deadline keeps its slot until it really ends, because the
   parsers cannot be stopped mid-page. Each file read sends the consistency check and the pipeline again.
2. **Documents** (`documents` analyzer). `CNT_RESUME_GENERATOR` when the producer or creator names a resume builder
   (Kickresume, Zety, Rezi and others) or an automatic PDF tool (wkhtmltopdf, headless Chrome, ReportLab and others).
   It is `Low`, carries the minutes between creation and submission when under 15, and says builders are common.
   `CNT_PROMPT_INJECTION` when a file hides 20 or more characters or addresses a model or screening tool
   (`ContentPatterns.Instructions`, matched in hidden and visible text). Instructions are `High`; hidden text alone is
   `Medium` with counter-evidence. Each signal quotes up to three hidden passages.
3. **Answers** (`answers` analyzer). Only short- and long-text answers of 20 words or more count: they are the
   candidate's own words. Each becomes a set of three-word sequences and a 64-bit SimHash, kept in
   `answer_fingerprints`. Another candidate's fingerprint from the last 180 days within 20 bits is a candidate match;
   a match needs at least 60% of the combined sequences shared (Jaccard). One word changed in twenty changes about
   three sequences in twenty, so light edits match, while two people's own words share almost none.
   `TextSimilarityTests` holds the thresholds. The signal shows both answers side by side and names the other
   applicant only by job and date. One candidate's applications never match each other, and no organization sees
   another's. pgvector was not needed: the sequence overlap is exact, explainable and needs no embedding model.
   `CNT_TEMPLATED_ANSWER` quotes a placeholder in square brackets or a chatbot's reply wording. It is informational:
   shown, never counted.
4. **Consistency** (`CheckApplicationConsistency`, then the `consistency` analyzer). The sources are the resume, the
   answers as the candidate saw them (option labels; text, choice and number answers only, so never links or yes/no
   answers such as right to work) and an uploaded LinkedIn export: a file question with
   the id `linkedin_export`, which a job opts into. ScreenHire never fetches a profile (CLAUDE.md non-negotiable 5).
   Only visible text is sent. The handler checks, in order: the organization's switch (`integrity_settings`, off until
   an admin turns it on), at least two sources with text, a configured model, and the monthly token budget. Then it
   claims a `consistency_checks` row (unique per application and prompt version) and asks the model with no database
   transaction open. A claim is taken over once it is older than the longest a check can take: four calls, each with
   the SDK's two retries, all timing out, plus a minute. A redelivered message for a finished check sends the pipeline again,
   because this handler has no outbox and the first reassessment may have been lost.

   The check has two steps behind `IChatClient` (Microsoft.Extensions.AI). The extraction model lists each source's
   claims about employers, job titles, employment dates, years of experience, education and certifications, quoting
   each. Every quote is matched against the text that was sent, ignoring whitespace and quote style; one that is not
   there is dropped. The consistency model then names pairs of claim ids that contradict each other. Only pairs of
   checked claims from two different sources with different quotes are kept, so every contradiction cites a verbatim
   span from each source, whatever the model wrote. The explanation a recruiter reads comes from a fixed topic, never from the model's prose.
   Both prompts (`ConsistencyPrompts`, version `consistency-2026-10-20.1`) treat the sources as untrusted data inside
   markers with a random nonce, strip marker sequences from the text, and never ask whether anything was written by AI.
   Answers are constrained to a JSON schema (`output_config.format`).

   Each `CNT_INCONSISTENCY` signal stores its `llm` provenance: the prompt version and the model ids the provider
   reported. `IntegrityPipeline.ToSignals` refuses an LLM-derived signal without a text comparison citing both
   sources, and a contract test runs every recorded response and synthetic applicant through it.

**Model access.** `LlmAccess` builds the client from `ScreenHire:Integrity:Llm` (§10.5). `Anthropic` uses Anthropic's
official C# SDK (`AnthropicClient.AsIChatClient`) and only with an API key and both model ids; without them the
provider is hidden and every check is `Unavailable`, with the reason as the run's note. `Fake`
(`FakeConsistencyChatClient`) answers both prompts deterministically: a sentence that states a number of years is a
claim, and two claims contradict when their ranges are two or more years apart. It reports its own model id, and a host
refuses to start with it outside Development. Model ids live in configuration only. A refusal, a cut-off or unreadable
answer, or any error from the provider makes the check `Unavailable`: a missing signal, never a failed application. No
source text is logged. Under Anthropic's commercial terms, API inputs and outputs are not used for training. The careers
privacy notice (version `2026-10-20.3`) tells candidates their files and answers may go to an AI provider, which is not
allowed to train on them.

**Budget.** Counted in tokens, not money: the input and output tokens of the organization's checks started this calendar
month (UTC), counted by `ConsistencyBudget` for both the handler and the settings endpoint. At or over the budget,
the check is `OverBudget` and says so. The default comes from configuration, and an admin can set the organization's
own. It is a soft limit: tokens are recorded when a check finishes, so checks that start together all pass the test and
the last can overshoot by a few checks' worth.

**Known gaps.**
- Parsing runs in the worker process. A crafted PDF that expands to gigabytes can still exhaust its memory; the slot
  limit only stops parses piling up. A Word part is capped at 5 million characters unzipped, but its document tree is
  still built in memory. The real fix is parsing in a separate process with memory and time caps.
- PDF white-text detection asks whether something is drawn behind a letter, not how much contrast there is, so white
  text on a pale full-page fill is not reported. Word text boxes and style-level colours are not read for hiding.
- A transient provider error (a 429 or 5xx after the retry) records the check as `Unavailable` for good; it is not
  tried again later.
- Two applications with the same answer that are assessed at the same moment do not see each other's fingerprints,
  which are saved with each pipeline run. A later application sees both.
- An application submitted under a notice older than `2026-10-20.3` whose check is still pending when an admin turns
  the check on is sent to the model. Only applications in flight at that moment can be affected; finished ones keep
  their `Disabled` outcome.

**Tests.** `DocumentExtractionTests` builds fixture PDFs and Word files with white, tiny, off-page and hidden text and
generator metadata. `ConsistencyCheckerTests` replays hand-written Messages API responses
(`tests/ScreenHire.UnitTests/Integrity/Llm/Recordings`) through the real SDK client. It checks the request (configured
models, a schema, no sampling parameters) and that invented quotes and unknown claim ids are dropped. The golden files
cover the synthetic applicants, and `ContentForensicsTests` (Aspire) covers blob reading, the LinkedIn export, the budget
and the organization boundary.

## Open questions

- Vendor choices: IP intelligence (MaxMind GeoLite2/GeoIP2, IPinfo), email/phone checks, IDV (Persona / Veriff /
  Incode) — pricing and DPAs.
- Per-jurisdiction fingerprint basis (consent vs strictly necessary) defaults for EU tenants.
- The device-check basis for a job with no country (remote, anywhere). It takes `DeviceCheck:Default` (`Notice`), so a
  candidate in the EU applying to such a job gets no consent tick. Setting the default to `Consent` closes that at the
  cost of asking everyone else; a legal call for the owner.
- Exact level thresholds — calibrate on synthetic + pilot data; publish methodology.
