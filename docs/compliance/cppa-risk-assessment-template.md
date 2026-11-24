---
type: Guide
title: "CPPA risk-assessment template"
description: "Draft, not legal advice: a California risk assessment (11 CCR 7150 to 7157) that an employer using ScreenHire completes, in the order section 7152 sets, with the triggers, deadlines, retention and submission to the CPPA, and ScreenHire's facts filled in."
tags: [compliance, california, customers]
---
# CPPA risk-assessment template

> Draft, not legal advice, for review by counsel.

California's privacy regulations ask a business to assess the risk of some processing before it starts (11 CCR
7150 to 7157, in force 1 Jan 2026). The duty is the employer's, as the business. Whether ScreenHire's checks trigger
it is for counsel; the triggers that may apply are below. ScreenHire's facts come from the
[system description](system-description.md). The rows follow section 7152(a) in order.

## Does it apply?

| Trigger | Section | How ScreenHire fits | Your answer |
|---|---|---|---|
| ADMT used for a significant decision, employment included | 7150(b)(3) | The checks keep a person in every decision (human involvement, 7001(e)(1)). An automatic rejection on a must-have answer, which you turn on, decides without one | |
| Automated processing that infers an applicant's behaviour or reliability from systematic observation | 7150(b)(4) | Form-use checks keep session totals only, after notice, and can be turned off; whether that is "systematic observation" (7001(eee)) is open | |
| Processing sensitive personal information | 7150(b)(2) | None by design; a candidate may volunteer some | |

## Timing

- Before the processing starts (7155(a)(1)). For processing begun before 1 Jan 2026 and still running: by 31 Dec 2027
  (7155(b)).
- Review at least every 3 years, and within 45 days of a material change (7155(a)(2)-(3)). A new ScreenHire rules
  version that changes what the checks do can be one.
- Keep each version for as long as the processing runs or 5 years after the assessment, whichever is later (7155(c)).
- Submit the summary for assessments done in 2026 and 2027 by 1 Apr 2028, through the CPPA's website, attested by a
  member of executive management (7157).

## The assessment (7152(a))

| # | What to document | ScreenHire's facts | Your answer |
|---|---|---|---|
| 1 | The specific purpose, not "security" | Find applications from bots, impersonation or copied and mass applications, and show the evidence to a person | |
| 2 | Categories of personal information, the minimum needed, sensitive ones named | Application, answers, files, emails; network country and operator; form-use totals | |
| 3(A) | How it is collected, used, disclosed and kept, and its sources | From the candidate and their browser during the application, after notice | |
| 3(B) | How long each category is kept | Raw network and form-use data at most 30 days; findings and the review log with the application, for your period | |
| 3(C) | How you interact with the candidates | Your careers site and apply page | |
| 3(D) | Roughly how many candidates | | |
| 3(E) | What you tell them, and how | The apply page notice and the privacy page; your own notices | |
| 3(F) | Service providers and why | ScreenHire (service provider); its sub-processors: hosting, email, the AI provider when the language-model check is on | |
| 3(G) | For ADMT: its logic, assumptions and limits; its output and how you use it | Categorical levels with reasons and evidence, never a score; a person decides. Limits: see the system description | |
| 4 | Benefits to you, candidates and others, specifically | | |
| 5 | Negative impacts and their causes (discrimination, loss of control, economic harm, stress) | A wrongly flagged candidate faces a delay or a question; see the safeguards | |
| 6 | Safeguards | No score; never a rejection from a check; evidence against each reason; assistive-technology switch; tenant isolation; retention limits; bias monitoring ([bias audit](bias-audit-method.md)); reviewer guidance | |
| 7 | Whether you will go ahead | | |
| 8 | Who gave information (not counsel) | | |
| 9 | Who reviewed and approved it, their role and the date; one must have authority over whether to proceed | | |

## What ScreenHire provides

- This template and the system description.
- Help answering a candidate's access request about the checks (7222(i)): the decision record.
- No model is trained on applicant data, so 7153's duty to share training facts does not arise. If that changes,
  ScreenHire will say so.
