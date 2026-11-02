---
type: Design
title: "Interview scheduling on team calendars"
description: "Members connect their own Google, Outlook or sandbox calendar from the account menu: the OAuth round trip kept apart from sign-in, tokens under Data Protection for each member, the ICalendarProvider seam with a Postgres-backed sandbox, token refresh under an advisory lock, and the configuration that hides a provider until its client id and secret are set."
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
  - resource: apps/dashboard/src/lib/components/calendar-account-sheet.svelte
  - resource: apps/dashboard/src/lib/calendar.ts
  - resource: tests/ScreenHire.IntegrationTests/Calendar/CalendarConnectionTests.cs
  - resource: tests/ScreenHire.IntegrationTests/Calendar/CalendarSandboxTests.cs
---
# Interview scheduling on team calendars

ScreenHire schedules interviews on the calendars the team already uses, through the free Google Calendar and Microsoft
Graph APIs. This page describes the part that exists so far: a member connecting and disconnecting their own calendar.
Booking, the candidate's page and the workflow action build on it and are described here as they land.

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

## Tenancy

`calendar_connections`, `calendar_sandbox_busy` and `calendar_sandbox_events` are tenant-owned with forced row-level
security, like every other tenant table. A connection is unique per organization and member. Removing a member from the
organization deletes their connection.
