---
type: Reference
title: "Bias audit method: impact ratios"
description: "Draft, not legal advice: an impact-ratio bias audit in the shape NYC Local Law 144 sets (6 RCNY 5-301 to 5-303), applied to ScreenHire's checks and pipeline, with the rates it computes, the categories and exclusions, the data it needs, what ScreenHire collects today and what it lacks, and who may run it."
tags: [compliance, bias, customers]
sources:
  - resource: apps/api/Reports/EqualityReportReads.cs
---
# Bias audit method: impact ratios

> Draft, not legal advice, for review by counsel.

This describes how an employer, or an auditor it hires, would measure whether ScreenHire's checks or its hiring
pipeline treat groups of candidates differently. It follows NYC's rule for automated employment decision tools (6 RCNY
5-301 to 5-303) because it is the most specific method in force. Whether an employer's use makes ScreenHire an AEDT is
counsel's question ([gap map](../research/compliance-gap-map.md)); the method is worth running either way, and
legal.md principle 8 commits ScreenHire to it.

## What it measures

Two questions, each by group:

1. **Advancement.** Of the candidates in a group who reached a stage, how many moved forward? This is LL144's
   selection rate: the number moved forward divided by the number in the group (6 RCNY 5-300).
2. **Flags.** Of the candidates in a group who were assessed, how many reached each authenticity level? A level is a
   classification, so LL144 treats the rate of being "assigned a classification" as a selection rate too (5-300).

For each, the **impact ratio** is the group's rate divided by the rate of the group with the highest rate (5-300). For
flags, where being flagged is the worse outcome, compare the share of each group left unflagged, so the ratio still
reads "lower is worse".

There are no scores, so LL144's scoring rate does not apply.

## Groups

- Sex, race and ethnicity, and their intersections, using the EEO-1 categories (5-300, 5-301(b)(3)).
- The number of candidates whose group is unknown, reported and left out of the ratios (5-301(b)(5)).
- A group under 2% of the data may be left out of the ratios, with the reason and its counts and rate still published
  (5-301(d)).

A ratio below 0.8 is the federal rule of thumb for adverse impact (29 CFR 1607.4(D)). It is a reason to look closer, not
a finding of discrimination, and a small group's ratio swings widely.

## Data

- **Historical data first.** The audit uses data from real use (5-302(a)). Data pooled from several employers counts
  for an employer only if it contributed its own. Test data is allowed only when history is too thin, and the summary
  says why and how it was made (5-302(b)).
- **What each candidate needs:** their group (self-reported, optional), each stage they reached and left, and each
  authenticity level they were assigned, with whether a reviewer dismissed it.
- **Never in the audit data:** names, contact details, answers, files, or anything that identifies a person.

## What ScreenHire has today, and what it lacks

| Need | Today | Gap |
|---|---|---|
| Self-reported group | Equality monitoring forms, off by default until a legal review; optional questions the organization writes; answers hidden from the hiring team | The questions are free-form, so the EEO-1 categories are not guaranteed |
| Totals by group | The equality report counts answers per question, hiding counts under 5 (`EqualityReportReads.cs`) | Answers are never joined to outcomes |
| Stage outcomes and levels | On every application, with the timeline and the review log | No export that joins them to groups for an auditor |
| Pooled history across employers | None: each organization's data is its own | A pooled audit needs each employer's agreement and a separate legal basis |

Closing the gaps is a follow-up product task (the gap map's follow-up 5). It would add an export of the joined,
de-identified rows above, for one organization, with small counts hidden, only for people who may read equality data.

## Who runs it

The auditor must be independent: not involved in using, developing or distributing the tool, and with no employment
relationship or financial interest with the employer or ScreenHire during the audit (5-300). ScreenHire can commission
an audit of its checks; each employer stays responsible for its own (DCWP FAQ, †).

## Publishing

An employer covered by LL144 publishes, before use and for 6 months after last use: the audit date, the source and
explanation of the data, the number of unknowns, each group's count, rate and impact ratio, and the date it started
using the tool (5-303). An audit is good for one year (5-301(a)).
