---
type: Research
title: "Competitive Teardown"
description: "ATS competitors' prices, AI features and fraud defences as of 2026-10-18, the AI and fraudulent applicant threat landscape, the gaps in the market, and ScreenHire's positioning against GoHire."
tags: [research, competitors, positioning]
---
# Competitive Teardown

> Research snapshot: **2026-10-18**. Prices from vendor pages fetched that day. **[U]** = could not verify.

## 1. Comparison

### ATSs

| ATS | Entry price | AI | Fraud / AI-applicant defenses |
|---|---|---|---|
| **GoHire** [1] | £89/$99/mo (3 active jobs); Growth £149 (10 jobs); Pro £249 (unlimited); unlimited users; Plus+ AI add-on £40/mo | "Match AI" scores CVs 1–10 + summaries; auto-reject by score needs Pro + Plus+ (~£289/mo) | **None documented** |
| **Breezy HR** [3] | Free (1 job); $157/mo annual | Credit-based scoring | Strongest SMB set: Resume Audit (generator-built CVs, same-day file creation, keyword stuffing), email verification, disposable-email filter on by default since Sep 2026 |
| Workable [2] | $299/mo annual (1–20 staff) | Workable Agent | Tags "AI-assisted" applications; WAF, rate limits, CAPTCHA |
| Recruitee (Tellent) [4] | Quote | Screening assistant | CAPTCHA; per-email/phone limits |
| Teamtailor [5] | Quote | Co-pilot | Email-domain spam rules; TrustID at background-check stage; actively *welcomes* AI agents ("agent-ready career sites", May 2026) |
| JazzHR (Employ) [6] | $1,000/yr | Matching; VONQ screening | None native; ID.me integration announced Jul 2026 |
| Manatal [7] | $15/user/mo | Scoring; AI interviewer | Duplicate detection only |
| Homerun [8] | €109/£99/mo (annual) | None, by design | None |
| Pinpoint [9] | Quote | Hiring Copilot | None shipped; fraud webinar 29 Oct 2026 |
| Zoho Recruit [10] | Free; $25/recruiter/mo | Zia | Email blocklist |
| Greenhouse [11] | Quote | Matching, notetaker | Real Talent (Jun 2025): rule-based IP, location, contact, data-centre signals; CLEAR ID + selfie. **Plus/Pro tiers only** |
| **Ashby** [12] | $400/mo (≤100 staff) | AI application review, notetaker | Fraud detection on **all plans** (Sep 2025): device, IP, email, phone; 2 AI credits per check |
| Lever (Employ) [13] | Quote | Matching | "Fraud Signals" in Core (May 2026): email, VoIP, bot-like behaviour, work-history consistency |
| Workday [14] | Quote | HiredScore, Paradox | 2026R1 fraud signals [U]; Persona as IDV partner |
| iCIMS [15] | Quote | Agents | Candidate Fraud Strategy (16 Oct 2026): hCaptcha, geo/IP controls, OTP; partners Socure (IDV), ApplicantX (deepfake) |
| SmartRecruiters (SAP) [16] | From $14,995 | Winston agents | Fraud detection "in development" |
| Gem [17] | $130/mo startup | Agents | Fraud agent powered by Tofu: 6 passive signals, high/medium/low with reason; add-on |

### GoHire in depth

- **Automation:** Pro-only "stage actions" (send email, add note, send questionnaire). **No event triggers, no knockout
  filters, no public API** [1].
- **Posting:** 27 boards. Pain points ScreenHire can fix: a closed job still counts as an active, billable job until
  archived; LinkedIn premium posting needs the customer's own Recruiter contract; Indeed sponsorship billed separately.
- **Reviews** (snippets only): Capterra 4.5 (153) — praise simple UI, support, multiposting; complaints about slowness,
  crashes, patchy support [18]. G2 3.7 (11) — "almost too simple" [19].
- **Company:** UK micro-entity accounts; no funding events on record [20].

### Specialist vendors (mostly "contact sales")

- **Application-stage scoring:** Tofu (synthetic identities, DPRK IT workers; integrates Greenhouse/Ashby/Lever/Workday)
  [21]; Sardine (virtual cameras, remote-access tools) [22]; Alex Verify (email age, VPN, LinkedIn depth) [23].
- **ID verification for hiring:** Persona (ID + liveness; Greenhouse/Ashby/Workday) [24]; CLEAR, Socure, Incode,
  Checkr, ID.me.
- **Interview deepfake/voice:** Pindrop Pulse [25], Reality Defender [26], GetReal, BrightHire Integrity ("LLM Answer
  Match", GA Aug 2026) [27].
- **Assessment cheating:** CodeSignal "Suspicion Score" [28]; HackerRank lockdown desktop app; TestGorilla from $215/mo [29].
- **AI interviewers:** HireVue (focus-loss detection, paste blocking, same-person continuity) [30]; Sapia, micro1.

### Earlier research (naming/discovery task, Sept–Oct 2026)

- **Sardine** — the owner called it "pretty close" to the ScreenHire concept. Enterprise financial-crime platform that
  added job-application fraud detection in 2026; plugs into an existing ATS via webhooks or hosted forms; passive
  device, behaviour and network signals plus deepfake detection; sold to security/compliance teams.
- **Tofu** (hiretofu.com) — candidate-fraud detection as an add-on to an existing ATS.
- **Pangram, It's AI** — AI-written-text detectors (see `legal.md` on why detector output must not be a verdict).
- **Ashby** — ATS with built-in fraud signals.
- **Kyle & Co landscape report** — counts **60+ vendors**; says the market has converged on *"surface signals not
  verdicts, explain every signal, never auto-reject"*; names the gap as **small teams who can't interpret raw fraud
  signals**. ScreenHire's opening: an affordable tracker whose assessment comes with a plain-language explanation and a
  suggested next step.

## 2. Threat landscape

1. **Auto-apply floods** — LazyApply sells up to 1,500 applications/day [31]; Ashby sees 291 applications per hire, up
   from ~100 in 2021 [32].
2. **Real candidates polishing with AI (not fraud)** — 39% used AI in applications [34]; 74% of US job seekers use AI [35].
3. **Prompt injection against AI screeners** — 41% of job seekers admit to it [35].
4. **Stolen/synthetic identities and stand-ins** — 31% of US hiring managers interviewed someone using a fake identity;
   35% saw a different person join an interview [36].
5. **Deepfakes** — 18% of hiring managers have caught one [35].
6. **North Korean IT workers** — DOJ found 29 laptop farms, 100+ affected US companies [38]; Pindrop links 1 in 343 of
   its own applicants to DPRK [37].
7. **Real-time interview copilots** — Cluely sells "undetectable" live help [39].

**Headline:** Gartner predicts that by 2028, 1 in 4 candidate profiles will be fake [34][40].

## 3. Gaps & positioning

- **Benchmarks to beat: Breezy (SMB) and Ashby (mid-market).** GoHire is the product template, not the fraud benchmark.
  Both score the applicant *once* from device/IP/email/phone or a CV audit.
- **Nobody covers the whole funnel.** No SMB-priced tool combines in-form behaviour signals, step-up ID, interview
  continuity and reason codes, or follows one identity from first page visit to Day 1. Enterprises stitch Persona +
  Pindrop + HackerRank via sales teams.
- **Accuracy is opaque** — no vendor publishes false-positive rates.
- **Most tools blur "AI-assisted" and "fraud"**, though three in four job seekers use AI.
- **Compliance can be a moat** (see `legal.md`): EU emotion-recognition ban, AI Act high-risk duties from 2 Dec 2027,
  UK Art. 22C human review/contest, BIPA, Colorado/CCPA ADMT from 2027, NYC LL144; ADA exposure if typing/paste signals
  penalise assistive tech (analysis, not a cited ruling) [41–48].

**Positioning:** *an integrity layer from first click to first day that recruiters and candidates can both understand,
at GoHire prices.*

## 4. Candidate MVP differentiators (from research — to be reconciled with `legal.md` in the ADR)

1. **Two separate assessments** — "real, unique person" vs "level of AI assistance", each with reason codes and evidence.
   Never auto-reject; tune with recruiter feedback; publish false-positive rates.
2. **Privacy-tiered telemetry** — IP/device reputation (data centre, residential proxy, VPN, timezone), email age, VoIP
   detection; paste/tab-switch/typing cadence as in-browser aggregates only; never sole reason for a flag;
   accessibility override; EU/UK consent + DPIA defaults.
3. **Cross-customer ring detection** — hashed devices/phones/emails/LinkedIn URLs/CV embeddings across customers.
   ⚠️ `legal.md`: makes ScreenHire a GDPR controller and a possible FCRA CRA — **owner decision required**.
4. **CV & answer forensics** — hidden prompt-injection detection in CVs, CV ↔ LinkedIn-export ↔ email consistency,
   near-identical answers across applicants (within an employer).
5. **Step-up ID verification only on high risk** — or at a template-chosen stage; per-check pricing (Gem cites $1.80/check;
   Ashby 2 credits); wrap Persona/Veriff/Incode first; BIPA release + GDPR Art. 9 consent built in.
6. **Interview continuity** — ScreenHire-issued interview links checking virtual cameras, VPN changes, face/voice
   consistency with verified ID. No emotion inference.
7. **Fraud-gated job templates + clean posting** — knockout questions, honeypot fields, trap questions, conditional
   verification stages; one-click teardown archives on every board; all via API + webhooks.
8. **Declared-AI lane and appeals** — candidates disclose AI help or apply via a signed agent API; "why we asked you to
   verify" page and appeal flow (UK Art. 22C, CCPA/Colorado notices).

## Could not verify

GoHire G2/Capterra figures (snippets only); LinkedIn "11,000 applications per minute" (second-hand via eWeek [33]);
Gartner exact wording (403; via HR Dive); Workday fraud signals; ID.me shipped status; Reality Defender GA; Persona
Ashby/Workday integration dates; Truely (likely discontinued), Polygraf, Sherlock, Mercor; all accuracy figures are
vendor claims; currency on Workable/Breezy/JazzHR; Honorlock, Ropes, Crosscheck not researched.

## Sources

1. https://gohire.io/pricing ; https://help.gohire.io/en/articles/7920630-how-much-is-gohire ; https://help.gohire.io/en/articles/12358433-boost-your-hiring-power-with-the-plus-plan ; https://help.gohire.io/en/articles/3385844 ; https://help.gohire.io/en/articles/6837442 ; https://help.gohire.io/en/articles/8009831
2. https://www.workable.com/pricing ; https://www.workable.com/workable-ai
3. https://www.breezy.hr/pricing ; https://www.breezy.hr/p/breezy-intelligence
4. https://recruitee.com/pricing
5. https://www.teamtailor.com/en/pricing
6. https://www.jazzhr.com/pricing ; https://www.employinc.com/?p=5617
7. https://www.manatal.com/pricing
8. https://www.homerun.co/pricing ; https://www.homerun.co/about/ai
9. https://www.pinpointhq.com/product-releases ; https://www.pinpointhq.com/pinpoint-how-to-series-how-to-combat-candidate-fraud
10. https://www.zoho.com/recruit/pricing.html
11. https://www.greenhouse.com/blog/introducing-greenhouse-real-talent ; https://support.greenhouse.io/hc/en-us/articles/40966215931291 ; https://greenhouse.com/newsroom/greenhouse-and-clear-announce-partnership-to-enable-candidate-verification
12. https://www.ashbyhq.com/blog/all/ashby-launches-the-first-ats-integrated-fraud-detection-system ; https://docs.ashbyhq.com/candidate-fraud-detection ; https://www.ashbyhq.com/pricing
13. https://www.lever.co/blog/from-chaos-to-clarity-to-connection-how-lever-s-spring-2026-release-puts-the-human-back-in-hiring
14. https://newsroom.workday.com/2025-10-01-Workday-Completes-Acquisition-of-Paradox
15. https://www.icims.com/company/newsroom/frauddetection/
16. https://www.smartrecruiters.com/news/sap-completes-acquisition-of-smartrecruiters ; https://www.smartrecruiters.com/pricing
17. https://www.gem.com/blog/introducing-ai-fraud-detection-agent ; https://help.gem.com/databases/gem-help-center/fraud-detection-agent-overview ; https://www.gem.com/pricing
18. https://www.capterra.com/p/184827/GoHire/reviews/
19. https://www.g2.com/products/gohire-technologies-ltd-gohire/reviews
20. https://find-and-update.company-information.service.gov.uk/company/10056150/filing-history
21. https://hiretofu.com
22. https://sardine.ai/job-applicant-fraud
23. https://alex.com/verify
24. https://integrations.greenhouse.com/partners/persona
25. https://www.pindrop.com/solution/meeting-security
26. https://www.realitydefender.com/product/realmeeting
27. https://www.brighthire.com/?p=15928
28. https://codesignal.com/cheating-and-fraud
29. https://www.testgorilla.com/pricing
30. https://www.hirevue.com/platform/cheating-and-fraud
31. https://lazyapply.com/
32. https://www.ashbyhq.com/talent-trends-report/reports/2023-recruiter-productivity-trends-report
33. https://www.eweek.com/news/ai-job-applications-linkedin/
34. https://www.gartner.com/en/newsroom/press-releases/2025-07-31-gartner-survey-shows-just-26-percent-of-job-applicants-trust-ai-will-fairly-evaluate-them
35. https://www.greenhouse.com/newsroom/an-ai-trust-crisis-70-of-hiring-managers-trust-ai-to-make-faster-and-better-hiring-decisions-only-8-of-job-seekers-call-it-fair
36. https://checkr.com/resources/articles/hiring-hoax-manager-survey-2025
37. https://www.pindrop.com/article/why-your-hiring-process-now-cybersecurity-vulnerability/
38. https://www.justice.gov/opa/pr/justice-department-announces-coordinated-nationwide-actions-combat-north-korean-remote
39. https://cluely.com
40. https://www.hrdive.com/news/fake-job-candidates-ai/757126/
41. https://digital-strategy.ec.europa.eu/en/library/commission-publishes-guidelines-prohibited-artificial-intelligence-ai-practices-defined-ai-act
42. https://eur-lex.europa.eu/eli/reg/2026/1744/oj/eng
43. https://www.legislation.gov.uk/ukpga/2025/18/section/80
44. https://www.ilga.gov/Documents/legislation/ilcs/documents/074000140K20.htm
45. https://leg.colorado.gov/bill_files/116489/download
46. https://cppa.ca.gov/regulations/ccpa_updates.html
47. https://www.nyc.gov/site/dca/about/automated-employment-decision-tools.page
48. https://www.ada.gov/resources/ai-guidance/
