---
type: Design
title: "Views"
description: "The sidebar's Views: six built-in views in code, and views people save from the Candidates table or a job's table, with their filters and sort, kept on the server per organization; private until shared with the team, changed only by the owner or a role that edits anyone's views, deleted softly so Undo restores the same row, and each member's choice of built-in views to hide."
tags: [dashboard, views, tenancy, permissions]
sources:
  - resource: src/ScreenHire.Domain/Views/SavedFilterView.cs
  - resource: src/ScreenHire.Domain/Organizations/ViewPreferences.cs
  - resource: src/ScreenHire.Domain/Organizations/OrganizationMember.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/SavedFilterViewConfiguration.cs
  - resource: src/ScreenHire.Application/Views/ViewsChanged.cs
  - resource: apps/api/Endpoints/ViewEndpoints.cs
  - resource: apps/api/Contracts/ViewContracts.cs
  - resource: apps/dashboard/src/lib/saved-views.ts
  - resource: apps/dashboard/src/lib/view-filters.ts
  - resource: apps/dashboard/src/lib/sidebar-views.ts
  - resource: apps/dashboard/src/lib/views.svelte.ts
  - resource: apps/dashboard/src/lib/undo.svelte.ts
  - resource: apps/dashboard/src/lib/components/app-sidebar.svelte
  - resource: apps/dashboard/src/lib/components/save-view-button.svelte
  - resource: apps/dashboard/src/lib/components/save-view-dialog.svelte
  - resource: apps/dashboard/src/lib/components/rename-view-dialog.svelte
  - resource: apps/dashboard/src/lib/components/undo-notice.svelte
---
# Views

The sidebar's Views list holds two kinds of view. The built-in ones (To review, To verify, Overdue, New today, Not
screened, Interviewing) are code: `savedViews` in `apps/dashboard/src/lib/saved-views.ts`, each a name, an icon and a
`matches` function over the rows. The others are saved by people from the Candidates table or from one job's table, and
live on the server. "Saved" now means saved candidates only, so the heading is Views.

## What a view holds

A `SavedFilterView` (`saved_filter_views`) has a name (at most 80 characters, unique for one person and table without
regard to case), an owner (a user id, because a membership id changes when someone leaves and rejoins), a scope
(`Candidates`, or `Job` with its `JobId`), `IsShared`, and a `ViewDefinition` stored as JSON:

| Member | Meaning |
|---|---|
| `baseView` | A built-in view it narrows, by id. A view saved while on To review keeps To review's rule and adds the filters. |
| `jobIds`, `stages` | The jobs and stages picked. Stage names on the Candidates page (they match across jobs), stage ids on a job's table. |
| `authenticity`, `aiAssistance` | The verification filter's levels, by name; `Pending` for checks not yet run. |
| `tag`, `sortColumn`, `sortDescending` | The tag filter and the table's sort. |

Search text is not part of a view: it is a lookup, not a filter. The built-in views stay in code, so the server knows only
their ids (`BuiltInViews`, checked against `saved-views.ts` by `ViewPolicyTests`).

A view picks rows, it never stores them. The jobs a view names must be jobs its saver sees (400 otherwise), so a shared view
never carries the id of a job they cannot see. When a member is removed, their private views go with them; the views they
shared stay with the team.

Everything a table shows still passes through the job scope (ADR-0015), so a
shared Candidates view shows each member only the candidates on jobs they see. A view of one job is returned only to
members who see that job: the list query reads `jobs` through the `JobScope` filter. The table has tenant row-level
security only, because a Candidates view has no job for the `job_scope` policy to check.

A view that narrows a verification view, or filters by a verification level, keeps the page's guardrails: the Candidates
page offers no bulk email while verification picked the rows, and bulk moves never offer a stage that ends a candidacy
(ADR-0004). A saved view changes which filters are set, never what those filters allow.

## Who may change a view

A private view is its owner's and nobody else's, an Admin's included. A shared view shows for the team. Only its owner, or
a role with the Edit anyone's views permission (`Permission.EditAnyView`; Owners and Admins among the built-in roles),
renames it, shares it, makes it private or deletes it (`ViewPolicy.MayChange`). Someone else gets 403 on a shared view they
can see and 404 on a private one. The permission is checked in the handler, as `DeleteAnyNote` is for notes, so the
routes need no `RequirePermission`.

The routes are session only, under `/v1/views`: an API key has no views, and none of them is a hiring write.

## Deleting and Undo

Delete is soft: it sets `deleted_at`, and `POST /v1/views/{id}/restore` clears it, so Undo brings back the same row with
the same owner, even when an Admin deleted a teammate's view. Each delete removes the rows that stayed deleted for more
than a minute, so there is no sweep to run. A restore is refused with 409 when another view took the name meanwhile.

The dashboard shows the Undo as `UndoNotice`, a card at the bottom of the window for 10 seconds (`UndoOffer`). The Undo
button takes focus when the card appears (the menu that deleted the view has just closed, so focus is nowhere), and the
clock waits while the pointer or focus is on the card, so the keyboard has time to reach it (WCAG 2.2.1).

## Hiding built-in views

Each member can hide built-in views from their own sidebar. The choice is `organization_members.views`
(`ViewPreferences.HiddenBuiltIns`), beside the notification settings, so it follows the person across devices and each
organization keeps its own. `PUT /v1/views/hidden-built-ins` replaces the list. The list ends with Show hidden views when
any are hidden.

## Real-time

Every change raises `ViewsChanged` (ids only) through the outbox, and the dashboard refetches `/v1/views`
(`invalidations` in `realtime.ts`), so a teammate's shared view appears without a reload. Hiding a built-in view raises
nothing: nobody else's list changes.

## In the dashboard

- **Opening a view.** `/candidates?view=<id>` and `/jobs/<id>?tab=candidates&view=<id>`. The page puts the view's filters and
  sort on the table, without changing the sort the person remembers for that table. A view that is gone shows every
  candidate and says so.
- **Save view.** Once the filters or sort differ from the view the table is on (`differsFromView`), a Save view button ends
  the filter row. Its dialog asks for a name and Share with the team, off by default. The new view opens.
- **Counts.** The sidebar counts the rows each view picks with `viewMatcher`, from the same applications list the pages show.
- **Menu.** Each row has a button that shows on hover and on focus: Rename, Share with the team or Make private, and Delete
  view for a view the person may change; Hide for a built-in one. All of them are menu items, so the keyboard reaches them.

## Tests

| Test | Proves |
|---|---|
| `ViewPolicyTests` (unit) | who sees and changes a view, the built-in ids match the dashboard's, the view's own rules |
| `ViewsTests` (integration) | private until shared, 403 and 404 for others, Admin may edit, Undo restores the same row, a job view follows job access, another organization sees nothing, validation, hidden built-ins, no API key |
| `ViewsRealtimeTests` (integration) | `ViewsChanged` reaches its own organization with an id only |
| `view-filters.test.ts`, `sidebar-views.test.ts` (Vitest) | what a view holds, when a table differs from it, the rows it picks and the sidebar's counts |
| `sidebar-views.test.ts`, `candidates-page.test.ts`, `job-pipeline.test.ts`, `undo-notice.test.ts` (Vitest) | the menu and its keyboard path, the Save view button and dialog, opening a view, the Undo notice |
