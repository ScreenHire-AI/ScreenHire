---
title: 'Illinois hiring-AI laws: HB 3773, AI video, BIPA'
description: 'Three Illinois laws that matter for hiring software: HB 3773 on AI and discrimination, the AI Video Interview Act, and BIPA on biometrics.'
name: Illinois
jurisdiction: Illinois, United States
status: 'HB 3773 in force since 1 January 2026. The AI Video Interview Act and BIPA also apply.'
lastReviewed: '2026-10-18'
order: 4
keyDates:
  - date: '1 January 2026'
    event: 'HB 3773 takes effect, amending the Illinois Human Rights Act to cover AI in employment decisions.'
  - date: '15 May 2026'
    event: 'The Illinois Department of Human Rights (IDHR) proposes notice rules under HB 3773.'
  - date: '2 June 2026'
    event: 'IDHR withdraws the proposed rules. The statute still applies.'
sources:
  - label: 'Burke Law, "Illinois Department of Human Rights withdraws proposed AI in employment rules", 16 June 2026'
    url: 'https://www.burkelaw.com/alert-illinois-department-of-human-rights-withdraws-proposed-ai-in-employment-rules'
  - label: 'Biometric Information Privacy Act, 740 ILCS 14, Illinois General Assembly'
    url: 'https://www.ilga.gov/Documents/legislation/ilcs/documents/074000140K20.htm'
  - label: 'Privacy World on Sosa v. Onfido (BIPA and ID-selfie matching), April 2022'
    url: 'https://www.privacyworld.blog/2022/04/breaking-federal-judge-in-illinois-affirms-that-bipa-extends-to-information-derived-from-photographs/'
  - label: 'American Bar Association, voiceprints and new trends in BIPA litigation'
    url: 'https://www.americanbar.org/groups/litigation/resources/newsletters/class-actions-derivative-suits/voiceprints-ai-bipa-new-trends-biometric-privacy-litigation/'
faqs:
  - question: 'Can hiring software use zip codes in Illinois?'
    answer: 'Not as a stand-in for protected characteristics. HB 3773, in force since 1 January 2026, bars employers that use AI in employment decisions from using zip codes as a proxy.'
  - question: 'Does BIPA apply to ID verification with a selfie?'
    answer: 'A federal court in Illinois held in Sosa v. Onfido (2022) that matching an ID photo to a selfie scans face geometry under BIPA, which requires a written release and a public retention schedule.'
---

## What it is

Illinois has three laws that matter for hiring software.

- **HB 3773** amends the Illinois Human Rights Act. Since **1 January 2026** it has covered employers' use of AI in employment decisions, including a ban on using zip codes as a proxy for protected characteristics. IDHR proposed notice rules on 15 May 2026 and withdrew them on 2 June 2026. The statute applies regardless [1].
- **The Artificial Intelligence Video Interview Act** covers employers that use AI to analyse video interviews. Our research records duties to give notice, explain how the AI works, get consent, limit sharing and delete videos within 30 days of a request. We have not re-checked these details against the statute text, so read the act itself before relying on them.
- **The Biometric Information Privacy Act (BIPA)** covers biometric identifiers such as face geometry and voiceprints. Collecting them requires a written release and a public retention schedule. Courts have applied it to matching an ID photo with a selfie [3] and to voiceprints [4].

## What it means if you use software to screen applicants

- Check that no input acts as a stand-in for a protected characteristic. Location data is the obvious risk.
- AI analysis of video interviews needs notice and consent before the interview.
- Any face or voice matching, including ID-and-selfie checks, needs a BIPA release and a retention schedule first.

## How ScreenHire is designed around it

- Location is used only as a mismatch between where the applicant says they are and the country or region their connection comes from. It is never a zip code and never a reason on its own.
- This release has no biometrics at all: no face, voice or ID-document checks.
- When optional ID verification arrives in a later release, the plan is to store pass or fail only, delete images within days, collect a BIPA release first and always offer a live check with a person instead.
- No emotion or personality inference from video, ever.

## Still unclear

- When IDHR will propose notice rules under HB 3773 again, and what they will require.
- Whether BIPA reaches brief, non-identifying analysis of a face or voice.
