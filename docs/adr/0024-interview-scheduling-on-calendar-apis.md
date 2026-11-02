---
type: Decision
title: "ADR-0024: Interview scheduling runs on the Google Calendar and Microsoft Graph APIs"
description: "Interviews are scheduled on members' own calendars through the free Google Calendar and Microsoft Graph APIs behind one provider interface and a Postgres-backed sandbox: calendar OAuth kept apart from sign-in with free/busy and event scopes only, encrypted per-member tokens, a no-account booking link on the careers site, one event per interviewer, double-booking prevented by advisory locks, lazy expiry, and why Cal.com, Nylas, Cronofy and a calendar of our own were rejected."
tags: [adr, scheduling, calendar, integrations, oauth]
---
# ADR-0024: Interview scheduling runs on the Google Calendar and Microsoft Graph APIs

- **Status:** Accepted (2026-11-02)
- **Deciders:** Owner
- **Detail:** [`docs/architecture/interview-scheduling.md`](../architecture/interview-scheduling.md),
  [`docs/architecture/workflow-engine.md`](../architecture/workflow-engine.md)

## Context

Interview scheduling is item 4 on the [roadmap](../roadmap.md). Until now the workflow engine refused the
`ScheduleInterview` action at save, and the engine's open questions listed three ways to get it: Cal.com, Nylas or
Cronofy, or building it. A team wants what its calendar already does: the candidate picks a time that is free for every
interviewer, and the event, with a meeting link, lands in each interviewer's calendar.

Cal.com's Platform API is paid, and its licence changed in 2026. The owner chose the free Google Calendar and Microsoft
Graph APIs, which between them cover the calendars small hiring teams use. Calendar tokens are secrets that reach a
person's whole schedule, and the booking page is reachable by anyone with a link, so the decision has to say how both are
kept narrow.

## Decision

1. **Calendar OAuth is separate from sign-in.** A member connects a calendar from the account menu, through a flow written
   by hand (`/v1/calendar`, an anonymous callback that checks a signed state, a nonce cookie and the signed-in person).
   Sign-in keeps no tokens and its OIDC handlers are untouched, as with Slack.
2. **The scopes are the calendar's own and no more.** Google: `calendar.freebusy` and `calendar.events`. Microsoft:
   `Calendars.ReadWrite` and `offline_access`, which is refresh-token plumbing and not a data scope. No `openid`,
   `email`, `profile` or `User.Read`; the address shown in the sheet comes from the calendar and is optional.
3. **Tokens are encrypted per member.** Access and refresh tokens are stored under Data Protection with a purpose that
   includes the organization and the member, so a token copied into another row does not read. A token that does not read
   marks the connection as needing a reconnect. Refresh runs under a per-connection Postgres advisory lock, because
   Microsoft rotates refresh tokens.
4. **One connection per member**, not one per provider. A member's calendar is Google, Microsoft or the sandbox.
5. **Everything sits behind `ICalendarProvider`.** The Google and Microsoft providers use plain `HttpClient` and add no
   package. A provider is available only when both its client id and its secret are set, and an unavailable provider is
   hidden, never an error. A Postgres-backed sandbox provider stands in locally and in tests; `CalendarSandboxGate`
   refuses to start a host with it outside Development. No test needs a Google or Microsoft account or the internet.
6. **The candidate books on the careers site with a link, no account.** The link carries a Data Protection token valid
   until the window ends plus 30 days. The request's status, not the token, closes it. The page is plain HTML with no
   script and no telemetry, and it names no interviewer.
7. **One event per interviewer on their own calendar.** It lands natively whichever provider each person uses. The
   organizer's event (the first interviewer) invites the candidate and asks for the Meet or Teams link; the other copies
   carry that link in their text. Event ids derive from the interview, the member and a revision, so every create is safe
   to repeat. A title and body hold the candidate's name, the job title and a link to the application, nothing else.
8. **Double booking is prevented by per-interviewer advisory locks and an overlap check.** Booking recomputes the offered
   times, takes `pg_advisory_xact_lock` per interviewer in sorted order and checks the interviews already confirmed. A
   `btree_gist` exclusion constraint would do the last step in the database but needs extension privileges.
9. **Requests expire lazily.** An Open request past its window reads as Expired and is stored as such by whoever next
   touches it. There are no timers and no cron job.
10. **The workflow engine gets `ScheduleInterview` and the `InterviewScheduled` trigger.** The action is never allowed
    from an automation driven by a verification signal or a passed time limit, because offering an interview is a
    candidate-facing outcome (non-negotiable 2).
11. **Scheduling reuses the permission that emails candidates**, `EmailCandidates`, instead of adding one: every route
    that offers an interview emails the candidate.
12. **Nothing from verification reaches a calendar, an email or the booking page**, and nothing infers a candidate's
    emotion, intent or honesty (non-negotiables 1 and 3).

## Rejected options

- **Cal.com.** Its Platform API is paid and its licence changed in 2026. It would also put candidate scheduling data in
  another vendor's system.
- **Nylas or Cronofy.** Both unify calendars behind one paid API. We would pay per connected calendar for something the
  two free APIs do, and the vendor would hold every member's calendar access.
- **A calendar of our own.** Teams already keep their time in Google or Outlook. A second calendar would be wrong the
  moment someone accepted a meeting elsewhere.

## Consequences

- Google treats both calendar scopes as sensitive, so the app needs Google's verification, which takes weeks. Until it is
  done the Google button works only for test users. This is an owner action, with the Entra app registration for Microsoft
  ([status](../status.md)).
- Two OAuth apps to keep registered and two sets of API behaviour to follow.
- Microsoft's refresh-token rotation is handled under a lock. A refresh the provider refuses for good records a problem and
  the member is asked to connect again.
- Events on different providers are separate copies and are not linked to the organizer's event. A candidate who accepts
  on the organizer's event does not change the others.
- A candidate can get two messages on booking: the provider's invitation and ScreenHire's confirmation. The confirmation is
  the only place the reschedule and cancel link lives, and the sandbox sends no provider mail.
- No reminders yet, and no way for a recruiter to move a booked interview; a recruiter cancels and offers again.
- An interview ends with its application: a rejection, withdrawal, hire or Save candidate cancels it, and the candidate
  gets no separate cancellation email. A leaving interviewer is taken off their interviews, and a Booked interview whose
  organizer left is cancelled and the candidate told. Erasing a candidate deletes their events from the calendars without
  notifying guests where the provider allows it (Google); Graph tells attendees when an organizer deletes a meeting. The
  alternative to all three was to leave the interview and let it fail at the next calendar call.
- No free/busy cache. The public routes are rate limited. If provider quotas bite, a cache is the next step.
- The production Data Protection key ring protection, already an owner item, now also guards calendar tokens.
