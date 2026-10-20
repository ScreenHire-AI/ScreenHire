# Testing

Every tier of test has a home, a pattern and a place in `pnpm verify`, so a feature task only adds tests. The gate is
the only CI for now ([`/verify`](../../.claude/skills/verify/SKILL.md)); a suite that is not in it does not exist.

## Which tier for what

| Tier | Where | Use it for | Needs |
|---|---|---|---|
| .NET unit | `tests/ScreenHire.UnitTests` (xUnit v3 + AwesomeAssertions) | Domain rules (`Job` lifecycle), pure API logic (mappings, validation). The API grants it `InternalsVisibleTo`. | Nothing |
| Vitest | next to the code as `*.test.ts`: `apps/dashboard` (Svelte 5 components on jsdom with @testing-library/svelte), `packages/api-client` (the fetch mutator), `apps/careers` (`src/lib/format.ts`) | Frontend logic and component rendering with fixed data | Nothing |
| Aspire integration | `tests/ScreenHire.IntegrationTests` | Endpoints, persistence, data rules (tenant scoping, status filters), anything that needs Postgres | Docker |
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
| `Built` | `preview` scripts over `pnpm -r build` output: what ships | `tests/ScreenHire.E2E` |
| `None` | no JS apps; API, worker and infrastructure only | `tests/ScreenHire.IntegrationTests` |

The API and every JS app have HTTP health checks, so the fixtures wait for servers that answer, never for a fixed
delay. The API only starts listening after its Development migrate-and-seed, so a healthy API has its schema and
demo data. A resource that fails to start fails the fixture at once, and a timeout names the resource.

### Test data: isolate by organization

There is no reset between tests and no Respawn. Instead:

- **Tests that write data create their own organization** with `fixture.CreateOrganizationAsync(...)`
  ([`TestData`](../../tests/ScreenHire.Testing/TestData.cs)), which gives it a unique slug, and touch only that
  organization's rows. `TestData.NewJob(organization, slug)` builds a draft job with defaults. Tests sharing one
  AppHost therefore never see each other's data, in any order or in parallel.
- **The seeded demo organization (`brightwater`) is read-only.** Assert on it (the end-to-end journeys do), never
  change it: other tests count its jobs.
- `TestData` is the one place tests write to the database directly. When tenant isolation (row-level security) lands,
  adapt it there rather than in each test. Today it connects as Aspire's `postgres` superuser, which owns the tables
  and so bypasses RLS. With RLS, `TestData` must write as the application's own role (not the owner, no
  `BYPASSRLS`) with a transaction-local `set_config('app.tenant_id', …, true)`. Otherwise every test passes without
  exercising RLS. Add an integration test that proves a cross-tenant read fails in the database itself.
- **Authentication:** when sign-in lands, end-to-end tests sign in through the local mock OIDC server in the AppHost,
  as a user does. Never add an auth bypass, test-only endpoint or anonymous policy to keep a test green.

### `E2EFixture` and `BrowserTest` (`tests/ScreenHire.E2E`)

`E2EFixture` extends `AppHostFixture` with `Frontends=Built`, waits for the API and all three frontends to be
healthy, and launches one headless Chromium for the assembly. Test classes derive from `BrowserTest`, which gives:

- `Page`, in a **new browser context per test** (no shared cookies or storage);
- `GoToAsync("careers", "/brightwater")`, which resolves the URL from Aspire and expects a 2xx;
- `ExpectNoAccessibilityViolationsAsync()`, which runs axe for WCAG 2.2 A/AA and lists every violation with its
  selector. Fix violations; do not exclude rules. If one must be excluded, scope it to one selector with a reason;
- **a full-page screenshot when a test fails**, at
  `tests/ScreenHire.E2E/bin/Debug/net10.0/TestResults/screenshots/<test>.png` (the path is printed in the test output).

Assert on what a person sees (headings, rows, cell text, links by accessible name) with Playwright's `Expect(...)`,
which retries until its timeout. Status codes alone are not an end-to-end assertion.

## Flake policy

A test that passes on a rerun with no change is a bug. Fix the cause: an unawaited task, a fixed delay, a shared
port, shared data, or ordering between tests. Never add retries, sleeps or `Skip`, and never loosen an assertion to get
through the gate. In end-to-end tests, wait on a condition (`Expect(...)`, a health check), never on time. The gate
must pass twice in a row with no changes.

## Gate time

Measured on the owner's machine (Windows 11, Docker Desktop), warm caches. Target: under ~8 minutes.

| | Before this harness | After |
|---|---|---|
| `pnpm verify` | 80 s | 47–54 s |
| .NET tests | 49 s: 2 integration tests, each booting its own AppHost with every frontend | ~27 s wall clock for 24 tests: unit (9, <1 s), integration (8, 19 s, frontends off) and end-to-end (7, 27 s) in parallel, one AppHost per assembly |
| Vitest | none | 27 tests, ~1 s per package |

Measured 2026-10-20 over two consecutive runs with no changes in between; both passed.
