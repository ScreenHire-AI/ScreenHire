---
type: Reference
title: "System description: what ScreenHire's checks do and do not do"
description: "Draft, not legal advice: the customer-facing description of ScreenHire's application checks, written to the shape of Colorado's developer documentation (C.R.S. 6-1-1702), covering intended and inappropriate uses, the data used and that no model is trained on applicants, known limitations, how a person reviews a flag, what an employer needs for its own notices, and how changes are announced."
tags: [compliance, verification, customers]
sources:
  - resource: apps/api/Endpoints/DecisionRecordEndpoints.cs
  - resource: src/ScreenHire.Domain/Hiring/DefinitionValidator.cs
  - resource: src/ScreenHire.Domain/Verification/VerificationRuleset.cs
---
# System description: what ScreenHire's checks do and do not do

> Draft, not legal advice, for review by counsel.

This is the description an employer reads before it uses ScreenHire's application checks. Its sections follow what
Colorado asks a developer to give a deployer (C.R.S. 6-1-1702(1)), and it also answers what a California risk
assessment and an NYC bias audit need from the vendor. It says what the checks do and what a reviewer sees. It does
not say how a check works or what it looks for, because that would only help someone get past it.

## What ScreenHire is

ScreenHire is an applicant tracking system for small hiring teams. Besides the usual pipeline, it checks each
application for signs that it came from a bot, from someone posing as another person, or from a copied or mass
application, and it notes how much of the content appears generated. It shows every finding to a person with the
evidence behind it. A person decides what happens next.

## Intended uses

- Help a hiring team spend its time on real applicants, by pointing out applications that need a closer look.
- Give the reviewer the evidence for each finding, so they can check it themselves.
- Suggest a next step, such as asking the candidate a question or to verify their identity.
- Keep a record of what was found and what each reviewer did, and why.

## What the checks produce

Each application gets two separate assessments:

- **Authenticity:** whether the application looks like it comes from a real, unique person who is who they say they
  are. The result is one level from a short, fixed list.
- **AI assistance:** how much of the content appears generated or templated, set against the job's own policy on AI
  use. It is information for the reviewer, never a mark against the candidate on its own.

Each assessment comes with its reasons, the evidence for each reason, the evidence against it, a plain-language
summary and a suggested next step. There is no score, no percentage and no ranking anywhere.

## What the checks never do

- **Never reject.** No setting, automation or integration can reject an application because of a check. The
  workflow builder refuses such an automation when it is saved, and again before it runs. A check can only route an
  application to a review stage, assign a reviewer, add a tag or suggest verification.
- **Never infer** emotion, intent, honesty or personality, from anything.
- **No biometrics.** There is no face or voice analysis and no biometric template.
- **No scraping.** ScreenHire reads what the candidate gives it. It never fetches a LinkedIn or other third-party
  profile.
- **No sharing between employers.** Each organization's data stays its own.
- **No training on applicants.** No model is trained on applicant data.

The one automated rejection ScreenHire offers is not a check: an employer may let a knockout question reject an
application whose answer it marked as disqualifying, and the candidate is told the reason. That is the employer's own
rule, and in some places it counts as an automated decision (see the [gap map](../research/compliance-gap-map.md)).

## Uses that are harmful or inappropriate

- Rejecting, or not advancing, a candidate because of a level alone.
- Weighting a level above every other criterion, or letting it overrule a reviewer. Under NYC Local Law 144 that use
  can make the checks an automated employment decision tool, with a bias audit and notices due.
- Treating AI assistance as dishonesty. Many candidates use AI tools within what a job allows, and the AI-assistance
  assessment is read against the job's own policy.
- Holding the assistive-technology choice against anyone. A candidate who turns it on has form-use checks switched
  off, and that is never a finding.
- Asking questions that act as a proxy for a protected class, such as a postal code, and automating on the answer.
  Illinois forbids using a zip code as a proxy.

## Data used

- **What the candidate gives:** the application form, their answers, the files they upload and their emails to the
  team.
- **What the application's session shows:** the network the application came from, kept as its country and operator,
  and totals of how the form was used. These are collected only after the notice on the apply page, and the raw
  values are deleted within 30 days.
- **Training data:** none. The rules are written by hand and versioned. The optional language-model check sends text
  to the AI provider on terms that forbid training on it. An employer that brings its own provider key uses that
  provider's terms instead.

## Known limitations

- A check can be wrong. Shared networks, privacy tools, assistive technology, dictation and autofill can all look
  like something they are not. Each finding shows the evidence against it for this reason.
- Judging whether text is AI-written is unreliable, and Liang et al. (2023) found such judgements biased against
  writers whose first language is not English. That is why AI assistance is a separate assessment from authenticity,
  shown as information and read against the job's own AI policy, and why it should never weigh on a decision alone.
- An application added by hand, or a group of checks the organization turned off, has fewer checks behind it. The
  result says which checks did not run, and a clean result then means less.
- ScreenHire cannot see how a reviewer weighs a level. The safeguards above stop a check from rejecting, not a person
  from leaning on it.

## How a person reviews a flag

Colorado calls this meaningful human review (6-1-1701(15)). The employer chooses and trains its reviewers. ScreenHire
gives them:

- each reason with its evidence and the evidence against it, so they can check the primary evidence themselves;
- no score to default to, and a level that cannot act on its own;
- a dismissal that needs a reason (verified another way, explained by the candidate, a false positive, or a note), and
  a per-reason "explained" mark;
- a review log of every action, with who took it and when.

Suggested practice for reviewers:

1. Read the evidence before the summary, and the evidence against before deciding.
2. When in doubt, ask the candidate. A question costs less than a lost hire.
3. Record why you set a finding aside.
4. Never reject on a finding alone. If a finding is part of a decision, write down what else was.

## Records and requests

- **Review log:** every dismissal, explanation and note, kept with the application.
- **Decision record:** one application's levels, evidence, reviewer actions, stage moves and automations in one file,
  for the team answering a candidate's request (off by default; see
  [data requests](../architecture/data-requests.md)).
- **Retention:** the organization sets how long ended applications are kept. Some laws set a minimum for decision
  records (3 years in Colorado, 4 under California's FEHA rules), so set the period with counsel.
- **Candidate requests:** a candidate can ask for a copy of their data, or its erasure, from the careers site, and,
  where the organization turns it on, for a person to look again at a rejection from their own page.
- **Rejection reasons:** a person rejecting can pick the principal reason from a short list, kept with the move; an
  automation that rejects on a knockout answer records that as its reason.

## For the employer's own notices

An employer that must name the tool in a notice or a disclosure can use these:

- **Name:** ScreenHire application checks.
- **Version:** every assessment records the version of the rules it ran under. The decision record shows it.
- **Developer:** ScreenHire. The legal entity is named in the customer agreement.
- **Personal data used:** the categories under "Data used" above.

## Changes

ScreenHire versions its rules, and a change in what the checks do raises the version. Each version has an entry in
the [changelog](checks-changelog.md), and the people who can change an organization's settings get a notice in the bell
for 14 days from the date in a new version (off until the owner turns on `ScreenHire:Compliance:ChangeNotices:Enabled`).
