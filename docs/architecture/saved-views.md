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
(`Candidates`, or `Job` with its `JobId`), `IsShared`, and a `ViewDefinition` stored as JSON. The API checks the name and answers
in words; the partial unique index `ux_saved_filter_views_name`, on the lower-cased name of rows not deleted, settles two saves
at the same moment:

| Member | Meaning |
|---|---|
| `baseView` | A built-in view it narrows, by id. A view saved while on To review keeps To review's rule and adds the filters. Candidates page only. |
| `jobIds`, `stages` | The jobs and stages picked. Stage names on the Candidates page (they match across jobs), stage ids on a job's table, which takes no `jobIds`. |
| `authenticity`, `aiAssistance` | The verification filter's levels, by name; `Pending` for checks not yet run. |
| `tag`, `sortColumn`, `sortDescending` | The tag filter and the table's sort. |

Search text is not part of a view: it is a lookup, not a filter. The built-in views stay in code, so the server knows only
their ids (`BuiltInViews`, checked against `saved-views.ts` by `ViewPolicyTests`).

A view picks rows, it never stores them. The jobs a view names must be jobs its saver sees (400 otherwise). When a member is
removed, their private views go with them; the views they shared stay with the team. The delete runs under the job scope, so
when a custom role with Manage team removes someone, a private view of a job that role cannot see stays, seen by nobody, until
the member rejoins.

A shared view shows for everyone who can see its jobs (ADR-0015). A view of one job is returned only to members who see that
job: the list query reads `jobs` through the `JobScope` filter, and the table's `job_scope` row-level security policy holds the
same rule, passing rows with no job. A shared Candidates view that picks jobs is returned only to members who see every one of
them (`ViewPolicy.MaySee`), because the view carries their ids; the check runs after the query, since the picks are in
`definition`. One job out of reach hides the whole view. Its owner keeps it after leaving a job's team, with the rows still
narrowed by the job scope. A Candidates view that picks no jobs shows to every member, and its rows pass through the job scope
like any table's. Stage names in a view are not checked: they are text the saver picked, and they match across jobs.

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
the same owner, even when an Admin deleted a teammate's view. A restore works for a minute (`SavedFilterView.RestoreWindow`),
then answers 404. Each delete removes the rows past that minute, so there is no sweep to run, and a deleted row can wait for
the organization's next delete. A restore is refused with 409 when another view took the name meanwhile.

The dashboard shows the Undo as `UndoNotice`, a card at the bottom of the window for 10 seconds (`UndoOffer`). The Undo
button takes focus when the card appears (the menu that deleted the view has just closed, so focus is nowhere). That focus
does not stop the clock, or the card would never leave. The clock waits while the pointer is on the card or the person moves
focus to it (WCAG 2.2.1), and Escape closes it.

## Hiding built-in views

Each member can hide built-in views from their own sidebar. The choice is `organization_members.views`
(`ViewPreferences.HiddenBuiltIns`), beside the notification settings, so it follows the person across devices and each
organization keeps its own. `PUT /v1/views/hidden-built-ins` replaces the list, so the dashboard sends one change at a time. The
list ends with Show hidden views when any are hidden.

## Real-time

Every change raises `ViewsChanged` (ids only) through the outbox, and the dashboard refetches `/v1/views`
(`invalidations` in `realtime.ts`), so a teammate's shared view appears without a reload. Hiding a built-in view raises
nothing: nobody else's list changes.

## In the dashboard

- **Opening a view.** `/candidates?view=<id>` and `/jobs/<id>?tab=candidates&view=<id>`. The page puts the view's filters and
  sort on the table, without changing the sort the person remembers for that table. A view that is gone shows every
  candidate and says so.
- **Save view.** Once the filters or sort differ from the view the table is on (`differsFromView`), a Save view button ends
  the filter row, outside the part that scrolls, so it is in view at any width. Its dialog asks for a name and Share with the
  team, off by default. The new view opens.
- **Counts.** The sidebar counts the rows each view picks with `viewMatcher`, from the same applications list the pages show.
- **Menu.** Each row has a button that shows on hover and on focus: Rename, Share with the team or Make private, and Delete
  view for a view the person may change; Hide for a built-in one. All of them are menu items, so the keyboard reaches them. A
  tablet's icon rail has no room for the menus, so its top bar has a button that opens the full sidebar.

## Tests

| Test | Proves |
|---|---|
| `ViewPolicyTests` (unit) | who sees and changes a view, the built-in ids match the dashboard's, the view's own rules, Undo's minute |
| `ViewsTests` (integration) | private until shared, 403 and 404 for others, Admin may edit, Undo restores the same row, a job view and a view that picks jobs follow job access, two saves of one name leave one view, another organization sees nothing, validation, hidden built-ins, no API key |
| `ViewsRealtimeTests` (integration) | `ViewsChanged` reaches its own organization with an id only |
| `view-filters.test.ts`, `sidebar-views.test.ts` (Vitest) | what a view holds, when a table differs from it, the rows it picks and the sidebar's counts |
| `app-sidebar.test.ts`, `candidates-page.test.ts`, `job-pipeline.test.ts`, `undo-notice.test.ts` (Vitest) | the menu and its keyboard path, the Save view button and dialog, opening a view, what a saved view keeps of the verification guardrails, the Undo notice |
