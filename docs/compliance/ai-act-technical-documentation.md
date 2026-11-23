---
type: Reference
title: "EU AI Act technical documentation and post-market monitoring plan"
description: "Draft, not legal advice: the skeleton of the technical documentation a provider of a high-risk AI system keeps under the EU AI Act (Art. 11 and Annex IV) and its post-market monitoring plan (Art. 72), for ScreenHire's checks if counsel finds them high-risk under Annex III 4(a), with ScreenHire's facts filled in where the repository already holds them and the gaps named, due before 2 December 2027."
tags: [compliance, ai-act, eu]
sources:
  - resource: src/ScreenHire.Domain/Verification/VerificationRuleset.cs
  - resource: src/ScreenHire.Domain/Hiring/DefinitionValidator.cs
---
# EU AI Act technical documentation and post-market monitoring plan

> Draft, not legal advice, for review by counsel.

Recruitment and selection tools are high-risk under the AI Act's Annex III 4(a), and profiling keeps a tool out of the
Art. 6(3) exemption ([legal.md](../research/legal.md)). The Annex III duties apply from 2 December 2027 under Reg. (EU)
2026/1744. Whether ScreenHire's checks are an AI system in the Act's sense, and whether they are high-risk, is counsel's
call: a level that only routes an application to a person may still count as profiling. Legal principle 8 commits
ScreenHire to the provider standard regardless, so this is the skeleton of what a provider keeps.

The headings follow Annex IV as summarized in the research; the regulation text was not re-read for this draft (†). This
repository is public, so the document a regulator receives holds what this page only points to: thresholds, signal
definitions and test results stay in the private technical file.

## 1. General description (Annex IV point 1)

| Item | ScreenHire | Gap |
|---|---|---|
| Intended purpose | Point out applications that may come from bots, impersonation or copied and mass applications, with evidence, for a person to review ([system description](system-description.md)) | None |
| Provider | ScreenHire; the legal entity is in the customer agreement | Owner fills in |
| Version | The rules version recorded on every assessment ([changelog](checks-changelog.md)) and the release | None |
| How it interacts with other software | An applicant tracking system: the careers site collects, the API and worker assess, the dashboard shows | None |
| Forms it is placed on the market in | Software as a service | None |
| Instructions for use | The system description; reviewer guidance in it | Finalise with counsel |

## 2. Elements and development (point 2)

| Item | Where it is | Gap |
|---|---|---|
| Design choices and the logic | ADR-0004 (evidence, not verdicts), the verification engine design | A private write-up of thresholds and rule rationale |
| Architecture | The architecture docs | None |
| Data | No model is trained on applicant data; the rules are written by hand; the optional language model runs on a provider's no-training terms | A datasheet for the synthetic applicants the rules are tested on |
| Human oversight (Art. 14) | No check can reject (`DefinitionValidator`); a person reviews every flag, sees the evidence and counter-evidence, and records a reason to set one aside | Training material for reviewers (the employer's) |
| Changes decided in advance | Every rule change moves the rules version | A policy on what counts as substantial modification |
| Validation and testing | Golden tests over synthetic applicants, run on every change | Measured error rates on real use, by group |
| Cybersecurity | Tenant isolation with row-level security, an audit log | A security assessment |

## 3. Monitoring, functioning and control (point 3)

| Item | ScreenHire | Gap |
|---|---|---|
| Capabilities and limits | The system description's known limitations | Measured rates of findings later set aside |
| Accuracy for groups of people | The [bias audit method](bias-audit-method.md) | No impact-ratio data yet |
| Foreseeable unintended outcomes | A genuine candidate flagged; an assistive-technology user misread (the switch turns form-use checks off) | None beyond the audit |
| Input data | What the candidate gives, and the session after notice | None |

## 4. Performance measures (point 4)

ScreenHire reports no score, so accuracy is measured on outcomes: the share of findings reviewers set aside as false
positives, by reason and by group, from the review log's dismissal reasons. The gap is the report that computes it.

## 5. Risk management (point 5, Art. 9)

The legal research's per-signal risk matrix and the product principles that follow from it are the start. The gap is a
living risk register with an owner, reviewed at each rules version.

## 6. Changes over the lifecycle (point 6)

The [changelog](checks-changelog.md), one entry per rules version, and the version on every assessment.

## 7. Standards applied (point 7)

None yet. The harmonised standards under the Act were not checked for this draft (†).

## 8. EU declaration of conformity (point 8)

Not drawn up. It follows an internal-control conformity assessment (Art. 43), which is the owner's with counsel.

## 9. Post-market monitoring plan (point 9, Art. 72)

What ScreenHire would collect, per organization and with nothing that identifies a person:

1. The share of findings set aside, by reason (verified another way, explained by the candidate, false positive).
2. Findings by level over time, and after each rules version.
3. Impact ratios by group where an organization runs equality monitoring ([bias audit](bias-audit-method.md)).
4. Candidates' review requests and their outcomes, once the appeal channel exists.

Who reads it: the owner, each quarter and after every rules version. What it triggers: a rule change through the
changelog, and a serious incident reported under Art. 73 when one occurs. The gaps are the report behind items 1 to 3 and
the appeal channel behind item 4.
