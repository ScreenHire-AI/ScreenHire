---
type: Design
title: "Shared tags"
description: "The organization's tags: read from the applications and the automations that name them, with counts under row-level security and job scope; one tag whatever its case; suggestions in the add field with an explicit Create; and Settings, Tags, whose rename, merge and delete reach the open jobs' pipelines, the template drafts and the saved views at once and the applications in batches in the worker, recorded on each timeline."
tags: [tags, applications, automations, settings]
sources:
  - resource: src/ScreenHire.Domain/Applications/JobApplication.cs
  - resource: src/ScreenHire.Domain/Hiring/WorkflowTags.cs
  - resource: src/ScreenHire.Domain/Views/SavedFilterView.cs
  - resource: src/ScreenHire.Application/Tags/RetagApplications.cs
  - resource: apps/api/Endpoints/TagEndpoints.cs
  - resource: apps/api/Contracts/TagContracts.cs
  - resource: apps/api/Hiring/OrganizationTags.cs
  - resource: apps/api/Applications/ApplicationChanges.cs
  - resource: apps/dashboard/src/lib/tags.ts
  - resource: apps/dashboard/src/lib/components/tag-list.svelte
  - resource: apps/dashboard/src/lib/components/candidate/header-tags.svelte
  - resource: apps/dashboard/src/lib/components/rename-tag-dialog.svelte
  - resource: apps/dashboard/src/lib/components/merge-tag-dialog.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/tags/+page.svelte
---
# Shared tags

A tag is a label people and automations put on an application ("Senior", "Night shift"). The owner's fourth
walk-through (2026-11-21) found "Senior", "senior" and "Sr" as three tags. Tags are now the organization's: the add
field suggests the ones the team already uses, a tag in another case is the same tag, and Settings, Tags renames,
merges and deletes them everywhere.

## Where tags live

There is no tag table. A tag is text in an application's `tags` column (`text[]`), and the timeline records every
change as `TagAdded` or `TagRemoved`. Automations read `application.tags` by replaying those two events
(`AutomationFacts.FromTimeline`), never the column, so every change to a tag is made through `JobApplication.AddTag`,
`RemoveTag` or `RenameTag`, which write the events.

Tags compare ignoring case everywhere: on an application, in a condition and in the list.

## The list: `GET /v1/tags`

`OrganizationTags.ListAsync` builds the list from:

- the tags on applications, counted, through the tenant filter, row-level security and job scope. A member on only
  their own jobs counts only the applications they can see, and never sees another organization's tags;
- the tags automations name in jobs that are not closed and in the organization's template drafts and newest published
  versions that are not archived (`WorkflowTags.Named`). Such a tag counts no application until one has it.

An automation names a tag in an Add a tag step and in a condition on `application.tags`: the operand of EqualTo or
Contains, and each value of In. Each tag shows as it is spelled on the most applications, most used first. `templates`
lists the hiring templates whose newest published version names it.

Every member reads it, and so does an organization API key: it is hiring data.

## One tag whatever its case

`POST /v1/applications/{id}/tags` adds a tag as the organization already spells it (`OrganizationTags.SpellingOfAsync`),
so "senior" added where "Senior" exists is "Senior". The MCP tools go through the same code. The worker's Add a tag
action writes the name the automation holds, which a rename keeps in step.

## Suggestions in the add field

`TagList` takes `suggestions`, the list above, and its field becomes a combobox (design system section 11, Tag list).
`suggestTags` in `#lib/tags.ts` picks what it offers:

- the organization's tags containing the text, ignoring case, leaving out the record's own, most used first;
- a tag whose whole name is the text, first, so Enter takes "senior" to "Senior" and never to a more used
  "Senior engineer";
- Create for the text itself, last, only when no tag has that name in any case.

With nothing typed it offers the most used. The arrow keys move, Enter picks, Esc closes the list only. The candidate's
header passes the list; the careers site's embed sites list does not, so it stays a plain field.

## Settings, Tags

`/settings/tags` lists every tag with its candidates and the published templates that name it. Its routes need Edit
settings and a member who sees all jobs (a 403 otherwise), because a change reaches every job's candidates:

| Route | Does |
|---|---|
| `POST /v1/tags/rename` | Renames a tag. A name that is another tag already, in any case, is a 409: Merge says it. A rename that changes only the case is allowed |
| `POST /v1/tags/merge` | Every application with the tag gets the other tag instead; one that had both keeps one |
| `DELETE /v1/tags?tag=` | Takes the tag off every application. Automations that name it keep naming it, so an Add a tag step adds it again, and the page keeps the tag while a job or template names it |

A rename or a merge changes, in one save and through the change tracker:

- the automations of every job that is not closed (`Job.ReviseWorkflow`, a new workflow version, `JobWorkflowChanged`);
- the automations of every template draft that is not archived (`HiringTemplateVersion.Revise`, `TemplateChanged`);
- the saved views that filter on the tag (`SavedFilterView.RenameTag`).

`ChangeHistoryInterceptor` records the jobs' and templates' changes in their history. A published version never
changes, so the response's `stillNamedBy` names the templates whose newest published version still names the old tag,
and the page says so. Publishing a new version from the draft brings them in step. A Contains condition on part of a
tag ("Sen") matches whatever contains it by design, so a rename leaves it as written.

The applications change in the worker. The route publishes `RetagApplications` with that save; its handler changes up to
`ScreenHire:Tags:BatchSize` applications (200; 2 in the test stack) in its own transaction and sends itself again while
a batch comes back full. Each application gets the events from `RenameTag` or `RemoveTag`, with the person who asked as
their actor, and one `ApplicationTagsChanged`, which refreshes the open dashboards' lists and `/v1/tags`. A changed
application no longer matches, so a repeated or redelivered message finds nothing to do. The added event comes a
microsecond after the removal, because the timeline sorts ties by version 7 ids, which can fall either way inside one
millisecond, and a case-only rename must replay in order.

## Tests

- `JobApplicationTests` and `WorkflowTagsTests`: a rename's events and what automations read from them, merges, case-only
  renames, and which operands a rename changes.
- `TagsTests` (integration): counts, isolation between organizations and job scope, the reused spelling, a rename across
  two batches with the jobs, drafts and views it changes and the published version it does not, merge, delete and who
  may change a tag.
- `tags.test.ts`, `tag-list.test.ts` and `tags-page.test.ts` (Vitest): the suggestions, the combobox and the page.
- `TeamWorkTests` (end to end): a tag added from a suggestion in the candidate's header.
