---
paths:
  - "tests/**"
  - "apps/**/*.test.ts"
  - "packages/**/*.test.ts"
  - ".github/**"
  - "scripts/test-shard.mjs"
  - "scripts/ci-timings.mjs"
  - "scripts/ci-together.mjs"
---
# Rule: what earns a test, and what earns a CI job

On 2026-11-18 a pull request waited a median 508 s for `Gate` (the 23 of the last 30 green runs that were not rerun,
347 to 1,229 s) and nothing watched what that cost. The suite was not padded: 207 end-to-end and about 1,200
integration tests, with few duplicates. The wait was structure:

- Two tests walked every dashboard page in one method, 169 and 203 s each, and a shard cannot end before its longest
  test.
- Their classes had no measurement, so the sharder weighed them at an eighth of what they cost.
- One row of those walks opened the same seven pages eight times each.
- An integration shard spent 98 s and an end-to-end shard 174 s before its tests, on every shard.
- A pull request started 21 jobs on an account that runs 20, so one always waited.

The measurements, what each change saved and what the audit of the tests found are in
[testing.md, "Time"](../../docs/process/testing.md#time). This rule keeps the wait from growing back.

## The rule

### The tier

Pick the lowest tier that can fail for the reason the bug would. The tiers, and what each one needs, are in
[testing.md, "Which tier for what"](../../docs/process/testing.md#which-tier-for-what).

- **A behaviour belongs to one tier.** The lower tier owns the logic and its cases. A higher tier keeps one test of the
  wiring, not a copy of the cases. An end-to-end test of a plan limit shows the notice a person reads, and the
  integration test that owns the limit proves which job is refused.
- **An end-to-end test names what only a browser proves:** what a person sees and can reach, with the whole stack
  behind it. A status code, a header or a row in the database is an integration test's assertion.

### The bar for a test

- **Say what a user or a rule loses if the test goes.** A test nobody can say that for does not get added.
- **Run the real code.** A test never asserts its own copy of the logic (a mirror test): a copy passes whatever
  production does. A constant asserted against the same literal pins nothing either. Tie it to the thing it must
  agree with, such as the page that states it.
- **Assert behaviour, not the code restated.** A test that breaks on a harmless refactor and catches nothing costs
  every later change.
- **Never remove a test only because it has not failed.** To remove one, name the test that covers the behaviour and
  read it. A removal gives its reason: a duplicate of a named test, the wrong tier, or it pins nothing.
- **Wait on a condition, never on time,** and never retry or skip: the
  [flake policy](../../docs/process/testing.md#flake-policy). In the .NET tests that is Playwright's `Expect(...)` or
  `Wait.UntilAsync` (`tests/ScreenHire.Testing`). A window that proves nothing more happens is
  `Wait.ForNothingAsync`, which takes its reason.
- **Know what a test pays before its first assertion.** Measured on CI on 2026-11-18: an end-to-end test of a public
  page takes about 1.3 s in all, and one with its own organization, the Dev SSO round trip and the first dashboard
  load at least 3 to 4 s, on 134 of the 225 end-to-end tests. An integration test's organization and session cost
  under 0.3 s: half of that suite's 1,215 tests take less than that in all. With 4 tests at once that is about 15 s of
  an end-to-end shard's 300 and about 7 s of an integration shard's 220, so a test keeps its own organization and
  signs in as a person does ([test data](../../docs/process/testing.md#test-data-isolate-by-organization)). Sharing
  pays when the seed is what costs: tests that only read one seed build it once a run and share it (`ProofScene`,
  `BusyOrganization`), and each still signs in for itself.
- **A shard cannot end before its longest test.** xUnit runs 4 tests at once on the runners, so a test that walks
  many pages is a theory over groups of them, one row a group (`ProofWalk`), never one method. No test runs over a
  quarter longer than an average shard's parallel tests take.
- **A class that boots an AppHost of its own runs alone,** in a collection with `DisableParallelization`
  (`OwnStackRun`), so two stacks never start at once and the sharder can weigh the class by its time on the clock.

### The bar for a CI job

- **Cost is slots, not minutes.** The repository is public, so runner minutes are free. What is scarce is the 20 jobs
  the account runs at once, which every open pull request shares. A pull request starts 18. Spend a slot by giving
  one up.
- **Name what a new job catches that nothing else can,** in the workflow's comment, with what it costs.
- **Size the suites to finish together.** A pull request waits for its slowest job, so a slot belongs to the suite
  that is the pole, and a suite far under it gives shards up. A shard is not free: before its first test an
  integration shard spends about two minutes and an end-to-end shard about three (setup, the builds, its own AppHost),
  which is why integration has 5 shards and not 8: its slowest job still ends before the slowest end-to-end job.
- **Cut shards by measured time.** `scripts/test-shard.mjs` weighs each class by what CI measured
  (`tests/shard-durations.json`). Refresh the file when one shard's test step runs about 25% past its suite's average
  in several runs (`node scripts/ci-timings.mjs --last 10` prints each shard's), or when `TestShardPlanTests` says the
  measurements are stale:

  ```
  gh run download <run-id> -p "durations-*" -D TestResults/durations/<run-id>
  node scripts/test-shard.mjs --record TestResults/durations
  ```

  A class heavier than an average shard is split.
- **Measure a change to CI by paired runs.** At least 3 runs on one commit, one after another: `ci.yml` cancels an
  earlier run on the same ref. Decide on what a queue cannot touch: a named step's seconds, a shard's test step,
  job-seconds (`node scripts/ci-timings.mjs <run id>...`). Count a run's seconds to `Gate` only when no job in it
  waited over 20 s for a runner: 18 of the 30 baseline runs had one that did. Dispatching runs on a branch
  (`gh workflow run CI --ref <branch>`) is past the one push `unattended-runs.md` gives a task, so it needs the
  owner's word in the brief.
- **No changed-files test selection on the gate:** no `--changed`, `vitest related` or `--only-changed`. Vitest's
  import graph cannot see the tests that read source as text, and nothing follows a change in a .NET project into the
  browser tests.

## Enforcement

- **Test.** `CiWorkflowTests` (`tests/ScreenHire.UnitTests/Tooling/`, in CI's `checks` job) fails when a pull request
  would start more than 20 jobs, counting each matrix leg, and when the workflow that fills `main`'s caches stops
  matching the cache keys.
- **Test.** `TestShardPlanTests` fails, at the shard counts `ci.yml` uses, when a class outweighs an average shard,
  when a single test runs over a quarter past an average shard's parallel tests, when over a quarter of a project's
  test classes have no measurement, when a class with an AppHost of its own is in no collection that runs alone, and
  when `tests/shard-durations.json` and the sources disagree about which classes run alone.
- **Test.** `TestShardTests` fails when the sharder would leave a test in no shard: a listing whose count and lines
  disagree fails the shard.
- **Test.** `TestSuiteRulesTests` fails on changed-files selection or a retry in the workflow, a package script or a
  Vitest configuration, on a skipped or exclusive test, and on a raw `Task.Delay`, `Thread.Sleep`,
  `WaitForTimeoutAsync` or timer sleep in a test.
- Each of those edits the real file in memory for its red cases, so a check that stops matching fails.
- **Review.** `/task-review` judges what no scan can: whether a test can fail for the reason its title gives, whether
  a new test is a mirror or a pin of a literal, whether a behaviour sits in the right tier, and whether a new job
  names what only it catches. Nothing stops a mirror test mechanically: a test with a local copy looks like any other.

## Scope

Tests under `tests/` and `*.test.ts` files, `.github/`, and the scripts that cut and measure the shards. Existing
tests are not findings: [the audit of 2026-11-18](../../docs/process/testing.md#the-audit-of-2026-11-18) went through
them, and a test it kept needs no new justification.
