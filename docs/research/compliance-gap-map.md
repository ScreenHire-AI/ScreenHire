---
type: Research
title: "Compliance gap map: automated-decision rules in hiring"
description: "Draft, not legal advice: each duty Colorado SB 26-189, the CPPA ADMT and FEHA rules, Illinois HB 3773 and NYC Local Law 144 place on a hiring-tool vendor or an employer, what ScreenHire does today with the code or doc that does it, the gap, the fix (product, document or an owner and legal decision), the tensions between the rules, and the product items built behind settings, as of 2026-11-23."
tags: [research, legal, compliance]
sources:
  - resource: apps/api/Configuration/ComplianceOptions.cs
  - resource: apps/api/Endpoints/DecisionRecordEndpoints.cs
  - resource: apps/careers/src/components/PrivacyNotice.astro
  - resource: apps/careers/src/pages/[organization]/privacy.astro
  - resource: src/ScreenHire.Domain/Hiring/DefinitionValidator.cs
  - resource: src/ScreenHire.Application/Automations/ExecuteAutomationActionHandler.cs
  - resource: src/ScreenHire.Domain/Verification/VerificationReviewAction.cs
  - resource: src/ScreenHire.Domain/Verification/VerificationRuleset.cs
  - resource: src/ScreenHire.Domain/Privacy/RetentionSettings.cs
  - resource: src/ScreenHire.Application/Privacy/CandidateDataExport.cs
  - resource: src/ScreenHire.Domain/Applications/ApplicationEvent.cs
  - resource: src/ScreenHire.Domain/Automations/AutomationRun.cs
  - resource: src/ScreenHire.Infrastructure/Verification/VerificationOptions.cs
  - resource: apps/api/Reports/EqualityReportReads.cs
---
# Compliance gap map: automated-decision rules in hiring

> Draft, not legal advice, for review by counsel.

This map sets each duty in the rules researched in [legal.md](legal.md#us-automated-decision-rules-refreshed-2026-11-23)
against what ScreenHire does today. It decides no legal question. Whether a rule reaches ScreenHire or a customer is
counsel's call, and the map says where the answer turns.

ScreenHire's role in every row is the vendor: a "developer" in Colorado, a "service provider" in California, a vendor
outside the duties in NYC. The customer is the employer: the "deployer" in Colorado, the "business" in California,
the "employer" in Illinois and NYC. Each fix is one of three kinds:

- **product**: we build it;
- **document**: we write it (drafts are in [docs/compliance](../compliance/index.md));
- **owner/legal**: a decision for the owner and counsel.

## Where applicability turns

Four readings decide how much of the map applies. None is ours to make.

1. **Colorado's fraud-prevention exclusion.** A consequential decision excludes "activities relating to technologies
   used for fraud prevention, including identity verification" (C.R.S. 6-1-1701(3)(b)(VIII)). If the clause covers
   ScreenHire's checks, most of Colorado drops away for verification. If "required under state or federal law"
   limits the whole clause, it does not. Verification levels are classifications, so the narrower exclusion for
   tools that only present information (1701(3)(b)(IV)) likely does not apply.
2. **California's "human involvement".** ADMT replaces or substantially replaces human decision-making (11 CCR
   7001(e)). Verification never decides: a person reviews every flag and only a person rejects
   (`DefinitionValidator.ValidateGuardrails`). That design is built to meet 7001(e)(1). The one place ScreenHire
   decides without a person is a knockout automation with `allowAutoReject` (see the
   [workflow engine](../architecture/workflow-engine.md)). For a covered California business, that use is likely ADMT.
3. **NYC's AEDT prongs.** A level becomes an AEDT's output only if the employer relies on it alone, weights it above
   every other criterion, or lets it overrule a person (6 RCNY 5-300). ScreenHire stops a level from rejecting. How
   an employer weighs it is the employer's practice, which the product cannot see.
4. **Illinois's "artificial intelligence".** The definition is broad (†). The optional language-model consistency
   check is AI on any reading, so the notice duty likely applies to an employer that turns it on.

## The map

### Colorado SB 26-189

| # | Duty | Falls on | What ScreenHire does | Gap | Fix |
|---|---|---|---|---|---|
| CO-1 | Documentation to deployers: intended and harmful uses, training data, limitations, use and human review instructions, disclosure inputs (1702(1)) | Developer | ADRs and architecture docs, written for builders | No document written for a customer | **document**: [system description](../compliance/system-description.md) |
| CO-2 | Notice of material updates and changed limitations, by release notes with direct notice (1702(2)) | Developer | Every assessment records its ruleset, analyzer, prompt and model versions (ADR-0004); `VerificationRuleset.Version` moves with a rule change | No customer-facing changelog and no direct notice to Owners | **product**: changelog and bell notice (built, off by default); **owner/legal** on what counts as material |
| CO-3 | Developer records for 3 years: versions, changelogs, notices (1702(4)) | Developer | Git history and the version on each assessment | No retention policy for the documentation and notices sent | **owner/legal**; **document** once the policy exists |
| CO-4 | Clear notice before use, a public notice at the point of interaction counting (1704(1)-(2)) | Deployer | The apply notice (`PrivacyNotice.astro`) says checks happen and a person reviews flags; the privacy page explains more | The notice does not say an automated tool is used in the decision, nor how to get more information | **owner/legal** (already open); **document**: draft wording in the appendix |
| CO-5 | Within 30 days of an adverse outcome: the decision, the ADMT's role, how to ask for its name, version, developer and data, and the consumer's rights (1704(3)) | Deployer | An automated rejection email carries a fixed sentence that a person reviews it on request; rejection reasons are free text | No structured reason for a rejection and no statement of the tool's role; the AG rules that set the content are not adopted | **product**: rejection reasons (built); **owner/legal** once the rules are final |
| CO-6 | On request after an adverse outcome: personal data and its correction, and meaningful human review and reconsideration (1705(1)) | Deployer | Data requests (copy, erasure) through the careers site; reviewers dismiss or explain a reason with a recorded reason; no verification-driven rejection | The candidate copy leaves verification out (`CandidateDataExport.cs`); no reconsideration request channel | **product**: decision record and review requests (built, off by default) |
| CO-7 | Meaningful human review: a trained reviewer with authority, who considers the evidence, does not default to the output and sees its use, limits, inputs and principal factors (1701(15)) | Deployer, with the developer's instructions (1702(1)(d)) | Reviewers see each reason with its evidence, counter-evidence and a plain summary; the review log records who did what (`VerificationReviewAction`) | No reviewer training material | **document**: reviewer guidance in the system description; **owner/legal** for training |
| CO-8 | Deployer records for 3 years after each decision (1703) | Deployer | Per-organization retention of ended applications, off by default, 1 to 120 months (`RetentionSettings`); verification rows are deleted with the application | An organization can set a period under 3 years; an erasure request removes the record | **product**: record floor (built, unset); **owner/legal** on the floor and on erasure |

### California: CPPA ADMT and risk assessments, FEHA

| # | Duty | Falls on | What ScreenHire does | Gap | Fix |
|---|---|---|---|---|---|
| CA-1 | Pre-use notice: purpose, opt-out or the exception relied on, access, how the ADMT works (7220) | Business | Apply notice and privacy page; verification keeps a person in every decision | If knockout auto-reject is ADMT, the notice lacks the purpose, opt-out or appeal and access statements | **owner/legal**; **document** (appendix); not built here, because the wording is the owner's |
| CA-2 | Opt-out, or the human appeal exception with an easy method and a reviewer who can overturn (7221) | Business | The rejection email's review sentence; a rejected candidate writes to the employer; `RejectionReview` tasks for automation rejects that lose their basis | No appeal method with minimal steps, and no record of the appeal | **product**: review requests (built, off by default); **owner/legal** on the exception |
| CA-3 | Access to ADMT: purpose, logic, output, outcome (7222); the service provider assists (7222(i)) | Business; ScreenHire assists | Verification and timeline reads in the dashboard | No single record to answer from | **product**: decision record (built, off by default) |
| CA-4 | Risk assessment before processing, or by 31 Dec 2027 for processing begun before 2026; submission by 1 Apr 2028 (7150, 7152, 7155, 7157) | Business | Nothing for customers | No template | **document**: [CPPA risk-assessment template](../compliance/cppa-risk-assessment-template.md) |
| CA-5 | Risk assessment for automated processing that infers an applicant's behaviour from systematic observation (7150(b)(4)) | Business | Form-use checks keep session totals only; raw network data and telemetry go within 30 days (`VerificationOptions.Retention`); the assistive-technology switch turns them off | Whether form-use checks are "systematic observation" is open | **owner/legal**; the template covers it either way |
| CA-6 | Facts for the customer's risk assessment, for ADMT trained on personal information (7153) | Developer | No model is trained on applicant data; the AI provider works on no-training terms | Likely none; the system description says so | **document** |
| CA-7 | FEHA: keep employment records, automated-decision data included, for 4 years | Employer, and an "agent" if the vendor is one (†) | As CO-8 | As CO-8, at 4 years | **product**: record floor (built, unset); **owner/legal** |

### Illinois HB 3773 (†)

| # | Duty | Falls on | What ScreenHire does | Gap | Fix |
|---|---|---|---|---|---|
| IL-1 | Notify applicants when AI is used in hiring decisions | Employer | The apply notice says checks happen | It does not say AI is used, and the consistency check is AI | **document** (appendix); **owner/legal** |
| IL-2 | No AI use with a discriminatory effect; no zip code as a proxy | Employer | Location is the IP's country only, never a postal code; a level never rejects | An employer can write a question asking for a postal code and an automation on its answer | **product**: the builder refuses it (built); **document**: guidance in the system description |

### New York City Local Law 144

| # | Duty | Falls on | What ScreenHire does | Gap | Fix |
|---|---|---|---|---|---|
| NY-1 | Decide whether the tool is an AEDT (5-300) | Employer | A level can route, tag or assign, never reject | The product cannot see how a person weighs a level | **owner/legal**; **document**: use guidance in the system description |
| NY-2 | Annual independent bias audit: selection rates and impact ratios by sex, race and ethnicity and their intersections (5-301, 5-302) | Employer; the vendor may commission one | Equality monitoring forms, off by default, report answer counts only (`EqualityReportReads.cs`) | Outcomes were never joined to answers | **document**: [bias audit method](../compliance/bias-audit-method.md); **product**: outcome counts (built, behind the equality setting) |
| NY-3 | Publish the audit summary and distribution date; notice 10 business days before use with how to ask for an alternative or accommodation; publish data retention, type and source (5-303, 5-304) | Employer | The privacy page lists what is kept and for how long | No AEDT notice or audit summary page | **document** (appendix); **owner/legal** |

### Rules outside the US

| # | Duty | Falls on | What ScreenHire does | Gap | Fix |
|---|---|---|---|---|---|
| EU-1 | GDPR Art. 15 access: verification output about a candidate is personal data; the processor assists (Art. 28(3)(e)) | Controller (employer); ScreenHire assists | The candidate copy leaves verification out, flagged for legal review | The employer could not produce the record in one piece | **product**: decision record (built, off by default); **owner/legal** on what goes to the candidate |
| EU-2 | AI Act Annex IV technical documentation and post-market monitoring, from 2 Dec 2027 | Provider | ADRs and the verification engine doc | Not assembled | **document**: [technical documentation skeleton](../compliance/ai-act-technical-documentation.md) |

## Tensions

**Record periods against minimisation.** Colorado keeps records 3 years after each decision (1703) and FEHA 4 years.
GDPR wants the least kept for the shortest time, and erasure on request. Today an organization chooses one retention
period, and an erasure removes everything. The two meet in a record floor: keep a minimal decision record (levels,
reasons, reviewer actions, stage moves, no answers or files) for the period the employer's law sets, and erase the
rest. Whether an erasure request yields to a record duty is a legal call per jurisdiction. Raw network data and
telemetry stay at 30 days at most either way: the record duties reach the decision, not the raw inputs (†).

**Notice, opt-out and appeal.** California wants an opt-out or an easy appeal, Colorado a reconsideration on
request, NYC instructions for an alternative process. One appeal channel can serve all three: a candidate asks for a
person to look again, the request is logged, and a reviewer with authority answers. The candidate-facing wording is
an open owner call already, so the appendix only drafts it.

**Meaningful human review.** Colorado's definition asks for a reviewer who sees the "principal factors" and does not
default to the output. ADR-0004 gives the reviewer the evidence and counter-evidence for each reason and no score to
default to, and a dismissal needs a reason. What the product cannot give is the reviewer's training, which is the
employer's.

**Explanation against evasion.** Colorado, the CPPA and NYC all ask for explanations. The repository is public and
candidate-facing text says checks happen, never how they work. All three rules leave room for that: Colorado needs no
disclosure that would compromise fraud-prevention programs required by law (1704(7)), and the CPPA lets a business
withhold what would compromise resisting fraud (7220(d)(2)(B), 7222(c)(2)(B)). The decision record therefore goes to
the team, and the team decides what reaches the candidate.

## Product items built

Every item is off by default or changes nothing until an organization uses it, and each setting is an owner action.

| Item | Rows | Setting | Described in |
|---|---|---|---|
| Decision record: one application's levels, evidence, reviewer actions and reasons, stage moves and automation runs, for the team, with a download on the application's Activity tab | CO-6, CA-3, EU-1 | `ScreenHire:Compliance:DecisionRecord:Enabled` | [data requests](../architecture/data-requests.md) |
| Review requests: a rejected candidate asks from their own page for a person to look again; the team gets one task | CO-6, CA-2 | `ScreenHire:Compliance:ReviewRequests:Enabled` | [candidate portal](../architecture/candidate-portal.md) |
| Record floor: the daily retention run never deletes an ended application sooner than the floor, and a shorter period is refused | CO-8, CA-7 | `ScreenHire:Retention:MinimumMonths` (unset) | [data requests](../architecture/data-requests.md) |
| Rejection reasons: a fixed list a person picks from when rejecting, kept on the move; a knockout automation records its own | CO-5 | none: optional for a person | [workflow engine](../architecture/workflow-engine.md) |
| Outcome counts in the equality totals: per answer, how many moved on, were hired and were flagged | NY-2 | `ScreenHire:Forms:EqualityMonitoring:Enabled` (already off until a legal review) | [reports](../architecture/reports.md) |
| Checks changelog and a bell notice for 14 days from the date in a new rules version (a later deploy sends none) | CO-2 | `ScreenHire:Compliance:ChangeNotices:Enabled` | [checks changelog](../compliance/checks-changelog.md) |
| No automation acts on an answer that gives a postal or zip code | IL-2 | none: the workflow builder refuses it | [workflow engine](../architecture/workflow-engine.md) |

The decision record meets the bar of a duty no reasonable reading avoids through GDPR Art. 15 alone: an employer
needs to be able to produce the record, whatever the US rules turn out to require. It shows a member nothing their
own screens do not, and it never reaches an API key.

Choices made while building, each with the alternative left open:

- **The record floor holds the daily run, not an erasure request.** A candidate's own erasure still erases everything.
  The alternative, keeping a minimal record past erasure where a law requires it, is the legal call in the tension above.
- **Rejection reasons are a fixed list,** so a disclosure reads the same everywhere. The alternative is a list each
  organization edits, which needs a new table and a settings page.
- **The proxy rule refuses rather than warns,** because the workflow builder has no warning tier. A location requirement
  is asked as itself. The alternative is a warning that lets the automation save.
- **The change notice is the bell,** to the people who can change settings, for 14 days from the date in the version. The
  alternatives are an email to every Owner, and recording which version each organization was told about, so a version
  deployed late is still announced.
- **The flagged count sits in the equality totals only,** behind their legal-review setting, never in the hiring report.
  The alternative is an auditor's export with no dashboard column.

## Not built here

- **An ADMT pre-use notice on the apply page.** It is candidate-facing legal wording, which the brief leaves to the
  owner and counsel, so it stays a draft in the appendix below.

## Appendix: candidate-facing wording, drafted only

These are owner calls already open (the apply notice, "a person looks at anything the checks flag", the careers
footer). Nothing here ships. Each draft says checks happen and never how they work.

**Apply notice, automated-tool sentence (CO-4, IL-1, NY-3):**

> We use automated tools, some of them AI, to check applications for bots, impersonation and copied applications. A
> person looks at anything they flag, and they never reject anyone on their own. To ask what they found about you, or
> to have a person look again at a decision, write to us at [contact].

**Knockout auto-reject, pre-use statement (CA-1, CA-2):**

> Some questions on this form have answers that mean we cannot consider the application, and we tell you which before
> you answer. If one of them ends your application, you can ask a person with authority to change the decision to
> look at it again: [how to appeal].

**Adverse-outcome sentence for a rejection email (CO-5), pending the Colorado AG's rules:**

> A person made this decision. [If an automation applied a knockout question: "An automated rule applied your answer
> to [question]."] You can ask for the information we used, correct it, and ask a person to reconsider: [link].
