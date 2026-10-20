---
title: 'California hiring-AI rules: CPPA ADMT and FEHA'
description: 'Two sets of California rules for automated hiring tools: the CPPA rules on automated decisionmaking and the civil rights rules under FEHA.'
name: California ADMT
jurisdiction: California, United States
status: 'FEHA rules in effect since 1 October 2025. CPPA ADMT compliance due 1 January 2027.'
lastReviewed: '2026-10-20'
order: 6
keyDates:
  - date: '1 October 2025'
    event: 'Civil Rights Council regulations on automated-decision systems under FEHA take effect.'
  - date: '1 January 2026'
    event: 'The CPPA regulations on automated decisionmaking technology (ADMT), risk assessments and cybersecurity audits take effect.'
  - date: '1 January 2027'
    event: 'Businesses must comply with the ADMT requirements.'
  - date: '31 December 2027'
    event: 'Deadline for risk assessments under the CPPA regulations.'
sources:
  - label: 'California Privacy Protection Agency, CCPA updates, cybersecurity audits, risk assessments and ADMT'
    url: 'https://cppa.ca.gov/regulations/ccpa_updates.html'
  - label: 'CPPA approved regulation text'
    url: 'https://cppa.ca.gov/regulations/pdf/ccpa_updates_cyber_risk_admt_appr_text.pdf'
  - label: 'California Civil Rights Council, regulations on employment discrimination and AI, 30 June 2025'
    url: 'https://calcivilrights.ca.gov/2025/06/30/civil-rights-council-secures-approval-for-regulations-to-protect-against-employment-discrimination-related-to-artificial-intelligence/'
  - label: 'Covington, "California governor signs SB 690", October 2026'
    url: 'https://www.insideprivacy.com/state-privacy/california-governor-signs-sb-690-eliminating-private-website-based-cipa-pen-register-claims-and-calling-for-broader-cipa-reform/'
faqs:
  - question: 'When do California employers have to comply with the CPPA ADMT rules?'
    answer: 'The regulations took effect on 1 January 2026. The ADMT requirements must be met by 1 January 2027, and risk assessments are due by 31 December 2027.'
  - question: 'How long must California employers keep automated-decision data?'
    answer: 'The Civil Rights Council regulations under FEHA, in effect since 1 October 2025, require employment records, including automated-decision data, to be kept for at least four years.'
---

## What it is

California regulates automated hiring tools from two directions.

- **The CPPA regulations** under the California Consumer Privacy Act give people rights to access information about, and opt out of, a business's use of automated decisionmaking technology (ADMT), and require certain businesses to carry out risk assessments [1]. The rules took effect on **1 January 2026**. ADMT compliance is due by **1 January 2027** and risk assessments by 31 December 2027 [1][2]. Systematic observation of job applicants is one of the activities that needs a risk assessment [2].
- **The Civil Rights Council regulations** under the Fair Employment and Housing Act (FEHA) took effect on **1 October 2025**. They make clear that an automated-decision system can break anti-discrimination law if it harms applicants or employees because of a protected characteristic, that AI assessments asking about disability may be unlawful medical inquiries, and that automated-decision data must be kept for at least four years [3].

Separately, SB 690 (signed 30 September 2026) removed private pen-register claims over websites. Wiretap claims under section 631 of the California Invasion of Privacy Act, for example over session replay or keystroke capture, remain [4].

## What it means if you use software to screen applicants

- Expect to give applicants notice of ADMT use and, subject to the regulation's exceptions, a way to opt out or access information by 1 January 2027.
- Observing applicants systematically needs a risk assessment.
- Test tools for disparate impact, and keep automated-decision records for four years.
- Capturing keystrokes or replaying sessions on application forms invites wiretap claims under CIPA section 631.

## How ScreenHire is designed around it

- A person reviews the evidence behind every integrity level, and integrity signals never reject anyone.
- Typing signals are session totals counted in the applicant's browser. We never record keystrokes or replay sessions.
- Applicants who use assistive technology can switch typing signals off, with no negative inference.
- Integrity flags, evidence and reviewer actions stay in the employer's account for the record periods the law requires.

## Still unclear

- How to reconcile data minimisation under GDPR with California's four-year retention requirement, for employers hiring in both places.
