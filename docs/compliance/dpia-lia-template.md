---
type: Guide
title: "DPIA and legitimate interests assessment template"
description: "Draft, not legal advice: a data protection impact assessment (GDPR Art. 35) and a legitimate interests assessment (Art. 6(1)(f)) that an employer using ScreenHire completes, with ScreenHire's facts filled in and the employer's choices left blank."
tags: [compliance, gdpr, customers]
---
# DPIA and legitimate interests assessment template

> Draft, not legal advice, for review by counsel.

An employer using ScreenHire's checks on applicants in the EU, the EEA or the UK is the controller and ScreenHire its
processor. A DPIA is expected: the checks evaluate people, observe how a form is used, and touch job applicants, who
cannot easily refuse. The lawful basis is usually legitimate interests, which needs an assessment of its own. This
template does both. ScreenHire's facts are filled in from the [system description](system-description.md). Every
"Your answer" is the employer's.

## Part 1: Data protection impact assessment

### 1. The processing

| Question | ScreenHire's facts | Your answer |
|---|---|---|
| Purpose | Find applications that come from bots, impersonation or copied and mass applications, and show the evidence to a person who decides | Your reasons, in your words (a purpose described as "security" is too vague) |
| Data subjects | Job applicants | Which roles and countries |
| Personal data | The application, answers, files and emails the candidate gives; the network's country and operator; totals of form use, after notice | Any extra questions you add |
| Special category data | None by design. A candidate may volunteer some in a free-text answer or a file | How your reviewers handle it if they do |
| Sources | The candidate, and their browser during the application, after the notice | |
| Recipients | Your hiring team. Sub-processors: hosting, email, and the AI provider when the language-model check is on (no training on the data) | Anyone else you share it with |
| Transfers outside the EEA or UK | Depends on the hosting region and the AI provider in the customer agreement | Your safeguards |
| Retention | Raw network and form-use data: at most 30 days. Findings and the review log: with the application, for the period you set | Your period, and the law that sets it |
| Automated decisions | None from the checks: a person reviews every finding and no check can reject. A knockout question you set to reject automatically is an automated decision under Art. 22 | Whether you use automatic knockout rejection, and on what |

### 2. Necessity and proportionality

| Question | ScreenHire's facts | Your answer |
|---|---|---|
| Is each kind of data needed? | Network and form-use data are kept as totals and deleted within 30 days | Why you need the checks you turn on |
| Could you do less? | Each group of checks can be turned off for the organization | Which you turn off |
| How are candidates told? | The apply page shows a notice before anything beyond server logs is collected | Your privacy notice |
| How do candidates exercise their rights? | Copy and erasure requests from the careers site; the decision record for an access request | Your process and its owner |

### 3. Risks to candidates

| Risk | Safeguard in ScreenHire | Likelihood and severity, after safeguards | Your extra measures |
|---|---|---|---|
| A genuine candidate is wrongly flagged | Evidence against each reason; no score; a person reviews; never a rejection | | |
| A flag is held against a disabled candidate | The assistive-technology switch turns form-use checks off with no finding | | |
| Bias against a group | No emotion, personality or biometric inference; a bias audit method ([bias audit](bias-audit-method.md)) | | |
| Data reaches the wrong people | Tenant isolation, roles and permissions, an audit log | | |
| Data kept too long | Retention settings; 30-day limit on raw data | | |
| AI provider misuse | No-training terms; the check can be turned off | | |

### 4. Measures and sign-off

| Item | Your answer |
|---|---|
| Measures you adopt beyond ScreenHire's | |
| Residual risk, and whether prior consultation with the authority is needed (Art. 36) | |
| Data protection officer's advice | |
| Approved by, role, date | |
| Review date | |

## Part 2: Legitimate interests assessment

### 1. Purpose test

| Question | Your answer |
|---|---|
| What is the interest? (for example, keeping fraudulent and automated applications from crowding out real applicants, and protecting the hiring process from impersonation) | |
| Whose interest is it: yours, the applicants', or both? | |
| Would you be able to say it plainly to a candidate? | |

### 2. Necessity test

| Question | Your answer |
|---|---|
| Do the checks actually help with the interest? | |
| Is there a less intrusive way to reach it? | |
| Have you turned off what you do not need? | |

### 3. Balancing test

| Question | ScreenHire's facts | Your answer |
|---|---|---|
| Would candidates expect it? | Notice before collection on the apply page | |
| What is the impact on them? | A finding leads to a review or a question, never a rejection on its own | |
| Are any candidates vulnerable? | Applicants cannot easily refuse; the assistive-technology switch carries no penalty | |
| Can they object? | Through your privacy contact; your process decides | |
| Outcome | | Does the interest outweigh the impact? |

Record the outcome, who decided and when, and review it when the processing changes.
