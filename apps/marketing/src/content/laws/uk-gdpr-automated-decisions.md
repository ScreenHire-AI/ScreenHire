---
title: 'UK GDPR Articles 22A to 22D and hiring'
description: 'The UK rules on solely automated decisions that took effect on 5 February 2026, and the safeguards they require when hiring software decides.'
name: UK GDPR 22A-D
jurisdiction: United Kingdom
status: 'In force since 5 February 2026.'
published: '2026-10-20'
lastReviewed: '2026-10-20'
order: 2
keyDates:
  - date: '5 February 2026'
    event: 'New Articles 22A to 22D of the UK GDPR, inserted by section 80 of the Data (Use and Access) Act 2025, come into force.'
sources:
  - label: 'Data (Use and Access) Act 2025, section 80, legislation.gov.uk'
    url: 'https://www.legislation.gov.uk/ukpga/2025/18/section/80'
  - label: 'SI 2026/82, legislation.gov.uk'
    url: 'https://legislation.gov.uk/uksi/2026/82'
  - label: 'Data (Use and Access) Act 2025, Schedule 12, legislation.gov.uk'
    url: 'https://www.legislation.gov.uk/ukpga/2025/18/schedule/12'
  - label: 'Lewis Silkin on the ICO audit of AI recruitment tools, 27 November 2024'
    url: 'https://www.lewissilkin.com/en/insights/2024/11/27/ai-in-recruitment-icos-recommendations-and-key-questions'
faqs:
  - question: 'What counts as a solely automated decision under UK GDPR Article 22A?'
    answer: 'A decision with "no meaningful human involvement in the taking of the decision". When judging that, the controller must consider how far profiling was used to reach the decision.'
  - question: 'What safeguards does Article 22C require?'
    answer: 'Where a significant decision is based solely on automated processing, the controller must tell the person about it and let them make representations, obtain human intervention and contest the decision.'
---

## What it is

The Data (Use and Access) Act 2025 replaced the UK GDPR's rule on automated decisions with four new articles, 22A to 22D, in force since **5 February 2026** [1][2].

- **Article 22A** defines the terms. A decision is solely automated where there is "no meaningful human involvement in the taking of the decision", and it is significant if it has a legal or similarly significant effect on the person [1].
- **Article 22B** limits solely automated significant decisions based on special category data, such as health data, to narrow cases [1].
- **Article 22C** sets the safeguards that apply when a significant decision is solely automated [1].
- **Article 22D** lets the Secretary of State define, by regulations, when human involvement is meaningful and add to the safeguards, but not amend Article 22C [1].

## What it means if you use software to screen applicants

Rejecting an applicant is likely to have a significant effect on them. If software makes that decision with no meaningful human involvement, you must inform the applicant and let them make representations, obtain human intervention and contest the decision [1]. The test is whether the human involvement is meaningful, so a review that only confirms a tool's output may not be enough.

Separately, the UK cookie rules (PECR Schedule A1, paragraph 4(2)(c)) now exempt access to a device that is strictly necessary to prevent or detect fraud [2][3].

The ICO's 2024 audit of AI recruitment tools found vendors classing themselves as processors when they were acting as controllers [4].

## How ScreenHire is designed around it

- Integrity signals never make a hiring decision. Integrity levels can route, tag, assign or suggest verification, and workflows that would reject on integrity signals are refused.
- Each flag shows its evidence and counter-evidence, so the reviewer weighs something real rather than accepting a score.
- Candidate-facing notice comes before collection, and the design includes a "why we asked you to verify" page and a way to ask for human review.

## Still unclear

- What regulations under Article 22D will say about when human involvement is "meaningful".
