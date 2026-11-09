---
type: Design
title: "November 2026 redesign"
description: "Which mockup direction the owner picked for each candidate-page and setup surface, the design-system rule changes the owner approved, the audit's corrections that cut noise and dense text before anything is built, and the product decisions the Launch readiness tasks build on."
tags: [design, redesign, mockups, candidate-page, templates, navigation]
---

# November 2026 redesign

On 2026-11-09 the owner walked the preview, then reviewed two mockup boards:

- [Candidate page board](redesign-2026-11-candidate-page.html): six surfaces, from the verification state in lists to the scorecard a task lands on.
- [Setup board](redesign-2026-11-setup.html): seven surfaces, from the template overview to navigation, drawn for a small plumbing firm.

Open a board in a browser. Each surface has two or three directions, and the picked one is marked Recommended. The boards are drawings, not specs. Read each direction together with its corrections below.

The owner's verdict on both boards: "doesn't look bad overall but we should reduce visual noise where appropriate, dense text and items that break our design rules are not desired." [The design system](design-system.md) stays the one source of truth. A build task changes the design system first for its surface, then builds. Where a board and the design system disagree and the change is not listed under "Approved rule changes", the design system wins.

## Approved rule changes

The owner approved these, and any similar change that follows from the decisions recorded here:

1. One-word verification states everywhere: Clear, Review, Verify. A calm candidate shows a quiet Clear beside their name. Pending and Dismissed are muted words with their icon; Dismissed's tooltip says who set it aside and why. "Review suggested" and "Verification suggested" remain only as screen-reader text.
2. The three-step level mark is the level's icon: three small squares filled to the level. It is the one custom glyph (I1), and the count of filled steps tells the levels apart without colour, so T1 holds.
3. AI help is a small sparkles mark with a tooltip, outside a badge, shown only for Some and Strong signs. It never sorts, filters or counts.
4. The Authenticity and AI assistance columns merge into one Verification column.
5. The "Review this application" task becomes "Give your scorecard", so "Review" only means the verification state.
6. A reviewer who has not submitted sees who submitted and when, never the recommendation, rating or answers. This needs an API change: today the others are left out entirely.
7. A template draft with problems still saves. Only Test and Publish wait for the fix.
8. Tags live in the candidate header. The pipeline tracker and the three mini cards are replaced by the rail and two fact lines. The Review log panel goes: notes and actions sit under the finding they concern.
9. Graph becomes Automations, Questionnaire becomes Questions, Simulate becomes Test, and templates gain an Overview tab.
10. Verification becomes its own sidebar area. Settings becomes one sidebar item that opens a Settings home. A notifications bell joins the top bar.

## Candidate page picks

### 1. Verification state in lists: B, Level mark, with the merged column

- Calm rows show the quiet Clear icon and muted word. The three-step mark appears only on Review and Verify.
- A cell holds one thing. Overdue leaves the Stage cell for a tooltip, the row's menu or its own column (L1).
- Rating cells show the number only, no stars (L1).
- The panel title is "Candidates", not a count that the filter toggles already give (W7).
- Do not build the board's "Sorted by Verification, highest first" line. The header arrow says it.

### 2. Header and tracker: A, Rail

- Drop the date under the first rail step (the meta line says "Applied 11 days ago") and show "Here 3 days" without "4 of 6" (W7).
- Keep the email and phone as links and Email as the action. Drop both copy buttons.
- The rating is a link to Scorecards (A5), not a chevron button. A tag's remove control shows on hover, focus and tap only, or behind one Edit tags button.
- The level's tooltip says "Verification suggested" and nothing more.
- Move to stage stays the only filled button (A1).

### 3. Overview: A, Briefing, cut to four panels

- Keep Needs you, Scorecards (the rating and one line per person), Interview and Latest message. Verification, Answers, Files and Activity are left to their tabs and count badges (W7, S3).
- The verification step reads "Confirm who they are on a short video call before interviewing." Nothing about rejecting appears in it.
- A to-do shows its title and due date, with no To-do or For you pills (W6).
- Latest message shows the sender, "replied yesterday" and the first line of the body.
- No note box on Overview. The Activity tab has it.
- Tabs: Overview, Application (with the answers), Files, Messages, Scorecards, Verification, Activity.

### 4. Scorecards tab: A, Roster and side sheet

- One line per reviewer: their name only, with the role in the sheet's subtitle (L1). Drop the Scorecard column: the sheet's title names the scorecard (W7).
- Every column header sorts (L2).
- The sheet shows the time submitted, the answers and the note, without repeating the row's facts and without a "Note" label (W7, W9).
- For a reviewer who has not submitted: hidden cells are blank with screen-reader text, no lock icons and no repeated caption. The one line is "Submit yours to see the others." with the Fill in your scorecard button.
- A waiting row opens nothing. Remind goes in its row menu, with the interview time in a tooltip.
- Fill in shows only to someone who has not scored (A3). Ask for one stays.

### 5. Verification tab: A, Check map, cut hard

- A calm result is one line, "Nothing unusual in the checks that ran. No action needed.", plus a collapsed "What was checked". It lists only a group that is turned off or not checked (W10, section 16). No "nothing found" lines anywhere.
- The level shows once, as the mark in the header. No spelled-out Clear / Review / Verify scale (W7).
- The AI row is its badge and the job's policy pill. "Information only" is said once, in the tooltip.
- Mark only the exception: AI notes say "Information only". No "Counts toward the level" tag on every finding (W6).
- No group icons in the panel headers and no group description paragraphs.
- One review action per finding: Mark explained, with an optional note. Information-only findings have none. Hide Expand all and Collapse all for a single finding.
- Findings read short: "The resume and the answer give different dates for Lumen Health. Ask the candidate."
- API key reads go to the Activity tab. The audit line keeps "Assessed" and "Ruleset" (W10).
- The six groups are the engine's own (CheckGroup.cs): Network, Contact details, Documents, Answers, Duplicates, Form use. The ruleset's summary names these groups instead of "network, identity and content checks".

### 6. From My tasks: A, Land on the sheet

- My tasks has Task and Type columns. No "What you'll do" column (W7). A scorecard task is named "Give your scorecard" everywhere.
- The sheet says "Asked by Avery Quinn on Nov 5." with no icon. Its close tooltip is "Close".
- A requested scorecard shows its name as the subtitle. The Scorecard dropdown appears only when more than one fits. "Low / High" shows once per group of scales.
- After Submit: "Scorecard submitted." and an "Open next task" link. Submit scorecard is the only filled button.

## Setup picks

### 1. Template overview: B, Candidate journey

- Only automations carry an icon (zap). Other lines have none.
- No summary sentence above the columns, and no person's name in generated text ("the team", not "Dana").
- No caption under the panel title, and no instruction text after the end stages.
- The stage column header is the one button that opens its sheet. No pencils. Choose a scorecard and Add an automation live in the stage sheet (L3).
- The header shows a status badge only when there is a problem (Needs fixes) (W6). The facts row has History beside Created (T6).

### 2. Pipeline: A, Stage cards with a side sheet, and Start from

- Stage number markers stay. No stage count in the panel title and no "Stage 3 of 4" in the sheet (W7).
- A card's grip and menu show on hover, focus and tap. Facts have no icons except the automation zap.
- Start from cards: the name, one example line ("Plumber, electrician, driver") and the stage names as plain text. No icon tiles, arrows or third caption. "Use this starting point" is the only filled button.
- The sheet's time limit reads "The team gets a reminder after 5 days." No "Runs here" label, no icon on More options, no "Changes save with the template".
- Stage kind is set from the name and sits under More options. The API is unchanged.

### 3. Automations: C, the map edited as sentences, with recipes behind Add automation

- Conditions say "Verification is Review or Verify", never the retired "Authenticity is Review suggested".
- Map nodes keep the words When, If and Then, without icons.
- The sheet has Done only, and no subtitle.
- The guardrail is one line, said once: "A check can only ask for a closer look. A person decides." After a verification condition the "then" list offers only asking a teammate to review, adding a tag, or moving into a stage whose own automations are allowed after a check. Turning someone down is never offered, and AI-use and accommodation answers are never conditions.
- A recipe is one line. Added recipes are hidden. Recipes use variables or the chosen teammate, never a person's name. No explanation card inside the recipe grid (S4).
- "Edit as rules" keeps today's full form one press away for power users.

### 4. Questions: A, forms on the candidate's timeline

- Edit and Preview are one switch. No preview column beside each question (A8).
- A question row is its words, with a Required badge only where it is the exception (W6). Duplicate and Delete go in the row menu (L3).
- The timeline names each moment by its stage ("When they reach Trial shift"). No numbers, arrows or icons, and no count that repeats the panel's.
- "Add follow-up questionnaire" becomes "Ask more later": it asks when, names the form after that moment, and adds the email automation ("We add the email automation for you.").
- The AI-use row reads "AI use. Asked by ScreenHire. Policy: Allowed with disclosure." with a Change policy link. The question is asked on every application. Its policy is set once for the organization (it already has `VerificationSettings.DefaultAiUse`), and a job may override it. Templates no longer carry it.

### 5. Test: C, a live test drive

- While paused, the tab shows the notice with the inline fix and nothing else. The Fix button beside the title shows on the other tabs only, with no coloured icon.
- No "No problems found" badge in the panel and no captions about safety or freshness (W4, W7).
- "Show in the map" is a link (A5).
- Sample lines stay short: "Goes to the team's review list", "No steps skipped". "A check never turns anyone down" appears once under the list.
- Shared fixes from the board: a new "Add a tag" step asks for the tag first, and an empty step is dropped with Undo. Problems are written in plain words ("The tag in "Queue for review if no right to work" has no name. Type one, or remove that step.").

### 6. Careers job cards: A, summary under the title

- A new "Short summary" field on the job, at most 160 characters, about the job rather than the company. It starts from the description's first sentence until someone edits it.
- The card clamps it to 3 lines with CSS only. Public pages keep about 0 KB of JavaScript.
- At most three chips: place, type and pay.
- The page title is "Open roles". The lede is one sentence.

### 7. Navigation: B, one Settings home

- The sidebar holds the work and Verification, then one Settings item at the bottom. Settings opens a home that lists every setting, with a search box ("Search settings").
- Settings rows have no icons. A description appears only where the name does not explain itself. No counts in the panel titles.
- A setting a role cannot use is left out, never shown disabled (A3).
- Settings groups: Organization (General, Team, Roles, Email domain, Data requests, Billing), Careers site (Careers page, Branding), Integrations (Slack, MCP server, Webhooks, API keys). Inside, the breadcrumb is Settings, the group, then the page.
- The top bar shows no page name. Views use non-status icons, or none, and no counts (I2). My tasks and the bell keep their counts.
- The bell holds what needs attention: the setup checklist until it is done (done steps fold into one line), then alerts. No team news, and no icons on its lines. Verification's alert settings tab is called Notifications, so the two are not confused.

## Product decisions for these tasks

- **Built-in AI.** Organizations use ScreenHire's key by default and pay for the tokens they use at the provider's price plus a margin. The margin is configuration, defaulting to 20%. Customers may bring their own key instead and pay us nothing for AI.
- **Usage and caps.** Every model call is recorded with the provider's request ID, so a customer can compare our bill with the provider's. Owners get alerts at 50%, 80% and 100% of a monthly budget. An Owner can set a hard monthly cap: at the cap the AI checks pause, the rule-based checks keep running, and the candidate page says the AI checks were paused.
- **Who sees usage.** AI usage, costs and the call-by-call log are for Owners and Admins only. Every member who can see verification results still sees each candidate's results.
- **Passkeys.** A faster way to sign in beside the email link and SSO. Built; the decisions and threat notes are in [sign-in and tenancy](../architecture/sign-in-and-tenancy.md#passkeys).
