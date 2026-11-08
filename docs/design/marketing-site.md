---
type: Design
title: "Marketing site messaging"
description: "What the marketing site sells and how it says it: who it is for, the five selling points as pain, promise and proof with their sourced numbers, the home hero, the voice and copy rules, the page map, and what may be claimed and what is never said."
tags: [marketing, messaging, copy, design]
sources:
  - resource: apps/marketing/src/lib/features.ts
  - resource: apps/marketing/src/lib/pages.ts
  - resource: apps/marketing/src/lib/features.test.ts
  - resource: apps/marketing/src/lib/copy.test.ts
---
# Marketing site messaging

The page tasks take their copy from this doc, and `apps/marketing/src/lib/features.ts` holds the same selling points as
data, so home, product and pricing say one thing. Change both together. How the pages look is
[the design system](design-system.md) section 22; the decision is [ADR-0030](../adr/0030-marketing-site-direction.md).

## Who the site is for

A small hiring team: an owner who hires for a café, a studio or a shop, a hiring manager with no recruiter, a recruiter on
a team of two or three. They have too many applicants and too few hours, they cannot tell who is real, they lose track of
who last wrote to whom, and they are tired of software built for a big recruiting team and priced by the seat. They are not
buying "AI". They are buying their time back and a hiring process they can see.

Speak to their pain and outcome in their words, as a recruiter, an owner or a hiring manager would say it.

## Under all five points

Built for small hiring teams. Simple to use. Every feature on every plan. Plain pricing. The pricing pain:

- per-seat prices;
- fraud checks and AI sold as add-ons;
- a closed job billed until it is archived.

ScreenHire's answer, which is how it is billed (ADR-0029): unlimited users, every feature on every plan, a price set by
how many jobs are open at once, and a closed job costs nothing. The AI consistency check is included on every plan, and an
organization may bring its own key and model. Nothing is metered. The numbers live only in `packages/plans/plans.json`;
until the pricing task sets them the site shows the placeholder. Working numbers (not final): Starter $79 with 3 open jobs,
Growth $149 with 10, Unlimited $249, and a 14-day trial on Growth.

## The five selling points

In this order. Each is told as the buyer's pain, then our promise, then the proof.

### 1. Real people

- **Pain.** Applicant floods, bots and impersonators waste the team's time.
- **Promise.** You know you are interviewing a real person.
- **Proof.** A check on every application. A level for every applicant, with the evidence and what speaks for them, and a
  suggested next step. A person decides.
- **Shown as.** Two lists side by side: what you see, and what ScreenHire never does.
- **Numbers.** 291 applications per hire on average (Ashby, Talent Trends Report, 2026), which is workload, not fraud.
  31% of US managers involved in hiring say they interviewed a candidate who turned out to be using a fake identity
  (Checkr, 2025). Gartner predicts 1 in 4 candidate profiles worldwide could be fake by 2028 (as reported by HR Dive, 2025);
  it is a forecast, never worded as a measurement.

### 2. Never guessing

- **Pain.** Nobody knows who spoke to a candidate last, and candidates are left without an answer.
- **Promise.** Your team always knows where each candidate is and what was said last, and so does the candidate.
- **Proof.** The pipeline, activity and when a candidate was last contacted. Two-way email kept on the application. Notes
  and tasks with mentions. The candidate's own page with its four steps (Applied, In review, Interview, Offer) and their
  messages. Reports.
- **Shown as.** The team's activity beside the candidate's own page.
- **Numbers.** None yet. No figure for candidates left without an answer could be read at its source on 2026-11-08
  (Monster's 2024 poll and Fast Company's 2025 report returned HTTP 403, Greenhouse's 2025 release has no such figure, and
  CareerBuilder's 75% is from 2013). The site states no number here until one is verified in
  [pain points](../research/pain-points.md).

### 3. Automate anything

- **Pain.** The same follow-ups by hand, or automation locked behind a top plan.
- **Promise.** Whatever your team does the same way every time, ScreenHire can run it, from one reminder to a multi-step
  workflow.
- **Proof.** Automations on every stage and on the whole workflow, on every plan. Triggers: an application arrives, a
  candidate enters or leaves a stage, a time limit passes, a stage's time limit is breached, a scorecard comes in, a
  follow-up questionnaire is answered, an interview is booked, or someone presses Run. Actions: email the candidate, move
  them, tag them, give a teammate or the team a review task, remind or notify the team, create a task, send follow-up
  questions, offer interview times, call a webhook. Follow-up questions. Interview scheduling. Test an automation on a
  sample application before it goes live. Guardrails: a check never rejects anyone.
- **Shown as.** One automation written out as a sentence, then three examples from one step to a whole process.
- **Numbers.** None that can be used. The recruiter admin-time figures found are from 2015 and 2022. A competitor's
  automation being on its top plan is not verified either (see "What is never said").

An automation is built as When, If, Then in a form. It is not written in free text and nothing "understands" a sentence.
Name only triggers and actions that exist, as above. The examples on the site are checked against that list:

- When a candidate reaches Interview, if they said yes to right to work, offer them interview times and ask Jordan to
  review (`StageEntered`, a condition on an apply-form answer, `ScheduleInterview`, `AssignReviewer`).
- One step: thank everyone who applies, in your own words (`ApplicationSubmitted`, `SendEmail` with the organization's own
  template).
- A few steps: remind the team when a candidate has waited three days in Screening (`TimeElapsed`, `NotifyTeam`).
- Your whole process: send follow-up questions, move people who meet your must-haves to Screening, offer interview times,
  and ask your reviewers for a scorecard (`SendQuestionnaire`, `MoveToStage`, `ScheduleInterview`, `AssignReviewer`).

### 4. Integrations

- **Pain.** Tools that do not talk to each other, or no API at all.
- **Promise.** No copying between tabs. If you want to build on ScreenHire, the API and webhooks are on every plan.
- **Proof.** Email with replies kept on the candidate. Google and Outlook calendars. Slack. The API and webhooks. AI
  assistants through MCP.
- **Shown as.** A plain list of what each one does.
- **Numbers.** None that can be used. A 2022 survey (JobSync and Talent Board, nearly 250 professionals) found one in four
  said their ATS was not well integrated, but it is four years old.

### 5. Built your way

- **Pain.** Rigid software built for big recruiting teams.
- **Promise.** ScreenHire bends to yours: your stages, questions, scorecards, emails, colours, and who sees which jobs.
- **Proof.** Stages and hiring templates. Questionnaires. Scorecards. Email templates. Branding and the careers site.
  Roles and job access. Your own AI key and model.
- **Shown as.** A hiring template with its own stages.
- **Numbers.** None needed. Entry prices of other tools (Ashby from $400 a month, Workable from $299 a month, read on their
  pricing pages on 2026-11-08) belong on the compare page, with their source.

## The home hero

- **Headline.** Interview real people, not bots.
- **Subline.** ScreenHire is the hiring tool for small teams. It checks every applicant, keeps your team and your
  candidates up to date, and does the busywork for you. One price, everything included.
- **Calls to action.** Start free trial, and See pricing.
- **Under them.** The price from `packages/plans`, or nothing while it is a placeholder, and "No card to start" only if
  billing starts that way.
- **The one bold element.** Two applicants for the same job: one with no concerns, one with a verification suggested, with
  why, but also, the next step, and "A person on your team decides."
- **Under the hero.** Three sourced numbers (point 1).

## Voice and copy rules

- **No AI fluff.** Never "AI-powered", "supercharge", "revolutionize", "seamless", "unlock", "effortless" or
  "next-generation". The word AI appears only where it names a feature: AI assistants through MCP, signs of AI help, your
  own AI key. `copy.test.ts` and `features.test.ts` fail on the words.
- **The buyer's words.** The pain first, in plain words, as the recruiter or owner would say it. Short sentences, one
  idea each, sentence case, no em-dash. Headlines are short and plain.
- **Sources.** Every number has a source the page links, with its year, in `docs/research/pain-points.md` or
  `competitors.md`. Never invent a number, round one up, or state a comparison the source does not state. Word a survey as
  what was asked and of whom ("31% of US managers involved in hiring say ...").
- **No overclaiming.** Every claim names a feature in `docs/status.md`, Built. No testimonials, customer logos, awards or
  results. The footer says ScreenHire is in development and that product pages describe it as designed.
- **Voice.** Confident and specific. Have an opinion. First person plural is fine. Say the awkward part out loud.

## What may be claimed

That checks happen on every application. What kind of thing they look at, in plain words: contact details that do not
match each other, an answer sent with other applications, a network in a different country from the one given. That
each result is a level with its evidence, what speaks for the candidate and a suggested next step, and that a person
decides. That a candidate is told what is collected before they start, and can see where they stand.

## What is never said

- Reason codes, rule names, thresholds, or how a check is computed.
- A numeric score, or a percentage of AI-written text.
- That ScreenHire rejects anyone, or that a check can. (A knockout question the team opts into is not a check.)
- That ScreenHire reads faces, voices, feelings, honesty or personality, or looks people up on social media or LinkedIn.
- Anything about a competitor that its own page does not say today. In particular, `competitors.md`'s lines that GoHire
  has no event triggers and no public API, and that its stage actions are Pro-only, were not confirmed on 2026-11-08:
  GoHire's help article says stage actions fire when a candidate is moved, its pricing page lists "Automated Stage
  Actions" on Pro, and nothing proves an API is absent. Word the comparison as what the page lists ("a closed job counts
  toward the limit until it is archived", "Gem sells fraud detection as checks you purchase separately"), with its date.
- Prices, limits or a trial length that `packages/plans` does not hold.

## Page map

| Page | Path | Job |
|---|---|---|
| Home | `/` | The hero, three sourced numbers, the five points in order, pricing, questions, the close |
| Product | `/product/` | What ScreenHire does, one section per point |
| One page per selling point | decided by the page task (`/product/#id` until then) | The point told in full, with its screenshot |
| Careers site and job posting | decided by the page task | The candidate's side: the careers page, the notice first, applying, posting to Google for Jobs |
| Pricing | `/pricing/` | The plans as rows, what is included, what you will not pay extra for |
| Compare | `/compare/` | Sourced, dated, factual comparisons |
| Hiring-AI laws | `/hiring-ai-laws/` | The law tracker, with dates and sources |
| Blog | `/blog/` | Notes on applicant fraud, AI in hiring and a fair process |

`pages.ts` is the registry of paths, titles and descriptions. A page that is not in it has no sitemap line, no share
image and no `llms.txt` entry.
