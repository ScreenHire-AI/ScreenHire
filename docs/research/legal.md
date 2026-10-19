# Legal & Regulatory Landscape — Applicant Fraud / AI-Applicant Detection

> Research snapshot: **2026-10-18**. Product-design input, **not legal advice**. Items marked (†) rest on
> secondary sources or recollection and were not re-verified against a primary source.

## Status snapshot (recent changes)

- **EU AI Act "Digital Omnibus" — adopted.** Reg. (EU) 2026/1744 of 8 Jul 2026 (OJ 24 Jul 2026) [1].
  Annex III high-risk obligations (recruitment included) now apply from **2 Dec 2027**; Annex I from 2 Aug 2028.
  Art. 5 bans have applied since **2 Feb 2025** and were *not* relaxed. Art. 4 AI-literacy softened to "support" [1][2].
- **GDPR omnibus (COM(2025) 837)** — still in trilogue; Council text reportedly drops the Art. 22 and
  personal-data-definition changes [27]. Do not design around it.
- **UK** — new Arts. 22A–D in force since 5 Feb 2026 (SI 2026/82). PECR Sch. A1 para 4(2)(c) now exempts device
  access "strictly necessary… to prevent or detect fraud" from consent [12].
- **Colorado** — SB 24-205 never took effect; replaced by **SB 26-189** (signed May 2026, effective **1 Jan 2027**;
  AG rules due by then) [5]. xAI/DOJ challenge to the old law filed Apr 2026 [6].
- **Illinois HB 3773** — in force since 1 Jan 2026. IDHR notice rules proposed 15 May 2026, withdrawn 2 Jun 2026;
  the statute still applies [9].
- **California CPPA ADMT regulations** — effective 1 Jan 2026; ADMT compliance due **1 Jan 2027**; risk assessments
  by 31 Dec 2027 [7].
- **California SB 690** (signed 30 Sep 2026) — eliminates only private pen-register website claims. CIPA §631
  wiretap claims (session replay / keystroke capture) remain [11].
- **NYC LL144** — unchanged. State Comptroller audit (2 Dec 2025) found enforcement ineffective → expect tightening [10].

## Per-signal risk matrix

| Signal | Laws triggered | Required mitigations |
|---|---|---|
| **IP address, geolocation, VPN/proxy** | GDPR (legit. interest, Art. 6(1)(f) + Recital 47); CCPA (precise geolocation = sensitive PI); IL HB 3773 (zip code as proxy is banned); national-origin disparate impact | Coarse IP-geo only, never the browser geolocation API. VPN = context, not fraud. Flag only *claimed vs observed* location mismatch. Short raw-data retention. |
| **Device / browser fingerprint** | ePrivacy Art. 5(3) covers fingerprinting [14] → consent unless "strictly necessary"; UK fraud exemption [12]; CIPA | Do **not** collect on careers-page landing; start at *application start, after notice*. EU consent path. Per-employer salted hash; short TTL. |
| **Behavioral telemetry** (paste, keystroke timing, focus/tab switches, time per question) | GDPR: keystroke dynamics are biometric only if used to uniquely identify (Art. 4(14)); becomes Art. 9 only when used *to identify*. ICO lists keystroke analysis as a behavioural biometric. AI Act biometric definition (Art. 3(34)) has no identification test, so the **Art. 5(1)(f) workplace emotion-recognition ban** reaches typing. CCPA biometric incl. keystroke rhythms; WA MHMDA (employment excluded) [21]; CPPA §7150(b)(4) risk assessment for systematic observation of applicants; CIPA §631; ADA "screen-out" + CA FEHA ADS rules [8] | **Session-level aggregates only.** No key content, no per-person typing templates, no cross-applicant matching. **No stress/"nervousness" inference.** Accommodation toggle that disables telemetry with **no negative inference**. **Never flag on telemetry alone** — dictation, screen readers, autofill and switch access look like paste/tab-switch. |
| **LLM analysis of resume, cover letter, answers** | AI Act Annex III 4(a) (profiling blocks the Art. 6(3) exemption → high-risk); GDPR Art. 22 + SCHUFA (C-634/21) (†); NYC LL144 AEDT; IL HB 3773; CO SB 26-189; CPPA ADMT; FEHA; Title VII / ADEA (*Mobley v. Workday*, ADEA collective certified May 2025 [25]) | Flag **verifiable inconsistencies with cited evidence**, not "% AI-written". AI-text detectors are unreliable and biased against non-native writers (Liang et al. 2023 †). Respect each employer's candidate-AI-use policy. Bias-audit flag rates. |
| **Email correspondence** | GDPR purpose limitation, Arts. 13/14; Art. 9 for health/accommodation content; wiretap/CIPA if intercepted in transit; ePrivacy for tracking pixels; Google Limited Use (no generalized AI training on Workspace data) [26] | Analyze only candidate threads in the recruiting mailbox, **post-delivery**, via employer-authorized API. Route accommodation emails out of fraud analysis. No tracking pixels. No training on email content. |
| **LinkedIn cross-checks** | hiQ v. LinkedIn: anti-scraping terms enforceable (Nov 2022) → consent judgment + permanent injunction (Dec 2022) [18]; Proxycurl enjoined 2025. **API Terms §3.1(17)** bar using API content for employment eligibility without written permission [15]. **Verified on LinkedIn** lists "Hiring or employment decisions" as restricted [16]. **Sign in with LinkedIn** "does not verify user identities" [17] | **No scraping or automated fetching.** Recruiter views the candidate-provided URL. Automated checks only on a candidate-uploaded profile PDF/export. Any API-based check requires a written LinkedIn partnership. |
| **Duplicate / mass-application detection across employers** | GDPR: cross-employer use makes us a **controller** (Art. 28(10)), possibly joint → own legal basis, Art. 14 notice, DPIA. ICO 2024 audit found recruitment-AI vendors mis-classing themselves as processors [13]. US: possible **FCRA consumer reporting agency** (CFPB Circular 2024-06 withdrawn 12 May 2025, statute unchanged [24]). CCPA §7050(a)(4) allows service-provider cross-client fraud use [7] | **Same-employer detection by default.** Cross-employer only as an opt-in network: keyed hashes of hard identifiers, velocity counts only, never outcomes or content. Separate notice, short retention. |
| **Optional ID document + liveness** | GDPR Art. 9 explicit consent (valid only if genuinely optional); AI Act excludes 1:1 verification from Annex III 1(a); **IL BIPA** — ID-selfie face match = face-geometry scan (*Sosa v. Onfido*, 2022) [19] → written release + public retention schedule; TX CUBI (AG penalties up to $25k/violation; TRAIGA fraud carve-out †) [23]; CO HB24-1130 (employers can't condition on biometrics) [22]; CCPA SPI; WA RCW 19.375 | Store **pass/fail only**; delete images/templates within days. Offer a live human ID check as the alternative. |
| **Video interview integrity** (deepfake / voice clone / off-screen help) | AI Act **Art. 5(1)(f)** — emotion recognition banned in hiring (Commission guidelines include recruitment; physical states excluded) [3]; Annex III high-risk; **IL AI Video Interview Act** (†) notice/explanation/consent, limited sharing, delete within 30 days on request; **MD Lab. & Empl. §3-717** (†) signed waiver before facial templates; BIPA (face geometry, voiceprints — *Carpenter v. McDonald's* [20]); TX CUBI | Detect **media artifacts only**: face-swap, lip-sync mismatch, virtual camera, injected audio, second speaker. **Never** infer affect, honesty or "intent to cheat". No persistent face/voice embeddings. Pre-interview consent + accommodation path. |

**GDPR baseline (all signals):** employer = controller relying on Art. 6(1)(f) + Recital 47 with a documented LIA.
Applicant consent is unreliable due to power imbalance (EDPB 05/2020 †). ScreenHire = Art. 28 processor for
per-employer scoring. **DPIA mandatory** (scoring + systematic monitoring + vulnerable subjects + new tech).

## Non-negotiable product design principles

1. **Evidence, not verdicts.** Discrete, sourced observations — no opaque score, **never auto-reject**. Reviewers see
   counter-evidence and record a reason. Satisfies CPPA §7001(e)(1) "human involvement", GDPR Art. 22, Colorado
   "meaningful human review", and keeps us outside LL144's narrow AEDT definition where possible.
2. **No inference of emotion, intent, honesty or personality** from face, voice or typing — anywhere.
3. **No biometric templates; no identification from behaviour.** Biometrics only in the optional ID step, transiently.
4. **Notice before collection**, with jurisdiction-aware consent logic (EU ePrivacy, BIPA/CUBI/CO biometric consent,
   IL video-interview consent).
5. **Accessibility parity.** Accommodation path disables telemetry-based signals without penalty; non-biometric ID
   alternative; test with assistive tech.
6. **Tenant isolation by default.** Cross-employer detection is a separate opt-in service where we act as controller.
7. **Short raw-data retention.** Keep flag + evidence + reviewer action in the employer tenant for record periods
   (CA ADS 4 yrs, CO 3 yrs). Full audit logs.
8. **Build to the AI Act provider standard now:** Arts. 9–15 & 17, internal-control conformity assessment (Art. 43),
   EU database registration (Art. 49), post-market monitoring (Art. 72), annual LL144-style impact-ratio bias audits,
   DPIA / LIA / CPPA risk-assessment templates for customers.
9. **No scraping.** LinkedIn only via candidate-supplied data or a written partnership.

## Open uncertainties

- Whether fraud flags count as an AEDT (LL144) or "materially influence" a decision (Colorado) — depends on employer
  use; neither regulator has addressed fraud tools.
- Whether session-level bot/automation detection can escape Annex III (Commission Art. 6 guidelines unverified).
- Whether "intentions" fall under Art. 5(1)(f) — Dutch DPA reads it broadly [3]; off-screen-gaze detection is borderline.
- Scope of EU "strictly necessary" exemption for anti-fraud fingerprinting — DPAs vary.
- Whether cross-employer shared signals are FCRA consumer reports.
- Whether BIPA reaches transient, non-identifying face/voice analysis.
- GDPR minimisation vs California's 4-year ADS record requirement.
- Colorado AG rules + litigation; federal preemption push (Dec 2025 executive order).
- IL HB 3773 notice rules re-proposal; GDPR omnibus outcome.

## Sources

1. Reg. (EU) 2026/1744, 8 Jul 2026 — https://eur-lex.europa.eu/eli/reg/2026/1744/oj/eng
2. Gibson Dunn, 27 May 2026 — https://www.gibsondunn.com/eu-ai-act-omnibus-agreement-postponed-high-risk-deadlines-and-other-key-changes/
3. FPF, 24 Mar 2026 — https://fpf.org/blog/red-lines-under-eu-ai-act-unpacking-the-prohibition-of-emotion-recognition-in-the-workplace-and-education-institutions/
5. Venable, Jul 2026 — https://www.venable.com/insights/publications/2026/07/colorados-new-ai-law-what-employers-need-to
6. Jenner, Apr 2026 — https://www.jenner.com/en/news-insights/client-alerts/doj-joins-xai-in-lawsuit-challenging-colorado-ai-act
7. CPPA regulation text (OAL approved 23 Sep 2025) — https://cppa.ca.gov/regulations/pdf/ccpa_updates_cyber_risk_admt_appr_text.pdf
8. Cal. Civil Rights Council, 30 Jun 2025 — https://calcivilrights.ca.gov/2025/06/30/civil-rights-council-secures-approval-for-regulations-to-protect-against-employment-discrimination-related-to-artificial-intelligence/
9. Burke Law, 16 Jun 2026 — https://www.burkelaw.com/alert-illinois-department-of-human-rights-withdraws-proposed-ai-in-employment-rules
10. NY State Comptroller, 2 Dec 2025 — https://www.osc.ny.gov/state-agencies/audits/2025/12/02/enforcement-local-law-144-automated-employment-decision-tools
11. Covington, Oct 2026 — https://www.insideprivacy.com/state-privacy/california-governor-signs-sb-690-eliminating-private-website-based-cipa-pen-register-claims-and-calling-for-broader-cipa-reform/
12. SI 2026/82 — https://legislation.gov.uk/uksi/2026/82 ; DUAA Sch. 12 — https://www.legislation.gov.uk/ukpga/2025/18/schedule/12
13. Lewis Silkin on ICO audit, 27 Nov 2024 — https://www.lewissilkin.com/en/insights/2024/11/27/ai-in-recruitment-icos-recommendations-and-key-questions
14. EDPB Guidelines 2/2023 v2, 7 Oct 2024 — https://www.edpb.europa.eu/documents/guideline/guidelines-22023-on-technical-scope-of-art-53-of-eprivacy-directive_en
15. LinkedIn API Terms (rev. 13 Dec 2022) — https://www.linkedin.com/legal/l/api-terms-of-use
16. Verified on LinkedIn FAQ (Aug 2026) — https://learn.microsoft.com/en-us/linkedin/consumer/integrations/verified-on-linkedin/faq
17. Sign in with LinkedIn (OIDC) — https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/sign-in-with-linkedin-v2
18. Proskauer, Dec 2022 — https://www.proskauer.com/blog/hiq-and-linkedin-reach-proposed-settlement-in-landmark-scraping-case
19. Privacy World, Apr 2022 — https://www.privacyworld.blog/2022/04/breaking-federal-judge-in-illinois-affirms-that-bipa-extends-to-information-derived-from-photographs/
20. ABA — https://www.americanbar.org/groups/litigation/resources/newsletters/class-actions-derivative-suits/voiceprints-ai-bipa-new-trends-biometric-privacy-litigation/
21. RCW 19.373.010 — https://app.leg.wa.gov/RCW/default.aspx?cite=19.373.010
22. Venable, Jun 2024 — https://www.venable.com/insights/publications/2024/06/colorado-amends-state-privacy-law-to-include
23. Texas AG on CUBI — https://www.texasattorneygeneral.gov/consumer-protection/file-consumer-complaint/consumer-privacy-rights/biometric-identifier-act ; SIA, Jun 2025 — https://www.securityindustry.org/2025/06/24/groundbreaking-texas-ai-law-also-brings-needed-clarity-on-use-of-biometric-technologies-for-security/
24. CFPB Circular 2024-06 withdrawal — https://digitalpolicyalert.org/event/29976-consumer-financial-protection-bureau-withdrew-guidance-on-protecting-workers-from-unchecked-digital-tracking-and-opaque-decision-making-systems-circular-2024-06
25. Maynard Nexsen on *Mobley v. Workday* — https://www.maynardnexsen.com/publication-emerging-liability-for-ai-driven-hiring-tools-key-developments-in-mobley-v-workday-inc
26. Google Workspace API user-data policy — https://support.google.com/cloud/answer/13805798
27. EDRi, Mar 2026 — https://edri.org/our-work/the-digital-omnibus-a-step-back-from-the-brink-but-the-risks-remain/
