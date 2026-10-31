---
type: Design
title: "Connecting to Slack"
description: "The organization's Slack workspace: the OAuth install from Settings > Slack, the bot token under Data Protection, the channel and the four events posted there, each person's own account linked with Sign in with Slack for their mentions and tasks, what a message may say, delivery through Wolverine with idempotency keys, the local sandbox workspace, configuration and tests."
tags: [slack, integrations, notifications]
sources:
  - resource: src/ScreenHire.Domain/Slack/SlackInstallation.cs
  - resource: src/ScreenHire.Domain/Slack/SlackAccountLink.cs
  - resource: src/ScreenHire.Domain/Slack/SlackMessageRecord.cs
  - resource: src/ScreenHire.Application/Slack/SlackOptions.cs
  - resource: src/ScreenHire.Application/Slack/ISlackClient.cs
  - resource: src/ScreenHire.Application/Slack/SlackApiClient.cs
  - resource: src/ScreenHire.Application/Slack/SandboxSlackClient.cs
  - resource: src/ScreenHire.Application/Slack/SlackAccess.cs
  - resource: src/ScreenHire.Application/Slack/SlackMessages.cs
  - resource: src/ScreenHire.Application/Slack/SlackDelivery.cs
  - resource: src/ScreenHire.Application/Slack/SlackRetention.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Configurations/SlackConfiguration.cs
  - resource: apps/api/Endpoints/SlackEndpoints.cs
  - resource: apps/api/Contracts/SlackContracts.cs
  - resource: apps/dashboard/src/routes/(app)/settings/slack/+page.svelte
  - resource: apps/dashboard/src/lib/components/slack-account-sheet.svelte
  - resource: apps/dashboard/src/lib/slack.ts
  - resource: tests/ScreenHire.E2E/SlackTests.cs
---
# Connecting to Slack

An Owner or Admin connects the organization's Slack workspace from Settings > Slack. ScreenHire then posts the events
the team picks to one channel, and sends each person who links their own Slack account their mentions and the tasks
given to them as direct messages. Every message names the candidate and the job and links to the record in the
dashboard. Verification output never reaches Slack (ADR-0004). Everything goes through `ISlackClient`, with a local
sandbox workspace in place of Slack, so no test needs Slack or the internet.

## Configuration

`ScreenHire:Slack` (`SlackOptions`), forwarded by the AppHost to the API and the worker so both work out the same mode
(`SlackOptions.Mode`):

| Mode | When | Client |
|---|---|---|
| Slack | `ClientId` and `ClientSecret` are both set | `SlackApiClient`, Slack's Web API |
| Sandbox | `Sandbox:Enabled` is true, and there is no app | `SandboxSlackClient`, refused outside Development (`SlackSandboxGate`) |
| Off | neither | none: `GET /v1/slack` says `available: false` and every other Slack route is a 404 |

The client id is plain configuration; the secret comes from the `slack-client-secret` parameter, which the AppHost adds
only once a client id is set. `pnpm dev` has neither, so Settings > Slack says Slack is not set up here. To try it
locally, turn the sandbox on (see [local demo](../operations/local-demo.md)). The test stacks turn it on, and the
`DevelopmentDemoFixture` stack leaves it off, so both states are tested.

`SlackAccess` is always registered, in both hosts and every test host, so a handler or endpoint that takes it always
resolves; with Slack off, nothing is published for Slack at all.

## Connecting the workspace

Settings > Slack needs Manage integrations (`Permission.ManageIntegrations`, Owners and Admins) with all jobs: a channel
post reaches everyone in the channel, whatever jobs they see in ScreenHire, as a webhook does (ADR-0015). Custom roles
get it only when an Owner adds it.

1. Add to Slack calls `POST /v1/slack/install`, which returns Slack's authorize URL (the bot scopes `chat:write`,
   `chat:write.public`, `channels:read`, `groups:read` and `im:write`) and sets the `sh.slack-oauth` cookie.
2. Slack sends the browser back to `GET /v1/slack/oauth/callback` on the dashboard's origin
   (`DomainOptions.AppUrl`, as sign-in does). The callback reads the state (`SlackOAuthState`: Data Protection,
   time-limited to 10 minutes, naming the purpose, the organization, the person and a nonce) and checks the nonce
   against the cookie, the person and organization against the session, and the permission again. Then it exchanges the
   code (`oauth.v2.access`) and stores the workspace.
3. The browser lands on `/settings/slack?slack=connected`, or `denied`, `expired`, `wrong-workspace` or `failed`.

The bot token is protected with ASP.NET Core Data Protection under the purpose `ScreenHire.Slack.BotToken.v1` and the
organization's id, so a token copied into another organization's row does not read. The key ring is the one the API
and the worker already share in Postgres; protecting it at rest in production is an existing owner action. A token that
no longer reads is treated as disconnected. No response, log or message carries the token.

Connecting again replaces the token. The same workspace keeps its channel, events and linked accounts; another
workspace starts over. Disconnect revokes the token (best effort) and deletes the workspace and every linked account.
The installation's row holds the token, so change history never reads it (ADR-0016): the header says when Slack was
connected and by whom.

## The channel

`GET /v1/slack/channels` lists the channels the app can post to: public ones (`chat:write.public` needs no invite) and
private ones it was invited to, never archived ones, up to 1,000. `PUT /v1/slack/channel` saves the channel and the
events, after asking Slack for the channel: an unknown, archived or uninvited private channel is a 400. `POST
/v1/slack/test` posts a fixed line and says whether it went.

| Event | Posted when | Comes from |
|---|---|---|
| New application | An application is submitted | `ApplicationSubmitted` |
| Candidate moved | An application moves to another stage | `ApplicationMoved` |
| Automation failed | An automation run fails or is blocked | `AutomationRunFinished` |
| Job published | A job goes live the first time (version 1); republishing changed content is not posted | `JobPublished` |

## Each person's account

Every member links their own account from their menu (Slack account, `SlackAccountSheet`), shown while the workspace is
connected. Linking uses Sign in with Slack (OpenID Connect, `openid profile`, with `team` set to the workspace) through
the same callback: `POST /v1/slack/account/link` takes the page to come back to, and the outcome returns as
`?slack-account=`. An account from another workspace is refused (`wrong-workspace`). A Slack account links to one
member: linking it again moves it. Matching people by email was rejected: it would message whoever holds an address
without them asking.

`PUT /v1/slack/account` turns Mentions and Tasks given to me on or off (both on at first); `DELETE /v1/slack/account`
unlinks. These routes need no permission, like switching organization, because each changes only the caller's own row.
Removing a member deletes their link in the same transaction (`RemoveMember`).

In the sandbox, the link code names the person signed in, and a code naming anyone else is refused: the code travels
in the address, and the sandbox reads the name from the users table, which is not a tenant table.

A direct message goes for:

- **A mention.** `ApplicationChanges.PublishMentionsAsync` publishes `SendSlackDirectMessage` beside
  `TeammateMentioned`, as its own message, so a failing email never holds it up.
- **A task given to someone.** `ApplicationChanges.CreateTaskAsync` (the route and the MCP tool) and
  `ApplicationChanges.RequestScorecardAsync` (a requested scorecard), when the assignee is not the person creating it,
  and `ExecuteAutomationActionHandler.AddTask` for an AssignReviewer task.

The handler sends nothing to someone who did it themselves, has that kind off, left the organization or no longer sees
the job (`JobAccessQueries.SeesJobAsync`, the check the mention email makes), nor for a mention removed or a task done
since.

## What a message says

The words are in `SlackMessages`, and they name the kind of event, the candidate and the job, with a link to the
record, and nothing else:

- "New application from {candidate} for {job}"
- "{candidate}'s stage changed on {job}"
- "An automation failed on {candidate}'s application for {job}"
- "{job} is published"
- "You were mentioned in a note on {candidate} for {job}"
- "You have a new task on {candidate} for {job}"

A message never carries a stage name, an automation's name or error, a teammate's name, or a note's or task's text.
Stage and automation names can carry a verification outcome: a routing automation on Verification facts may move
someone to a stage named for a check, or be called "Route suspected bots", so "moved to {stage}" in a channel would be a
verdict. Nothing in `SlackMessages` takes verification output, and there is no verification event. One exposure stays: that
an event happened at all. If a team's automation moves candidates on a Verification fact, the channel sees that a
candidate's stage changed, without where to or why. That is accepted: the move is a hiring step the team chose to
announce, and it carries no level, reason or evidence. Names are escaped
(`&`, `<` and `>`), so a candidate called `<!channel>` pings nobody. Links have no previews (`unfurl_links` off): they
need a sign-in.

## Delivery

The four domain events' worker handlers cascade `PostSlackChannelMessage` beside the webhook fan-out
(`SlackEvents.From`), as its own message: a second handler on those events would join their chains, and a Slack failure
would retry the automations. Both Slack messages carry ids only, and an idempotency key from stable ids:
`channel:{eventId}` (the webhook event's id for the same domain event) and `dm:{kind}:{sourceId}:{recipient}`.

Each handler is idempotent three ways: the worker's inbox, `[Deduplicated]` on the key, and a `slack_messages` row
(`SlackMessageRecord`, no text) written with the post and checked first. A crash between Slack's answer and the commit
can repeat one message, as email can. The rows are deleted after 30 days (`PurgeExpiredSlackMessages`, daily at 03:57
UTC, one tenant message per organization).

`SlackApiClient` is one long-lived `HttpClient`, not a factory client, for the reason `HttpWebhookSender` gives: the
standard resilience handler on factory clients would post a message twice. The worker's error policy is the one retry
layer. Slack's answers sort into (`SlackErrors`):

- **Disconnected** (`invalid_auth`, `token_revoked`, `account_inactive` and the like): recorded on the workspace;
  nothing posts until someone reconnects. Slack's uninstall events need a public events endpoint, which is not built, so
  an uninstall is noticed on the next call.
- **A channel problem** (`channel_not_found`, `not_in_channel`, `is_archived`, `restricted_action`): recorded on the
  workspace and shown on the page until a post goes through or the channel changes.
- **A direct message problem** (`user_not_found`, `cannot_dm_bot`, `messages_tab_disabled`): recorded on that
  person's link and shown in their sheet.
- **Anything else** (rate limits, Slack's outages, timeouts, codes not in the list): thrown into the retry policy, then
  the dead-letter table.

## The sandbox workspace

`SandboxSlackClient` is Slack's part of every flow with no third party. Its "authorize page" is the API's own
`GET /v1/slack/sandbox/authorize`, which sends the browser straight back to the callback with a code, and only there.
The workspace is "ScreenHire sandbox", with `#hiring`, `#recruiting`, `#general`, `#announcements` (only admins may
post, so posts fail with `restricted_action`) and an archived `#old-hiring`. What it receives lands in
`slack_sandbox_messages`, which `GET /v1/slack/sandbox/messages` lists, newest first. Both sandbox routes are mapped only
in sandbox mode, so the API's description never lists them.

## Data

Four tenant-owned tables, each with the `tenant_isolation` row-level security policy (migration `AddSlack`):
`slack_installations` (one per organization), `slack_account_links` (one per member and one per Slack account, going
with the workspace), `slack_messages` and `slack_sandbox_messages`.

## Tests

| Test | Proves |
|---|---|
| `Slack/*Tests` (unit) | the wording and escaping, error codes, the Web API client against a stub handler, the token and state protection, the modes and the sandbox gate |
| `Slack/SlackTests` (integration) | the install and link round trips through the sandbox, the state and cookie checks, permissions, channel validation, a post for each event and none when it is off, one post for a replayed event, recorded problems, direct messages and when they are not sent, disconnecting, tenant isolation |
| `DevelopmentDemoTests` (integration) | with Slack off, `available` is false and the routes are 404s |
| `SlackTests` (end to end) | connecting the sandbox from the page, picking a channel, linking from the menu as an Owner and as a Recruiter, disconnecting through the dialog |

## Not built

- More than one channel, or channels per job.
- Slack's events and interactivity (an uninstall event, buttons in a message), which need a public endpoint.
- Token rotation: the Slack app keeps it off.
