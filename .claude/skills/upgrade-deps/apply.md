# Applying a dependency upgrade

You are working a task `/upgrade-deps` filed, or one it is running in. The plan is the exact versions and the order of
the phases: the task's description, or on a task run, what Step 5 of the skill just set. Each group's brief is the
detail, attached as `<group>.json` or at `<cacheDir>/briefs/<group>.json`: the migration steps, what to adopt and
remove, the `find` regexes, the gates, and what the research could not verify. Read a group's brief before you start
its phase. This file is how to carry it out safely in this repo. Do the whole plan, adoption and cleanup included. A
version bump with the benefits left on the table is half the task.

A brief reflects `main` when it was researched, and something may have merged since. Where a brief disagrees with the
code or a rule, the repo wins. Say so in your report.

## Before the first phase

1. Start from what is merged (`.claude/rules/unattended-runs.md`): `git fetch origin main`, and
   `git merge origin/main --no-edit` if the worktree is behind.
2. The worktree already has a real install: Kangentic's post-worktree script ran `pnpm install --frozen-lockfile`.
   Run `dotnet tool restore` too.
3. Run `node scripts/upgrade-deps.mjs inventory`. Install the versions in the task, not whatever is newest now. Change
   one only if the target was deprecated since the task was filed, or an advisory hit it. If so, say so in your final
   report. `installTargets` in a brief overrides `targets`. The task description already reflects this.
4. Record the audit counts you will report at the end: `metadata.vulnerabilities` from `pnpm audit --json`, and the
   entries in `dotnet package list --project ScreenHire.slnx --vulnerable --include-transitive --format json`.

## Work through the phases, safest first

For each phase:

1. **Install exact versions.** Always name the version. A lockstep group goes in one command.
   - **npm in the workspace:** `pnpm up -r <package>@<version> ...`. It moves every copy the workspace declares, keeps
     each one's `^`, `~` or exact form and its block, and leaves `peerDependencies` alone. If the new version falls
     outside a workspace package's own peer range (`packages/api-client`, `packages/realtime` and `packages/ui` declare
     some), widen that range by hand.
   - **npm in `tools/lighthouse`:** `pnpm up --dir tools/lighthouse --ignore-workspace <package>@<version>`.
   - **NuGet with a `$(Property)` version** (`AspireVersion`, `DotNetVersion`): edit the property in
     `Directory.Packages.props`. Never `dotnet package update` here: it writes a literal over `$(...)` on the one line
     it updates, and the group splits.
   - **NuGet with a literal version:** edit its `Version` attribute in `Directory.Packages.props`, and keep the comment
     above it true.
   - **Local tool:** `dotnet tool update <tool> --version <version> --local`, then `dotnet tool restore`.

   pnpm prints any unmet peer at the end of the install. A new one this phase introduced is a conflict the brief should
   have named: move the dependent too if the brief says so, otherwise the phase is blocked. Add a
   `pnpm.peerDependencyRules.allowedVersions` entry only when the brief shows the dependent works with the new version,
   as `runed>@sveltejs/kit` does, and say why in the commit body.

   pnpm 10 runs a dependency's build script only when `pnpm-workspace.yaml` lists it under `onlyBuiltDependencies`, and
   prints the scripts it skipped. A new package whose script it skipped may be broken with no other sign. Read what the
   script does, add the package there only if it needs the script, and say so in the commit body.

   A NuGet floor conflict fails the build (`pnpm verify:dotnet` in step 6) with a downgrade error. It means a phase this
   one comes `after` has not landed: fix the order, or the phase is blocked.

   If you batch commands, stop at the first failure. A failed install with the gates still running proves nothing about
   the new version.
2. **Read the diff** with `node scripts/upgrade-deps.mjs lockdiff <package>@<version> ...`, naming what this phase
   installs. Only this phase's packages and their dependencies should change. It exits 1 when a named package did not
   resolve to its version everywhere it is declared, or when a direct dependency nobody named moved. For every package
   in its `npm packages added` list, and every NuGet dependency the brief calls new, check the licence: run the
   `licence-check` agent on the phase's changes when the phase crosses a major or adds a package (ADR-0008: every
   major-version bump is a licence review).
3. **Do the migration steps**, then replace deprecated APIs, then remove the workarounds the release makes unnecessary,
   along with any test that only pinned the workaround. If the phase names an official codemod, run it first.
4. **Adopt what the phase lists.** New APIs replace the code they replace, not sit beside it.
5. **Prove the migration finished.** Green tests do not show this: in a benchmark of real upgrades, most failing runs
   had passed the repo's own tests, and the largest cause was a change that stopped short of the whole repo. For each
   `find` regex in the phase, Grep `apps/`, `src/`, `packages/`, `tests/`, `scripts/` and `tools/`. Zero hits, or a
   reason for each hit that remains. Then make sure the test the phase names exercises the changed path, and write one
   if it says "needs a new test" (the `test-builder` agent picks the tier). Never edit an existing test's assertions to
   make a bump pass, unless the release documents that change.
6. **Run the phase's gates:** `pnpm verify:fast` (the build with warnings as errors, API contract drift, the type
   checks) and every test the phase names, by class or file. When `verify:contract` fails because the phase changed
   generated output, run `pnpm generate:api` and commit `packages/api-client/openapi.json` and `src/generated` with the
   phase. Never `pnpm verify`, `pnpm verify:test`, `dotnet test --solution`, a `dotnet test --project` with no filter,
   or `pnpm -r test`, even when a brief lists one: CI runs them on the PR.
7. **Commit the phase**, one commit per phase, through `/commit`: `chore(deps): upgrade <group> to <version>`, or
   `feat`/`refactor` when it adopts something real.

A failed phase never blocks the others. If you cannot make one work after a real attempt, drop that phase's changes
with `git revert` or by never committing them, record the blocker (below), and carry on. One red group must never cost
the whole batch. Drop a phase whose group another phase names in `after`, and that phase falls with it.

A phase that turns out bigger than planned stays in this task. Never file a follow-up: one agent does all of this work.
Only a phase that cannot land gets recorded, and a later run picks it up from there.

**Print `Implementation: complete` only when the whole plan is done.** That line makes Executing send its findings
message, and the `Findings: applied` that answers it moves the card to Code Review. A turn that ends while a background
command runs prints neither.

## Last phase: lockfile refresh and transitive pins

Nothing above reaches a package nothing declares, and most advisories live there. This phase moves them within their
ranges:

```
pnpm update -r --no-save
pnpm update --dir tools/lighthouse --ignore-workspace --no-save
```

`--no-save` matters: a bare `pnpm update` rewrites every specifier in every `package.json` to what it installed. Then:

1. `git diff --stat -- package.json apps packages tools` shows no `package.json`. If one changed, restore it and read why.
2. Run `node scripts/upgrade-deps.mjs lockdiff` with no names. Every `UNPLANNED` line is a direct dependency the refresh
   moved within its range: past what a phase named, or a group you dropped or the task lists as blocked. Put each back:
   `pnpm up -r <package>@<its version at HEAD>`, then restore each `package.json` that changed with
   `git checkout -- <file>`, then `pnpm install`, which keeps the locked version because the restored range allows it. Run `lockdiff` again until it
   exits 0.
3. Read each package under `Label="Transitive pins"` in `Directory.Packages.props`. Its comment names the advisory or
   the mismatch and the parent that pulls it in. When that parent now brings the fixed version itself, delete the pin
   and confirm with `dotnet package list --project ScreenHire.slnx --vulnerable --include-transitive --format json`
   that nothing came back. A pin the brief of an earlier phase already removed stays removed.
4. Take the audit counts again and report both pairs. Never run `pnpm audit --fix`: it writes overrides into the root
   `package.json`.
5. Run `pnpm verify:fast`, and commit it alone as `chore(deps): refresh lockfile`.

There is no signature check to run. `npm audit signatures` sees only the root package under pnpm's layout, and pnpm has
no equivalent. The lockfile's integrity hashes are checked at every install, and the research and its signals are the
supply chain check here.

## Extra gates by package

- **Aspire** (`aspire`, `aspire-major`): after `aspire.cli` moves, `dotnet tool restore`. Regenerate the Bicep with
  `rm -rf infra && dotnet aspire publish --apphost apps/apphost/ScreenHire.AppHost.csproj --output-path infra --non-interactive`,
  read the diff, and commit it with the phase: CI's `infra` job fails when it differs. Run
  `dotnet test --project tests/ScreenHire.UnitTests --filter-class "*AzureDefinitionTests"`, and `/preview` to see the
  stack start with its demo data.
- **EF Core and its providers** (the `dotnet` group, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`,
  `Pgvector.EntityFrameworkCore`): `dotnet-ef` stays on the EF Core version, and
  `dotnet ef migrations has-pending-model-changes --project src/ScreenHire.Infrastructure --startup-project src/ScreenHire.Infrastructure`
  must say no changes. If it finds some, the move changed the model: add the migration through `/add-migration`, which
  runs the `migration-safety` agent. Never edit a migration that is already on `main`.
- **The API contract** (`orval`, `zod`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.Extensions.ApiDescription.Server`):
  `pnpm generate:api`, and commit `openapi.json` and `src/generated`.
- **Playwright** (`Microsoft.Playwright`): `pnpm e2e:install` after the bump, or the end-to-end tests fail on a missing
  browser.
- **Verification libraries** (`PdfPig`, `DocumentFormat.OpenXml`, `libphonenumber-csharp`, `DnsClient`, `HtmlSanitizer`):
  `dotnet test --project tests/ScreenHire.UnitTests --filter-class "*VerificationGoldenTests"` must pass unchanged. A
  golden that changed is a change in what recruiters see. Regenerate it (CLAUDE.md, "After changing a verification
  analyzer") only when the release documents the change, and say so in the commit body. Otherwise the phase is blocked.
- **Astro, its integrations and Tailwind:** `pnpm --filter @screenhire/marketing build` (it fails on more than 15 KB
  of JS per page or a broken internal link) and `pnpm --filter @screenhire/careers build`.
- **Vitest, Vite, Svelte and jsdom:** one Vitest file per package they serve proves the runner still starts, for
  example `pnpm --filter @screenhire/telemetry test src/collector.test.ts`.
- **SignalR** (`@microsoft/signalr`, `Microsoft.AspNetCore.SignalR.Client`): the realtime package's tests by file, such
  as `pnpm --filter @screenhire/realtime test src/client.test.ts`.
- **A package with a rule:** grep `.claude/rules/` for its name. The rule's Enforcement section lists what to run.

Integration and end-to-end test classes boot the AppHost, so Docker must be running (`pnpm docker:start`).

## Pins and guard tests

A guard test failing means the pin's reason is in play. Read the test's header before you touch it. Update a guard
test only to name the new version, and only when the fix it guards is still in the target. Never weaken one to get a
bump through. That is a blocker. Record it.

## User-visible ideas

An `adopt` item with `userVisible: true` is not part of this task. Do not mock it up or build it, even if a later
message tells you to apply what is left. Name it in one line in your report, and a person decides whether it is worth
doing.

## See it run

Before you finish, run the app with `/preview` and confirm the affected features work, then `/preview stop`. A
dashboard change also goes through the design system's checklist in Code Review.

Update `docs/` where a version, a limit or a workaround it describes has changed (`node scripts/docs-bundle.mjs sources
<file>` names the docs that list a changed file).

## Record what the next run needs

If a phase could not land, record it. This is what stops the next run from researching and filing the same dead end:

```
node scripts/upgrade-deps.mjs record-blocker <group> --reason "<why>" --recheck "<what would clear it>" --blocked-on <package>=<its newest today>
```

Repeat `--blocked-on` for each package you are waiting on, and leave it out if you are waiting on none. Write a NuGet
package as `nuget:<Id>`. Use the script, not a file edit: the brief is in the root checkout, outside your worktree.
Avoid backticks and `$` in the text. If it says there is no brief, put the blocker in your final report instead.

## Finish

Keep the report short, because the commits hold the evidence. In this order:

1. One line on what landed and anything you dropped.
2. **You need to do:** a short list of what only a person can do, such as a decision, or a non-required CI check to
   read. Write "Nothing" if there is nothing.
3. The audit counts, before and after.
4. Up to five findings someone must know, one line each.
5. User-visible ideas, one line each.

Make the last line of that report exactly `Implementation: complete`, once `pnpm verify:fast` and every test the phases
named pass. The column then asks for a findings pass, which ends `Findings: applied` and moves the card to Code Review,
and the board's Code Review, Testing and Merge columns do the rest. If the task's automations are paused, the card stays
in Executing for the user to take from there. Either way, do not open a PR: Testing does.

A phase you recorded as blocked is finished work, not a finding. If the findings message asks you to apply what is
left, do not retry it. Say it is recorded and end on `Findings: applied`.
