---
type: Research
title: "Legal & Regulatory Landscape: Applicant Fraud / AI-Applicant Detection"
description: "Not legal advice: the EU AI Act, GDPR, NYC Local Law 144 and the US state rules as they bear on detecting fraudulent and AI applicants, a per-signal risk matrix, the product principles that follow, and open uncertainties."
tags: [research, legal, ai-act, gdpr]
---
# Legal & Regulatory Landscape: Applicant Fraud / AI-Applicant Detection

> Research snapshot: **2026-10-18**, with the Colorado, California, Illinois and New York City items refreshed on
> **2026-11-23** ([US automated-decision rules](#us-automated-decision-rules-refreshed-2026-11-23)). Product-design
> input, **not legal advice**. Items marked (†) rest on secondary sources or recollection and were not re-verified
> against a primary source. The EU and GDPR items were not re-checked on 2026-11-23.

## Status snapshot (recent changes)

- **EU AI Act "Digital Omnibus" — adopted.** Reg. (EU) 2026/1744 of 8 Jul 2026 (OJ 24 Jul 2026) [1].
  Annex III high-risk obligations (recruitment included) now apply from **2 Dec 2027**; Annex I from 2 Aug 2028.
  Art. 5 bans have applied since **2 Feb 2025** and were *not* relaxed. Art. 4 AI-literacy softened to "support" [1][2].
- **GDPR omnibus (COM(2025) 837)** — still in trilogue; Council text reportedly drops the Art. 22 and
  personal-data-definition changes [27]. Do not design around it.
- **UK** — new Arts. 22A–D in force since 5 Feb 2026 (SI 2026/82). PECR Sch. A1 para 4(2)(c) now exempts device
  access "strictly necessary… to prevent or detect fraud" from consent [12].
- **Colorado.** SB 24-205 never took effect. **SB 26-189** (Session Laws ch. 131, approved 14 May 2026) repeals and
  re-enacts C.R.S. 6-1-1701 to 1709, takes effect **1 Jan 2027** and applies to decisions made from then on [28].
  The Attorney General's rules (4 CCR 904-6) were proposed on 11 Aug 2026 and heard on 26 Oct 2026. No adoption was
  found on 2026-11-23 (†) [29]. The xAI/DOJ suit (D. Colo. 1:26-cv-01515) stayed enforcement of the old law pending
  rulemaking. Whether the stay reaches SB 26-189 is not confirmed (†) [6][31].
- **Illinois HB 3773.** In force since 1 Jan 2026 (†). IDHR notice rules were proposed on 15 May 2026 and withdrawn on
  2 Jun 2026. IDHR's rules page listed no AI rule on 2026-11-23, so the statute applies without rules [9][38].
- **California CPPA ADMT regulations.** Effective 1 Jan 2026 (OAL approval 22 Sep 2025 [33]). A business already
  using ADMT for a significant decision complies by **1 Jan 2027**. A risk assessment is due before processing
  starts; only processing begun before 2026 has until **31 Dec 2027**, and submissions for 2026-2027 are due by
  1 Apr 2028 [32].
- **California SB 690** (signed 30 Sep 2026) — eliminates only private pen-register website claims. CIPA §631
  wiretap claims (session replay / keystroke capture) remain [11].
- **NYC LL144.** Unchanged as of 2026-11-23: the DCWP rules (6 RCNY 5-300 to 5-304) are as adopted in 2023 [35]. The
  State Comptroller's audit (2 Dec 2025) found enforcement ineffective, so expect tightening [10]. The Council's 2026
  bills found are study and reporting bills, not amendments (†) [41].

## Per-signal risk matrix

| Signal | Laws triggered | Required mitigations |
|---|---|---|
| **IP address, geolocation, VPN/proxy** | GDPR (legit. interest, Art. 6(1)(f) + Recital 47); CCPA (precise geolocation = sensitive PI); IL HB 3773 (zip code as proxy is banned); national-origin disparate impact | Coarse IP-geo only, never the browser geolocation API. VPN = context, not fraud. Flag only *claimed vs observed* location mismatch. Short raw-data retention. |
| **Device / browser fingerprint** | ePrivacy Art. 5(3) covers fingerprinting [14] → consent unless "strictly necessary"; UK fraud exemption [12]; CIPA | Do **not** collect on careers-page landing; start at *application start, after notice*. EU consent path. Per-employer salted hash; short TTL. |
| **Behavioral telemetry** (paste, keystroke timing, focus/tab switches, time per question) | GDPR: keystroke dynamics are biometric only if used to uniquely identify (Art. 4(14)); becomes Art. 9 only when used *to identify*. ICO lists keystroke analysis as a behavioural biometric. AI Act biometric definition (Art. 3(34)) has no identification test, so the **Art. 5(1)(f) workplace emotion-recognition ban** reaches typing. CCPA biometric incl. keystroke rhythms; WA MHMDA (employment excluded) [21]; CPPA §7150(b)(4) risk assessment for systematic observation of applicants; CIPA §631; ADA "screen-out" + CA FEHA ADS rules [8] | **Session-level aggregates only.** No key content, no per-person typing templates, no cross-applicant matching. **No stress/"nervousness" inference.** Accommodation toggle that disables telemetry with **no negative inference**. **Never flag on telemetry alone** — dictation, screen readers, autofill and switch access look like paste/tab-switch. |
| **LLM analysis of resume, cover letter, answers** | AI Act Annex III 4(a) (profiling blocks the Art. 6(3) exemption → high-risk); GDPR Art. 22 + SCHUFA (C-634/21) (†); NYC LL144 AEDT; IL HB 3773; CO SB 26-189; CPPA ADMT; FEHA; Title VII / ADEA (*Mobley v. Workday*, ADEA collective certified May 2025 [25]) | Flag **verifiable inconsistencies with cited evidence**, not "% AI-written". AI-text detectors are unreliable and biased against non-native writers (Liang et al. 2023 †). Respect each employer's candidate-AI-use policy. Bias-audit flag rates. With ScreenHire's model the provider is a sub-processor on no-training terms; an employer may bring its own key, and the provider then handles the data under the employer's own terms, which the settings page and the candidate privacy page say. |
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

## US automated-decision rules (refreshed 2026-11-23)

Read from the enacted or adopted text where it was reachable: the Colorado session law [28], the CPPA regulations as
in force on 1 Jan 2026 [32] and the NYC rule [35]. The Illinois statute's host refused automated reads, so its items
rest on law-firm summaries that agree with each other (†). The FEHA regulation PDF gave no text to the extractor
used, so its items come from the Civil Rights Department's own pages [8][34]. Which of these rules reach ScreenHire or a
customer, and how, is a question for counsel. The gap map ([compliance-gap-map.md](compliance-gap-map.md)) sets each
duty against the product.

### Colorado SB 26-189 (C.R.S. 6-1-1701 to 1709)

What changed since the last snapshot: the enacted text is now read. It has no impact assessment or risk-management
program duty, which SB 24-205 had (†). It adds a fraud-prevention exclusion and a definition of meaningful human
review. The AG rules are still proposed.

Applicability turns on four definitions:
- "ADMT" processes personal data and computes an output, a classification included, used to make, guide or assist a
  decision (1701(2)(a)). A tool used solely to summarize, organize, translate, draft, route or present information
  for human review of administrative processing is not ADMT (1701(2)(b)(II)).
- A "consequential decision" covers employment (1701(3)(a), (6)(b)). It excludes presenting information for human
  review where the system produces no score, ranking, recommendation, classification, prediction or other inference
  that materially influences the outcome (1701(3)(b)(IV)). It also excludes "activities relating to technologies used
  for fraud prevention, including identity verification, consumer identification, monitoring, and reporting controls
  required under state or federal law" (1701(3)(b)(VIII)). Whether "required under state or federal law" limits the
  whole clause or only the controls is the open question for a fraud tool.
- "Materially influence" means the output is a non-de minimis factor and affects the outcome, by constraining,
  ranking, scoring, recommending, classifying or otherwise meaningfully altering how the decision is made
  (1701(13)). The AG may set presumptions and examples by rule (1706(5)(b)).
- A "consumer" includes a job applicant who is a Colorado resident (1701(4)(b)).

Developer (vendor) duties, from 1 Jan 2027, only where the ADMT was marketed, configured, contracted, sold or
licensed to materially influence a consequential decision (1702(3)):
- give each deployer intended and known harmful uses, the categories of training data, known limitations and risks,
  instructions for use, monitoring and meaningful human review, and what the deployer needs for its disclosures
  (1702(1)(a)-(e)), protecting trade secrets;
- notify deployers of material updates, changes of intended use, limitations or risk mitigation within a reasonable
  time, by public release notes with direct notice if preferred (1702(2));
- keep records showing compliance, version identifiers and changelogs included, for 3 years (1702(4)).

Deployer (employer) duties:
- a clear and conspicuous notice before use, which a prominent public notice at the point of interaction satisfies
  (1704(1)-(2));
- within 30 days of an adverse outcome: a plain-language description of the decision and the ADMT's role, how to ask
  for more (the ADMT's name, version and developer, and the personal data used) and the consumer's rights
  (1704(3));
- on request after an adverse outcome: how to get and correct personal data, and meaningful human review and
  reconsideration "to the extent commercially reasonable" (1705(1)). Opinions, predictions and scores need not be
  corrected (1705(1)(c));
- keep compliance records for 3 years after each decision (1703);
- accessible notices for people with disabilities and limited English (1704(8)).

Meaningful human review means a reviewer designated by the deployer, with authority to approve, modify or override,
who considers the primary evidence, is trained, does not default to the output, and can see its intended use,
limitations, input categories and principal factors (1701(15)). Nothing requires a disclosure that would compromise
fraud-prevention programs required by law (1704(7)).

Enforcement is the AG's alone under the Consumer Protection Act, with a 60-day cure notice until 1 Jan 2030 and no
private right of action (1706). A contract term indemnifying a party for its own discrimination is void (1707(7)).

| Date | What | Citation |
|---|---|---|
| 14 May 2026 | Signed; the AG rulemaking sections take effect | Session Laws ch. 131, s. 5(2) [28] |
| 11 Aug 2026 | Proposed rules filed, 4 CCR 904-6 (†) | eDocket 2026-00360 [29] |
| 26 Oct 2026 | Rulemaking hearing (†) | [29][30] |
| 1 Jan 2027 | Act effective; applies to decisions made from this date; AG rules due | s. 5(1), 5(3); 1704(4)(b); 1705(3) [28] |
| 1 Jan 2030 | The cure-notice provision is repealed | 1706(3)(f) [28] |

### California: the CPPA ADMT regulations and FEHA

What changed since the last snapshot: the text is now read. Two corrections follow. The 31 Dec 2027 risk-assessment
date applies only to processing begun before 2026; new processing needs its assessment first. And OAL approved the
text on 22 Sep 2025.

Applicability (11 CCR 7001):
- ADMT is technology that replaces or substantially replaces human decision-making, which means a decision made from
  its output without human involvement (7001(e)). Human involvement needs a reviewer who knows how to interpret and use
  the output, reviews it and other relevant information, and has authority to make or change the decision
  (7001(e)(1)).
- A significant decision includes employment opportunities (7001(ddd)).

Duties of the business (the employer) using ADMT for a significant decision (Article 11):
- comply by 1 Jan 2027 if already in use, otherwise from first use (7200(b));
- a pre-use notice at or before collection: the specific purpose, the opt-out right or the exception relied on, the
  access right, no retaliation, and how the ADMT works and what happens on opt-out (7220). It may sit in the notice
  at collection (7220(a)). It need not include what would compromise resisting fraud (7220(d)(2)(B));
- an opt-out, unless an exception applies (7221). The human appeal exception needs a designated reviewer who can
  overturn the decision and an easy appeal method (7221(b)(1)). A hiring exception covers ADMT used solely to assess
  ability to perform the job that works for its purpose without unlawful discrimination (7221(b)(2));
- an access response explaining purpose, logic, output and outcome (7222(b)), withholding what would compromise
  resisting fraud (7222(c)(2)(B)).

Risk assessments (Article 10), the business's duty:
- required for ADMT used for a significant decision (7150(b)(3)) and for automated processing that infers a job
  applicant's "behavior" or "reliability" from systematic observation (7150(b)(4); "systematic observation" is
  methodical and regular or continuous observation, 7001(eee));
- contents: purpose, categories, operational elements, the ADMT's logic and output, benefits, negative impacts,
  safeguards, whether to proceed, contributors and approvers (7152);
- before processing starts (7155(a)(1)); for processing begun before 2026, by 31 Dec 2027 (7155(b)); review every 3
  years and within 45 days of a material change (7155(a)(2)-(3)); keep 5 years or while processing continues
  (7155(c)); submit by 1 Apr 2028 for 2026-2027 (7157(a)(1)).

Duties that reach a vendor: a service provider or contractor must help the business answer an access request
(7222(i)). A business that makes ADMT available to another business must give it the facts it needs for its risk
assessment, but only for ADMT trained using personal information (7153).

FEHA automated-decision system rules (2 CCR, Civil Rights Council): effective 1 Oct 2025. Employers keep employment
records, automated-decision data included, for at least 4 years. The rules define "automated-decision system",
"agent" and "proxy" [8][34]. Whether a vendor is an "agent" turns on the definition's text, which was not read (†).

| Date | What | Citation |
|---|---|---|
| 22 Sep 2025 | CPPA regulations approved by OAL | [33] |
| 1 Oct 2025 | FEHA ADS rules effective | [8][34] |
| 1 Jan 2026 | CPPA regulations effective | [32] |
| 1 Jan 2027 | ADMT compliance for existing uses | 11 CCR 7200(b) |
| 31 Dec 2027 | Risk assessments for processing begun before 2026 | 11 CCR 7155(b) |
| 1 Apr 2028 | Risk assessment submissions for 2026-2027 | 11 CCR 7157(a)(1) |

### Illinois HB 3773 (775 ILCS 5/2-102(L)) (†)

What changed since the last snapshot: nothing in the statute. IDHR's rules page listed no AI rule on 2026-11-23 [38].
SB 3777, the Civil Rights Safeguard Act (effective 1 Jun 2027), preserves disparate-impact claims under the Human
Rights Act. IDHR's release does not mention AI [40].

- Employer: a civil rights violation to use AI with a discriminatory effect in recruitment, hiring and other
  employment decisions, or to use zip codes as a proxy for a protected class; a duty to notify employees and
  applicants of AI use; IDHR may set notice rules [39].
- Vendor: no direct duty in the statute as summarized.
- Dates: in force 1 Jan 2026 [39].

### New York City Local Law 144 (Admin. Code 20-870 to 20-874; 6 RCNY 5-300 to 5-304)

What changed since the last snapshot: the rule text is now read. No amendment was found.

- An AEDT is a tool whose simplified output (a score, tag, classification or ranking) is relied on solely, weighted
  more than any other criterion, or used to overrule conclusions including a human's (5-300). The "machine learning
  ... or artificial intelligence" element needs a computer to identify, at least in part, the inputs or their weights
  (5-300).
- Employer or employment agency: a bias audit by an independent auditor no more than a year before use (5-301(a)).
  It gives selection or scoring rates and impact ratios by sex, race and ethnicity and their intersections, and the
  count of unknowns (5-301(b)-(c)). A category under 2% of the data may be left out with a stated reason (5-301(d)).
  The audit uses historical data; data pooled across employers counts if the employer contributed its own, and test
  data only when history is insufficient (5-302). The employer publishes the audit summary and the distribution date,
  kept up 6 months after last use (5-303). It gives notice 10 business days before use, with instructions to ask for
  an alternative process or accommodation (5-304(a)-(c)), and publishes the AEDT's data retention policy, data types
  and sources, answering a written request within 30 days (5-304(d)) [35].
- Vendor: no duty. An auditor who works for the vendor is not independent (5-300). A vendor may commission the audit,
  and the employer stays responsible (DCWP FAQ, †) [37].
- Penalties: up to $500 for a first violation, $500 to $1,500 after, each day of use a separate violation (20-872) [36].

## Sources

1. Reg. (EU) 2026/1744, 8 Jul 2026 — https://eur-lex.europa.eu/eli/reg/2026/1744/oj/eng
2. Gibson Dunn, 27 May 2026 — https://www.gibsondunn.com/eu-ai-act-omnibus-agreement-postponed-high-risk-deadlines-and-other-key-changes/
3. FPF, 24 Mar 2026 — https://fpf.org/blog/red-lines-under-eu-ai-act-unpacking-the-prohibition-of-emotion-recognition-in-the-workplace-and-education-institutions/
5. Venable, Jul 2026 — https://www.venable.com/insights/publications/2026/07/colorados-new-ai-law-what-employers-need-to
6. Jenner, Apr 2026 — https://www.jenner.com/en/news-insights/client-alerts/doj-joins-xai-in-lawsuit-challenging-colorado-ai-act
7. CPPA regulation text (approved text; OAL approval was 22 Sep 2025 [33]) — https://cppa.ca.gov/regulations/pdf/ccpa_updates_cyber_risk_admt_appr_text.pdf
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
28. Colorado Session Laws 2026, ch. 131 (SB 26-189 as enacted), read 2026-11-23 - https://leg.colorado.gov/laws/session-laws/SB26-189/131/download ; bill page https://leg.colorado.gov/bills/sb26-189
29. Code of Colorado Regulations eDocket 2026-00360, 4 CCR 904-6 (status from search results, †) - https://www.coloradosos.gov/CCR/eDocketDetails.do?trackingNum=2026-00360
30. Colorado Department of Law, interim draft rules, 6 Oct 2026 (not read) - https://coag.gov/app/uploads/2026/10/2026.10.06-ADMT-Chatbot-Act-Rulemaking-Interim-Draft-Redline.pdf ; AG page https://coag.gov/resources/ai/ (did not load on 2026-11-23)
31. *X.AI LLC v. Weiser*, D. Colo. 1:26-cv-01515 (docket summary, †) - https://dockets.justia.com/docket/colorado/codce/1:2026cv01515/253513 ; Colorado Politics, 28 Apr 2026 - https://www.coloradopolitics.com/2026/04/28/court-order-delays-enforcement-of-states-first-in-the-nation-ai-law/
32. CCPA regulations, 11 CCR 7000 ff., effective 1 Jan 2026, read 2026-11-23 - https://cppa.ca.gov/regulations/pdf/ccpa_statute_eff_20260101.pdf
33. CPPA rulemaking page for the ADMT, risk assessment and cybersecurity audit package - https://cppa.ca.gov/regulations/ccpa_updates.html
34. Civil Rights Council rulemaking actions (ADS regulations, final text) - https://calcivilrights.ca.gov/civilrightscouncil/rulemaking-actions/
35. NYC DCWP Notice of Adoption, 6 RCNY 5-300 to 5-304, read 2026-11-23 - https://rules.cityofnewyork.us/wp-content/uploads/2023/04/DCWP-NOA-for-Use-of-Automated-Employment-Decisionmaking-Tools-2.pdf
36. NYC Int. 1894-A (Local Law 144 of 2021) - https://legistar.council.nyc.gov/LegislationDetail.aspx?ID=4344524&GUID=B051915D-A9AC-451E-81F8-6596032FA3F9
37. DCWP AEDT FAQ - https://www.nyc.gov/site/dca/businesses/faq-automated-employment-decision-tools.page
38. IDHR rules and regulations, checked 2026-11-23 - https://dhr.illinois.gov/about-us/rules-and-regulations.html
39. Seyfarth on HB 3773 - https://seyfarth.com/news-insights/legal-update-new-illinois-ai-law-requires-employee-notice-affirms-existing-employer-nondiscrimination-duties.html ; Duane Morris - https://www.duanemorris.com/alerts/illinois_enacts_artificial_intelligence_law_focused_employment_practices_0824.html
40. IDHR on SB 3777, 13 Aug 2026 - https://dhr.illinois.gov/about-us/news-and-media/all-news/2026-08-13-civil-rights-safeguard-act.html
41. NYC Council, 25 Sep 2026 - https://council.nyc.gov/press/2026/09/25/3252/
