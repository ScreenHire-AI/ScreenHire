---
type: Reference
title: "Changes to ScreenHire's checks"
description: "Draft, not legal advice: the public release notes for ScreenHire's application checks, one entry per rules version with what changed for a reviewer and whether any level moved, which Colorado's developer duty to notify deployers of material updates (C.R.S. 6-1-1702(2)) can rely on."
tags: [compliance, verification, customers]
sources:
  - resource: src/ScreenHire.Domain/Verification/VerificationRuleset.cs
  - resource: src/ScreenHire.Application/Compliance/ChecksChangeNotice.cs
---
# Changes to ScreenHire's checks

> Draft, not legal advice, for review by counsel.

Every assessment records the version of the rules it was made under, so a reviewer can tell which rules produced a
finding. Each version gets an entry here: what changed for the people reading the results, and whether any level could
come out differently. The entries say that the checks changed, never how a check works.

With `ScreenHire:Compliance:ChangeNotices:Enabled` on, the people who can change an organization's settings also get a
notice in the bell. A daily job raises it for 14 days from the date in the version (`2026-11-09.1` is dated 9 November
2026), not from the day the version is deployed: a version deployed, or the setting turned on, more than 14 days after
that date sends no notice. Recording which version each organization was told about would close that gap.

Colorado lets a developer meet its duty to notify deployers of a material update with public release notes and direct
notice of them (C.R.S. 6-1-1702(2)(b)). Whether a change is material is for the owner and counsel; every version is
listed either way.

`ChecksChangelogTests` fails when the rules version in the code has no entry here.

## 2026-11-09.1

The wording of a finding's summary changed. No level moved, and an application keeps the summary it was assessed with
until something assesses it again.

## 2026-11-02.1

A check's measurement changed, and the version moved with it so each assessment says which one it was made under. A
level could come out differently for an application assessed after the change.

Earlier versions predate this changelog.
