---
type: Design
title: "Notifications"
description: "The bell in the top bar: setup steps and per-person alerts, how any handler raises or clears one through RaiseNotification, the audience by permission, the unique row per person, live refresh, retention, the Jobs overview numbers, and the daily email domain check that raises the first alert."
tags: [notifications, dashboard, wolverine, tenancy, email]
sources:
  - resource: src/ScreenHire.Domain/Notifications/Notification.cs
  - resource: src/ScreenHire.Domain/Organizations/SendingDomain.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs
  - resource: src/ScreenHire.Application/Notifications/Notifications.cs
  - resource: src/ScreenHire.Application/Billing/TrialReminderHandlers.cs
  - resource: src/ScreenHire.Application/SendingDomains/RecheckSendingDomain.cs
  - resource: src/ScreenHire.Application/SendingDomains/SendingDomainService.cs
  - resource: src/ScreenHire.Application/Messaging/ScreenHireMessaging.cs
  - resource: apps/api/Endpoints/NotificationEndpoints.cs
  - resource: apps/api/Contracts/NotificationItemContracts.cs
  - resource: apps/api/Endpoints/SendingDomainEndpoints.cs
  - resource: apps/dashboard/src/lib/components/notification-bell.svelte
  - resource: apps/dashboard/src/lib/notification-lines.ts
  - resource: apps/dashboard/src/lib/setup-progress.svelte.ts
  - resource: apps/dashboard/src/lib/setup-checklist.ts
  - resource: apps/dashboard/src/lib/components/jobs-overview.svelte
  - resource: apps/dashboard/src/lib/components/overview-stat.svelte
  - resource: apps/dashboard/src/lib/saved-views.ts
  - resource: apps/dashboard/src/routes/(app)/settings/sending-domain/+page.svelte
---

# Notifications

A bell in the top bar holds what needs one person's attention. It has two parts:

- **Setup**, for whoever can do the steps (Owners and Admins): the five steps a new organization takes, each linking to the
  page where it is done. It is not stored. The dashboard works it out from the organization's own data, as it always did, and
  shows only the steps left, with the done ones folded into one line ("2 steps done"). It goes by itself once the last step is
  done, and Hiding setup (an icon button in the panel) hides it in this browser for the organization.
- **Alerts**, stored on the server, one row per person: a plain line that links to the page that fixes it. They are read,
  dismissed and pushed live per person.

Setup used to sit on top of Jobs. A team can use ScreenHire fully without a careers site or an email domain, so it no longer
takes over the page where people look at their jobs. The bell's count is the setup steps left plus the alerts not yet read.
Opening the panel marks nothing read; following an alert's link does.

The panel is called Needs attention, because Notifications already names the toast settings in the account menu and the
toast tray. The bell holds no team news: that stays in the tray (the realtime doc).

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

The two trial kinds are one story: `trial-ending` appears 7 days before the trial ends and is replaced by `trial-ended` when it
ends, and a plan starting clears both ([billing](billing.md#trial-reminders)). Their line says the days left, which the dashboard
works out from `endsAt` when it draws the line, so one row stays right as the days pass. Tasks that add kinds later (AI usage
alerts) add their rows here.

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
| Open jobs | open jobs, as "3 of 5" against the plan's limit for whoever may manage billing | `/?status=Open` |

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
| `ScreenHire:Notifications:RetentionDays` | 30 | Days a notification is kept |
| `ScreenHire:SendingDomainRecheck:ConfirmDelay` | 1 hour | Time between a failing daily look and the confirming one |

Both are forwarded to the API and the worker by the AppHost.

## Tests

- Unit: the `Notification` rules, `VerificationLostAt`, the recheck decision table, and a probe that saves nothing.
- Integration: raising, reading, dismissing and clearing through the real worker queue, per-person isolation, row-level security,
  the live event, retention, the schedules, and the daily check on a real Wolverine host with the sandbox zone as fake DNS.
- Vitest: the bell, the Jobs tiles, the new views, the lines and the Email domain page.
- End to end: a new Owner sees setup in the bell and not on Jobs.

## Not built

- Email or Slack copies of alerts other than the email domain one. A kind that wants them sends them itself.
- Notification settings per kind. Every alert of a kind goes to everyone its audience names.
- A history page. The bell lists what needs attention, not what happened.
