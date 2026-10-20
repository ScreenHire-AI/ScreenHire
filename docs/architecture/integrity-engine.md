# Integrity Engine — Applicant Authenticity & AI-Assistance Signals

> Status: **v1 application stage built (2026-10-20)**; §10 describes the implementation. Design draft v0.1
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
`src/ScreenHire.Application/Integrity` (analyzers, pipeline, retention, synthetic applicants),
`src/ScreenHire.Infrastructure/Integrity` (local data sources), `apps/api/Endpoints/IntegrityEndpoints.cs` (API).

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
3. when either changed, appends an `IntegrityAssessed` timeline entry with both level names, and publishes the
   `IntegrityAssessed` tenant event and `RunAutomations(IntegrityAssessed)` for the workflow engine.

A later analyzer (content forensics) registers as another `IIntegrityAnalyzer`, or publishes
`AnalyzeApplicationIntegrity` again when its own asynchronous work lands. Bumping an analyzer's version re-runs it.

| Analyzer | Signals | Reads | When it sees nothing |
|---|---|---|---|
| `network` | `NET_DATACENTER_IP`, `NET_ANON_PROXY` (context only), `NET_GEO_CLAIM_MISMATCH` | the submission's IP address; IP intelligence | no address (recruiter-added), no source configured, nothing known |
| `identity` | `ID_EMAIL_DISPOSABLE`, `ID_EMAIL_NO_MX`, `ID_PHONE_INVALID`, `ID_PHONE_VOIP` | email and phone | MX not checked, number without a country code |
| `automation` | `AUTO_HONEYPOT`, `AUTO_WEBDRIVER`, `AUTO_SUBMIT_VELOCITY`, `AUTO_TURNSTILE_FAIL` (context only) | honeypot, bot check, markers, form start time | |
| `duplicates` | `ID_DUPLICATE_IN_ORG` | other candidates' email (plus tag ignored), phone, device hash, resume hash, same organization only | |
| `behavioural` | `BEH_*` | `ITelemetrySource` (none registered yet) | always, until the telemetry module lands |

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
- **Accommodation** (the assistive-technology switch): the API stores no form start time, accessibility-sensitive
  signals (timing, telemetry, the honeypot) are never stored, the behavioural analyzer never asks for telemetry, and the
  run's note reads the same either way, so nothing records the switch.

### 10.3 Ruleset (`IntegrityRuleset`, version `2026-10-20.2`)

Weights count `Low` 1, `Medium` 2, `High` 3. A category **corroborates** when its weights add up to at least 2;
context-only signals (`NET_ANON_PROXY`, `AUTO_TURNSTILE_FAIL`) add their weight only next to a direct signal of the same
category. The honeypot is `Medium` and accessibility-sensitive: autofill and assistive tools can fill a hidden field.
For each assessment, in order:

| Rule id | Rule |
|---|---|
| `accommodation-suppression` | With an accommodation, accessibility-sensitive signals are dropped. |
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
and the next step only. The summary states the policy and the declaration side by side ("Declared AI use: no."), and
the next step is the same whatever the candidate declared, so a declaration is never set against the evidence. Summaries and next steps are generated from the outcome in plain language; none contains a
number that grades the candidate.

Changing a weight, threshold or rule means a new `IntegrityRuleset.Version`. Assessments store the version they were
made with.

### 10.4 Data and retention

| Table | Holds | Application role |
|---|---|---|
| `submission_observations` | raw network and session data of a careers submission: IP address, user agent, device hash, form start time, automation markers, `expires_at` | insert, read, delete (retention) |
| `integrity_analyzer_runs` | one row per analyzer version per application | insert, read |
| `integrity_signals` | signals with their evidence (JSONB) | insert, read |
| `integrity_assessments` | each assessment, newest per kind is current | insert, read |
| `integrity_review_actions` | the reviewers' audit log | insert, read |
| `signal_network_entries` | the Signal Network (§6), cross-tenant | none |

All but the last carry `organization_id` with the usual `tenant_isolation` policy. Only careers-site submissions get
an observation; a recruiter's own address is never recorded for a candidate. The device fingerprint is stored as an
HMAC with a per-organization salt (`ScreenHire:Integrity:DeviceHashKey`, generated by the AppHost locally), so it never
matches across employers. Evidence keeps the network operator and country, never the address.

`PurgeExpiredIntegrityData` runs daily at 03:17 UTC and fans out one `PurgeOrganizationIntegrityData` per organization
(the worker sees no tenant rows without a tenant). It deletes observations past `expires_at`
(`ScreenHire:Integrity:Retention:RawNetworkDays`, 30, as the privacy page promises) and asks `ITelemetrySource` to
purge expired telemetry. Signals, assessments and the audit log stay for the employer's record period.

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

A commercial source such as MaxMind GeoIP2 needs an owner account; none is wired in. Adding one is another
`IIpIntelligence` behind configuration.

### 10.6 API

Every route is tenant-scoped (another organization's application is 404). No response carries a score or a percentage.

| Route | What |
|---|---|
| `GET /v1/applications/{id}/integrity` | `status` (`Pending` until assessed), both current assessments with level, reason codes, summary, next step, signals with evidence and counter-evidence, any dismissal, AI policy context, the review log, and whether verification can be requested (not until phase 2) |
| `POST .../integrity/assessments/{assessmentId}/dismiss` | a reason (`VerifiedOutsideScreenHire`, `ExplainedByCandidate`, `FalsePositive`, or `Other` with a note); only the current assessment, above the lowest level, once |
| `POST .../integrity/notes` | a note on the application's integrity or one assessment |
| `POST .../integrity/request-verification` | always 409 until phase 2 |

Writes need a hiring role and the antiforgery header. Dismissing records a decision and never changes the assessment.
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
- `IntegrityTests` (Aspire) submits them over HTTP and reads the assessments back through the API;
- with `pnpm dev` the API submits them to Brightwater's "Senior .NET Engineer" job once it has started
  (`SyntheticApplicantsDemo`, on when `ScreenHire:Demo:SyntheticApplicants` is true, which the AppHost sets only for
  the dev frontends), so the local demo shows every level. Skipped for applicants already there. The test stacks do
  not run it.

Expected results: genuine and honest AI-assisted `NoConcerns` (the VPN is context, with counter-evidence); the bot and
every farm member `VerificationRecommended`; AI assistance `NotIndicated` for all, because no content or telemetry
analyzer exists yet.

### 10.9 Known gaps

- The "Software engineer" system template's integrity automation tests `integrity.aiAssistance` for
  `ReviewSuggested`, which is an authenticity level; that half of its condition never matches. Fixing it needs a new
  system template version. Level names in conditions are not validated yet for the same reason: validation would
  block every job copied from that template.
- Duplicates do not revisit earlier applications (§10.2).
- With the demo seeding on in the end-to-end stack, `LiveUpdatesTests` failed in 3 of 9 runs: a live `JobClosed` took
  more than 5 seconds to reach the second browser. With it off, 4 of 4 runs passed. The trigger is the burst of
  submissions the API makes just after startup; how it delays later real-time delivery is not isolated yet. The demo
  now runs only for `pnpm dev`. Worth a look before real traffic bursts matter.
- Webdriver markers and the device fingerprint have an API contract (`automationMarkers`, `deviceFingerprint`) but no
  careers-page script sends them yet; the telemetry module does.

## Open questions

- Vendor choices: IP intelligence (MaxMind GeoLite2/GeoIP2, IPinfo), email/phone checks, IDV (Persona / Veriff /
  Incode) — pricing and DPAs.
- Per-jurisdiction fingerprint basis (consent vs strictly necessary) defaults for EU tenants.
- Exact level thresholds — calibrate on synthetic + pilot data; publish methodology.
