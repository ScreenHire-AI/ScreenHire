---
type: Guide
title: "Testing"
description: "Which test tier proves what (xUnit, Vitest, Aspire integration, Playwright), running one test, the shared AppHostFixture, isolating data by organization, golden files, the marketing JS and Lighthouse checks, CI on GitHub Actions and how to read a failed run, the flake policy and its history, and gate time."
tags: [testing, xunit, playwright, vitest]
sources:
  - resource: tests/ScreenHire.Testing/AppHostFixture.cs
  - resource: tests/ScreenHire.E2E/E2EFixture.cs
  - resource: tests/ScreenHire.Testing/ScheduledMessages.cs
  - resource: apps/marketing/scripts/check-dist.mjs
  - resource: apps/marketing/scripts/lighthouse.mjs
  - resource: scripts/install-browsers.mjs
  - resource: global.json
  - resource: .github/workflows/ci.yml
  - resource: .github/actions/setup/action.yml
  - resource: scripts/test-shard.mjs
---
# Testing

Every tier of test has a home, a pattern and a place in `pnpm verify`, so a feature task only adds tests. The local
gate ([`/verify`](../../.claude/skills/verify/SKILL.md)) is the board's gate, and CI runs the same suites on Linux for
every pull request ([Continuous integration](#continuous-integration)); a suite that is in neither does not exist.

## Which tier for what

| Tier | Where | Use it for | Needs |
|---|---|---|---|
| .NET unit | `tests/ScreenHire.UnitTests` (xUnit v3 + AwesomeAssertions) | Domain rules (`Job` lifecycle and versions, template versioning, the definition validator and its guardrails, the verification ruleset), the verification analyzers with local fakes, golden files of the synthetic applicants' assessments, pure API logic (mappings, validation, the workflow diff, sanitizing), and the repository tooling in `Tooling/`: the MCP approvals, and the process guard and docs bundle checks, which run their `scripts/*.mjs` with Node. The API grants it `InternalsVisibleTo`. | Node for `Tooling/` |
| Vitest | next to the code as `*.test.ts`: `apps/dashboard` (Svelte 5 components on jsdom with @testing-library/svelte, including the shared question components in the questionnaire preview; pages and query-backed components render inside `src/test-support/with-query-client.svelte` with `fetch` stubbed (it also gives them the sidebar's context and a tooltip provider), anything else with a tooltip inside `with-tooltips.svelte`, the signed-in frame (sidebar, top bar, shortcuts dialog) inside `shell-harness.svelte`, a workflow's tabs with the builder or the read-only view inside `workflow-harness.svelte`, with `stubViewport` in `viewport.ts` setting the window width the media queries see, and `expectNoAxeViolations` in `src/test-support/axe.ts` runs axe for WCAG 2.2 A/AA on each component), `packages/api-client` (the fetch mutator), `packages/realtime` (the SignalR client, event schemas against the C# contract file), `apps/careers` (`src/lib`: formatting, the apply flow's state and checks, tenant hosts, the brand stylesheet; `src/components`: the shared question components rendered through Astro's container API with the Svelte renderer, on the server as the apply page uses them and as a `client:load` island), `apps/marketing` (JSON-LD builders, page registry, pricing, the post-build checks in `scripts/`) | Frontend logic and component rendering with fixed data | Nothing |
| Aspire integration | `tests/ScreenHire.IntegrationTests` | Endpoints, persistence, data rules (tenant scoping, status filters), messaging (outbox, worker handlers, retries, dead letters), anything that needs Postgres | Docker |
| End-to-end | `tests/ScreenHire.E2E` (Microsoft.Playwright for .NET on xUnit v3, axe) | User journeys in a real browser against the whole stack: rendered content, JSON-LD, no-JS public pages, accessibility | Docker, Chromium, `pnpm -r build` |

When Docker is down, `pnpm docker:start` starts Docker Desktop and waits for the engine.

Pick the lowest tier that can catch the bug. A rule in the domain gets a unit test even when an integration test also
covers the endpoint. End-to-end tests prove that the pieces connect and render; they do not enumerate edge cases.

## Running

| What | Command |
|---|---|
| The whole gate | `pnpm verify` (see `/verify`) |
| All .NET suites | `dotnet test --solution ScreenHire.slnx` |
| One .NET project | `dotnet test --project tests/ScreenHire.UnitTests` |
| One .NET test or class | `dotnet test --project tests/ScreenHire.UnitTests --filter-method "*closed_job_cannot*"` (also `--filter-class`, `--filter-trait`) |
| All Vitest suites | `pnpm -r --if-present test` |
| One Vitest file or test | `pnpm --filter @screenhire/careers exec vitest run src/lib/format.test.ts -t formatSalary` |
| Watch a Vitest suite | `pnpm --filter @screenhire/dashboard exec vitest` (the `test` scripts use `vitest run`, which never watches, so the gate cannot hang; each also sets its own `--fsModuleCachePath`, because packages that share Vitest's default cache folder race to clear it on Windows) |

The .NET suites run on Microsoft Testing Platform (`global.json`); `dotnet test` runs the test projects in parallel.

### One-time setup for end-to-end tests

```
pnpm e2e:install
```

[`scripts/install-browsers.mjs`](../../scripts/install-browsers.mjs) builds `tests/ScreenHire.E2E` and runs the
Playwright driver bundled with the `Microsoft.Playwright` NuGet package, so the Chromium revision matches the .NET
package (the npm `playwright` CLI may fetch a different one). Browsers go to Playwright's per-user cache
(`%LOCALAPPDATA%\ms-playwright` on Windows), shared by every worktree. Run it again after bumping
`Microsoft.Playwright`. Without it the end-to-end tests fail at once with a message that names this command.

The end-to-end tests serve the frontends' **production builds**, so run `pnpm -r build` before running them on their
own (`pnpm verify` builds first). They fail fast, naming the missing folders, when a build is absent; after changing a
frontend, rebuild or they test the old build. `HEADED=1` shows the browser while you debug.

## Fixtures

### `AppHostFixture` (`tests/ScreenHire.Testing`)

A shared library both Aspire test projects reference. `AppHostFixture` is an xUnit v3 **assembly fixture**: it boots
the AppHost once per test assembly and every test class takes it as a constructor parameter.

```csharp
[assembly: AssemblyFixture(typeof(AppHostFixture))]          // once, in AssemblyInfo.cs

public sealed class PublicJobsTests(AppHostFixture fixture)  // every test class
{
    [Fact]
    public async Task Board_lists_open_jobs()
    {
        var organization = await fixture.CreateOrganizationAsync(org => [/* jobs */], TestContext.Current.CancellationToken);
        using var client = fixture.CreateHttpClient("api");
        // ...
    }
}
```

It always passes `PersistData=false` (no volumes: fresh Postgres and Azurite) and `PinPorts=false` (random ports), so
a run never collides with a running dev stack or a sibling worktree, and Aspire removes its containers on dispose.
The AppHost's `ScreenHire:Frontends` setting decides which JS apps start:

| Mode | Starts | Used by |
|---|---|---|
| `Dev` (default) | `dev` scripts with hot reload | `pnpm dev` |
| `Built` | the `pnpm -r build` output served as it ships: the dashboard (with its `/v1` route to the API) and marketing from Caddy containers, careers from its own Node server (`dist/server/entry.mjs`). No Vite or Astro preview server | `tests/ScreenHire.E2E` |
| `None` | no JS apps; API, worker and infrastructure only | `tests/ScreenHire.IntegrationTests` |

It also sets job distribution for tests ([job-distribution.md](../architecture/job-distribution.md) §5): the sandbox
board's acceptance and removal delay to `AppHostFixture.SandboxDelay` (4 s), so a test can wait for its confirmations,
and `GoogleIndexing:Mode=Local`, the in-process Indexing API fake, so the Google queue runs with no account. For
webhooks ([workflow-engine.md](../architecture/workflow-engine.md) §5) it sets `Webhooks:AllowLoopback`, so the worker
may post to a `WebhookReceiver` (a Kestrel server on `127.0.0.1` that a test starts), and shortens the retry delays
(`AppHostFixture.WebhookRetryDelays`, 1 s and 3 s) and the disable limit (two failed deliveries).

A fixture that needs a stack of its own subclasses `AppHostFixture` and takes it as a class fixture: `ExtraArguments`
adds AppHost settings (`DevelopmentDemoFixture` turns the demo on), and `Configure` changes the resources before the
stack builds. `ProductionApiFixture` uses it to run the API in Production after doing what a deploy does first (migrate,
the application role's login, Wolverine's tables), with no worker and no health wait, because Production maps no
`/health`.

Every resource's console log goes to `TestResults/logs/<fixture>/<resource>.log` next to the test assembly (for
example `tests/ScreenHire.E2E/bin/Debug/net10.0/TestResults/logs/E2EFixture/dashboard.log`), streamed with Aspire's
`ResourceLoggerService` and rewritten each run. A resource that stops with an error while the tests run is kept in
`fixture.StoppedResources`, and a failing end-to-end test prints it with its exit code and log, so a server that dies
mid-run names itself instead of failing each test with its own symptom. Read these logs first when a test fails on a
server that stopped answering.

On Windows the fixture stops the stack's own processes (API, worker, careers) by identity before Aspire disposes the
stack, because DCP 0.26's stop can kill unrelated processes through a reused parent PID. What that covers, what it
does not and the measurements are in [Processes on the dev machine](board-pipeline.md#processes-on-the-dev-machine).

The API and every JS app have HTTP health checks, so the fixtures wait for servers that answer, never for a fixed
delay. The API only starts listening after its Development migrate-and-seed, so a healthy API has its schema and
Brightwater. With the demo on (`DevelopmentDemoFixture`, `pnpm dev`), `/health` also waits for the demo's data. A resource that fails to start fails the fixture at once, and a timeout names the resource.
The worker has no HTTP endpoint, so the fixture also waits for its log to say Wolverine listens on the worker's queue
before any test runs; a test that publishes never races the worker's start. Listening comes before leading, so it then
waits until Wolverine has assigned the worker's durability agent (a `wolverinedb://` row in
`wolverine.wolverine_node_assignments`, about 13 s later under load): by then scheduled messages run too. A test that
restarts the worker waits the same way for an assignment made after the restart (`WaitUntilWorkerRunsAgentsAsync`, with
the database's time from before it), because Aspire calls the new process running up to a minute before it listens.

### Test data: isolate by organization

There is no reset between tests and no Respawn. Instead:

- **Tests that write data create their own organization** with `fixture.CreateOrganizationAsync(...)`
  ([`TestData`](../../tests/ScreenHire.Testing/TestData.cs)), which gives it a unique slug, and touch only that
  organization's rows. `TestData.NewJob(organization, slug)` builds a draft job with defaults, and
  `fixture.AddApplicationHistoryAsync(...)` adds an application with a past submission and dated moves (timeline
  entries cannot change once written, so history is written already dated; `ReportsTests` uses it). Tests sharing one
  AppHost therefore never see each other's data, in any order or in parallel.
- **The seeded demo organization (`brightwater`) is read-only.** Assert on it (the end-to-end journeys do), never
  change it: other tests count its jobs.
- **A test that needs a different stack gets a fixture of its own.** Subclass `AppHostFixture`, add the AppHost
  settings through `ExtraArguments`, and use it as the class's `IClassFixture`. `DevelopmentDemoTests` does this with
  `--ScreenHire:Demo=true`: it checks the data `pnpm dev` seeds on Brightwater, and that a second start of the API adds
  none, on a stack the other tests never see. Each such fixture boots another stack, about 25 s, so keep them rare.
  A stack whose startup is heavy goes in a collection with `DisableParallelization = true`, as the demo's
  `DevelopmentDemoRun` does: run beside the parallel tests, the demo's burst held the shared stack's worker past their
  deadlines.
- `TestData` is the one place tests write to the database directly. It writes as the application role
  (`screenhire_app`, which neither owns the tables nor bypasses RLS) with the organization as tenant, so row-level
  security checks test data too. `fixture.CreateDbContextAsync(tenant, ...)` gives a context on the same role.
  `fixture.GetOwnerConnectionStringAsync` is the superuser, only for state no endpoint can reach (an expired sign-in
  link), never for tenant data. `RowLevelSecurityTests` proves the database itself refuses cross-tenant reads and
  writes.
- **Authentication.** Tests sign in as a person does, never through a bypass, test-only endpoint or anonymous policy.
  In integration tests, `ApiSession` is one client with its own cookie jar (never share one across tests) that sends
  the antiforgery header on POSTs and signs in with `SignInWithMagicLinkAsync(email)` (the link read from Mailpit) or
  `SignInWithDevSsoAsync(username)` (the mock provider's login form). `ApiSession.NewEmail()` gives each test its own
  address and account; `OnboardAsync()` gives it its own organization. End-to-end tests click through the sign-in
  pages; `fixture.Mailpit` reads the emailed link. The seeded Brightwater Owner (`avery@brightwater.example.com`, Dev
  SSO user `brightwater-owner`) and Recruiter (`jordan@brightwater.example.com`, `brightwater-recruiter`) are
  read-only like their organization: sign in as them, change nothing. A test that needs a member with another role
  invites one through the API: `fixture.InviteAndJoinAsync(owner, "Viewer")` (`TeamTestSupport`) reads the invitation
  with `Mailpit.WaitForInvitationTokenAsync` and accepts it in a new session. Invitation links carry `#invitation=`,
  so they never pass for a sign-in link.
- **Careers applicants.** `fixture.ApplyAsync(slug, jobId, applicant, ct)` (`SyntheticApplications` in
  `tests/ScreenHire.Testing`) submits one of the synthetic applicants (`SyntheticApplicants`) to a published job the
  way the careers server does, with its address, session and resume, so the verification engine assesses it. Integration
  and end-to-end tests both use it.

### Messaging tests (`tests/ScreenHire.IntegrationTests/Messaging`)

Messages cross processes, so these tests wait on Wolverine's tables in Postgres, never on time:

- `WolverineStore` counts a message in the outbox, the worker queue, the inbox (handled or not) or the dead-letter
  table, found by an id in its body so a test only sees its own messages. A published job's id is in its postings' commands and events too, so
  each count takes an optional message type (`".JobPublished"`) to narrow it. `WolverineStore.WaitUntilAsync` polls a
  condition with a 60-second deadline.
- `MessagingHost` starts an in-process Wolverine host with the API's or the worker's setup
  (`ScreenHireMessaging.ConfigurePublisher` / `ConfigureHandler`) in a private schema of the AppHost's database, and
  drops the schema afterwards. Use it to make a handler fail or a transaction roll back without touching the running
  worker.
- A test that stops or restarts the worker joins the `WorkerLifecycle` collection (`[Collection<WorkerLifecycle>]`).
  xUnit runs that collection on its own after the parallel tests, so no other test waits on a stopped worker. Refer
  to a collection by its type, never `[Collection(nameof(...))]`: a `[CollectionDefinition]` without a name is matched
  by type, and a string quietly makes a separate collection that runs in parallel.

A test that needs the worker in the middle of something, such as saving a board's answer, plays that part itself
rather than racing the real worker: `BoardAnswerRaceTests` holds a posting's row lock as a handler does, starts the
request, waits until Postgres shows it blocked (`pg_blocking_pids` on the owner connection, which only observes), then
commits. The outcome is the same on every run.

A timer that waits hours or days (a `TimeElapsed` automation, a stage's time limit) is never waited for. An
integration test reads when it falls due with `WolverineStore.ScheduledTimesAsync` and publishes the message the
schedule would release, through `MessagingHost.StartPublisherToAppHostQueuesAsync`. An end-to-end test calls
`fixture.ReleaseAsync(id)` (`ScheduledMessages`), which moves the real scheduled message's due time to now, and runs the
browser's clock ahead with `Page.Clock.SetFixedTimeAsync`.

What each test proves is in [`docs/architecture/messaging.md`](../architecture/messaging.md#tests).

### Real-time tests (`tests/ScreenHire.IntegrationTests/Realtime`)

`EventsConnection` is the .NET SignalR client on the API's hub, signed in with an `ApiSession`'s cookie
(`session.Cookies`). It records every event, `WaitForAsync` waits for one by type and id, and `ReceivedAnythingAbout`
checks that another organization's connection got nothing. To make the real worker raise an event, put a message on
its queue with `MessagingHost.StartPublisherToAppHostQueuesAsync`. What each test proves is in
[`docs/architecture/realtime.md`](../architecture/realtime.md#tests).

### `E2EFixture` and `BrowserTest` (`tests/ScreenHire.E2E`)

`E2EFixture` extends `AppHostFixture` with `Frontends=Built`, waits for the API and all three frontends to be
healthy, and launches one headless Chromium for the assembly. Test classes derive from `BrowserTest`, which gives:

- `Page`, in a **new browser context per test** (no shared cookies or storage);
- `GoToAsync("careers", "/brightwater")`, which resolves the URL from Aspire and expects a 2xx, and `OpenAsync`, which
  returns the response whatever its status (a 404 or 410 page);
- `WithoutJavaScriptAsync()`, which swaps `Page` for one in a context with JavaScript off, for journeys that must work
  on plain HTML (the careers apply flow);
- `ExpectNoAccessibilityViolationsAsync()`, which runs axe for WCAG 2.2 A/AA and lists every violation with its
  selector. Fix violations; do not exclude rules. If one must be excluded, scope it to one selector with a reason. With
  JavaScript off, axe cannot run in the page, so it checks the same HTML served again to a page that runs scripts;
- **a full-page screenshot when a test fails**, at
  `tests/ScreenHire.E2E/bin/Debug/net10.0/TestResults/screenshots/<test>.png` (the path is printed in the test output).

Assert on what a person sees (headings, rows, cell text, links by accessible name) with Playwright's `Expect(...)`,
which retries until its timeout. Status codes alone are not an end-to-end assertion.

`AcceptanceJourneyTests` is the MVP acceptance journey: the whole product in one test, in the order a hiring team meets
it (Dev SSO sign-in and onboarding, a job from a template, publish to the careers page and the sandbox board, a genuine
candidate and a bot applying on the careers site while the job is open, their levels and evidence arriving live, a move
that sends the stage-change email to Mailpit, and the teardown). The other classes go deep on each step; this one
proves the steps connect, so a change that breaks the hand-off between two areas fails here. It keeps the verification
levels deterministic in a browser: the genuine candidate's browser runs no JavaScript, so Playwright's automation
markers and the device check never reach the API, and the bot uses Playwright's browser as it is.

### Golden files

Two unit tests compare output with files in the repository, and fail with the command that rewrites them:

| Test | File | Rewrite with |
|---|---|---|
| `RealtimeContractTests` | `packages/realtime/contract/events.json` | `UPDATE_REALTIME_CONTRACT=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*RealtimeContractTests"` |
| `VerificationGoldenTests` | `tests/ScreenHire.UnitTests/Verification/Golden/*.json` | `UPDATE_VERIFICATION_GOLDEN=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*VerificationGoldenTests"` |

Read the diff before committing a rewrite: it is the review of what changed for a person reading the output.

## Marketing site checks

The marketing site's SEO and performance budgets (ADR-0003) are checked at three tiers:

| Check | Where | Runs in |
|---|---|---|
| Unique titles and descriptions in the page registry, JSON-LD builders, pricing placeholders | Vitest, `apps/marketing/src/lib/*.test.ts` | `pnpm -r --if-present test` |
| At most 15 KB of brotli JS per page (JSON-LD excluded), no broken internal links or Open Graph images, a body-font preload on every page | `apps/marketing/scripts/check-dist.mjs`, after `astro build` | `pnpm -r build` |
| Mobile LCP under 1.8 s, CLS under 0.05, JS bytes, on every page in the sitemap | `apps/marketing/scripts/lighthouse.mjs` (Lighthouse on Astro's preview server) | `verify:lighthouse`, run by the Testing column (`/pull-request`) for a branch that changes `apps/marketing`, `packages/ui` or `pnpm-lock.yaml`, and by CI's report-only `lighthouse` job for the same paths; not in `pnpm verify` |
| Unique title, description and canonical; hreflang; Open Graph image; JSON-LD types per page; no script but JSON-LD; axe; no sideways scroll at 390px; 404 | `tests/ScreenHire.E2E/MarketingTests.cs` | `verify:test` |

`MarketingTests` keeps a table of every indexable page and the schema.org types it must carry, and fails if the
sitemap lists a page the table does not, so a new page cannot skip the checks. Lighthouse uses CHROME_PATH, then
Playwright's Chromium, then an installed Chrome. On Linux it starts Chrome with `--no-sandbox`, because Ubuntu 24.04
restricts the user namespaces Chromium's sandbox needs; Playwright's own launches run without it by default. Its simulated mobile numbers were identical to within 3 ms across
two consecutive full runs (about 90 s each). Run a subset with
`pnpm --filter @screenhire/marketing lighthouse / /pricing/`; in Git Bash set `MSYS_NO_PATHCONV=1` first, or the
paths are rewritten into Windows paths.

### Why Lighthouse runs once, and one at a time

Lighthouse used to be the last step of `pnpm verify`. On 2026-10-21 five gates ran it at once on one machine:
five `lighthouse.mjs` processes at about 250 MB each and fourteen `pnpm` wrappers at about 92 MB each, about 2.5 GB
before their Chromes. Five runs also measure each other, because Lighthouse multiplies the main-thread time it
observes by 4. So it left the gate. The Testing column runs it once per task, and only when the branch changes what
the marketing build outputs. Merge does not run it again after merging a newer `main`, so two marketing changes are
never measured together before both land. That is a deliberate trade. Every page sits about 300 ms under the LCP
budget, and the next marketing task's Testing run measures the combined site.

`lighthouse.mjs` takes a lock file, `screenhire-lighthouse.lock` in the user's temp folder, before it starts the
preview server. A second run waits for it, up to 10 minutes, and says so. A lock older than the 5-minute run limit
plus a minute belongs to a run that is gone, and is taken over. Two runs started together on 2026-10-21 (all 15 pages
and `/ /pricing/`) both passed. The second waited for the first and then measured the same LCP to within 3 ms.

What the lock does not do: keep a sibling task's end-to-end tests off the CPU. Today every page has LCP about 1505 ms
against the 1800 ms budget, and the network part of that is simulated from byte counts, so load moves it little.
If a page fails by a few tens of milliseconds while other gates run, rerun it with nothing else running before you
call it a regression. Do not add retries (flake policy).

### What Kangentic's agent browser can and cannot measure

We looked at whether the `kangentic_browser_*` tools could check the budgets against a running site instead of
Lighthouse. The facts come from Kangentic's source (0.47) and one trial on 2026-10-21. The trial ran against the
built site, with the viewport at 412 x 823, on `/pricing/`.

| Budget | Agent browser | Lighthouse |
|---|---|---|
| 15 KB brotli JS per page | `kangentic_browser_network` lists method, URL, type, status and time to headers, with no size of any kind. Resource Timing through `kangentic_browser_eval` gives `encodedBodySize`, which is 0 for a cache revalidation (every subresource in the trial). | Counts script transfer bytes. Both are redundant: `check-dist.mjs` measures brotli bytes exactly in every build, and `MarketingTests` fails on any script but JSON-LD. |
| CLS under 0.05 | A buffered `PerformanceObserver` for `layout-shift` works (0 in the trial). The load is always a warm-cache repeat visit, so a late font or image never shifts anything. | Cold load, on a simulated slow network. |
| Mobile LCP under 1.8 s | 24 ms in the trial, on localhost with no throttling and a warm cache. That clears 1.8 s on any page, so it could not fail. | 1505 ms for the same page: Lantern's simulated slow 4G (150 ms RTT, 1.6 x 1024 Kbps) and 4x CPU slowdown (`mobileSlow4G` in `@paulirish/trace_engine`, `models/trace/lantern/simulation/Constants.js`), on a Moto G Power screen (412 x 823 at DPR 1.75) with its user agent (`lighthouse/core/config/constants.js`). |

What the agent browser lacks, so the trial cannot stand in for the budget:

- **No throttling.** Kangentic emulates neither network nor CPU throttling anywhere.
- **No mobile emulation.** `kangentic_browser_set_viewport` sets the size only. A docked pane is set with
  `mobile: false`, an offscreen pane is resized as a window, and the user agent stays desktop Chromium (the trial
  saw `Windows NT 10.0 ... Kangentic/0.47.0`).
- **No cold load.** Each task's pane keeps a persistent cache. The debugger attaches after the first load, so a
  measured load is a repeat visit, and no tool clears or bypasses the cache.
- **Skewed paint timing offscreen.** An offscreen pane, which is what an agent gets when the project is not on
  screen, paints at 10 fps.
- **Not a command.** It runs only inside an agent session, so a script like `pnpm verify` cannot call it.
  `kangentic_browser_eval` is off by default, and only the user can turn it on, for every project at once, in
  Kangentic's Settings.
- **Not the build.** `/preview` serves marketing from `astro dev`, not the build. The trial needed a separate
  preview server.

It is still useful while working on a page: a quick look for layout shifts and their sources, or for a script that
should not be there. The budget stays with Lighthouse.

## Continuous integration

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs the gate's suites on GitHub's hosted Linux runners
for every pull request to `main`, every push to `main`, and on demand (`workflow_dispatch`). A newer push to the same
branch cancels the run before it. The board still gates on the local `pnpm verify`; CI is the second opinion on
another OS until the board moves to it.

The runners have 4 cores and each long suite is CPU-bound there, so the long suites run as matrix shards on separate
runners, as Kangentic's workflow does. Every matrix sets `fail-fast: false`, so one shard's failure never hides
another's.

| Job | Runs | Timeout |
|---|---|---|
| `checks` | `pnpm verify:dotnet`, `pnpm verify:contract`, `pnpm check`, `pnpm -r build`, Vitest for every package but the dashboard, and `dotnet test --project tests/ScreenHire.UnitTests`. Every step runs even when an earlier one failed, and the last step fails the job if any did, so a run reports every broken check at once | 20 min |
| `vitest` (2 shards) | The dashboard's Vitest suite with Vitest's own `--shard`. No .NET | 15 min |
| `integration` (8 shards) | `tests/ScreenHire.IntegrationTests` on the runner's Docker Engine, one AppHost per shard | 25 min |
| `e2e` (8 shards) | `pnpm -r build`, then `tests/ScreenHire.E2E` with Playwright's Chromium, one AppHost per shard | 25 min |
| `lighthouse` | `pnpm verify:lighthouse` on the marketing build, only when the change touches `apps/marketing`, `packages/ui` or `pnpm-lock.yaml` (otherwise a notice says so). Report-only: a budget miss is a warning, the job stays green, and it is not in the gate, because shared runners have no LCP baseline yet | 20 min |
| `gate` (`Gate`) | Nothing but a check that `checks` and every shard of `vitest`, `integration` and `e2e` succeeded. It runs even when they fail (`always()`), so a skipped or cancelled job never reads as a pass. It is the one check branch protection requires | 5 min |

xUnit and Microsoft Testing Platform have no shard option, so [`scripts/test-shard.mjs`](../../scripts/test-shard.mjs)
splits a .NET test project by class:
- it lists the built project's tests (`dotnet test --list-tests`);
- it weighs each class by its measured seconds in [`tests/shard-durations.json`](../../tests/shard-durations.json), or,
  for a class with no measurement, by its test count times the median seconds per test;
- it gives each class, whole, to the lightest shard, heaviest class first;
- it prints one shard's `--filter-class` arguments.

A class stays whole because a class fixture can boot a stack of its own (`DevelopmentDemoFixture`). A shard with no
class fails rather than run everything. After a build, `node scripts/test-shard.mjs tests/ScreenHire.E2E 2 8` prints
what CI's second end-to-end shard runs.

Every Aspire shard writes xUnit's CTRF report (`TestResults/*.ctrf.json`, uploaded as `durations-<suite>-<shard>` on
every run) and prints its 15 slowest tests. When the shards drift apart, refresh the measurements from a green run:

```
gh run download <run-id> -p "durations-*" -D TestResults/durations
node scripts/test-shard.mjs --record TestResults/durations > tests/shard-durations.json
```

A stale file still splits correctly, only less evenly, and a new class gets its estimate until the next refresh.

Meziantou.ShardedTest does the same per test, but it has no licence (ADR-0008), and it builds a VSTest `--filter`,
which `dotnet test` on this platform does not take. Each Aspire shard boots its own AppHost, about 40 s, so a shard is
worth adding while its tests run well past that. Raise a matrix's `shard` list and `shards` value together.

Three settings make each shard faster without changing what it proves:
- **Test-only builds** (`TEST_BUILD_FLAGS`): no analyzers, no code-style enforcement and no build-time OpenAPI
  document, about a third less compile time. `checks` builds with all of them and warnings as errors.
- **Every test in parallel** (`TEST_PARALLEL_FLAGS`, xUnit's `--parallel all`): tests in one class run side by side.
  Within one class xUnit otherwise runs tests one after another, so a shard's floor was its slowest whole class. The
  aggressive scheduler (`--parallel-algorithm aggressive`) gained nothing more and failed five load-sensitive
  end-to-end tests, so CI keeps the default one. The collections that
  must run alone say `DisableParallelization`, which `all` honors. Local runs keep xUnit's defaults, because the dev
  machine runs several gates at once.
- **No SQL in the test stacks' logs**: `AppHostFixture` sets `Microsoft.EntityFrameworkCore.Database.Command` to
  Warning for the API and the worker. Logging every command was 80 MB of console output per integration stack; failed
  commands still log.

Every job runs on `ubuntu-24.04`, pinned, and sets up through
[`.github/actions/setup`](../../.github/actions/setup/action.yml):
- pnpm (from `packageManager`), Node 24 with the pnpm store cached, and the .NET SDK from `global.json` with NuGet
  packages cached;
- `pnpm install --frozen-lockfile` and `dotnet tool restore`;
- `dotnet aspire setup`, which lays out the Aspire CLI's bundle (DCP and the dashboard) under `~/.aspire`. The AppHost
  sets `AspireUseCliBundle=true` and reads DCP from there, as on a dev machine;
- for `vitest`, none of the .NET steps; for `integration`, no Node or pnpm (no frontend starts, and
  `scripts/test-shard.mjs` runs on the runner's own Node) and a NuGet cache of its own without Playwright's browser
  drivers, which restores faster than the full 1.5 GB one;
- for `e2e` and `lighthouse`, Playwright's browsers cached on the `Microsoft.Playwright` version. The `e2e` job
  installs Chromium with `node scripts/install-browsers.mjs --no-build` after its test-only build, `lighthouse` with
  `pnpm e2e:install`.

The integration and end-to-end jobs pull the AppHost's container images (`APPHOST_IMAGES` in the workflow) before
the tests, so the pulls do not count against the fixture's 3-minute start. DCP builds its own small container-tunnel
image at the first start; it is not on the list.

CI never sees a secret: the repository's logs and artifacts are public, and nothing in the stack needs a real
credential (the consistency check's LLM is the Development fake). No test retries in CI either (flake policy).
`CiWorkflowTests` (`tests/ScreenHire.UnitTests/Tooling/`) fails when a job leaves `ubuntu-24.04` or has no timeout,
when `Gate` does not need every test job, when a matrix's `shard` list is not 1 to its `shards` value, when anything under `.github/` reads `secrets.` or runs on a self-hosted
runner, and when `APPHOST_IMAGES` differs from the images `apps/apphost/AppHost.cs` names. That is why `AppHost.cs`
names Azurite's and Mailpit's tags itself: bump them with the Aspire packages.

### What differs on Linux

The first runs found two differences from the Windows dev machine. Both were fixed at their cause:

- **Line endings in the contract.** The OpenAPI XML-comment generator joins a doc comment's lines with the build
  machine's newline, so `openapi.json` had `\r\n` from Windows and `\n` from Linux, and `verify:contract` failed on
  one of them. The API's last OpenAPI transformer (`OpenApiLineEndings`) writes `\n` on both.
- **Graceful stops.** On Windows DCP stops a process by killing it; on Linux it sends SIGTERM and waits about 12 s.
  The test stack has no dashboard, but Aspire's testing builder still pointed every service's telemetry exporter at
  `localhost:4317`, with retries on, so the worker spent its shutdown retrying exports and the restart test's stop
  timed out. `AppHostFixture` now clears `OTEL_EXPORTER_OTLP_ENDPOINT` in the test stacks, so no exporter is added.

### What each speed-up saved

Measured on the dev machine on 2026-10-31, one configuration at a time with nothing else of this task running, each
change added to the ones above it. Every run passed every test unless the table says otherwise.

| Change | Integration (622 tests) | End-to-end (111 tests) |
|---|---|---|
| Before | 177 s | 100 s |
| No SQL in the test stacks' logs (`AppHostFixture`) | 158 s | |
| Analyses and consistency checks ordered per application (messaging.md, "Ordering") | 155 s | 99 s (1 failure, below) |
| `--parallel all` | 143 s | 88 s |
| `--parallel all --parallel-algorithm aggressive` | 144 s | 84 s, 5 failures: not used |

So the two suites went from 277 s to 231 s, 17% less, before sharding. On CI the same changes apply to every shard,
and the test-only build flags take about a third off each shard's compile on top.

### Reading a failed run

1. `gh run list --branch <branch> --workflow CI` finds the run, and `gh pr checks <number>` shows each job on a PR.
2. `gh run view <run-id> --log-failed` prints the log of each failed step only. A test failure names the test and
   its assertion there, as it does locally.
3. When `integration` or `e2e` fails or is cancelled (a timeout included), the job uploads its `TestResults` folder as
   an artifact for 7 days: `integration-logs-<shard>` or `e2e-results-<shard>`, with each resource's console log in `logs/` and a
   screenshot of every failed end-to-end test in `screenshots/`. `gh run download <run-id> -n e2e-results-2` fetches the second shard's.
   Read the resource logs first, as locally ([`AppHostFixture`](#apphostfixture-testsscreenhiretesting)).
4. Rerun a job only to tell a flake from a defect, and record a flake in [Flake history](#flake-history) as for a
   local run.

### Time

With the sharded layout and every speed-up above, run 41246212243 (2026-10-31, commit `c8493aa6`) took 9 min 0 s
from start to `Gate`. The jobs themselves were short:

| Job | Wall clock per job |
|---|---|
| `checks` | 3.4 min |
| `vitest` (2 shards) | 1.7 to 2.0 min |
| `integration` (8 shards) | 1.9 to 4.0 min |
| `e2e` (8 shards) | 2.8 to 4.1 min |
| `lighthouse` | 3.5 min |

The rest was queueing. The organization, created on 2026-10-31, shows a limit of 20 concurrent jobs in its runner
settings, but its runs got at most 5, 6, 8, 9 and then 10 runners at once, and the other jobs waited for one. With 20
at once, the same run takes about 4.5 minutes, with no change to the workflow. Its test counts matched the local gate on
the same commit: 1333 unit, 623 integration and 111 end-to-end tests, and 1393 Vitest tests.

The first green run, before the suites were sharded, on 2026-10-31 at commit `579ed99b` (run 41213499536), took 13 min 6 s from start to `Gate`, with
the caches the run before it saved (NuGet, the pnpm store, Playwright's Chromium):

| Job | Wall clock | Its longest steps |
|---|---|---|
| `checks` | 12 min 17 s | Vitest 7 min 0 s, `verify:dotnet` 2 min 3 s, `pnpm check` 1 min 9 s, setup 54 s, .NET unit tests 25 s |
| `integration` | 10 min 54 s | the tests 9 min 32 s (622), setup 49 s |
| `e2e` | 13 min 1 s | the tests 9 min 18 s (111), setup with Chromium 2 min 32 s, `pnpm -r build` 32 s, image pulls 23 s |
| `lighthouse` | 4 min 33 s | setup with Chromium 2 min 31 s, 15 pages 1 min 40 s (every LCP 1505 to 1521 ms, CLS 0) |
| `gate` | 3 s | |

The test counts were the local gate's on the same commit: 1324 unit, 622 integration and 111 end-to-end tests, and
1401 Vitest tests (dashboard 1101, careers 127, realtime 81, marketing 64, telemetry 15, api-client 13). The runners
have 4 cores, so Vitest is the longest step of `checks`: the dashboard's suite took 409 s there and 47 s on the dev
machine.

## Flake policy

A test that passes on a rerun with no change is a bug. Fix the cause: an unawaited task, a fixed delay, a shared
port, shared data, or ordering between tests. Never add retries, sleeps or `Skip`, and never loosen an assertion to get
through the gate. In end-to-end tests, wait on a condition (`Expect(...)`, a health check), never on time. The gate
must pass twice in a row with no changes.

### Flake history

What failed the gate with no change, and the cause that was fixed, so the next flake is compared with these first.

| When | Symptom | Cause and fix |
|---|---|---|
| 2026-10-21 | 8 of 896 failed: five end-to-end tests with `ERR_EMPTY_RESPONSE` or a missing redirect, the template builder's Validation region still "checking", the acceptance journey's sandbox board still Queued after 45 s, and a 409 on a republish | (1) The built dashboard's `vite preview` died 12 s after start with exit code 0xC0000409 (a native abort) and no message, and later runs saw `astro preview` do the same for marketing. Every failure but the 409 followed: the screenshots show "Reconnecting…", because validation and the live updates go through the dashboard's `/v1` proxy, so the worker was never slow. The cause inside Node was not found (25 isolated starts of marketing's `astro preview` and a handful of the dashboard's `vite preview` never crashed), so `Built` no longer runs a preview server: Caddy serves the static builds and careers runs its own Node server. Resource logs are now captured. (2) The API's publish and close read postings without the worker's row lock and failed their row version when a board's answer landed; they now take the lock (job-distribution.md). (3) The template builder test could also read the previous change's validation result; the Validation region now sets `aria-busy` while a check is on its way, and the test waits for it to clear. |
| 2026-10-21 | 8 of 908 failed, all integration tests: `DevelopmentDemoTests.Starting_the_api_again_adds_nothing` timed out waiting for the API's second start, then seven `WorkerMessagingTests` got a 500 from the magic-link request in `ApiSession.SignInWithMagicLinkAsync` | Not proven. The two classes passed on their own straight after. Two runs later the whole integration suite failed at start with "Container runtime 'docker' was found but appears to be unhealthy" while two other worktrees ran their suites at the same time, and the next run was stopped for low memory, so the likely cause is the machine, not the tests. The failing run's resource logs were gone, because each run rewrites `TestResults/logs`; copy that folder as soon as a gate run fails, before rerunning anything. |
| 2026-10-21 | 1 of 907 failed: `WorkflowEngineTests.A_retried_manual_run_with_the_same_idempotency_key_runs_once` waited 60 s for the automation run to complete; it passed alone and in the next two full runs | Postgres logged `FATAL: sorry, too many clients already` seconds before. The API, the worker, their Wolverine agents and the tests each keep a connection pool, and together they passed Postgres's default of 100 connections while the worker assessed every applicant. The AppHost now starts Postgres with `max_connections=300`, which `DatabaseServerTests` checks. |
| 2026-10-22 | 15 of 981 failed, all end-to-end tests that open the careers server: the first with `ERR_EMPTY_RESPONSE`, the rest timing out after 30 s on navigation | Not proven. The careers resource log shows the Node server listening and then nothing: no request errors, no exit. Another worktree's processes were running at the same time. The next full run, with no change, passed 981 of 981. The run's resource logs and screenshots were kept outside the repository before the rerun. If careers stops answering again, first check whether its Node process is still alive: the preview servers in the first row died the same way, without a message. |
| 2026-10-23 | 1 of 1248 failed: `PipelineTests.Moving_a_candidate_by_keyboard_updates_the_board_and_a_second_session_without_a_reload` timed out on the board's card after pressing V; the next full run, with no change, passed 1248 of 1248 | Not proven. The failure screenshot shows the table still showing, with the J cursor on the second row, so V reached no element inside the table: keyboard focus had left it while the test opened its second browser context. A component test that refetches the rows keeps focus on the row link, so a live refetch is not shown to be the cause. If it recurs, check `document.activeElement` on the first page just before V. |
| 2026-10-25 | The same test failed the same way in 2 of 2 full `pnpm verify` runs on one branch, with the same screenshot. It passed alone, in the end-to-end project alone (76 of 76) and in the next full .NET run (1280 of 1280) with no change | Not proven. In a passing full run, the element focused just before V was the J row's link, with `document.hasFocus()` true. The failing state is still not captured: the next check should log `document.activeElement` only when the board's card is missing, so the failure itself carries it. |
| 2026-10-25 | 1 of 1324 failed: `EmailTests.An_automation_sends_an_organization_template_that_cannot_be_deleted_while_it_is_named` found no `EmailSent` entry on the timeline ("Sequence contains no matching element") | The automation's run completes when it queues the email, and `SendAutomationEmailHandler` mails it before it commits the timeline entry. The test waited for the email in Mailpit and the run, then read the timeline once, so it could read it between the send and the commit. It now waits for the entry. |
| 2026-10-25 | `PipelineTests.Moving_a_candidate_by_keyboard_updates_the_board_and_a_second_session_without_a_reload` again, 1 of 1324, the same timeout on the board's card | Not proven. The test now brings its page to the front after the second browser context opens, because a new window can take the window's focus, and checks the focus is inside the table before V. A recurrence fails on that check, which says the focus left the table, instead of timing out on the board. |
| 2026-10-25 | 1 of 1386 failed: `ApiReferenceTests.The_api_reference_and_its_openapi_file_are_served_with_production_settings` was cancelled after its 1-minute listen deadline while the first `GET /scalar` was still open (`TaskCanceledException` over an aborted socket). It passed alone straight after, and in the two full runs before it on the same branch | Not proven. The production API answered no request within the minute, during the full gate's busiest stretch, with the other AppHosts up. The change on the branch (custom roles) does not touch the API reference. If it recurs, keep the production API's resource log from `TestResults/logs` before rerunning, and see whether it was still starting or stuck on the first request. |
| 2026-10-25 | 1 of 1386 failed: `IntegrityPanelTests.Synthetic_applicants_show_their_levels_live_with_reasons_and_a_dismissal_is_logged` waited 60 s for Alex Reed's authenticity chip to read Verification suggested; it stayed at No concerns. It passed alone straight after, and in four full runs before it on the same branch | Not proven. The chip did render, with the level the table shows before the worker's assessment arrives, so either the assessment took over a minute under the full gate's load or its live update was missed. If it recurs, keep `TestResults/logs` and check the worker's log for the applicant's assessment time before rerunning. |
| 2026-10-25 | 1 of 1548 failed: `DashboardTests.Style_guide_is_a_development_page_only` found no heading within 5 s; the screenshot is the blank app background. It passed alone twice and in two full end-to-end runs straight after | The production build answers 404 at `/style-guide`, but the page's node imported the whole component catalogue first, about 1 MB of JavaScript across 75 chunks, which the browser had to download and parse before the 404 could render. Under the full gate's load that took longer than the wait. `+page.svelte` now imports the catalogue (`style-guide.svelte`) only when `dev`, so the production node is half a kilobyte and the 404 shows at once. |
| 2026-10-26 | 2 of 21 failed in one run of `ApplicationTasksTests`, `McpServerTests`, `TaskTests` and `HiringRealtimeTests`, right after merging main: `GET /v1/tasks?scope=Team` returned an empty list for a task a person (`ApplicationTasksTests`) or an API key (`McpServerTests`) had just created by hand, 219 and 472 ms into their tests. Each class passed alone, and the same four classes passed 21 of 21 twice straight after | Not proven. Both tests sit at the start of the run, and the list is the one query row-level security and the job scope filter, so a first request right after the AppHost starts is the suspect. If it recurs, keep `TestResults/logs` and see whether the task row existed and which `app.*` settings the list request carried. |
| 2026-10-30 | On the branding branch, with about 4 GB of the machine's memory free while other tasks ran: two full `pnpm verify` runs were stopped by Claude Code for low memory; a third finished with 35 of 1621 failed, every one a 60 s wait on the worker (automations, realtime, tasks, job close, telemetry, branding). The steps run with `dotnet test --max-parallel-test-modules 1` left 6 of 1621: `AutomationRealtimeTests`, `JobScopeRealtimeTests` and `RealtimeHubTests` waiting 30 s for a worker event, `ApiReferenceTests` past its 1-minute listen deadline, and two end-to-end tests (Chromium's "Target crashed", and a dashboard page that could not reach the API). Each passed alone straight after (13 of 13, 28 of 28), the integration project alone passed 489 of 489, and the next one-at-a-time .NET run passed 1621 of 1621 | Not proven; memory pressure from other work on the machine is the suspect. The worker logged about 480,000 lines in the 35-failure run, mostly EF Core commands, with duplicate-key retries from the integrity analyzers among them. If the realtime trio recurs on an idle machine, keep `TestResults/logs` and check whether the worker handled `ApplicationSubmitted` before the 30 s ran out. |
| 2026-10-31 | Three full runs on one branch failed in different places while about seven AppHosts from other worktrees ran: 6 integration and 2 end-to-end tests with the API's Postgres and blob health checks timing out; then 12 end-to-end tests on the careers server timing out on navigation (all 14 passed alone straight after); then 5 integration tests (`RealtimeHubTests`, `AutomationRealtimeTests`, `HiringRealtimeTests`, `JobScopeRealtimeTests`, one `EmailTests`) waiting 30 s for something only the worker does, 4 of which failed again alone | Not proven, but the last group has a likely cause. `AppHostFixture` waits only for `api` to be healthy. The worker is a generic host with no health check, so Aspire counts it as up when its `dotnet run` starts, and under that load its log shows the process starting 6 s before the first tests and Wolverine listening 36 s later, past the tests' 30 s waits. `AppHostFixture` now also waits, after the required resources are healthy, until the worker's log says Wolverine listens on its queue ("Started message listening at postgresql://worker/"), unless the fixture removed the worker. |
| 2026-10-31 | One gate run: `AutomationRealtimeTests` waited 30 s for `TeamNotified`, then 95 end-to-end tests failed in the fixture's start (`TaskCanceledException`) while other worktrees ran their suites. The realtime test passed alone once and failed alone once; the same gate had passed the run before | The fixture waited for the API only. The worker is a `dotnet run` with no health endpoint that starts after the API, and the failing run's log shows its first line 29 s after it started, so the first test that needs the worker raced its own 30 s wait against that start, and lost on a loaded machine. `main` now has the fix, `AppHostFixture.WaitUntilWorkerListensAsync`, which waits for the worker's log to say Wolverine listens on its queue before any test runs. The fixture start itself timing out is the machine: low memory and 75 node processes from other tasks. |
| 2026-10-31 | 4 of 852 dashboard Vitest tests failed in one gate run with `Test timed out in 5000ms`: the first test of the hiring templates, branding, organization settings and scorecard template page files. The same files passed 28 of 28 alone straight after, each of those tests in 0.7 to 1.2 s, and the three gates before it on the branch passed | Not proven. The tests are cold first renders in files the branch does not touch, and the gate ran while other worktrees ran their own suites (24 Node processes, a Vitest worker pool per run). A 5 s limit with a 1 s test leaves a fivefold margin, so the run was starved of CPU, not slow in code. If it recurs on a quiet machine, run the file with `--reporter=verbose` and compare the first test's time with the rest of its file. |
| 2026-10-31 | 3 of 1595 end-to-end tests failed in one gate run: `ScorecardTests` (the stage never read Offer within 30 s of the scorecard), `TeamWorkTests` (Next steps still empty 30 s after a manual run) and `DashboardTests.Signed_out_visitor_is_sent_to_sign_in` (5 s redirect wait). Integration and Vitest were green in the same run, and both automation tests had passed in two earlier gates on the branch | The worker log shows both automations `Completed` at 04:56:56, seconds after the tests gave up, and nothing between 04:56:34 and 04:56:56: in those 21 s it ran 69 integrity analyses (383 analyzer-run inserts, about 2,000 `jobs` queries) for the other end-to-end tests, with dozens of commands stalled 1.5 s at once at 04:56:38. CPU was at 100% from four other worktrees' runs, and the gate runs the integration and end-to-end projects side by side. So an automation message waited behind an integrity burst on a starved machine. Not a defect in the automations. If it recurs on a quiet machine, check whether `RunAutomations` should not queue behind integrity analysis. |
| 2026-10-31 | 3 of 1618 failed in one gate run on the talent pool branch, and 2 of the same 12 when run alone straight after: `AutomationRealtimeTests`, `RealtimeHubTests.An_event_raised_by_the_worker...` and `JobScopeRealtimeTests` waited 30 s for an event the worker raises (`TeamNotified`, `LiveUpdatesChecked`); events the API raises arrived. The same 12 passed on `main` and then on the branch, with no change | Not proven. A lead: on `main` and on the branch alike, the worker's log shows it taking envelopes from the real-time queue ("No known handler for ... from postgresql://realtime/") though it listens only on the worker queue, and any event it takes never reaches a dashboard. The worker does that on `main` too (476 envelopes in a full integration run that passed), so it is not enough on its own. The same three failed again in the next full gate on the branch, while the whole integration suite passed alone on the branch (518 of 518) and on `main` (494 of 494): they fail only beside the end-to-end stack, and only for events the worker raises. If it recurs, copy `TestResults/logs` before rerunning and check, in the API's log, whether its real-time listener handled anything in the 30 s the test waited. In the same gate, `form-builder.test.ts` failed to load once with Windows' "UNKNOWN: unknown error, open" on a file in `node_modules`, while 112 Vitest workers started; it passed alone and in the next run |
| 2026-10-31 | 1 of 1618 failed on the talent pool branch: `TeamWorkTests.A_note_a_tag_the_tag_filter_a_manual_run_and_its_task_completed` found a submit button in a candidate panel at 4.32:1 (#797978 on #fefefe) in the axe check right after Add tag. The same test passed in the two full gates before it on the same branch, which does not change that screen | The colour is between the button's disabled and enabled looks (it fades to half opacity, with `transition-all`), so axe sampled it mid-transition. The check already waited for transitions in flight, but this one had not started yet when it looked. `ExpectNoAccessibilityViolationsAsync` now waits two animation frames, so a state change the page has just made starts its transition, then waits for every transition |
| 2026-10-31 | 2 of 942 Vitest tests failed on the talent pool branch: the style guide page's "shows a panel for every component" and "passes axe with every component" each passed their 20 s limit (23.9 s and 23.3 s). Alone they take 5.3 s and 4.3 s | Not proven beyond load. Four other task worktrees ran their own stacks and gates on the machine at the same time (41 `dotnet` processes), and the dashboard suite created jsdom 113 times in 1,689 s of tracked time. The page renders every component in the catalogue, so it is the suite's slowest by far. If it recurs with the machine otherwise idle, render the catalogue once per file rather than once per test |
| 2026-10-31 | On the country picker branch, with `main`'s listening wait not yet merged, four full runs failed the same realtime tests as the rows above. With a wait for the worker's durability agent added, one full run still failed `JobScopeRealtimeTests`, and the next `WorkerMessagingTests`' restart test | The realtime test's `NotifyTeam` ran 33 s after its application, behind the burst of every test starting at once in the one worker queue; `EventsConnection` waited 30 s, the only wait on the worker shorter than the suite's 60 s (`WolverineStore`, `Mailpit`, `ScheduledMessages`), and now waits 60 s too. The restart test's 60 s began when Aspire called the restarted worker running, 59 s before Wolverine listened; `StartWorkerAsync` now waits for the new node's agents (`WaitUntilWorkerRunsAgentsAsync`). The fixture's start-up wait also waits for the agents after `main`'s listening wait. The worker's "No known handler for ... from postgresql://realtime/" lines, the lead in the talent pool row, are not reads from the real-time queue: Wolverine's template is "No known handler for {Name}#{Id} from {ReplyUri}", and the reply address is the sender's listener, so they are the worker dropping its own copy of an event the API raised (`RouteTenantEvents` sends every tenant event to the worker queue as well). |
| 2026-10-31 | On the Slack branch, 3 of 1679 failed in `McpServerTests`: `Each_tool_follows_the_hiring_rules...` read a timeline entry with no actor, and `A_read_key_is_refused_every_write...` and `Another_organizations_jobs_and_candidates_are_out_of_reach` found `IntegrityAssessed` after `Submitted`. On a clean copy of main, `Another_organizations...` failed in 2 of 3 runs alone; on the branch two tests failed in 3 of 3 | The tests read the application's timeline about a second after creating it, and the integrity engine writes its `IntegrityAssessed` entry in the background, so whether it was in yet was a race. The tests are about the key's changes, so they now leave the engine's entry out (the `Assessed` filter in `McpServerTests`; the talent pool branch made the same fix, and the merge kept its version). |
| 2026-10-31 | `IntegrityPanelTests.Synthetic_applicants_show_their_levels_live...` again, 1 of 1679 in the same run: Alex Reed's chip stayed at No concerns for 60 s. It passed alone twice straight after | Not proven. Alex Reed is the farm's first member, so no duplicate signal applies (`DuplicateAnalyzer` never revisits earlier applications). Two of his five signals in the golden file, `CNT_INCONSISTENCY` and `CNT_RESUME_GENERATOR`, come from the content stages, which reassess the application as each lands; a content stage still queued under the full gate's load would leave the earlier level showing. The run's logs were overwritten by the reruns. If it recurs, keep `TestResults/logs` and check when the worker handled his consistency check and document analysis. |
| 2026-10-31 | On the Slack branch, one gate run failed 7 of 1888 end-to-end tests (`CandidateNotesTests`, `TemplateBuilderTests`, `DashboardTests`, `SettingsTests`, `JobPageTests`, `ScorecardTests`, `TeamWorkTests`), each with "element(s) not found" on a heading or a row after the dashboard loaded; the signed-out redirect test ended on a blank page at the app's background colour. The same seven classes alone then failed 5 of 26 and 2 of 26, a different set each time | Not proven; the machine was the suspect. It ran at 100% CPU with 41 dotnet processes and 22 containers from other tasks' stacks, and the set that failed shrank as the rerun count rose. With the evidence capture below, the last rerun's failures show no console error, no uncaught page error, no failed request and no HTTP error beyond the expected 401 before sign-in: after the sign-in's second `/v1/auth/me`, the Jobs heading took longer than the test's 5 s wait. `BrowserTest` now prints the browser's console errors and warnings, page errors, failed requests and HTTP errors when a test fails, so the next blank page carries its own cause. |
| 2026-10-31 | 5 of the full gate's tests failed on the reports branch: `McpServerTests.Each_tool_follows_the_hiring_rules_and_its_changes_land_in_activity_under_the_key` threw reading a null `actorApiKeyId`; `AutomationRealtimeTests` and `HiringRealtimeTests` waited 30 s for a worker's event; `DevelopmentDemoTests.Each_candidate_email_automation_sent_an_email` waited 60 s for Mailpit; `ApiReferenceTests` hit its 1-minute listen deadline. The last four passed alone straight after; the MCP test failed again alone, then passed | The MCP test read the owner's timeline, which includes the worker's `IntegrityAssessed` entry (no actor) once the worker has assessed the application, so it failed whenever the assessment beat the read. It now leaves that entry out. The other four are not proven: they wait on the worker or a second stack's start, and the end-to-end suite booted its own stack beside them. |
| 2026-10-31 | Two more full runs on the same branch each failed 3 of 1584, all waiting on the worker: `AutomationRealtimeTests` and `HiringRealtimeTests` (30 s for an event the worker raises to reach a SignalR client) in both, then `RealtimeHubTests` once and `AutomationEmailTests.A_new_application_gets_the_application_received_email` (60 s for Mailpit) once. Other worktrees' stacks were running beside them | Not proven. The branch (reports) changes no messaging. Run back to back with only these three classes, a clean checkout of `main` failed 2 of 9 (`AutomationRealtimeTests` and the email test) and the branch then passed 9 of 9, so at least two of them fail on `main` under the same load. The worker ran the NotifyTeam action and logged "No known handler for TeamNotified from postgresql://worker/", which is expected: the worker drops its copy, and the API should push the copy on the realtime queue. The API's captured log ends before the 30 s wait does, so whether the API received it is not shown. If it recurs, capture the API's log until the test ends and look for the realtime queue's handling of the event. |
| 2026-10-31 | After merging `main` (which brought `AppHostFixture.WaitUntilWorkerListensAsync`) into the reports branch, the realtime and email tests above passed in the full gate, and 3 of 1775 end-to-end tests failed instead: `CandidateNotesTests` and `TalentPoolTests` waited 5 s for the Jobs heading after signing in, and `ScorecardRequestTests` found the "Ask for a scorecard" sheet's Scorecard list still empty. All three, with `ReportsTests`, passed 5 of 5 alone straight after, and the next full gate passed 1775 of 1775. The gate after that failed 1 of 1775: `IntegrityPanelTests` waited 60 s for Alex Reed's authenticity chip to leave No concerns, and passed alone straight after | Not proven. Each failure is a first load that did not finish inside its wait while the integration and end-to-end stacks ran side by side, and none of the three touches Reports. If the sign-in wait recurs on a quiet machine, check how long the dashboard's first `GET /v1/auth/me` takes in the end-to-end stack's API log. |
| 2026-10-31 | On the verification branch, seven full gate runs failed in different places while other worktrees ran their stacks: 32, then 7, then 3 integration tests waiting on the worker (automations, realtime, the integrity pipeline); one run stopped by Claude Code for low memory with about 5 GB free; one with 4 dashboard Vitest workers exiting at start with Windows' 0xC0000142; and two with 5 and 27 end-to-end tests on a blank page, the first page's heading missing within 5 s while the API logged 2,000 to 3,800 lines a second as every end-to-end test signed in at once beside the integration suite. Every failing test passed alone straight after, and the next gate, with no other stacks on the machine, passed 1894 of 1894 | Not proven beyond load. The realtime trio stopped failing once `main`'s `WaitUntilWorkerListensAsync` was merged. The end-to-end failures all fell in the first 30 s of the project's run, so if they recur on a quiet machine, check whether the fixture should let the API settle before the first sign-ins, and keep `TestResults/logs` first |
| 2026-10-31 | On the verification rename branch, one full gate run failed 1 of 2003: `DistributionTests.The_indexing_dispatcher_sends_queued_notifications_and_google_never_reaches_closed` waited 60 s for the Google posting to reach Submitted. The test publishes the dispatch message itself, so the worker's once-a-minute cron is not what it waits on. The whole `DistributionTests` class passed alone straight after (10 of 10), and the gate run before the code review commits passed 2001 of 2001. Other worktrees' stacks were running beside it | Not proven beyond load on the worker's queue, like the rows above. Nothing in the rename touches distribution. If it recurs on a quiet machine, read the worker's log for the `SyncJobPosting` command of that job before changing the 60 s wait |

| 2026-10-31 | `ApplyTests.Candidate_applies_with_JavaScript_turned_off` failed twice: once in a local end-to-end benchmark run and once on CI (run 41247065921, end-to-end shard 6/8). Both times `ApplySteps.ApplyAsync` waited 30 s for the applied page after the last "Send application", Playwright logged no navigation in that time, and the failure screenshot shows "Application sent": the submission landed, more than 30 s late. Three local end-to-end runs straight after passed it | Not proven. The API checked the upload (the blob reads every submission makes, all under 0.1 s) about 30 s before the wait ran out, so the time went after that check, in the submit or in the careers server's redirect. Neither the API nor the careers server logged requests, so nothing named the slow one. The test stacks now log every API request with its duration (`AppHostFixture`, `Microsoft.AspNetCore.Hosting.Diagnostics`): if it recurs, read the `POST /v1/public/...applications` line in the artifact's `api.log` to see whether the API was slow or the careers server was |
| 2026-10-31 | CI run 41244830579, integration shard 4/8: `PublicApplicationTests.Submission_creates_the_candidate_and_application_and_publishes_its_event` threw "Sequence contains more than one element" reading the application's timeline | The test took the timeline's only entry as the submission. Once analyses of one application stopped racing (messaging.md, "Ordering"), the worker's `VerificationAssessed` entry landed within about 400 ms, before the test read. It now picks the entry by type, `Submitted`. |

## Gate time

Measured on the owner's machine (Windows 11, Docker Desktop), warm caches. Target: under ~8 minutes.

| | Before this harness | After |
|---|---|---|
| `pnpm verify` | 80 s | 47–54 s |
| .NET tests | 49 s: 2 integration tests, each booting its own AppHost with every frontend | ~33 s wall clock for 111 tests: unit (49, <1 s), integration (52, 20 s, frontends off) and end-to-end (10, 33 s) in parallel, one AppHost per assembly (measured 2026-10-20 with sign-in) |
| Vitest | none | 53 tests, ~1 to 9 s per package |

Measured 2026-10-20 over two consecutive runs with no changes in between; both passed.

With messaging (2026-10-20, two consecutive passing runs): `pnpm verify` 79-83 s; .NET tests ~54 s wall clock for 35
tests, of which integration is 19 tests in about 53 s. The longest is `RecurringMessageTests` (~33 s): Wolverine's recurring
agent acts on a manual trigger on its next tick, and that interval is not configurable. It runs in parallel with the
others, and the `WorkerLifecycle` collection runs alone after them.

With the marketing site v1 on top of messaging (Lighthouse on 15 pages, 59 .NET tests, 73 Vitest tests), `pnpm verify`
took 2 min 58 s and 2 min 54 s over two consecutive runs on 2026-10-20; both passed. Lighthouse is about 90 s of that.

With the MVP acceptance journey (881 .NET tests), `pnpm verify` took 3 min 55 s and 3 min 40 s over
two consecutive runs on 2026-10-20; both passed. The end-to-end assembly, the journey included, takes about 65 s of
that, in parallel with the integration tests (about 85 s).

With the gate-stability fixes (898 .NET tests; `Built` serves the frontends without preview servers, and publish and
close take the posting locks), `pnpm verify:test` passed five times in a row on 2026-10-21, at commit `891dd3d`, with
the suites in parallel as the gate runs them: 109, 112, 113, 111 and 114 s wall clock. Each run: unit about 1 s,
end-to-end 53 to 59 s, integration 1 min 46 s to 1 min 51 s, 898 passed, none failed, and no resource stopped
(`TestResults/logs`).

With Lighthouse out of the gate (947 .NET tests), `pnpm verify` took 3 min 26 s on 2026-10-21 and passed. The
integration tests are the longest suite, at about 2 min 05 s, in parallel with the end-to-end tests (about 65 s).
