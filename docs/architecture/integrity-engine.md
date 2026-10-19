# Integrity Engine — Applicant Authenticity & AI-Assistance Signals

> Status: **Draft v0.1 (2026-10-18)**. Grounded in `docs/research/legal.md` and `docs/research/competitors.md`.
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

In: notice + accommodation toggle; telemetry aggregates; network/identity/automation signals; prompt-injection +
generator-metadata + within-employer near-duplicate detection; LLM consistency (resume ↔ answers ↔ uploaded LinkedIn
export); two assessments with reason codes; review actions + audit log; step-up ID via one vendor (wrapped);
Signal Network built but **flagged off**.

Later: interview integrity, mailbox correspondence analysis, network enablement post-legal-review, published accuracy
reports.

## Open questions

- Vendor choices: IP intelligence (MaxMind GeoLite2/GeoIP2, IPinfo), email/phone checks, IDV (Persona / Veriff /
  Incode) — pricing and DPAs.
- Per-jurisdiction fingerprint basis (consent vs strictly necessary) defaults for EU tenants.
- Exact level thresholds — calibrate on synthetic + pilot data; publish methodology.
