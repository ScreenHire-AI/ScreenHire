---
type: Design
title: "Interview scheduling on team calendars"
description: "Members connect their own Google, Outlook or sandbox calendar from the account menu: the OAuth round trip kept apart from sign-in, tokens under Data Protection for each member, the ICalendarProvider seam with a Postgres-backed sandbox, token refresh under an advisory lock, and the configuration that hides a provider until its client id and secret are set. Interview requests: the slot finder in the interview's time zone, the offered times, and the booking transaction that never double-books an interviewer. Offering, booking, moving and cancelling an interview: the staff and candidate routes with their problem codes, and the worker that writes one calendar event per interviewer and emails the candidate. The careers page where the candidate picks, moves or cancels a time with JavaScript off. The recruiter's side in the dashboard: the Schedule interview sheet on the candidate's page, the booked time in the page's header and the Interview panel on Overview."
tags: [scheduling, calendar, integrations, oauth]
sources:
  - resource: src/ScreenHire.Domain/Scheduling/CalendarConnection.cs
  - resource: src/ScreenHire.Domain/Scheduling/CalendarSandbox.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarOptions.cs
  - resource: src/ScreenHire.Application/Scheduling/ICalendarProvider.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarTokens.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarAccess.cs
  - resource: src/ScreenHire.Application/Scheduling/SandboxCalendarProvider.cs
  - resource: src/ScreenHire.Application/Scheduling/RealCalendarProviders.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/CalendarConfiguration.cs
  - resource: apps/api/Endpoints/CalendarEndpoints.cs
  - resource: apps/api/Contracts/CalendarContracts.cs
  - resource: src/ScreenHire.Domain/Scheduling/InterviewRequest.cs
  - resource: src/ScreenHire.Domain/Scheduling/SlotFinder.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewAvailability.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewBooking.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewLinks.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewMessages.cs
  - resource: apps/api/Endpoints/InterviewEndpoints.cs
  - resource: apps/api/Contracts/InterviewContracts.cs
  - resource: apps/api/Endpoints/PublicInterviewEndpoints.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewHandlers.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewText.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewLocks.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewJourneyTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewBookingTests.cs
  - resource: apps/dashboard/src/lib/components/calendar-account-sheet.svelte
  - resource: apps/dashboard/src/lib/calendar.ts
  - resource: apps/dashboard/src/lib/schedule-interview.ts
  - resource: apps/dashboard/src/lib/components/schedule-interview-sheet.svelte
  - resource: apps/dashboard/src/lib/components/candidate/interview-panel.svelte
  - resource: apps/dashboard/src/lib/activity.ts
  - resource: tests/ScreenHire.IntegrationTests/Calendar/CalendarConnectionTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Calendar/CalendarSandboxTests.cs
  - resource: apps/careers/src/pages/[organization]/interviews/[token].astro
  - resource: apps/careers/src/lib/interview.ts
  - resource: tests/ScreenHire.E2E/InterviewBookingTests.cs
  - resource: tests/ScreenHire.E2E/ScheduleInterviewTests.cs
---
# Interview scheduling on team calendars

ScreenHire schedules interviews on the calendars the team already uses, through the free Google Calendar and Microsoft
Graph APIs. This page describes the parts that exist so far: a member connecting and disconnecting their own calendar, the interview
request with its slot finder and booking transaction, the routes that offer, book, move and cancel an interview, the
worker that writes the events and emails the candidate, the careers page the candidate books on, and the recruiter's side
in the dashboard. The workflow action builds on them and is described here as it lands.

## Connecting a calendar

Any member can connect a calendar from the Calendar item in the account menu. No permission is needed: each route
changes only the caller's own row. The menu item shows only when this ScreenHire has a calendar to connect.

| Route | What it does |
|---|---|
| `GET /v1/calendar` | The providers available here, whether the sandbox is one, and the caller's own connection (provider, account address, any problem). Never a token. |
| `POST /v1/calendar/connect` | `{provider, returnUrl}` gives `{authorizeUrl}` and sets the nonce cookie. A provider that is not available is a 404. |
| `GET /v1/calendar/oauth/callback/{provider}` | Where the provider sends the browser back. Anonymous, so a session that ended reaches sign-in; everything it does checks the state against the signed-in person. |
| `DELETE /v1/calendar` | Revokes the token at the provider on a best-effort basis, then deletes the row. |

The round trip is written by hand, not with ASP.NET Core's OAuth handler, for the reason the Slack flow gives: that
handler signs a person in, while this stores tokens for later calls. Sign-in's own OIDC flow is untouched. The `state` is
Data Protection under its own purpose, limited to ten minutes, and carries the provider, the organization, the member and
a nonce. The nonce also sits in an HttpOnly, Lax cookie whose path is `/v1/calendar/oauth/callback`, so a state copied out
of one browser does not work in another. The callback redirects to the dashboard with `?calendar=connected`, `denied`,
`expired` or `failed`, and the account menu opens the sheet to show how it went.

The redirect URI is built like Slack's, `ScreenHire:Domains` plus the callback path, so it sits on the dashboard origin,
which proxies `/v1`.

## Tokens

`CalendarTokens` protects the access and refresh tokens with Data Protection under the purpose
`ScreenHire.Calendar.Token.v1` and the organization's and the member's ids, so a token copied into another member's row
does not read. A token that no longer reads (the key ring lost its key, or the row was changed) marks the connection as
needing a reconnect. The row holds secrets, so change history never reads it.

`CalendarAccess.GetSessionAsync(userId)` is how later work gets a token. It returns the member's provider and an access
token that is valid now, or null when the member has no calendar, the provider is not configured here, or the connection
needs reconnecting. A token within two minutes of expiry is refreshed first, under a per-connection Postgres advisory
lock, because Microsoft rotates the refresh token and two refreshes at once would burn one. A refresh the provider
refuses for good records the problem, so the sheet tells the member to connect again. A provider that is only down throws,
so a handler's retry policy runs.

## Providers and configuration

Everything goes through `ICalendarProvider`: the authorize URL, the code and refresh exchanges, revoke, free/busy, and
creating, moving and cancelling an event on the member's own calendar. `SandboxCalendarProvider` is the local stand-in.
`GoogleCalendarProvider` and `MicrosoftCalendarProvider` are stubs until the real providers land.

`ScreenHire:Calendar` (`CalendarOptions`), forwarded by the AppHost to the API and the worker:

| Setting | Meaning |
|---|---|
| `Google:ClientId`, `Microsoft:ClientId` | The OAuth app's id. Not a secret. |
| `Google:ClientSecret`, `Microsoft:ClientSecret` | From the `google-calendar-client-secret` and `microsoft-calendar-client-secret` parameters, created only when the matching client id is set. |
| `Sandbox:Enabled` | The local sandbox calendar. Development and tests only: `CalendarSandboxGate` stops a host from starting with it anywhere else. |
| `Interviews:MinNoticeHours`, `Interviews:StepMinutes` | How much notice a slot needs and the step between offered start times (24 and 30). |

A provider is available only when both its client id and its secret are set, because an id without a secret cannot work.
A provider that is not available is hidden, never an error.

## The sandbox calendar

The sandbox needs no Google or Microsoft account. Its consent page is the API's own `GET /v1/calendar/sandbox/authorize`,
which sends the browser straight back to the callback with the code `sandbox-connect.{userId}`. The code must name the
signed-in person, so a swapped id connects nothing. The page refuses any `redirect_uri` that is not our callback.

A member's busy time is their rows in `calendar_sandbox_busy` plus their live rows in `calendar_sandbox_events`.
`POST /v1/calendar/sandbox/busy` adds a busy block for the caller, so a test can take a slot away. Events the sandbox
creates are idempotent on the caller's id, and their meeting link is on `meet.example.org`, a reserved host. Both routes
exist only while the sandbox is on, and neither is in the API's description.

## Interview requests and booking

An `InterviewRequest` is one interview offered to one application's candidate: the interviewers (`InterviewAttendee` rows,
the first is the organizer), a length (15, 30, 45, 60, 90 or 120 minutes), a window of at most 90 days, working hours
(09:00 to 17:00 by default, weekdays only) in an IANA time zone, and a status of Open, Booked, Cancelled or Expired. There
is at most one Open or Booked request per application, held by a partial unique index. An Open request past its window
reads as Expired without a timer (`EffectiveStatus`); whoever next touches it stores that (`MarkExpired`), because the
index sees the stored status. `Revision` counts bookings, moves and cancellations, and the idempotency keys of the
calendar messages and the provider event ids derive from it.

`SlotFinder` is pure. It walks local days in the interview's zone, so daylight saving time is right: a local time that
does not exist is skipped, a local time that happens twice means its first occurrence, and start times are `dayStart + k *
step` in local wall-clock time. Busy time is half-open, so a meeting that ends at 10:00 leaves 10:00 free.

`InterviewAvailability` gives each interviewer's busy time from their calendar (free/busy, one interviewer at a time) plus
the interviews ScreenHire has booked for them, and offers the times where all of them are free, at least
`ScreenHire:Calendar:Interviews:MinNoticeHours` from now, on the `StepMinutes` grid. An interviewer with no usable calendar
is reported by user id and nothing is offered, because times offered without their calendar could double-book them.

`InterviewBooking` books or moves an interview. It recomputes the offered times and checks the chosen start is one of them,
then takes `pg_advisory_xact_lock` per interviewer in sorted order and checks the interviews already confirmed in the
database before it changes the request and marks every attendee Pending. A `btree_gist` exclusion constraint would do the
last check in the database but needs extension privileges, so the lock plus the overlap query is the guard. Outcomes: Done,
SlotTaken (not offered, or another booking got it first), NotOpen, TooLate (the candidate cannot move an interview that
starts inside the minimum notice), NoCalendar and NotFound. It runs for the whole organization (the careers endpoints and
the worker), because a member on Only jobs they're on would not see an interviewer's bookings on other jobs. It publishes
nothing: the caller opens its transaction first when it needs the outbox, and publishes
`CreateInterviewEvents` or `UpdateInterviewEvents` (ids and revision only) with the outcome.

`GET /v1/interviewers?jobId=` lists the members who see the job (everyone who sees all jobs, and its hiring team), each with
`calendarConnected` and `calendarProvider`. It needs the permission that emails candidates, since scheduling emails the
candidate. A job the caller cannot see is a 404.

`InterviewLinks` signs the careers page link (`/interviews/{token}`) with Data Protection under the purpose
`ScreenHire.Interview.Link.v1`. One token serves the whole life of the request, valid until the window ends plus 30 days;
the request's status is what closes it.

## Offering an interview

All three routes need a signed-in person, never an API key, and take the permission that emails candidates. A host with no
email sender or no careers site address (`ScreenHire:Domains`) answers 404, as the talent pool invitation does, because
the candidate could not be sent the link.

| Route | What it does |
|---|---|
| `POST /v1/applications/{applicationId}/interviews` | `{interviewerUserIds, durationMinutes, windowStart, windowEnd, timeZone, dayStart?, dayEnd?}` creates the request and its attendees, adds an `InterviewInvited` timeline entry and queues `SendInterviewInvitation`. 201 with the interview. 400 names the field that is wrong, 409 when an Open (not past its window) or Booked interview exists, 422 names the interviewers with no working calendar (`interviewerUserIds` on the problem). |
| `GET /v1/applications/{applicationId}/interviews` | Every interview of the application, newest first, with status, time, window, zone, length, meeting link and each interviewer's sync status. Visible to anyone who sees the application. |
| `POST /v1/interviews/{interviewId}/cancel` | Cancels an Open or Booked interview. A booked one queues `CancelInterviewEvents`. 409 when it is already cancelled or has expired. |

These routes run for a member who may see only some jobs, so they never call `InterviewAvailability` or `InterviewBooking`
(those refuse such a context). The interviewers must be members who see the job, the first is the organizer, and an
Open request past its window is stored as Expired and saved before the new row is inserted, because the partial unique
index sees the stored status. A team cancellation takes the interviewers' advisory locks first, as a booking does, so a
cancellation and a booking at the same moment cannot both win.

`status` is the effective status: an Open request past its window reads `Expired`. Removing a member also marks their
attendee rows on booked interviews that have not started `Failed` with a plain problem, and they stay visible.

## The candidate's routes

`/v1/public/organizations/{organizationSlug}/interviews/{token}` is anonymous and rate limited like the apply flow. A token
for another organization, or one that is forged, damaged or past its own limit, is a 404.

| Route | What it does |
|---|---|
| `GET ?tz=` | State (`Open`, `Booked`, `Cancelled`, `Expired`), employer, job title, the booked time, the zone, the length, the offered times as UTC instants (for an Open interview, and for a Booked one the candidate can still change), `canChange` and `minNoticeHours`. `slotsUnavailable` is true when a calendar cannot be read. It names no interviewer. |
| `POST /book` `{start}` | Open to Booked. 204. |
| `POST /reschedule` `{start}` | Moves a Booked interview. 204. |
| `POST /cancel` | Cancels an Open or Booked interview. 204. |

A change that cannot happen is a problem whose `type` (also its `code` member) says why: 410 `cancelled` or `expired`; 409
`already_booked`, `not_booked`, `slot_taken` (the time is not offered, or another booking got it first) and `too_late`
(the interview starts inside the minimum notice); 503 `calendar_unavailable`. Booking and moving run in one transaction
that holds the interviewers' locks and the outbox: the request and attendees, the timeline entry
(`InterviewBooked` or `InterviewRescheduled`, no actor), `CreateInterviewEvents` or `UpdateInterviewEvents` with the
revision, and `InterviewChanged` commit together or not at all. A first booking also raises the `InterviewScheduled`
automation trigger (`RunAutomations`, keyed on the interview), which no automation can use until the validator accepts it.

## The worker

`SendInterviewInvitation` issues the link in the worker, sends `interview-invitation` and keeps a copy that hides the link.
The timeline entry of the email has the send's id, so a redelivery sends nothing twice.

`CreateInterviewEvents`, `UpdateInterviewEvents` and `CancelInterviewEvents` write one event per interviewer on their own
calendar, so it lands natively whichever provider each uses. The organizer's event invites the candidate by address and
asks for a meeting link; the other copies carry that link in their text and invite nobody. The title is
`Interview: {candidate} - {job}` and the body holds the job, the candidate and a link to the application, nothing else.
Event ids derive from the interview and the member, so creating twice finds the first. A message does nothing when the
request has a newer revision or is no longer in the state the message is about (`InterviewText.IsCurrent`). A member with
no usable calendar, or a calendar that refuses the event for good, is marked `Failed` with a short problem and the others
go on; a calendar that is only down throws, so Wolverine retries and the events already made are found again. Then the
candidate gets `interview-confirmation` (or `interview-cancelled`), whose timeline entry has an id derived from the interview
and revision, and `InterviewChanged` tells open dashboards.

## The candidate's page

The invitation and confirmation emails link to `/interviews/{token}` on the careers site ([apply flow](apply-flow.md), §8.1),
which calls the public routes above. It is plain HTML with no script: the candidate picks a time zone with a GET form and
a time with a radio, and the careers server writes every time with `Intl.DateTimeFormat` in the chosen zone. The API
offers instants and the zone; it never sends formatted text. Booking, moving and cancelling are Astro actions that
redirect back to the same address with an outcome word (`booked`, `rescheduled`, `cancelled`, `slot-taken`, `too-late`).
A conflict maps to its notice by the problem's `type`. The page shows one screen of five days that have times and links to the
next, shows no meeting link of its own (the calendar invitation has it), names no interviewer, and says nothing about
verification. The browser test (`InterviewBookingTests`) runs the whole journey with JavaScript off, axe clean, and checks
the page at 390 and 1280 pixels wide for sideways scrolling and touch targets.

## The dashboard

A recruiter offers an interview from the candidate's page, and the page then shows it in two places (design system
ADR-0017: a record's facts sit in its header, and Overview is a grid of panels).

**Schedule interview** is a button beside Email in the header band, and an item in the More menu on a phone. It shows only
where Email does (the person may email candidates and this deployment can send), and it is hidden while the application has
ended, while an interview is open or booked (the API would refuse both) and until the application's interviews have loaded.
It opens `ScheduleInterviewSheet`, which lists the members who see the job from `GET /v1/interviewers`:

- A member with no calendar is listed but cannot be ticked, and says "No calendar connected" in words. The person scheduling
  starts ticked when their own calendar is connected. The first person ticked is the organizer, in the order of ticking, and
  the sheet says whose calendar sends the invitation.
- When nobody can be ticked, the sheet shows one line and a Connect your calendar button instead of the form. The button
  closes the sheet and opens the Calendar sheet of the account menu through `shell.openCalendar()`.
- Length is one of the six the API allows. The first and last day are the person's own days in the zone they pick, which
  starts as the browser's zone, and the working hours default to 9:00 to 17:00 on weekdays. `lib/schedule-interview.ts` turns
  the days into the instants the API takes: the start of the first day to the start of the day after the last, in the chosen
  zone whatever zone the browser is in (the offset is read twice, so a clock change in the window is right). A window longer
  than 90 days is refused in the form; a 90-day window that crosses the clock change that ends daylight saving time is an
  hour over the API's 90 days of elapsed time, so its end is held to 90 days.
- The form checks the same things the API does and names the same fields, so a 400 lands under the field it is about. A 422
  becomes one sentence naming the interviewers whose calendars do not work, under the list. A 409 shows the API's own words
  (an interview is already open or booked, or the application has ended), and a 404 says scheduling is not set up here.

**The header** gets one fact, "Booked for" and the time in the reader's own zone, only while an interview is booked. The
time is said nowhere else on the screen except Activity, which records the time each entry was about.

**The Interview panel** sits under Next steps on Overview and is not drawn while no interview was ever offered. An open
interview says it is waiting for the candidate and gives the days they can choose from, in the interview's zone. A booked
one lists the interviewers with the organizer marked in words, the meeting link, and a note under an interviewer whose
calendar event is still pending or failed (the failure's own words, such as "Their calendar refused the event"). A cancelled or expired
one is one line saying how it ended. Cancel is a destructive item in the panel's menu behind a dialog, for people who may
email candidates, because it tells the candidate and removes the event from every calendar.

Activity lists the four interview entries under an Interviews filter. A booking or a move says the time it was about, so a
move is history the header no longer shows.

## Tenancy

`calendar_connections`, `calendar_sandbox_busy`, `calendar_sandbox_events`, `interview_requests` and `interview_attendees`
are tenant-owned with forced row-level security, like every other tenant table. The last two sit under an application,
so they also carry the restrictive `job_scope` policy (ADR-0015). A connection is unique per organization and member. Removing a member from the
organization deletes their connection.
