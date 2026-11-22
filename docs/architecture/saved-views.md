---
type: Design
title: "Views"
description: "The sidebar's Views: six built-in views in code, and views people save from the Candidates table or a job's table, with their filters and sort, kept on the server per organization; private until shared with the team, shared as an offer that each teammate browses, previews and adds to their own sidebar, changed only by the owner or a role that edits anyone's views, deleted softly so Undo restores the same row, and each member's choice of built-in views to hide."
tags: [dashboard, views, tenancy, permissions]
sources:
  - resource: src/ScreenHire.Domain/Views/SavedFilterView.cs
  - resource: src/ScreenHire.Domain/Views/SavedViewMember.cs
  - resource: src/ScreenHire.Domain/Organizations/ViewPreferences.cs
  - resource: src/ScreenHire.Domain/Organizations/OrganizationMember.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/SavedFilterViewConfiguration.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/SavedViewMemberConfiguration.cs
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
  - resource: apps/dashboard/src/lib/components/team-views-sheet.svelte
  - resource: apps/dashboard/src/lib/components/rename-view-dialog.svelte
  - resource: apps/dashboard/src/lib/components/undo-notice.svelte
  - resource: apps/dashboard/src/lib/components/job-pipeline.svelte
  - resource: apps/dashboard/src/lib/components/top-bar.svelte
  - resource: apps/dashboard/src/lib/components/current-candidates.svelte
---
# Views

The sidebar's Views list holds two kinds of view. The built-in ones (To review, To verify, Overdue, New today, Not
screened, Interviewing) are code: `savedViews` in `apps/dashboard/src/lib/saved-views.ts`, each a name, its own icon
(a status view's is its status's: To review `eye`, To verify `id-card`, Overdue `clock-alert`; design system I2) and a
`matches` function over the rows. No two share an icon, so the tablet's icon rail tells them apart. To review holds Review and To verify holds Verify, each not
dismissed, so the two never overlap. Each of those two is a verification level too (`authenticity` on the view): opening it puts
the level in the Candidates filter row ("Verification: Review"), `verificationOf` in `view-filters.ts` reads it for a saved view
built on one, and choosing another level, or Any state, leaves the view for the Candidates page with that filter. Two more, New this week and Interviews this week (`overviewViews`), open from the Jobs
overview's numbers ([notifications](notifications.md), "The Jobs overview"); they resolve from `?view=` like the others but are not in the sidebar. The others are saved by people from the Candidates table or from one job's table, and
live on the server. "Saved" now means saved candidates only, so the heading is Views.

## What a view holds

A `SavedFilterView` (`saved_filter_views`) has a name (at most 80 characters, unique for one person and table without
regard to case), an owner (a user id, because a membership id changes when someone leaves and rejoins), a scope
(`Candidates`, or `Job` with its `JobId`), `IsShared`, `SharedAt` (when it was last shared, null while private) and a `ViewDefinition` stored as JSON. The API checks the name and answers
in words; the partial unique index `ux_saved_filter_views_name`, on the lower-cased name of rows not deleted, settles two saves
at the same moment:

| Member | Meaning |
|---|---|
| `baseView` | A built-in view it narrows, by id. A view saved while on To review keeps To review's rule and adds the filters. Candidates page only. |
| `jobIds`, `stages` | The jobs and stages picked. Stage names on the Candidates page (they match across jobs), stage ids on a job's table, which takes no `jobIds`. |
| `authenticity`, `aiAssistance` | The verification filter's state, by level name (Clear, Review and Verify show as one word); `Pending` for checks not yet run. `aiAssistance` is kept in the API but never written or read: AI assistance is information and never filters, so a view saved with one keeps its other filters and drops that one. A saved sort by the old `authenticity` column opens on the Verification column, and one by `aiAssistance` on the table's default. |
| `tag`, `sortColumn`, `sortDescending` | The tag filter and the table's sort. A tag renamed or merged in Settings, Tags is renamed here too ([shared tags](tags.md)). |

Search text is not part of a view: it is a lookup, not a filter. The built-in views stay in code, so the server knows only
their ids (`BuiltInViews`, checked against `saved-views.ts` by `ViewPolicyTests`).

A view picks rows, it never stores them. The jobs a view names must be jobs its saver sees (400 otherwise). When a member is
removed, their private views go with them, and so do the shared views they had added to their sidebar; the views they shared stay with the team. The delete runs under the job scope, so
when a custom role with Manage team removes someone, a private view of a job that role cannot see stays, seen by nobody, until
the member rejoins.

A shared view is offered to everyone who can see its jobs (ADR-0015), and sits in a teammate's sidebar only once they add it ("Sharing is an offer", below). A view of one job is returned only to members who see that
job: the list query reads `jobs` through the `JobScope` filter, and the table's `job_scope` row-level security policy holds the
same rule, passing rows with no job. A shared Candidates view that picks jobs is returned only to members who see every one of
them (`ViewPolicy.MaySee`), because the view carries their ids; the check runs after the query, since the picks are in
`definition`. One job out of reach hides the whole view. Its owner keeps it after leaving a job's team, with the rows still
narrowed by the job scope. A Candidates view that picks no jobs shows to every member, and its rows pass through the job scope
like any table's. Stage names in a view are not checked: they are text the saver picked, and they match across jobs.

A view that narrows a verification view, or filters by a verification level, keeps the page's guardrails: the Candidates
page offers no bulk email while verification picked the rows, and bulk moves never offer a stage that ends a candidacy
(ADR-0004). A saved view changes which filters are set, never what those filters allow.

## Sharing is an offer

Sharing a view does not put it in anyone's sidebar. A view is always in its owner's sidebar. A teammate's shared view is in
a person's sidebar once they add it, and that is a row of `saved_view_members` (organization, view, user, position, when):
tenant-owned with row-level security like every table, with foreign keys that cascade from the organization, the view
and the user, so the purge of a deleted view takes its rows. The table has no job id, so it has no `job_scope` policy: the
list reads the view through the rules above, and a row for a view the person cannot see shows nothing.

- `GET /v1/views` still returns every view the person may see, each with `inSidebar` (their own, or added), `ownerName`
  (who saved it; "A former teammate" once they left) and `sharedAt`. `sidebarOrder` lists the ids of the shared views they
  added, in the order they sit. The page that opens `?view=<id>` resolves it from this list, so a Preview of a view that
  is not in the sidebar opens like any other.
- `PUT /v1/views/{id}/sidebar` adds a view (idempotent, at the end of the person's added views; 404 for a view they cannot
  see; nothing to do for their own) and `DELETE /v1/views/{id}/sidebar` removes it. The view stays shared. Neither raises
  an event, as hiding a built-in view raises none: only that person's list changes.
- Making a view private deletes its rows, so it leaves every sidebar and sharing it again is a new offer. Deleting it
  keeps the rows, so Undo puts it back in every sidebar it was in.
- `ViewPreferences.TeamViewsOpenedAt` (in `organization_members.views`) is when the person last opened the sheet of team
  views, saved by `PUT /v1/views/team-views-opened`; until then their membership's start stands in, so a new member does not
  see the backlog as New. `teamViewsSeenAt` in the list is that date. A view is New when `sharedAt` is later. A rename does
  not move `sharedAt`.
- The migration `AddSavedViewMembers` kept today's sidebars: every shared view got a row for each member of its
  organization who does not own it, in the order the views were saved, and `shared_at` took the view's `updated_at`.
  Members who cannot see a view got a row too. That is harmless, since what a person sees is still the rules above, and
  it is what they had if they gain access later.

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
Views heading's menu (Views options) has a Show hidden views choice with their count when any are hidden, and a
built-in view's own menu says Built-in view and offers Hide or Show.

## Real-time

Every change to a view raises `ViewsChanged` (ids only) through the outbox, and the dashboard refetches `/v1/views`
(`invalidations` in `realtime.ts`), so a teammate's shared view reaches the sheet of team views without a reload. Hiding a
built-in view, adding a view to the sidebar, removing it and opening the sheet raise nothing: nobody else's list changes.
There is no toast or bell line when a teammate shares a view; the sheet's New mark is the only sign.

## In the dashboard

- **Opening a view.** `/candidates?view=<id>` and `/jobs/<id>?tab=candidates&view=<id>`. The page puts the view's filters and
  sort on the table, without changing the sort the person remembers for that table. A view that is gone shows every
  candidate and says so.
- **Save view.** A Save view button is always in the filter row, disabled with the reason in its tooltip until the filters or sort differ from the view the table is on (`differsFromView`). It ends
  the filter row, outside the part that scrolls, so it is in view at any width. Its dialog asks for a name and Share with the
  team, off by default. The new view opens.
- **No counts.** A sidebar view carries no count (design system T4): it only filters, and the layout no longer loads the applications list for it. `viewMatcher` still picks the rows a table shows.
- **Menu.** Each row has a button that shows on hover and on focus: Rename, Share with the team or Make private, and Delete
  view for a view the person may change; Hide for a built-in one; Remove from sidebar for a teammate's view they added,
  first in the menu (the view stays shared). All of them are menu items, so the keyboard reaches them. A
  tablet's icon rail has no room for the menus, so its top bar has a button that opens the full sidebar. In the rail a
  saved view shows the first letter of its name in a small rounded square, with its name in the tooltip, and a divider
  sets the views apart from the sections above them.

- **Browse team views.** An item in the Views heading's menu (Views options), so the list holds only views, or in the icon rail an icon button with a tooltip in the
  fixed group above Settings (outside the list that scrolls, so it stays in reach at 700 px tall), opens `TeamViewsSheet`: the shared
  views that are a teammate's, newest share first. Each row has its name and a New mark, who shared it, its filters in one
  muted line (`describeFilters`, which leaves out the sort and AI assistance), how many candidates it shows now, Preview and
  Add (or "In your sidebar" and Remove). Preview opens the view's page and adds nothing. The count is read in the browser
  from the applications list, read only while the sheet is open, with the same `viewMatcher` the pages filter by. Opening the
  sheet saves the visit, and its New marks stay until it closes. A view already in the person's sidebar when the sheet
  opens carries no New mark, so the views the migration kept in every sidebar do not all read New on the first visit.
  A row's Add and Remove wait while their request runs, and a failure shows on its own row. If the applications list cannot
  be read the count line stays blank.

## Tests

| Test | Proves |
|---|---|
| `ViewPolicyTests` (unit) | who sees and changes a view, the built-in ids match the dashboard's, the view's own rules, Undo's minute |
| `ViewsTests` (integration) | private until shared, 403 and 404 for others, Admin may edit, Undo restores the same row, a job view and a view that picks jobs follow job access, two saves of one name leave one view, another organization sees nothing, validation, hidden built-ins, no API key |
| `ViewSharingTests` (integration) | sharing adds to nobody else's sidebar, add and remove per person and safe to repeat, the owner always has the view, job scope on browse and add, making private clears every sidebar and a delete keeps them for Undo, a removed member's rows go, the migration's backfill keeps today's sidebars, another organization sees and adds nothing, the visit date |
| `ViewsRealtimeTests` (integration) | `ViewsChanged` reaches its own organization with an id only |
| `view-filters.test.ts`, `sidebar-views.test.ts` (Vitest) | what a view holds, when a table differs from it, the rows it picks and the sidebar's names, distinct icons and order |
| `team-views-sheet.test.ts`, `views.svelte.test.ts` (Vitest) | the sheet's rows, New mark, Preview, Add and Remove, its count and empty state; the sidebar actions |
| `ViewSharingTests` (end-to-end) | a teammate does not see a shared view in the sidebar, browses it with a New mark, previews, adds and removes it |
| `app-sidebar.test.ts`, `candidates-page.test.ts`, `job-pipeline.test.ts`, `undo-notice.test.ts` (Vitest) | the menu and its keyboard path, the Save view button and dialog, opening a view, what a saved view keeps of the verification guardrails, the Undo notice |
