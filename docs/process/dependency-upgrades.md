---
type: Guide
title: "Dependency upgrades"
description: "How /upgrade-deps finds what can move across pnpm, NuGet and the local dotnet tools, why a group whose newest release is a major splits in two, how npm peer ranges and NuGet floors are checked before any install, the supply chain and licence signals, how research is reused so it can run as often as you like, and the one rolling board task that does the work."
tags: [process, dependencies, pnpm, nuget, skills, board]
sources:
  - resource: .claude/skills/upgrade-deps/SKILL.md
  - resource: .claude/skills/upgrade-deps/apply.md
  - resource: .claude/agents/upgrade-researcher.md
  - resource: scripts/upgrade-deps.mjs
  - resource: scripts/upgrade-researcher-guard.mjs
---
# Dependency upgrades

`/upgrade-deps` researches every dependency with a newer release, and one agent does the upgrade by following
`.claude/skills/upgrade-deps/apply.md`. The task then runs through Code Review, Testing and Merge like any other
([board pipeline](board-pipeline.md)). It is a port of Kangentic's skill of the same name, adapted to two package
managers.

Dependabot's security updates are on for the repository, as a GitHub setting with no `.github/dependabot.yml`. They open
a pull request for a package with an advisory, and nothing else: no routine version updates, no grouping, no research.
That leaves `/upgrade-deps` as the path for every other move of pnpm packages, NuGet packages and the local dotnet tools.
The two do not coordinate. The inventory reads `main`, so a Dependabot bump that merged counts as installed, and one
still open is invisible to it. Dependabot closes its own pull request once `main` moves the package past it.

There are two ways to run it, and the folder it runs in decides which:

- **In a task.** Make a card with `/upgrade-deps` as its description, give it the `Upgrade` Board Profile, and drag it
  straight to Executing. That agent researches and does the upgrade itself, and files nothing. Planning runs in plan
  mode, which cannot install, so a run there stops and says so.
- **From the root checkout or a Command Terminal.** No task branch can carry the work there, so the run researches,
  files one task in To Do, and stops. Drag that task when you want it.

Run it as often as you like. Packages ship daily, so nothing depends on the date: every run targets the newest release,
and research already done is skipped. `--dry-run` prints the planned task and files nothing.

## Who does what

| Piece | Job |
|---|---|
| `scripts/upgrade-deps.mjs` | Reads the lockfiles, `Directory.Packages.props`, `dotnet-tools.json`, `pnpm outdated`, `pnpm audit` and the npm and NuGet registries. Decides by version number what can move, what it conflicts with, and whether earlier research still answers it. |
| `upgrade-researcher` agent | Reads the release notes for every version crossed, checks how the repo uses the package and what licence the target carries, finds what can be removed or adopted, and writes a brief. |
| `scripts/upgrade-researcher-guard.mjs` | The researcher's hook: Bash may only read, and Write may only touch a brief. |
| `/upgrade-deps` skill | Drives the run: spawns the researchers, orders the phases, and either follows `apply.md` or files a task. |
| `apply.md` | What the task agent follows to install, migrate, adopt, verify and commit, with the gates each package needs. |

## What it covers

The pnpm workspace (the root, `apps/*`, `packages/*`), `tools/lighthouse` with its own lockfile, every `PackageVersion`
in `Directory.Packages.props` including the three transitive pins, and `aspire.cli` and `dotnet-ef` in
`dotnet-tools.json`. Packages nothing declares move in the task's lockfile refresh.

Out of scope: GitHub Actions (they float on their `@vN` tags), pnpm itself (see
[why pnpm 10.34.7 was not adopted](dev-tooling.md#pnpms-global-virtual-store-was-tried-for-worktree-installs-and-not-adopted)),
Node, the .NET SDK in `global.json`, and container images, which move with Aspire.

## Why a script, and what it leaves to the native tools

The script uses the native tools where they answer. `pnpm outdated -r --format json` is the filter for npm: a package it
does not list is current, and its registry document is never fetched. Its `wanted` field is the lockfile's version, so
the answer holds even when `node_modules` is stale. `pnpm audit --json` supplies the npm advisories.

Three gaps made the script necessary. `dotnet package list --outdated` lists only what a project references, after a
restore, so it never sees the transitive pins or the local tools. Neither tool knows our lockstep groups: a
`$(Property)` version in `Directory.Packages.props` is one, and the script keeps a short list of the others (Tailwind,
Tiptap, Wolverine, the Aspire Community Toolkit and `aspire.cli`, `dotnet-ef` with EF Core). And both report one
`latest`.

That last gap bit on the day this was written. On 2026-11-18, .NET 11 and Aspire 17 were the `latest` of their
packages, while 10.0.15 was the newest release on .NET 10. A group with one target would have tied the 10.0.15
servicing patch to a framework retarget. So a group whose newest release crosses a major splits in two: `dotnet` held
10.0.14 to 10.0.15, and `dotnet-major` held 11.0.0. Each has its own brief, its own blocker and its own phase, and a
blocked major holds nothing back. `dotnet-major` is never a phase. A new .NET major needs a new SDK and target
framework, which this skill does not move, so its brief informs the owner's decision. Both majors end support on
2028-11-14 (.NET 10 is LTS, 11 is STS), per Microsoft's release metadata.

## Conflicts before any install

Each group is checked alone against what is installed, so a blocked major reports against its own group and not a
neighbour's patch.

- **npm peers.** The lockfile carries every installed package's `peerDependencies`, and the registry document carries
  the target's, so the check needs no install. Ranges go through `semver`, npm's own range library, rather than a
  rewrite of it. On the first run TypeScript 7 conflicted with the peer ranges of `@astrojs/svelte`, `@astrojs/check`,
  `@sveltejs/kit` and `svelte-check`.
- **NuGet floors.** With `CentralPackageTransitivePinningEnabled`, a central pin below a dependency's minimum is a
  restore error. The script reads each target's dependency group for `net10.0` from the NuGet registration and compares
  its ranges with our pins. On the first run Aspire 17 needed `Microsoft.Extensions.Hosting` 10.0.15 and OpenTelemetry
  1.18, and Npgsql's EF Core provider capped EF Core below 11. A floor that another group's ready move clears is not a
  blocker: the brief names that group in `after`, and the plan orders the phases.

## Supply chain and licence signals

There is no age gate. A target can be minutes old, so the inventory compares each target with what is installed for
what supply chain attacks change. On npm that is a new dependency, a new install script, a `binding.gyp` with no
script, lost provenance and a new publisher. On NuGet it is a new dependency and a new build-time file: an MSBuild
`.props` or `.targets`, or an analyzer, since both run code during `dotnet build`. NuGet's registration names no
publisher per version, so that signal is npm only.

Both registries also report the licence, and a change is a signal. ADR-0008's banned packages left the policy at a
major (FluentAssertions 8, MassTransit 9), so a target whose licence leaves the policy is blocked until the owner
amends the ADR. On the first run, Anthropic's C# SDK 13 moved from MIT to Apache-2.0, which the policy allows.

A signal gives the group its own researcher, who has to explain it or block the group. There is no signature check
after install: under pnpm's layout `npm audit signatures` saw only the root's one dependency when tried, and pnpm has
no equivalent. pnpm 10 runs a dependency's build script only when `onlyBuiltDependencies` in `pnpm-workspace.yaml` lists
it, so a new install script stays off until someone reads it.

Research reads text anyone can publish, about packages that can be minutes old, so the researcher reads a package and
never runs one. Kangentic learned that an instruction alone does not hold: one of its researchers extracted a release
from that morning and loaded it in node. The guard is an allowlist that fails closed. Bash may run `npm view`,
`dotnet package search`, read-only `gh` and `git`, `ls` and one clock command, with no chain, pipe or redirect. Write
and Edit may touch only a brief, and PowerShell is refused.

## How research is reused

The registries are read on every run, so a release from an hour ago shows up. Only research is cached, as one JSON brief
per group in `.upgrade-deps/briefs/`, a gitignored folder in the root checkout that every worktree reaches through
`git rev-parse --git-common-dir`. `.kangentic/` belongs to the board, and `.claude/` is a protected path that background
agents cannot write to unattended.

Research never expires. A published version's release notes do not change, so only a version moving, or `--refresh`,
sends a group back to an agent:

| State | When | Cost |
|---|---|---|
| `reuse` | Same versions | None |
| `blocked` | Same versions, and what the blocker waits on has not moved | None |
| `filed` | A board task already carries the brief | None |
| `recheck` | Blocked, and what it waited on moved | The blocker alone |
| `delta` | Brief exists for older versions | The new releases only |
| `research` | No usable brief | Everything |

A blocked brief names the packages it waits on and their versions, an npm name or `nuget:<Id>`, so the check is a
comparison and needs no agent. A brief's `targets` is its cache key and always holds what the inventory asked for. When
the task should install something else, the brief says so in `installTargets`. A brief is local to one machine, so
another machine researches again, which is the worst losing one can do.

## One task, one agent

All of it goes in one task, whatever its size. Kangentic's early runs split the work and got four tasks, three in
sequence. Tasks in sequence wait on a merge each, and tasks in parallel conflict in the lockfile, so a split only adds
waiting. Here they would also conflict in `Directory.Packages.props`.

Each group is a phase with its own commit, ordered safest first: patches and dev tools, then medium risk, then the
backend stack, then frontend framework majors, then the lockfile refresh. `apply.md` drops a phase that cannot pass its
gates and records why, so a red group late in the run leaves every commit before it standing. An upgrade task is one
with the `dependencies` label, and while one is past To Do a run reports what could move and stops.

The task ends on Executing's own contract. The agent prints `Implementation: complete` only when the whole plan is done,
answers the column's findings message, and ends that turn on `Findings: applied`.

What a release adds is part of the job: workarounds it makes removable, deprecated APIs, and new APIs that replace our
code. A new feature a person would see is not. It gets one line in the report and no mockup, because Kangentic's first
full run mocked one up inside an upgrade and the mockup read as finished work.

## Which model does what

| Stage | Model |
|---|---|
| Orchestrator, which judges the briefs and orders the phases | The session's, Opus |
| Research, low-risk batches of up to six | Haiku at medium effort, escalated to Sonnet when a brief is not simple |
| Research, risky groups (majors, pins, signals, conflicts, advisories, `dotnet`) | Sonnet at high effort |
| Research, `aspire`, `wolverine`, and the majors of the EF Core providers, Svelte, SvelteKit, Astro and Vite | Opus |
| Executing and Testing on an upgrade task | Opus at high effort, through the `Upgrade` Board Profile |

This board runs Executing on Opus and Testing on Sonnet. On an upgrade task, Testing is where `/pull-request` fixes
code when CI fails, so the profile lifts it to Opus as well. Kangentic measured that Haiku is
fine at reading but over-flags and misjudges reach, which is why its briefs are escalated before planning.

## Commands that behave differently here

Each was run in a scratch workspace before it went into `apply.md`.

- `pnpm up -r <package>@<version>` moves every workspace copy, and keeps each one's `^`, `~` or exact form and its
  block. It leaves `peerDependencies` alone.
- A bare `pnpm update -r` rewrites every specifier in every `package.json` to what it installed. The refresh uses
  `--no-save`, which moves the lockfile within the ranges and leaves the manifests alone.
- `dotnet package update <Id>@<version>` writes a literal over a `$(Property)` version on the one line it updates, which
  splits the group. `apply.md` edits the property instead.
- `dotnet tool update <tool> --version <version> --local` rewrites `dotnet-tools.json` and keeps its format.

## What it costs

On 2026-11-18 the inventory took 6 s. It found 60 groups that could move, counted 36 packages as current, and printed
33 KB of JSON. `pnpm audit` reported 25 advisories in the workspace. Five packages carried one against themselves
(Astro, `@astrojs/node`, `@astrojs/svelte`, SvelteKit and its static adapter), and the rest arrived through other
packages. `dotnet package list --vulnerable --include-transitive` reported none, in 19 s. Agent tokens are the real cost. Kangentic measured 43k tokens for a patch
and 98k for a small major, and 3.6M for its first full run of 28 groups. A repeat run pays only for what changed.
