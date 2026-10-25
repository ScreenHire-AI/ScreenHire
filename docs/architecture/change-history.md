---
type: Design
title: "Change history"
description: "Who changed a hiring template, an email template, a job or a custom role, when and what: change records written by an EF Core interceptor in the same save, lines in plain words matched by element ids, Last updated on each read contract, the paged history routes, and how to track another kind."
tags: [history, audit, templates, jobs]
sources:
  - resource: src/ScreenHire.Domain/History/ChangeRecord.cs
  - resource: src/ScreenHire.Domain/History/ChangeLine.cs
  - resource: src/ScreenHire.Domain/History/DefinitionChanges.cs
  - resource: src/ScreenHire.Domain/History/HiringTemplateChanges.cs
  - resource: src/ScreenHire.Domain/History/EmailTemplateChanges.cs
  - resource: src/ScreenHire.Domain/History/ScorecardTemplateChanges.cs
  - resource: src/ScreenHire.Domain/History/JobChanges.cs
  - resource: src/ScreenHire.Domain/History/RoleChanges.cs
  - resource: src/ScreenHire.Domain/History/ChangeLabels.cs
  - resource: src/ScreenHire.Domain/History/RichTextToPlain.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/History/TrackedKinds.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/History/ChangeHistoryInterceptor.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/History/ChangeContext.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/History/ChangeHistoryQueries.cs
  - resource: apps/api/Contracts/HistoryContracts.cs
  - resource: apps/api/Endpoints/ChangeHistoryRoutes.cs
  - resource: apps/dashboard/src/lib/components/history-sheet.svelte
  - resource: apps/dashboard/src/lib/components/last-updated.svelte
  - resource: apps/dashboard/src/lib/history.ts
  - resource: apps/dashboard/src/lib/history-pages.svelte.ts
---
# Change history

Anything people edit says who updated it last and how long ago, and its History sheet lists every change in plain
words (design system T6). This doc is how that is recorded and read. History covers configuration only: hiring
templates, email templates, scorecard templates, jobs and custom roles today. Built-in roles never change, so they
have no history. It never holds
candidate data, so it has no retention limit.

## What is recorded

One `change_records` row (`ChangeRecord`) per object per save:

| Column | Holds |
|---|---|
| `subject_kind`, `subject_id` | The object: `HiringTemplate`, `EmailTemplate`, `ScorecardTemplate`, `Job` or `Role`, and its id. A built-in email template's id is its built-in id (`stage-change`), because restoring deletes its row and the next edit adds a new one. A template version belongs to its template |
| `actor_user_id`, `actor_api_key_id` | The member who made it, or the organization API key. Neither means ScreenHire: the worker, an automation, the demo's setup |
| `occurred_at` | From `TimeProvider`, when the save ran |
| `lines` | JSONB: one line per change, each its words (`parts`, the changed thing's name marked strong) and its values before and after (`details`) |
| `sequence` | An identity column: the order. It counts every organization's records, so it never leaves the API: the paging cursor is an entry's id |

Everything one save changes in an object is one entry with several lines. People are stored by id and named when the
history is read, so a renamed person shows their current name. The words are stored as they were said: a later
rename of a question leaves the old lines naming it as it was.

Objects that existed before history was recorded start their history at their first edit; nothing is back-filled.
Their Last updated falls back to the row's own `updated_at` with nobody named.

## How it is written

`ChangeHistoryInterceptor` is an EF Core `SaveChangesInterceptor`. Before a save it runs `DetectChanges`, then for
every added, modified or deleted entry of a type in `TrackedKinds` it compares the entity as loaded
(`OriginalValues.ToObject()`; JSON columns snapshot their original through their comparer) with the entity now, and
adds a `ChangeRecord` per object to the same save, so the record commits in the same transaction as the change or not
at all. A failed save detaches its records, so a retried save writes them once.

The interceptor is added by `AddScreenHireDatabase` (the API and the worker), not by `UseScreenHireConventions`, so the
owner-run seeder, the design-time factory and the test contexts write no history. The actor comes from
`ITenantContext`: `UserId` for a signed-in member, `ApiKeyId` (set by `TenantResolutionMiddleware`) for a key.

An edit that skips the change tracker (`ExecuteUpdate`, `ExecuteDelete`, raw SQL) is not recorded. Tracked objects
change through the tracker; the email template endpoints delete through it for this reason.

## How the lines read

The describers in `src/ScreenHire.Domain/History` are pure functions from the entity before and after to lines:

- `DefinitionChanges` compares hiring definitions. Stages, questions, scorecard questions, automations and follow-up
  questionnaires are matched by their stable ids, so a renamed question is one rename. Question ids are unique across
  the apply questionnaire and every follow-up, so a question moved between them is no change. Parts without ids (an
  automation's actions and conditions) show in that automation's details. Raw JSON never reaches a line.
- `HiringTemplateChanges`, `EmailTemplateChanges`, `JobChanges` and `RoleChanges` cover the rest. A role's lines name
  permissions as the Roles page does: "created the role from **Viewer**", "allowed **Move candidates**", "took away
  **Email candidates**". A built-in email template's first
  edit compares against ScreenHire's wording, and deleting its row reads "restored the original wording". A job's
  description is sanitized HTML, so `RichTextToPlain` turns it to text first. A time, such as when a posting expires,
  is an `Instant` detail in ISO 8601, which the browser formats.
- Bookkeeping makes no line: `updated_at`, workflow and publish counters, the row version.

Examples: "added **Did they finish the exercise in the time?**", "made **Notes on the exercise** optional", "changed
**Years of experience** from Short text to Number", "added the stage **Technical interview**", "renamed the
template from Tech interview to **Technical interview**", "created the template from **Software engineer**",
"published version **2**", "changed the subject".

`ChangeLabels` names things with the dashboard's words. They mirror the label maps in
`apps/dashboard/src/lib/workflow.ts` and `format.ts`, and `ChangeLabelsTests` fails when the two drift apart. It fails
on purpose when a task renames an answer kind on one side only: change both.

## Secrets

`TrackedKinds` is an allow-list: the interceptor never reads an entity that is not on it, so an API key's hash, a
webhook endpoint's signing secret and an invitation's token hash cannot reach the history. `TrackedKindsTests` fails
when a tracked entity has a secret-like property, and `ChangeHistoryTests` checks the stored lines against a real key,
its hash and a webhook secret.

A tracked definition can hold a secret too: a webhook's address often carries its token in the path or the query. A
"Call a webhook" action is recorded by its host alone, and a change to the rest of the address is the line "changed
the webhook address of" the automation, with no values (`DefinitionChanges`).

## Isolation

`change_records` is tenant-owned (`ITenantOwned`, the `Tenant` query filter) with forced row-level security and the
`tenant_isolation` policy (migration `AddChangeHistory`). It is append-only: `screenhire_app` has no `UPDATE` or
`DELETE` on it. It has no foreign key to users or API keys, so a removed member or a revoked key keeps their record.

## API

- `lastUpdated` on `HiringTemplateSummary`, `HiringTemplateDetail`, `EmailTemplateEntry`, `JobSummary`, `JobDetail`,
  `RoleSummary` and `CustomRoleDetail`: `at`, `by` (`Member`, `ApiKey`, `ScreenHire`, or null when nobody was recorded), `userId`, `apiKeyId`.
  A system hiring template is by ScreenHire; a built-in email template nobody changed, and a built-in role, are by
  ScreenHire with no `at`.
  Lists read every object's newest change in one query (`ChangeHistoryQueries.LastAsync`).
- The history, newest first, `?limit=` (1 to 100, default 50) and `?before=` (the previous page's `nextBefore`, the id
  of its last entry; an id that is not one of the object's entries is a 400):
  - `GET /v1/templates/{templateId}/history` (the hiring API: API keys too)
  - `GET /v1/jobs/{jobId}/history` (the hiring API: API keys too)
  - `GET /v1/email-templates/{templateId}/history` (any member, as the email template routes are)
  - `GET /v1/organization/roles/{roleId}/history` (any member, as the role routes are)

  Each route first checks that the caller can see the object, so another organization's id is a 404.

## Dashboard

`LastUpdated` is the last fact of the header ("2 hours ago by Avery Quinn"), with a History button, and the jobs, hiring
templates and email templates tables have a Last updated column with the date (section 13); `HistorySheet`
groups the entries by day and shows each line, with the values before and after behind Show the change. Pages read the
pages with `createHistoryPages` (`#lib/history-pages.svelte.ts`) over the generated query options, only while the sheet
is open; a save that invalidates the object's path refreshes them. `#lib/history.ts` names the actor the way the rest
of the dashboard names people (`personName`: "you", the current name, "a former teammate").

## Tracking another kind

1. Add a `ChangeSubjectKind` value.
2. Add one line to `TrackedKinds.All`: the entity, its subject id and its describer. A describer that needs only the
   entity before and after is a plain function; reuse `DefinitionChanges` for questions.
3. Add a `GET .../{id}/history` route in the kind's area with `ChangeHistoryRoutes`, after the area's own visibility
   check.
4. Put `lastUpdated` on its read contracts with `ChangeHistoryQueries.LastAsync` and `LastUpdate.From`.
5. On its page, the Last updated fact and the History sheet; run `pnpm generate:api`.
