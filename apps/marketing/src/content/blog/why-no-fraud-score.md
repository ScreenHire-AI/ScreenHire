---
title: "Why we don't give applicants a fraud score"
description: 'A single number feels decisive, but it hides the evidence a reviewer needs. Why ScreenHire shows levels, reasons and counter-evidence instead.'
published: '2026-10-20'
---

Most applicant fraud tools end in a number: a fraud risk out of 100, or a percentage of the text that was "AI-written". It looks decisive, it sorts nicely, and it is the wrong thing to hand a small hiring team.

## A number hides the part you need

When a tool says an applicant is risky, the useful questions are what it saw and whether there is an innocent explanation. A score answers neither. It folds a hosting-provider IP address, a two-day-old email domain and an answer that matches four other applications into one figure, and drops the one fact that might clear the person, such as a phone number that is an ordinary mobile line.

So ScreenHire shows a level instead: **No concerns**, **Review suggested** or **Verification recommended**. Each level lists its reason codes, the evidence behind each one, anything that counts in the applicant's favour, a short summary and a suggested next step. A reviewer can read it in a few seconds and disagree with it.

## Two questions, kept apart

Using AI to polish a cover letter is not fraud. Many genuine candidates do it, and some employers welcome it. A tool that blends "used AI" with "is not who they say they are" punishes the first group for the second group's behaviour.

We keep two separate assessments. **Authenticity** asks whether this is a real, unique person who is who they claim to be. **AI assistance** asks how much of the content looks generated, and it is shown as information, judged against the AI policy you set for each job.

## Detectors are not judges

AI-text detectors are unreliable, and a 2023 study found them biased against people writing in a second language ([Liang et al., "GPT detectors are biased against non-native English writers"](https://arxiv.org/abs/2304.02819)). A "% AI-written" figure dresses a guess up as a measurement. We report concrete, checkable indicators, such as an answer pasted in whole or near-identical to other applicants, and never a percentage.

Typing and paste signals get the same caution. Dictation software, screen readers and autofill all look like pasting. Applicants can switch those signals off before they start, with no negative inference, and those signals alone never raise a level.

## The law points the same way

Rules on automated decisions in hiring keep converging on the same ideas: tell people, explain the decision, and let a person review it. The UK's new Article 22C requires the right to human intervention and to contest a solely automated significant decision. Colorado's SB 26-189 asks for meaningful human review where commercially reasonable. The EU AI Act treats recruitment AI as high-risk from 2 December 2027 and has banned emotion recognition at work since February 2025. Our [hiring-AI law tracker](/hiring-ai-laws/) has the details and sources.

A level with evidence gives the reviewer something real to review. A score invites a rubber stamp.

## What this means in practice

- Integrity signals never reject anyone. Integrity levels can route, tag, assign or suggest verification, and ScreenHire refuses workflows that try to reject on them.
- A level above the lowest needs signals from at least two independent categories, such as network and identity.
- Levels come from documented, versioned rules, so we can explain any result and test the rules for bias.

If you want the full list of what we look at and what we never do, read [how detection works](/how-detection-works/).
