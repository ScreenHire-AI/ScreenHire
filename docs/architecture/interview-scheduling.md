---
type: Design
title: "Interview scheduling on team calendars"
description: "Members connect their own Google, Outlook or sandbox calendar from the account menu: the OAuth round trip kept apart from sign-in, tokens under Data Protection for each member, the ICalendarProvider seam with a Postgres-backed sandbox, token refresh under an advisory lock, and the configuration that hides a provider until its client id and secret are set. Interview requests: the slot finder in the interview's time zone, the offered times, and the booking transaction that never double-books an interviewer. Offering, booking, moving and cancelling an interview: the staff and candidate routes with their problem codes, and the worker that writes one calendar event per interviewer and emails the candidate. The careers page where the candidate picks, moves or cancels a time with JavaScript off. The recruiter's side in the dashboard: the Schedule interview sheet on the candidate's page, the booked time in the page's header and the Interview panel on Overview. Reminders to the candidate and the interviewers a day before a booked interview, and to the candidate an hour before. Moving a booked interview: the team offers new days, the booked time stays held until the candidate picks one, and the events in every calendar then move."
tags: [scheduling, calendar, integrations, oauth]
sources:
  - resource: src/ScreenHire.Domain/Scheduling/CalendarConnection.cs
  - resource: src/ScreenHire.Domain/Scheduling/CalendarSandbox.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarOptions.cs
  - resource: src/ScreenHire.Application/Scheduling/ICalendarProvider.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarTokens.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarAccess.cs
  - resource: src/ScreenHire.Application/Scheduling/SandboxCalendarProvider.cs
  - resource: src/ScreenHire.Application/Scheduling/CalendarHttp.cs
  - resource: src/ScreenHire.Application/Scheduling/GoogleCalendarProvider.cs
  - resource: src/ScreenHire.Application/Scheduling/MicrosoftCalendarProvider.cs
  - resource: tests/ScreenHire.UnitTests/Scheduling/GoogleCalendarProviderTests.cs
  - resource: tests/ScreenHire.UnitTests/Scheduling/MicrosoftCalendarProviderTests.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/CalendarConfiguration.cs
  - resource: apps/api/Endpoints/CalendarEndpoints.cs
  - resource: apps/api/Contracts/CalendarContracts.cs
  - resource: src/ScreenHire.Domain/Scheduling/InterviewRequest.cs
  - resource: src/ScreenHire.Domain/Scheduling/SlotFinder.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewAvailability.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewBooking.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewLinks.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewMessages.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewLifecycleHandlers.cs
  - resource: src/ScreenHire.Application/Applications/ApplicationMoved.cs
  - resource: apps/api/Endpoints/InterviewEndpoints.cs
  - resource: apps/api/Hiring/InterviewerReferences.cs
  - resource: src/ScreenHire.Application/Automations/ExecuteAutomationActionHandler.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewWorkflowActionTests.cs
  - resource: apps/api/Contracts/InterviewContracts.cs
  - resource: apps/api/Endpoints/PublicInterviewEndpoints.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewHandlers.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewReminderHandlers.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewText.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewLocks.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewJourneyTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewBookingTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewReminderTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Scheduling/InterviewNewTimesTests.cs
  - resource: apps/dashboard/src/lib/components/calendar-account-sheet.svelte
  - resource: apps/dashboard/src/lib/calendar.ts
  - resource: apps/dashboard/src/lib/schedule-interview.ts
  - resource: apps/dashboard/src/lib/components/schedule-interview-sheet.svelte
  - resource: apps/dashboard/src/lib/components/offer-new-times-sheet.svelte
  - resource: apps/dashboard/src/lib/components/candidate/interview-panel.svelte
  - resource: apps/dashboard/src/lib/components/candidate/interview-details.svelte
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
Graph APIs ([ADR-0024](../adr/0024-interview-scheduling-on-calendar-apis.md)). This page describes a member connecting
and disconnecting their own calendar, the interview request with its slot finder and booking transaction, the routes that
offer, book, move and cancel an interview, the team offering new times for a booked interview, the workflow action that
offers one without a person, the worker that writes the events, emails the candidate and sends the reminders, the careers
page the candidate books on, and the recruiter's side in the dashboard.

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
lock, because Microsoft rotates the refresh token and two refreshes at once would burn one. The refresh commits on a
connection of its own (`ScreenHireDbContext.CreateIndependent`), because the caller's transaction can roll back, and a
rotated token lost that way would leave the member to connect again. A refresh the provider refuses for good records the
problem, so the sheet tells the member to connect again. A provider that is only down throws, so a handler's retry policy
runs.

## Providers and configuration

Everything goes through `ICalendarProvider`: the authorize URL, the code and refresh exchanges, revoke, free/busy, and
creating, moving and cancelling an event on the member's own calendar. `SandboxCalendarProvider` is the local stand-in.
`GoogleCalendarProvider` and `MicrosoftCalendarProvider` call the real APIs; see "Google and Microsoft".

`ScreenHire:Calendar` (`CalendarOptions`), forwarded by the AppHost to the API and the worker:

| Setting | Meaning |
|---|---|
| `Google:ClientId`, `Microsoft:ClientId` | The OAuth app's id. Not a secret. |
| `Google:ClientSecret`, `Microsoft:ClientSecret` | From the `google-calendar-client-secret` and `microsoft-calendar-client-secret` parameters, created only when the matching client id is set. |
| `Sandbox:Enabled` | The local sandbox calendar. Development and tests only: `CalendarSandboxGate` stops a host from starting with it anywhere else. |
| `Interviews:MinNoticeHours`, `Interviews:StepMinutes` | How much notice a slot needs and the step between offered start times (24 and 30). |

A provider is available only when both its client id and its secret are set, because an id without a secret cannot work.
A provider that is not available is hidden, never an error.

## Google and Microsoft

Both real providers are singletons over one long-lived `HttpClient` each (`CalendarHttp`), with the timeout from
`ScreenHire:Calendar:Timeout`. They do not use the HTTP client factory: its resilience handler would retry a call, and
Wolverine's retry policy is the only retry layer. Creating an event is safe to repeat because of the ids below. Neither
provider logs a token, an address or event text, and an exception carries only a short code, never the provider's message.
`CalendarOptions.Endpoints` holds the providers' base URLs. The defaults are the real ones and only a test changes them.

| | Google | Microsoft |
|---|---|---|
| Scopes | `calendar.freebusy` and `calendar.events` (both sensitive) | `Calendars.ReadWrite` and `offline_access` |
| Authorize | `accounts.google.com/o/oauth2/v2/auth`, `access_type=offline`, `prompt=consent` | `login.microsoftonline.com/common/oauth2/v2.0/authorize`, `response_mode=query` |
| Tokens | `oauth2.googleapis.com/token`; the refresh token stays the same | `.../common/oauth2/v2.0/token`; the refresh token rotates on every refresh |
| Revoke | `oauth2.googleapis.com/revoke` | none (a no-op: Graph has no per-token revoke for delegated tokens) |
| Busy time | `POST /calendar/v3/freeBusy` on `primary` | `GET /me/calendarView` in UTC, paged through `@odata.nextLink` |
| Create | `POST calendars/primary/events` with our event id | `POST /me/events` with `transactionId` |
| Meeting link | `conferenceData.createRequest` (Meet), `conferenceDataVersion=1` | `isOnlineMeeting` with `teamsForBusiness` |
| Guest email | `sendUpdates=all` when there are guests | Graph sends it |
| Move | `PATCH events/{id}` with `sendUpdates=all` | `PATCH /me/events/{id}` |
| Cancel | `DELETE events/{id}` with `sendUpdates=all` | `POST /me/events/{id}/cancel`, else `DELETE` |
| Account address | the primary calendar's title, only if it looks like an address | `owner.address` of `GET /me/calendar` |

No identity scope is requested (`openid`, `email`, `profile`, `User.Read`): the address in the sheet is a label from the
calendar itself and is dropped when the calendar does not give one. `GoogleCalendarProviderTests` and
`MicrosoftCalendarProviderTests` assert the scope strings exactly. After a code exchange the scopes the member actually
allowed are checked (Google lets a member untick one), and a missing one fails the connect with `consent_required`. A
first connect that returns no refresh token fails with `no_refresh_token`.

**Busy time on Microsoft** uses `calendarView`, not `getSchedule`. `getSchedule` asks about addresses, so it needs the
member's own address on every call and the call site holds only the access token. `calendarView` on `/me` needs none.
`showAs` of `free` and a cancelled event are free; `busy`, `tentative`, `oof`, `workingElsewhere` and `unknown` block the
time, because a time we cannot read must not be offered. A window with more than 2000 events fails as transient rather
than offering times from a partly read calendar. A next-page link is followed only on the Graph host.

**Idempotency ids.** `EventSpec.IdempotencyId` is derived from the interview and the member
(`InterviewText.EventIdempotencyId`, 32 lowercase hex characters). Google event ids may use only `a` to `v` and `0` to `9`
and be 5 to 1024 long, so a valid id is used as it is and any other becomes the SHA-256 of it in lowercase hex. A 409 means
the event exists: it is read back and returned. A deleted Google event keeps its id as `cancelled`, so a 409 for one
patches it back to `confirmed`. Microsoft gets the id as `transactionId`. Graph's documentation says only that it avoids
redundant creates, not what a repeat answers, so a success is taken as the existing event, and a 409 or an error naming the
transaction is answered by looking in `calendarView` for the event with that `transactionId`.

**Moving and cancelling.** Moving an event that is gone (404, 410, or held as cancelled) throws a non-permanent
`event_not_found`, and the handler creates it again. Cancelling one that is gone is a no-op.

**Errors.** Every refusal becomes a `CalendarApiException` whose code is the provider's reason when it is a short token
(`[A-Za-z0-9_.-]`, 64 characters at most) and `http_<status>` otherwise.

| Answer | Permanent |
|---|---|
| 401, `invalid_grant`, `interaction_required`, `AADSTS70008` or `AADSTS700082` (expired refresh token), `consent_required` | yes: the member must connect again |
| 403 with `rateLimitExceeded`, `userRateLimitExceeded` or `quotaExceeded`; 429 (with `RetryAfter` when sent); 408; any 5xx; a timeout; no connection; a body that is not JSON | no |
| Any other 4xx (a 403 for a missing permission, a 400, a 404 on create) | yes for the call, with the reason as the code |

## Setting up the providers (owner)

Without these steps the Google and Outlook buttons stay hidden. The redirect URIs are on the dashboard origin, which proxies
`/v1` to the API, as for Slack. Set the keys as user secrets locally or as app settings in a deployment.

1. **Google.** In a Google Cloud project, configure the OAuth consent screen (external) and add the two scopes
   `calendar.freebusy` and `calendar.events`. Both are sensitive scopes, so Google must verify the app, which takes weeks.
   Create an OAuth client of type Web application with the redirect URI
   `<dashboard origin>/v1/calendar/oauth/callback/google`. Set `ScreenHire:Calendar:Google:ClientId`; the secret goes in the
   `google-calendar-client-secret` parameter (`ScreenHire:Calendar:Google:ClientSecret`).
2. **Microsoft.** In Entra, register an application that supports accounts in any organizational directory and personal
   Microsoft accounts. Add a Web redirect URI `<dashboard origin>/v1/calendar/oauth/callback/microsoft` and the delegated
   permissions `Calendars.ReadWrite` and `offline_access`. Create a client secret. Set `ScreenHire:Calendar:Microsoft:ClientId`;
   the secret goes in the `microsoft-calendar-client-secret` parameter.
3. A provider shows in the sheet only once both its id and its secret are set.

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

`WindowStart` and `WindowEnd` are the days the candidate picks from now. Offering new times for a booked interview replaces
them, so they can stop covering the time the interview was booked for. `LookupStart` and `LookupEnd` are the span of every
window the request has had, and only widen. Looking an event up by the id its create used (a create whose transaction
rolled back after the provider answered) searches that span, so it finds the event at any time the interview has had.

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

`status` is the effective status: an Open request past its window reads `Expired`.

## Offering new times for a booked interview

A booked interview moves without a cancellation. A recruiter offers new days from the Interview panel and the candidate
picks again on the link they already have.

| Route | What it does |
|---|---|
| `POST /v1/interviews/{interviewId}/new-times` | `{windowStart, windowEnd}` replaces the days the candidate picks from. 200 with the interview. 400 names `windowEnd` when the days break the request rules (they end after they start, at most 90 days, not already past). 409 when the interview is not Booked or its application has ended. 404 for another organization's interview, and when the host has no email sender or careers site address. |

Like cancelling, it is for people only, needs the permission that emails candidates, and takes the interviewers' advisory
locks before it reloads the interview, so an offer and the candidate's move at the same moment cannot both win. In one
transaction it sets `NewTimesOfferedAt`, replaces the window, adds an `InterviewNewTimesOffered` timeline entry that names
the time still held, and queues `SendInterviewNewTimes` and `InterviewChanged`. The worker emails the candidate
`interview-new-times` with a fresh link, and keeps a copy that hides it. Offering again replaces the window again and sends
another email, because each offer carries its own send id.

An offer is not a change to the booking, so it leaves `StartsAt`, `Revision`, the attendees and the calendar events alone.
`Revision` stays because the calendar messages and the provider event ids derive from it: a recruiter who offers new times
just after a booking must not turn the booking's still-queued `CreateInterviewEvents` into a no-op. The old time stays held
for the same reason a booking is: its row is still Booked, so nobody else is offered it, and its events are still on the
calendars. The interview keeps its reminders too, because they are about the booked time. If the candidate never picks, the
interview stays at that time. The end of the offered days ends the offer. Nothing is left to pick, so the page and the panel
stop saying new times wait, and the usual notice applies again (`HasPendingNewTimes`).

While an offer is pending the candidate may move or cancel the interview whatever the minimum notice, even after the old
time has passed, because the team asked for the move (`CanBeChangedByCandidate`). New days can end before the booked time,
so a link issued for a booked interview reads until the later of the two, plus the usual 30 days. The pick is the ordinary reschedule route:
the same `UpdateInterviewEvents` moves each interviewer's event, the candidate gets `interview-confirmation` for the new
time, the new time gets its own reminders, and `Reschedule` clears `NewTimesOfferedAt`. Cancelling clears it too.

## When an interview is cut short

Three changes outside the interview itself end or change it. Each is a worker message sent in the same transaction as the
change, so a crash between the two cannot leave one without the other.

- **The application ends.** A move to Hired, Rejected or Withdrawn (the move route, an automation, the MCP server) and
  Save candidate (Keep in mind) send `EndApplicationInterviews`. The handler checks the application has ended when it runs,
  so a move back in between cancels nothing, then cancels the Open or Booked interview, adds the `InterviewCancelled`
  timeline entry, removes the events and tells open dashboards. The candidate gets no `interview-cancelled` email: a
  rejection, or no message at all, is the team's word on the application, and a second notice beside it is noise. The
  calendar provider's own cancellation notice for the organizer's event still reaches a candidate who was invited to it.
- **A member leaves.** Removal deletes their connection and sends `RemoveInterviewerFromInterviews`. The handler takes them
  off every Open or Booked interview. An Open interview whose organizer left makes the next interviewer the organizer, and
  one with nobody left is cancelled. A Booked interview goes on without a leaver who was not the organizer, so it can still
  be moved. If the organizer left, or nobody is left, it is cancelled and the candidate is told, because the candidate's
  invitation is on an event ScreenHire can no longer reach, and it could not be moved later. That event stays on the
  leaver's own calendar. The handler takes the locks of all those interviews in one sorted pass, as a booking does.
- **The candidate is erased.** An event holds the candidate's name and address, and the rows that say where it is go with
  the candidate. So the erase route takes the interviewers' locks, reads each Booked interview's attendees (user, saved
  event id, time), and sends `RemoveInterviewEvents` with those ids and times only. The handler deletes each event without
  notifying guests on Google; Microsoft Graph has no switch for that, and a meeting deleted by its organizer tells its
  attendees. An event whose id was never saved (a create whose transaction rolled back after the provider answered) is
  found by the id its create used: Google's event id is derived from it, and Graph is searched by `transactionId` across the
  interview's lookup span (`LookupStart` to `LookupEnd`, every window it has had), because such an event sits at a time the
  interview may have moved from. A cancellation and a
  move use the same lookup, so a rolled-back create cannot leave an event behind or at the old time. The sandbox deletes
  the row for good.

## The workflow action

A `ScheduleInterview` automation action offers the same interview a person would, from a workflow
([workflow-engine.md](workflow-engine.md) section 3 has its row in the action table). The action holds
`interviewerUserIds` (one to five members, the first is the organizer), `interviewMinutes`, `interviewWindowDays` (1 to 60)
and `interviewTimeZone`. The organization has no time zone, so the automation form fills the zone from the editor's browser
and shows it in a select. The window runs from the moment the action executes to that many days later, with the default
working hours.

The handler (`ExecuteAutomationActionHandler`) does what the staff route does, without a person. It stores an Open request
that is past its window as Expired first, then creates the request with `SourceKey` set to the action key, the attendees
with each interviewer's calendar provider, an `InterviewInvited` timeline entry linked to the run, and queues
`SendInterviewInvitation` and `InterviewChanged` through the handler's outgoing messages, so they leave only if the action
commits. It skips when email or the careers site address is not set up, when the application has ended, when the request
for this action key exists already, or when the candidate has an Open or Booked interview. It fails, writing nothing, when
an interviewer is no longer a member, cannot see the job, or has no working calendar (the connection has a problem or its
provider is not configured here). The failure says who, in the interviewers' display names, and what to do. The booking
side is unchanged: a candidate's first booking raises the `InterviewScheduled` trigger once per interview, so an
automation can tag or notify when a time is picked.

Saving a workflow checks that each interviewer is a member of the organization. A `ScheduleInterview` automation is
refused when a verification signal or a passed time limit drives it, because offering an interview is a candidate-facing
outcome.

## The candidate's routes

`/v1/public/organizations/{organizationSlug}/interviews/{token}` is anonymous and rate limited like the apply flow. A token
for another organization, or one that is forged, damaged or past its own limit, is a 404. An Open or Booked interview whose
application has ended (rejected, withdrawn, hired or kept in mind) reads as `Cancelled` here, so a candidate who has left
the job cannot book, move or cancel.

| Route | What it does |
|---|---|
| `GET ?tz=` | State (`Open`, `Booked`, `Cancelled`, `Expired`), employer, job title, the booked time, the zone, the length, the offered times as UTC instants (for an Open interview, and for a Booked one the candidate can still change), `canChange`, `minNoticeHours` and `newTimesOffered` (the team offered new times and the booked time stays until the candidate picks one, until the offered days end). `slotsUnavailable` is true when a calendar cannot be read. It names no interviewer. |
| `POST /book` `{start}` | Open to Booked. 204. |
| `POST /reschedule` `{start}` | Moves a Booked interview. 204. |
| `POST /cancel` | Cancels an Open or Booked interview. 204. |

A change that cannot happen is a problem whose `type` (also its `code` member) says why: 410 `cancelled` or `expired`; 409
`already_booked`, `not_booked`, `slot_taken` (the time is not offered, or another booking got it first) and `too_late`
(the interview starts inside the minimum notice); 503 `calendar_unavailable`. Booking and moving run in one transaction
that holds the interviewers' locks and the outbox: the request and attendees, the timeline entry
(`InterviewBooked` or `InterviewRescheduled`, no actor), `CreateInterviewEvents` or `UpdateInterviewEvents` with the
revision, and `InterviewChanged` commit together or not at all. A first booking also raises the `InterviewScheduled`
automation trigger (`RunAutomations`, keyed on the interview), so an automation can tag or notify when a time is picked.

## The worker

`SendInterviewInvitation` issues the link in the worker, sends `interview-invitation` and keeps a copy that hides the link.
The timeline entry of the email has the send's id, so a redelivery sends nothing twice. `SendInterviewNewTimes` does the same
with `interview-new-times`, and sends nothing once the interview is no longer Booked with an offer pending. Like every email about
an application, the invitation, the confirmation, the new times, the reminder and the cancellation go out with a reply address (so
an emailed answer lands in Messages) and end with the link to the candidate's own page ([email](email.md),
[candidate portal](candidate-portal.md)).

`CreateInterviewEvents`, `UpdateInterviewEvents` and `CancelInterviewEvents` write one event per interviewer on their own
calendar, so it lands natively whichever provider each uses. The organizer's event invites the candidate by address and
asks for a meeting link; the other copies carry that link in their text and invite nobody. The title is
`Interview: {candidate} - {job}` and the body holds the job, the candidate and a link to the application, nothing else.
Event ids derive from the interview and the member, so creating twice finds the first. A find does not change the event's
time, so a move with no saved id looks the event up first and moves it, and creates one only when there is none. A message does nothing when the
request has a newer revision or is no longer in the state the message is about (`InterviewText.IsCurrent`). A member with
no usable calendar, or a calendar that refuses the event for good, is marked `Failed` with a short problem and the others
go on; a calendar that is only down throws, so Wolverine retries and the events already made are found again. Then the
candidate gets `interview-confirmation` (or `interview-cancelled`), whose timeline entry has an id derived from the interview
and revision, and `InterviewChanged` tells open dashboards.

These handlers take the interviewers' locks before they read the request and hold them through the calendar calls, until
the handler's transaction commits. `ScreenHire:Calendar:Timeout` (10 seconds) bounds each call, so a provider that hangs
holds an interviewer's bookings for seconds, not for good. A cancellation or a move takes the same locks, so it cannot slip in while events are
being written: it waits, then finds the event ids it needs to remove or move.

## Reminders

`CreateInterviewEvents` and `UpdateInterviewEvents` each schedule two `SendInterviewReminder` messages in their outgoing
messages, so they leave only if the events are written: `DayBefore` 24 hours before the start, and `HourBefore` one hour
before it. A reminder whose time is already past is not scheduled, because it would say a day about an interview that is
sooner and the confirmation has just given the time. An interview booked before reminders existed has none.

A reminder is about a start time, not a revision. Its idempotency key is the interview, the start instant and the kind. The
handler loads the interview when the schedule releases the message and sends only while it is still Booked at that start,
the start is still ahead and the application has not ended. A move (a new start), a cancellation (a new status) and an
application that ended therefore make an old reminder harmless, an offer of new times (which changes neither) does not,
and a move back to the same time is the same reminder.

- **The candidate** gets `interview-reminder` for both kinds: the time, the meeting link when there is one and a fresh link to
  change or cancel it. The timeline entry of the email has an id derived from the interview, the kind and the start, so a
  redelivery finds it and sends nothing.
- **Every interviewer** gets an email for `DayBefore` only, in ScreenHire's own wording (`InterviewReminderEmail`, like the mention
  email, not a template an organization edits): the candidate's name, the job, the time in the interview's zone, the meeting
  link and a link to the application. `InterviewAttendee.RemindedForStart` records the start it was sent for, so a redelivery
  sends nothing and a move starts again. The interviewers' emails and the candidate's commit together, so a failed send repeats
  on retry the ones already sent in that attempt. A member who has left, or no longer sees the job (ADR-0015), is skipped.

The email goes before its record commits, so a crash between the two can repeat one email once, as with the other emails.
A worker that was down at the due time sends the reminder when it returns, unless the interview has started by then.

## The candidate's page

The invitation and confirmation emails link to `/interviews/{token}` on the careers site ([apply flow](apply-flow.md), §8.1),
which calls the public routes above. It is plain HTML with no script: the candidate picks a time zone with a GET form and
a time with a radio, and the careers server writes every time with `Intl.DateTimeFormat` in the chosen zone. The API
offers instants and the zone; it never sends formatted text. Booking, moving and cancelling are Astro actions that
redirect back to the same address with an outcome word (`booked`, `rescheduled`, `cancelled`, `slot-taken`, `too-late`).
A conflict maps to its notice by the problem's `type`. When the team has offered new times the page says so, keeps the booked time in view and leads with Pick a new time, which
opens the picker on the new days. The usual notice does not apply while the offer is pending. The page shows one screen of five days that have times and links to the
next, shows no meeting link of its own (the calendar invitation has it), names no interviewer, and says nothing about
verification. Cancelling asks first: a `details` element opens a line that says what happens and a Yes, cancel it button. The browser test (`InterviewBookingTests`) runs the whole journey with JavaScript off, axe clean, and checks
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

**The header** gets a mini card, Interview, only while an interview is booked: the time in the reader's own zone, then the
length and how many interviewers. The time is said nowhere else on the screen except Activity, which records the time each
entry was about.

**The Interview panel** sits under Next steps on Overview and is not drawn while no interview was ever offered. Each
state has a status badge (design system section 3.2): Waiting for the candidate (grey), Booked (clear), Cancelled and Expired
(grey). An open interview gives the days the candidate can choose from, in the interview's zone. A booked
one lists the interviewers with the organizer marked in words when there are two or more, the meeting link, and a note under an interviewer whose
calendar event is still pending or failed (the failure's own words, such as "Their calendar refused the event"). A cancelled or expired
one is one line saying how it ended. The panel's menu, for people who may email candidates, has two items. Offer new times
(booked interviews only) opens a sheet with the first and last day, in the interview's zone, and the interview's own length and
working hours; it emails the candidate. While the offer is pending a booked interview also shows Waiting for a new time, the new
days, and says the booked time stays until the candidate picks one. Cancel is a destructive item behind a dialog, because it
tells the candidate and removes the event from every calendar.

Activity lists the five interview entries under an Interviews filter. A booking or a move says the time it was about, so a
move is history the header no longer shows.

## Tenancy

`calendar_connections`, `calendar_sandbox_busy`, `calendar_sandbox_events`, `interview_requests` and `interview_attendees`
are tenant-owned with forced row-level security, like every other tenant table. The last two sit under an application,
so they also carry the restrictive `job_scope` policy (ADR-0015). A connection is unique per organization and member. An attendee's foreign key to its interview includes the application,
so an attendee cannot name another application than its interview's, which is the application its `job_scope` policy reads.
Removing a member from the organization deletes their connection and takes them off the interviews still to come.
