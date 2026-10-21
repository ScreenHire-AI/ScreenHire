# Testing

Every tier of test has a home, a pattern and a place in `pnpm verify`, so a feature task only adds tests. The gate is
the only CI for now ([`/verify`](../../.claude/skills/verify/SKILL.md)); a suite that is not in it does not exist.

## Which tier for what

| Tier | Where | Use it for | Needs |
|---|---|---|---|
| .NET unit | `tests/ScreenHire.UnitTests` (xUnit v3 + AwesomeAssertions) | Domain rules (`Job` lifecycle and versions, template versioning, the definition validator and its guardrails, the integrity ruleset), the integrity analyzers with local fakes, golden files of the synthetic applicants' assessments, pure API logic (mappings, validation, the workflow diff, sanitizing). The API grants it `InternalsVisibleTo`. | Nothing |
| Vitest | next to the code as `*.test.ts`: `apps/dashboard` (Svelte 5 components on jsdom with @testing-library/svelte, including the shared question components in the questionnaire preview; pages and query-backed components render inside `src/test-support/with-query-client.svelte` with `fetch` stubbed), `packages/api-client` (the fetch mutator), `packages/realtime` (the SignalR client, event schemas against the C# contract file), `apps/careers` (`src/lib`: formatting, the apply flow's state and checks, tenant hosts, the brand stylesheet; `src/components`: the shared question components rendered through Astro's container API with the Svelte renderer, on the server as the apply page uses them and as a `client:load` island), `apps/marketing` (JSON-LD builders, page registry, pricing, the post-build checks in `scripts/`) | Frontend logic and component rendering with fixed data | Nothing |
| Aspire integration | `tests/ScreenHire.IntegrationTests` | Endpoints, persistence, data rules (tenant scoping, status filters), messaging (outbox, worker handlers, retries, dead letters), anything that needs Postgres | Docker |
| End-to-end | `tests/ScreenHire.E2E` (Microsoft.Playwright for .NET on xUnit v3, axe) | User journeys in a real browser against the whole stack: rendered content, JSON-LD, no-JS public pages, accessibility | Docker, Chromium, `pnpm -r build` |

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
| Watch a Vitest suite | `pnpm --filter @screenhire/dashboard exec vitest` (the `test` scripts use `vitest run`, which never watches, so the gate cannot hang) |

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
and `GoogleIndexing:Mode=Local`, the in-process Indexing API fake, so the Google queue runs with no account.

Every resource's console log goes to `TestResults/logs/<fixture>/<resource>.log` next to the test assembly (for
example `tests/ScreenHire.E2E/bin/Debug/net10.0/TestResults/logs/E2EFixture/dashboard.log`), streamed with Aspire's
`ResourceLoggerService` and rewritten each run. A resource that stops with an error while the tests run is kept in
`fixture.StoppedResources`, and a failing end-to-end test prints it with its exit code and log, so a server that dies
mid-run names itself instead of failing each test with its own symptom. Read these logs first when a test fails on a
server that stopped answering.

The API and every JS app have HTTP health checks, so the fixtures wait for servers that answer, never for a fixed
delay. The API only starts listening after its Development migrate-and-seed, so a healthy API has its schema and
Brightwater. With the demo on (`DevelopmentDemoFixture`, `pnpm dev`), `/health` also waits for the demo's data. A resource that fails to start fails the fixture at once, and a timeout names the resource.

### Test data: isolate by organization

There is no reset between tests and no Respawn. Instead:

- **Tests that write data create their own organization** with `fixture.CreateOrganizationAsync(...)`
  ([`TestData`](../../tests/ScreenHire.Testing/TestData.cs)), which gives it a unique slug, and touch only that
  organization's rows. `TestData.NewJob(organization, slug)` builds a draft job with defaults. Tests sharing one
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
  SSO user `brightwater-owner`) is read-only like its organization: sign in as them, change nothing.
- **Careers applicants.** `fixture.ApplyAsync(slug, jobId, applicant, ct)` (`SyntheticApplications` in
  `tests/ScreenHire.Testing`) submits one of the synthetic applicants (`SyntheticApplicants`) to a published job the
  way the careers server does, with its address, session and resume, so the integrity engine assesses it. Integration
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
proves the steps connect, so a change that breaks the hand-off between two areas fails here. It keeps the integrity
levels deterministic in a browser: the genuine candidate's browser runs no JavaScript, so Playwright's automation
markers and the device check never reach the API, and the bot uses Playwright's browser as it is.

### Golden files

Two unit tests compare output with files in the repository, and fail with the command that rewrites them:

| Test | File | Rewrite with |
|---|---|---|
| `RealtimeContractTests` | `packages/realtime/contract/events.json` | `UPDATE_REALTIME_CONTRACT=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*RealtimeContractTests"` |
| `IntegrityGoldenTests` | `tests/ScreenHire.UnitTests/Integrity/Golden/*.json` | `UPDATE_INTEGRITY_GOLDEN=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*IntegrityGoldenTests"` |

Read the diff before committing a rewrite: it is the review of what changed for a person reading the output.

## Marketing site checks

The marketing site's SEO and performance budgets (ADR-0003) are checked at three tiers:

| Check | Where | Runs in |
|---|---|---|
| Unique titles and descriptions in the page registry, JSON-LD builders, pricing placeholders | Vitest, `apps/marketing/src/lib/*.test.ts` | `pnpm -r --if-present test` |
| At most 15 KB of brotli JS per page (JSON-LD excluded), no broken internal links or Open Graph images, a body-font preload on every page | `apps/marketing/scripts/check-dist.mjs`, after `astro build` | `pnpm -r build` |
| Mobile LCP under 1.8 s, CLS under 0.05, JS bytes, on every page in the sitemap | `apps/marketing/scripts/lighthouse.mjs` (Lighthouse on Astro's preview server) | `verify:lighthouse` |
| Unique title, description and canonical; hreflang; Open Graph image; JSON-LD types per page; no script but JSON-LD; axe; no sideways scroll at 390px; 404 | `tests/ScreenHire.E2E/MarketingTests.cs` | `verify:test` |

`MarketingTests` keeps a table of every indexable page and the schema.org types it must carry, and fails if the
sitemap lists a page the table does not, so a new page cannot skip the checks. Lighthouse uses CHROME_PATH, then
Playwright's Chromium, then an installed Chrome. Its simulated mobile numbers were identical to within 3 ms across
two consecutive full runs (about 90 s each), which is why it is in the gate. Run a subset with
`pnpm --filter @screenhire/marketing lighthouse / /pricing/`; in Git Bash set `MSYS_NO_PATHCONV=1` first, or the
paths are rewritten into Windows paths.

## Flake policy

A test that passes on a rerun with no change is a bug. Fix the cause: an unawaited task, a fixed delay, a shared
port, shared data, or ordering between tests. Never add retries, sleeps or `Skip`, and never loosen an assertion to get
through the gate. In end-to-end tests, wait on a condition (`Expect(...)`, a health check), never on time. The gate
must pass twice in a row with no changes.

### Flake history

What failed the gate with no change, and the cause that was fixed, so the next flake is compared with these first.

| When | Symptom | Cause and fix |
|---|---|---|
| 2026-10-21 | 8 of 896 failed: five end-to-end tests with `ERR_EMPTY_RESPONSE` or a missing redirect, the template builder's Validation region still "checking", the acceptance journey's sandbox board still Queued after 45 s, and a 409 on a republish | (1) The built dashboard's `vite preview` died 12 s after start with exit code 0xC0000409 (a native abort) and no message, and later runs saw `astro preview` do the same for marketing. Every failure but the 409 followed: the screenshots show "Reconnecting…", because validation and the live updates go through the dashboard's `/v1` proxy, so the worker was never slow. The cause inside Node was not found (25 isolated starts of marketing's `astro preview` and a handful of the dashboard's `vite preview` never crashed), so `Built` no longer runs a preview server: Caddy serves the static builds and careers runs its own Node server. Resource logs are now captured. (2) The API's publish and close read postings without the worker's row lock and failed their row version when a board's answer landed; they now take the lock (job-distribution.md). |

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
