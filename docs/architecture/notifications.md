---
type: Design
title: "Notifications"
description: "The bell in the top bar: setup steps (with the organization's skipped steps and hidden checklist) and per-person alerts, how any handler raises or clears one through RaiseNotification, the audience by permission, the unique row per person, each person's choices by toast, email and Slack with the organization's alerts as defaults, the daily digest, live refresh, retention, the Jobs overview numbers, and the daily email domain check that raises the first alert."
tags: [notifications, dashboard, wolverine, tenancy, email]
sources:
  - resource: src/ScreenHire.Domain/Notifications/Notification.cs
  - resource: src/ScreenHire.Domain/Organizations/SendingDomain.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs
  - resource: src/ScreenHire.Application/Notifications/Notifications.cs
  - resource: src/ScreenHire.Application/Billing/TrialReminderHandlers.cs
  - resource: src/ScreenHire.Application/Verification/Models/VerificationAlerts.cs
  - resource: src/ScreenHire.Application/SendingDomains/RecheckSendingDomain.cs
  - resource: src/ScreenHire.Application/SendingDomains/SendingDomainService.cs
  - resource: src/ScreenHire.Application/Messaging/ScreenHireMessaging.cs
  - resource: src/ScreenHire.Domain/Notifications/NoticeKind.cs
  - resource: src/ScreenHire.Domain/Notifications/DigestItem.cs
  - resource: src/ScreenHire.Domain/Organizations/MemberPreferences.cs
  - resource: src/ScreenHire.Application/Notifications/NoticePreferences.cs
  - resource: src/ScreenHire.Application/Notifications/NoticeDelivery.cs
  - resource: src/ScreenHire.Application/Notifications/MemberNotices.cs
  - resource: src/ScreenHire.Application/Notifications/Digest.cs
  - resource: src/ScreenHire.Application/Applications/ApplicationSubmitted.cs
  - resource: src/ScreenHire.Application/Candidates/CandidateErasure.cs
  - resource: apps/api/Endpoints/AccountEndpoints.cs
  - resource: apps/dashboard/src/routes/(app)/account/notifications/+page.svelte
  - resource: apps/dashboard/src/lib/notice-grid.ts
  - resource: apps/api/Endpoints/NotificationEndpoints.cs
  - resource: apps/api/Endpoints/OrganizationEndpoints.cs
  - resource: src/ScreenHire.Domain/Organizations/SetupStepId.cs
  - resource: apps/api/Contracts/NotificationItemContracts.cs
  - resource: apps/api/Endpoints/SendingDomainEndpoints.cs
  - resource: apps/dashboard/src/lib/components/notification-bell.svelte
  - resource: apps/dashboard/src/lib/notification-lines.ts
  - resource: apps/dashboard/src/lib/setup-progress.svelte.ts
  - resource: apps/dashboard/src/lib/setup-panel.svelte.ts
  - resource: apps/dashboard/src/lib/preferences.svelte.ts
  - resource: apps/dashboard/src/lib/setup-checklist.ts
  - resource: apps/dashboard/src/lib/components/jobs-overview.svelte
  - resource: apps/dashboard/src/lib/components/overview-stat.svelte
  - resource: apps/dashboard/src/lib/saved-views.ts
  - resource: apps/dashboard/src/routes/(app)/settings/sending-domain/+page.svelte
  - resource: apps/dashboard/src/routes/(app)/settings/organization/+page.svelte
---

# Notifications

A bell in the top bar holds what needs one person's attention. It has two parts:

- **Setup**, for whoever can do the steps (Owners and Admins): the four steps a new organization takes, each linking to the
  page where it is done, in the order a team does them: branding, the first published job, inviting the team, then sending email from the company's own domain (last, because it is DNS work outside ScreenHire and
  the team can start without it). Whether a step is done is not stored. The dashboard works it out from the organization's own
  data, as it always did, and shows only the steps left, with the done ones folded into one line ("2 steps done"). Sending email
  from the company's own domain says Optional and never counts toward the badge. What the server keeps is the organization's
  choices about the list (see Skipping a step and hiding setup, below).
- **Alerts**, stored on the server, one row per person: a plain line that links to the page that fixes it. They are read,
  dismissed and pushed live per person.

Setup used to sit on top of Jobs. A team can use ScreenHire fully without a careers site or an email domain, so it no longer
takes over the page where people look at their jobs. The bell's count is the setup steps left plus the alerts not yet read.
Opening the panel marks nothing read; following an alert's link does.

Two things open the panel at its setup section. The Jobs empty state, while steps remain, says how many are left and has a button,
Open the setup checklist, which counts up `setupPanel.requests` (`setup-panel.svelte.ts`); every page mounts its own bell, which
opens when the count passes the one it saw when it mounted. And right after onboarding creates the organization, the page
queues a one-time ask in `preferences` for that person (`queueSetupTour`); the first bell that has steps to show takes it
(`takeSetupTour`, so it never opens itself twice) and opens, so a bell that unmounts while the steps load leaves it for the next
one. In both cases focus starts on the setup section, until the panel closes.

### Skipping a step and hiding setup

A team that will never set up its own email domain should not keep that step forever, and the only way out used to hide the
whole checklist, in one browser (the owner's walk-through, 2026-11-21). Both choices now live on the organization row:

- `SkippedSetupSteps` (`text[]` of `SetupStepId` names) holds the steps someone marked **Not needed**, a button on each open
  step. A skipped step leaves the list and the count for every Owner and Admin on every device, keeps its place so the steps
  after it keep their numbers, and is not counted as done. Its settings page still does it, and once the data says it is done
  it counts as done.
- `SetupHidden` is set by the section's icon button, **Hide setup for everyone**.
- **Show setup again**, on Settings, General, clears both. The panel there appears only while something is hidden or skipped,
  and names the skipped steps.

The routes are `GET /v1/organization/setup` (any member), and `POST /v1/organization/setup/skip` (`{ step }`), `/hide` and
`/show`, each with Edit settings and the antiforgery token. Each write is one change made under a lock on the organization
row, so two Owners skipping different steps at once both keep theirs. The history describers ignore these columns, so a skip never
moves General's Last updated. The dashboard asks for the choices first, and asks the five questions behind the steps only
when the checklist is not hidden. There is no live event for these choices: another open dashboard picks them up when it next
reads `/v1/organization/setup` (a minute's stale time, or a reload). The old per-browser hide (`screenhire:setup-dismissed:*`
in localStorage) is no longer read, so a checklist someone hid that way shows again until it is hidden for the organization.

The panel is called Needs attention, because Notifications already names the person's choices on Your account and the toast
tray. The bell holds no team news: that stays in the tray (the realtime doc). It does keep what is addressed to the person
(a mention, a candidate's reply, a task given to them, an interview they are on), whatever they chose for toasts and email.

## Raising a notification

There is one way. A handler returns a `RaiseNotification` (or an endpoint publishes it through the outbox, in the same
transaction as its change):

```csharp
new RaiseNotification(
    organizationId,
    Permission.ManageOwners,                 // who: the members who hold it and see every job
    NotificationKinds.SendingDomainFailing,  // what kind
    domain.Id.ToString("N"),                 // what it is about, stable for that thing
    new Dictionary<string, string> { ["domain"] = domain.Domain },
    "/settings/sending-domain");             // the page that fixes it, a path inside the dashboard
```

- **Audience** is a `Permission`, resolved with the same rule as the verification alerts (the member holds it and sees every
  job). Owners only is `ManageOwners`. Billing alerts use `ManageBilling`. A handler never loops over members itself.
- **Key** names the thing, so raising the same kind and key again for a person is one row. The unique index on (organization,
  person, kind, key) and an `INSERT ... ON CONFLICT DO NOTHING` do this, so a redelivery or a second run changes nothing and
  never throws inside the handler's transaction. There is no `[Deduplicated]` on purpose: its 24 hour claim would also swallow
  a real second raise after the first was cleared.
- **A dismissed alert is not raised again** while its row exists. Clearing it removes the row, so the next failure is new.
- **Payload** holds the facts the line is made from, never personal data about a candidate, and never prose.
- **Link** must be a path inside the dashboard (one leading slash, not `//`). A message with another link is refused and
  logged, because an alert is a link people trust.

To take an alert away because what it said is no longer true, send `ClearNotification(organizationId, kind, key)`. It deletes
every person's copy.

Both messages implement `ITenantMessage`, so the handler runs under the organization's row-level security. Each returns a
`NotificationsChanged` event only when a row was added or removed.

### A new kind

1. Add a constant to `NotificationKinds` with a comment naming the key and the payload.
2. Add its line to `notificationLine()` in `apps/dashboard/src/lib/notification-lines.ts`. A kind the dashboard does not know
   shows "Something needs a look" with its link, so an older dashboard never shows raw data.
3. Raise it from the handler that detects the state, and clear it from the handler that sees the state end.
4. Test the raise, the clear and the audience (see `NotificationTests` for the shape).

| Kind | Key | Payload | Link | Audience |
|---|---|---|---|---|
| `sending-domain-failing` | the sending domain's id | `domain` | `/settings/sending-domain` | Owners (`ManageOwners`) |
| `trial-ending` | `trial` | `endsAt`, `plan` | `/settings/billing` | Owners (`ManageBilling`) |
| `trial-ended` | `trial` | `endsAt`, `plan` | `/settings/billing` | Owners (`ManageBilling`) |
| `ai-usage-budget` | `yyyy-MM:budget-N` (N is 50, 80 or 100) | `percent` | `/verification/usage` | who sees AI usage (`ViewAiUsage`) |
| `ai-usage-cap` | `yyyy-MM:cap` | none | `/verification/usage` | who sees AI usage (`ViewAiUsage`) |
| `ai-usage-spike` | `yyyy-MM-dd:spike` | `day` | `/verification/usage` | who sees AI usage (`ViewAiUsage`) |
| `mention` | the note's id | `actor`, when known | the note on the candidate's Activity | the person mentioned |
| `candidate-reply` | the reply's id | none: the line never names the candidate | the candidate's Messages | the person who sent the email it answers |
| `task-assigned` | the task's id | `actor`, when a teammate gave it | the application | the person the task is for |
| `interview-changed` | `interviewId:revision` | `change` (`Booked`, `Moved`, `Cancelled`) | the application | each member on the interview |

The last four are raised for one person, not an audience by permission: `NoticeDelivery.RaiseForPersonAsync` inserts the row
with the same `ON CONFLICT DO NOTHING` and returns whether it did, so the caller sends `NotificationsChanged`.

The two trial kinds are one story: `trial-ending` appears 7 days before the trial ends and is replaced by `trial-ended` when it
ends, and a plan starting clears both ([billing](billing.md#trial-reminders)). Their line says the days left, which the dashboard
works out from `endsAt` when it draws the line, so one row stays right as the days pass.

The AI usage kinds are raised by `NotifyAiUsageHandler`, which also sends the email or Slack copy each person chose, starting
from the organization's choice ([ai-usage](ai-usage.md) and below). Raising the cap clears `ai-usage-cap`.

## Each person's choices

Your account > Notifications (`/account/notifications`) is a grid: a row per kind of thing, a column per way to hear of it.
The rows are one list on each side, `NoticeRows` in `NoticePreferences.cs` and `noticeRows` in the dashboard's
`#lib/account.ts`; a new row is one entry in each, appended to `NoticeKind`.

| Row | In the app (toast kinds) | Email starts at | Slack | Shown to |
|---|---|---|---|---|
| Mentions | `Mentions` (the tray's card) | Instantly | on (the Slack link's Mentions) | everyone |
| Candidate replies | `Replies` (the tray's card) | Instantly | none | everyone |
| Interviews booked or changed | none yet: the event carries no actor, so it cannot toast | Instantly | none | everyone |
| Tasks assigned to you | `Tasks` | Daily digest | on (the Slack link's Tasks) | everyone |
| New applications on your jobs | `NewCandidates` | Daily digest | none | everyone |
| Teammates' moves and notes | `Moves`, `Notes`, `Tags`, `Emails`, `Scorecards`, `Templates` (one switch; Choose kinds opens each) | Off | none | everyone |
| Verification suggested | none: never a toast (ADR-0004) | the organization's verification email alert | the organization's verification Slack alert | holders of Review verification |
| AI use and billing | none: the bell keeps them | the organization's AI usage email alert; Instantly for whoever handles billing, whose trial reminders start there. While the organization's switch is off, whatever such a person saves for the row is kept and covers both; until they save, they get the trial's reminders and no AI alerts | the organization's AI usage Slack alert | holders of View AI usage or Manage billing |

- **In the app** switches the toast or card in the tray; the bell keeps what is addressed to the person either way. It is the
  `Off` list of `NotificationSettings` on the membership, the same column the Teammate changes sheet saved, so nobody's
  earlier choice moved. Pause toasts is `Toasts` turned off, and Close toasts automatically is `CloseToasts`.
- **Email** is Instantly, Daily digest or Off. **Slack** shows once the person's Slack account is linked, on the rows ScreenHire
  sends a Slack message for; Mentions and Tasks keep their choice on the Slack link, as the direct messages always read it.
- **Scope** (My jobs, Everything I can see) decides whose jobs New applications and Teammates' moves and notes cover: the hiring
  teams the person is on (the toasts' rule until now), or every job they see. It applies to toasts and email alike.
  Verification suggested keeps going to everyone who reviews verification and sees the job.
- **The organization's alerts are defaults.** Verification's Notifications tab is unchanged: its four switches are each
  member's starting value for the last two rows, and a person's own choice wins. `PersonNotices.Normalize` keeps only what
  differs from the person's defaults, so someone who never chose follows a later change to the organization's switches, and
  someone who did keeps theirs.
- Where it lives: `member_preferences` (`MemberPreferences`, one row per membership, a tenant table with row-level security)
  holds the email and Slack choices (`notices`, JSONB, overrides only) and the scope; the toasts stay on
  `organization_members.notifications`. Both belong to the membership, so each organization keeps its own.

`GET /v1/account/notifications` returns the rows this person sees, each with its toast kinds, the person's email choice and its
default, and their Slack choice (null for a row without one). `PUT` saves the whole grid in one transaction; a row or a toast kind
named twice is a 400, and a row the person does not see is ignored (its earlier choice stays, for a permission given back).
Session only, with antiforgery, and no permission.

### Delivery

Every notice goes through `NoticeDelivery.EmailAsync` with the person's choice: Instantly sends it now, Daily digest keeps a
`DigestItem`, Off does nothing. The handlers that pick the people:

| Row | Handler | Who |
|---|---|---|
| Mentions | `TeammateMentionedHandler` | the person mentioned, unless they wrote the note or no longer see the job |
| Candidate replies | `CandidateRepliedHandler` | the person who sent the email it answers |
| Interviews | `NotifyInterviewChanged` from the calendar handlers (booked, moved, cancelled), then `NotifyMember` | the members on the interview, never the member who cancelled it from the dashboard (bookings and moves are the candidate's); a later revision tells them again, so an out-of-date one tells nobody |
| Tasks | `NotifyMember` from `ApplicationTaskCreated` (a member or a key) and from an automation's task | the assignee, never the member who gave it to themselves |
| New applications | `NotifyNewApplication` from `ApplicationSubmitted` | the members whose scope covers the job and who do not have it Off |
| Teammates' moves and notes | `NotifyTeammateChange` from `ApplicationMoved` and `ApplicationNoteAdded` with a member as actor | as new applications, never the actor; never an API key, an automation or a candidate |
| Verification suggested, AI use and billing | `NotifyVerificationSuggested`, `NotifyAiUsage`, `EmailTrialEnding` | as before; each person's choice now decides email and Slack |

`NotifyMember` is deduplicated on `notice:{kind}:{source}:{user}`; the bell row and the digest row are unique on their own. It sends
nothing to someone who no longer sees the job or for a task done since. Instant emails use ScreenHire's own wording (`MemberNoticeEmails`),
names and titles only, never a note's, a task's or a reply's text, and never a verification level, reason or evidence.

Every notification email ends with "Change what we email you", a link to `/account/notifications` (`TeamEmail.WithFooter`): the
rows above, the interviewer's day-before reminder, the email domain alert, the data request alert and the digest. Sign-in links
and invitations are not notifications and have none.

### The daily digest

One email per person per organization per day, at 08:00 in their time zone (UTC while they have none; the dashboard fills it from the
browser once), only when there is something in it.

- `SendDueDigests` runs every half hour (UTC cron, in the worker) and fans out `SendOrganizationDigests(organization, at)`.
  The instant is in the message, so a test picks the hour.
- `SendOrganizationDigests` picks the people with unsent items whose local time at `at` is 08:00 or later, whose
  `LastDigestOn` is before their local date, and who have an item from before that day's 08:00 (`DigestSchedule.Due`). It sends
  `SendDigest`, deduplicated on the organization, the person and the local date.
- `SendDigest` reads the person's unsent items from before the cutoff, leaves out what they no longer see and what is gone (an
  erased candidate), and what belongs to a row they no longer see or have since turned Off (their permissions and choices as
  they are at send time, not when the item was queued), marks every item it read sent, and sends one email grouped by row, each line a link to its record. With
  nothing left it sends nothing. Something that arrives at 10:00 waits for the next morning, so a day that starts empty never gets
  a "daily" email in the afternoon.
- `digest_items` holds ids and a word for what happened (`Moved`, `Booked`), never a name or text: the lines are written when the
  digest is sent. The daily notification purge deletes items past the retention, sent or not. Erasing an application, a
  candidate's or by record retention, deletes its digest items and the bell rows that link to it (`CandidateErasure`). Its row-level security is the
  organization's, as for `notifications`, and every read narrows to the person. Because it names a job and an application it also has
  a restrictive `job_scope` policy (ADR-0015, written in the migration: both ids are optional, so it is not an `IApplicationScoped`
  entity), which the worker, acting for no user, passes.

## Storage and reading

`notifications` is a tenant table (`ITenantOwned`) with row-level security on the organization. The policy is the
organization's, not the person's, because the worker raises and clears rows for people it does not act as. Every API read and
write is narrowed to the signed-in user, and another person's id is a 404.

| Route | What it does |
|---|---|
| `GET /v1/notifications` | The person's alerts that are not dismissed, newest first, at most 50, with `unreadCount` for all unread ones |
| `POST /v1/notifications/{id}/read` | Marks one read. Repeating it is fine |
| `POST /v1/notifications/read-all` | Marks every unread alert read |
| `POST /v1/notifications/{id}/dismiss` | Hides one for this person. It counts as read |

The routes are session only (an API key is no person) and need no permission: they change nothing but the caller's own rows.

## Live refresh

`NotificationsChanged` carries only the organization id. The push reaches every member of the organization, so it names no
person and no words; each open dashboard refetches its own list (`invalidations` maps it to `/v1/notifications`). The list is
the source of truth: a missed push costs nothing because the bell reads the list again on reconnect.

## Retention

`ScreenHire:Notifications:RetentionDays` (default 30) is how long a row is kept after it was raised, read or not. A daily
`PurgeExpiredNotifications` (04:32 UTC) fans out one `PurgeOrganizationExpiredNotifications` per organization. Nothing depends
on a row outliving its purge: the sending domain keeps its own state (below), and a domain that is still failing raises its alert
again on the next daily look, so a purged alert comes back unread within a day. The one exception is the trial kinds
(`NotificationKinds.KeptUntilCleared`): a trial that ended with no plan is still true a month later and nothing raises it again,
so the purge skips them and the code that raises them clears them when a plan starts.

## The Jobs overview

Four numbers sit above the jobs table where the setup panel was, each a link to the list it counts. They are worked out in the
browser from the applications and jobs lists, with the same predicates as the views they open (`saved-views.ts`), so a tile and
its list cannot disagree. "This week" is a rolling seven days.

| Tile | Counts | Opens |
|---|---|---|
| New this week | applications submitted in the last 7 days | `/candidates?view=new-this-week` |
| Not screened | applications in the first stage that nobody kept in mind | `/candidates?view=not-screened` |
| Interviews this week | applications with a booked interview starting within 7 days (`nextInterviewAt` on the application summary) | `/candidates?view=interviews-this-week` |
| Open jobs | open jobs, as "3 of 5 on your plan" against the plan's limit for whoever may manage billing (the limit comes from the billing route, which only they may read; "on your plan" drops under the number in a narrow tile). With no limit, or for anyone else, the count alone | `/settings/billing` with a limit shown, otherwise `/?status=Open` |

An organization with no jobs yet sees no overview: four zeros say nothing to a new team, so the numbers start with the first job and
the empty state says what to do first.

The two new views are not in the sidebar. Not screened is the count of applications waiting for a first look; the other
"review" count, To review, depends on a person's verification access, so it is not used here. A tile whose number cannot load is
left out, never shown as zero.

## The daily email domain check

Once a day (05:47 UTC) `RecheckSendingDomains` looks at every organization's sending domain, only where the installation uses real
DNS (the sandbox's DNS lives in the API process, which the worker cannot see, so sandbox domains are not rechecked on their own).
One failed lookup must not change anything, so the first failing look saves nothing:

| Domain state | Probe | What happens |
|---|---|---|
| Verified | passes | Saved, so Last checked moves |
| Verified | fails, daily | Nothing saved. One confirming look is scheduled `ScreenHire:SendingDomainRecheck:ConfirmDelay` later (1 hour) |
| Verified | fails, confirming | Saved: the domain falls back to Pending and mail to ScreenHire's address (and History records it, as for any failed check). `VerificationLostAt` is set, the Owners get a `sending-domain-failing` alert and one email |
| Lost (`VerificationLostAt` set) | passes | Saved as Verified, `VerificationLostAt` cleared, the alert cleared |
| Lost | fails | Saved, and the alert raised again: nothing changes for an Owner who has it, while an Owner whose copy was purged, or who joined since, gets it. No second email |
| Pending, never verified | any | Saved, so a domain waiting for DNS becomes Verified when its records appear. It never raises anything |

`SendingDomain.VerificationLostAt` is the state, not the notification rows, so dismissing or purging an alert cannot stop the
check from noticing the recovery. A manual Check records that succeeds clears it and the alert in the same save. A manual Check
records that finds a verified domain failing loses it at once, as the second look would: without that, the domain would fall back to
Pending and the daily check would take it for one still being set up. The handler is `[Deduplicated]` on the organization, the day
and which look it is.

Both ways of losing a domain go through `SendingDomainAlerts.LoseAsync`: it sets the mark and returns the alert and one
`EmailSendingDomainFailing` per Owner, which the caller sends in the same save. The email is ScreenHire's own wording (domain,
organization name, a link), sent to the members who hold `ManageOwners`. Each is its own message, `[Deduplicated]` on the domain,
the day and the Owner, so a failing send retries alone and never rolls back the mark or the alert. It sends nothing when the domain
recovered before it ran. A host with no email sender or no dashboard address raises the alert and logs that nobody was emailed.

Once the domain is verified and every record is found, the Email domain page shows one line (every record is in place, and ScreenHire
checks again every day) with the records behind a Show records button, and the last-checked time. The instructions for adding
records and the records themselves show openly only until then.

## Configuration

| Setting | Default | Meaning |
|---|---|---|
| `ScreenHire:Notifications:RetentionDays` | 30 | Days a notification, and a digest item, is kept |
| `ScreenHire:SendingDomainRecheck:ConfirmDelay` | 1 hour | Time between a failing daily look and the confirming one |

Both are forwarded to the API and the worker by the AppHost.

## Tests

- Unit: the `Notification` rules, `VerificationLostAt`, the recheck decision table, and a probe that saves nothing.
- Integration: the setup choices (`OrganizationSetupTests`): a skip and a hide hold for another Admin, Show setup again clears
  both, skips sent at the same time all stay, only Edit settings changes them, an unknown step is refused, and another
  organization is untouched.
- Unit: `NoticeChoicesTests` (each row's default, the organization's alerts as the start, a person's choice winning, what a save
  keeps, the footer) and `DigestTests` (when a digest is due in a zone, its lines and its email).
- Integration: raising, reading, dismissing and clearing through the real worker queue, per-person isolation, row-level security,
  the live event, retention, the schedules, and the daily check on a real Wolverine host with the sandbox zone as fake DNS.
  `AccountNotificationsTests` (defaults from the organization, a choice that survives a change to it, each membership its own),
  `DigestEmailTests` (a digest with a mention and a task, sent once a day and never empty, a 10:00 item waiting for the next
  morning, Instantly and Off, scope).
- Vitest: the checklist's skipped steps, the bell's Not needed and Hide setup for everyone, Settings, General's Show setup
  again, the Jobs tiles, the new views, the lines and the Email domain page. The account pages: the notification grid (rows, the Slack column, the dash cells, one save), Profile, Sign-in and
  security, Preferences and the shrunk menu.
- End to end: a new Owner's bell opens by itself once at the setup steps after onboarding, the Jobs empty state points to them
  and its button opens them again; a step marked Not needed is gone, and a hidden checklist hidden, for the same Owner in
  another browser.

## Not built

- Choices for the email domain and data request alerts: they are duties for the people who hold them.
- Toasts for interviews and questionnaires, and a bell tab of the workspace's activity (the new applications and teammates'
  changes the bell does not keep): the next task on the board.
- A history page. The bell lists what needs attention, not what happened.
