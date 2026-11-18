---
description: Research every pnpm and NuGet dependency, and the local dotnet tools, with a newer release, reading each changelog and migration guide and what the release lets us remove or adopt, and get the upgrade done by one agent. Inside a board task it does the upgrade itself by following apply.md. Anywhere else (the root checkout, a Command Terminal) it files one board task for an agent to do that. It never files a follow-up. Reuses earlier research when nothing moved or a known blocker still stands. Use for "update our packages", "upgrade dependencies", or "what can we upgrade".
allowed-tools: Read, Glob, Grep, Write, Edit, Agent, Bash(node:*), Bash(pnpm:*), Bash(dotnet:*), Bash(git:*), Bash(gh:*)
argument-hint: [--dry-run] [--refresh] [package or group ...]
---

# Upgrade deps

Find every dependency that can move, research what each move means for this repo, and get the upgrade done by one
agent. Why it works this way, and what it cost: [`docs/process/dependency-upgrades.md`](../../../docs/process/dependency-upgrades.md).

**Where this runs decides what it does.** Check the working directory first:

- **Inside `.kangentic/worktrees/`, a task run.** This session does the upgrade. Research as below, skip Step 6, and
  follow [`apply.md`](apply.md) in this task. The branch then has commits for Code Review and Testing to work on. If
  this session is in plan mode (the Planning column), it cannot install anything: say the card belongs in Executing
  and stop.
- **Anywhere else, a filing run.** The root checkout is the owner's, and no task branch there can carry the work.
  Research as below, file one task, and stop. A filing run edits no code, installs nothing and pushes nothing.

Go by the folder. `kangentic_get_current_task` with a branch name is no test for this: `main` matches old tasks.

Run it as often as you like. Every run checks the newest releases, and work already done is skipped. A run with
nothing new costs one inventory and no agents.

**Usage:** `/upgrade-deps [--dry-run] [--refresh] [package or group ...]`

- `--dry-run` researches and prints the planned task but files nothing. Research is still cached.
- `--refresh` ignores the cache and researches every group again.
- Naming packages or groups limits the run to those groups.

**Arguments:** $ARGUMENTS

Run it on an up-to-date `main` in an Opus session, because this session judges the briefs and orders the phases. It
reads the tree it is in, so a checkout behind `main` reports versions that already moved. Its agents write to the root
checkout's cache from any worktree.

## What it covers, and what never moves

Every pnpm package the workspace declares (the root, `apps/*`, `packages/*`), `tools/lighthouse` with its own lockfile,
every `PackageVersion` in `Directory.Packages.props` (the transitive pins included), and the local tools in
`dotnet-tools.json`. Packages nothing declares move in a lockfile refresh, the task's last upgrade phase.

Out of scope: GitHub Actions (they float on their `@vN` tags), pnpm itself
(`packageManager`, see "pnpm's global virtual store" in `docs/process/dev-tooling.md`), Node, the .NET SDK in
`global.json`, and the container images, which move with Aspire.

Everything else is up for a full upgrade, majors and exact pins included, unless one of these stops it:

- **A downgrade.** The inventory never picks a version at or below what is installed.
- **A conflict nothing released clears:** an npm peer range, or a NuGet dependency floor above one of our pins. It goes
  on the record as blocked, with what would clear it.
- **A pin whose reason still holds** and which the target would break. An exact pin moves only to another exact pin.
- **A licence off the ADR-0008 policy** at the target.
- **`dotnet-major`.** A new .NET major changes the target framework and the SDK, which the owner decides. It is
  researched for that decision and never made a phase.

## Step 1: Inventory

```
node scripts/upgrade-deps.mjs inventory
```

Pass `--refresh` through if given. It reads the registries on every run, so a release from an hour ago shows up. Only
research is cached. A field that is absent means no. Read:

- `groups`: what can move. A group whose newest release crosses a major is split: `<group>` holds the newest release on
  the major we run, and `<group>-major` the newest of all, so a patch never waits on a major. Each group carries its
  `members` (`ecosystem`, `installed`, `target`, `published`, `pinned`, `property`, `tool`, `transitivePin`, `kind`,
  `locations`), its `conflicts`, a `research` state, `hasBrief`, and any `task`. `targetsDisagree` means the members'
  newest releases do not match, so the researcher picks a matching set. `signals` means a member's target changed what
  supply chain attacks change (a new dependency, an install script, a `binding.gyp`, a NuGet build-time file, lost
  provenance, a new publisher) or its licence. A `kind` of `deprecated` means there is no upgrade, and the work is to
  replace or remove the package.
- `cacheDir`: where briefs live. It is the same folder from any worktree.
- `errors`: packages the registries would not answer. Report them. A package missing from the run is not up to date.

## Step 2: Work out what needs research

If the arguments named packages or groups, work only on those from here on, and leave the rest out of the report. The
`research` state says how much of the earlier work still stands:

| State | Meaning | Action |
|---|---|---|
| `reuse` | Same versions | Read the brief. No agent. |
| `blocked` | Same versions, and what the blocker waits on has not moved | Report it. No agent. |
| `filed` | A board task carries this brief | Confirm the task in Step 3. No agent. |
| `recheck` | Blocked, and what it waited on moved | One agent tests the blocker alone. |
| `delta` | A brief exists for older versions | One agent covers only the new releases. |
| `research` | No usable brief | The full pass. |

Research never expires. Only a version moving, or `--refresh`, sends a group back to an agent, so do not research a
`reuse` group again because you are unsure, or because the brief is old. Read it.

## Step 3: Check the board

Search active tasks for earlier upgrade work:

```
kangentic_search_tasks query: "Upgrade deps", status: "active"
```

An upgrade task is one with the `dependencies` label. Ignore any other match, however it is titled, and ignore the
task you are running in. There is only ever one upgrade task.

**If one is past To Do, stop here.** Name it in the report with how many groups could move, then spawn nothing and file
nothing. Whatever moved waits for the next run after that task merges, so a run during an upgrade costs one inventory.
A task run stops too if one is waiting in To Do, because that task already holds the plan: say to drag it.

On a task run, find your own task with `kangentic_get_current_task`, passing `cwd` and no branch, and set the
`dependencies` label on it now with a labels-only `kangentic_update_task`, so a run that starts while this one works
stops here.

For every group with a `task`, whatever its `research` state, find that task with `kangentic_find_task` and
`task.displayId`. A new release moves a filed group to `delta`, and it still belongs to that task:

- **Still in To Do:** research it as its state says, then update that task in Step 6 rather than filing another.
- **Done or deleted, and the group still shows as movable:** the work did not land. Set the brief's `task` to `null`.
  Treat a `filed` group as `reuse`, and research a `delta` group as usual.

## Step 4: Research

Spawn `upgrade-researcher` agents in parallel, in one message:

- **Critical groups get one agent each on Opus,** with `model: "opus"`: `aspire`, `aspire-major`, `wolverine`,
  `wolverine-major`, and the `-major` group of `npgsql.entityframeworkcore.postgresql`, `efcore.namingconventions`,
  `svelte`, `sveltejs-kit`, `astro` or `vite`. A miss here can stop the stack or every frontend.
- **Other risky groups get one agent each on the agent's default, Sonnet at high effort:** `dotnet`, `dotnet-major`,
  and any group whose `kind` is `major` or `deprecated`, or that is `pinned`, holds a `transitivePin`, or has `signals`,
  `conflicts`, `targetsDisagree`, an `advisory` or an `inheritedAdvisory`.
- **Everything else goes in batches of up to six groups on Haiku,** with `model: "haiku"` and `effort: "medium"`. A
  patch to a dev tool needs its release notes read, not an investigation.

When the batches finish, read their briefs and **escalate**. Haiku is trusted for groups that turned out simple, not
for judging hard ones. Send a group to one agent on the default, with state `research` and its brief as the prior
brief, if the brief has any of these:

- `risk` above `low`, or `verdict: blocked`
- a change that needs a source edit, or that moves a package an app ships. A range in `package.json` or a lockfile line
  for a dev tool is not one, even when the brief marks it as affecting us
- a value in `installTargets`, which means it wants something other than the newest release
- anything in `adopt`, `workaroundsToRemove` or `after`
- a licence that changed
- an unverified claim about how the repo uses the package
- a claim about an advisory that `pnpm audit` or the registry does not back

Each prompt carries, per group: the name, every member's `ecosystem`, `installed`, `target`, `published`, `pinned`,
`property`, `tool`, `transitivePin`, `locations`, `signals` and `deprecated`, the `conflicts`, any `advisory` or
`inheritedAdvisory`, the `research` state, and the absolute path `<cacheDir>/briefs/<group>.json`. Say whether a prior
brief exists at that path. For a `-major` group, also name the group on our major and whether it is in this run.

The agents write their briefs to disk as they finish, so a stopped run keeps its work. Run the inventory again to pick
up where it stopped. Do not research anything yourself in the main loop.

## Step 5: Plan the task

Read every brief with `verdict: ready`. **All of it goes in one task, worked by one agent.** Never split a group out
and never file a follow-up, whatever its size or risk. Tasks run one after another wait on a merge each, and tasks run
in parallel conflict in `pnpm-lock.yaml` and `Directory.Packages.props`. Groups become phases inside the task, each
with its own commit.

On a filing run, if an upgrade task is waiting in To Do, add the new groups to it. Otherwise file a task.

Order the phases safest first, so a red group late in the run leaves every commit before it standing: patches and dev
tools, then medium risk groups, then the backend stack (`dotnet`, EF Core providers, `wolverine`, `aspire`), then
frontend framework majors (`svelte`, `sveltejs-kit`, `vite`, `astro`, `tailwind`), then the lockfile refresh. A group
whose brief names `after` goes after each group it names. `apply.md` drops a phase that cannot pass its gates and
carries on, so a high risk group never needs a task of its own.

A group's `-major` phase comes after its in-major phase. When both are ready and the major lands, the in-major phase
still runs first: it is the smaller step, and it stands if the major is dropped.

A blocked group gets no phase. It goes in the task's "Not in this task" section and in the report, so nobody wonders
where it went. So does `dotnet-major`, under "Decision", with its brief's summary.

**A user-visible idea is never a phase.** An `adopt` item with `userVisible: true` gets one line in the report and
nothing else: no mockup and no build. Whether it is worth doing is a separate decision for a person, and a mockup inside
an upgrade reads as finished work.

## Step 6: File the task

A task run skips the filing here. Record your own task on the briefs, as the last paragraph of this step says, then
follow [`apply.md`](apply.md), with the order from Step 5 as your plan.

With `--dry-run`, print the planned title and description and stop here.

Otherwise create the task in To Do with `kangentic_create_task`, with `profile: "Upgrade"` and no `column`. Title it
`Upgrade deps: <what moves>`, and attach the brief of every group in it, as
`attachments: [{ filePath: "<cacheDir>/briefs/<group>.json" }, ...]`. Then set the label `dependencies` in a separate
labels-only `kangentic_update_task` call, because labels on a long create can drop.

To change the waiting task, use `kangentic_update_task` with `descriptionEdits`, and pass the new or changed briefs in
`attachments`, which adds to what is there. Remove a superseded copy with `kangentic_remove_task_attachment`.

Attach the briefs rather than copy them into the description, because the attachment paths reach the agent's opening
prompt anyway. Keep the description to the plan a person reads on the card, and under 25,000 characters, which leaves
room for the briefs' paths. The field allows 27,500, and the paths ride the same command line.

The `Upgrade` Board Profile runs Executing and Testing on Opus at high effort. This board runs both on Sonnet, and on an
upgrade task Executing is where the migration happens, and Testing is where `/pull-request` fixes code when CI fails.
If the board has no such profile, file without one and say so in the report.

**Set no `autoCommand`, `modelOverride`, `effortOverride`, `agentOverride` or `permissionMode`.** The columns and the
profile decide how a task runs. A pin lasts the task's whole life and locks the permission mode too, and `autoCommand`
replaces the commands the board runs as the task moves.

The description stands alone, because the task agent shares no context with this run:

```markdown
Follow `.claude/skills/upgrade-deps/apply.md`. Install exactly the versions below. Each group's full research is
attached as `<group>.json`: read it before you start that phase. Drag this card to Executing, not Planning: this is
the plan.

## Phases

### 1. <group>: <installed> to <target>  (risk: low)
<the brief's summary>
Must change: <the source edits in a line, or "nothing">

### 2. ...

### Last: lockfile refresh

## Not in this task
Blocked: <group, reason, what clears it>
Decision: <dotnet-major, its summary in a line>
Idea: <a user-visible item in one line, not to be mocked up or built here>
```

Paraphrase, and never write a contributor's path or name, because the repository is public
(`.claude/rules/no-personal-info.md`).

After the task exists, record it on its briefs, so the next run knows it is handled. In each brief, set
`"task": { "displayId": <number>, "filedAt": "<UTC ISO timestamp>" }`.

## Step 7: Report

A task run reports through `apply.md`'s Finish section instead. A filing run ends with what the user needs to act on,
short:

- The task filed or updated, with its number and how many groups it holds.
- Blocked groups, each with its reason and what clears it, and the `dotnet-major` decision.
- User-visible ideas, one line each.
- What the run reused, as a count: "11 groups from the cache, 4 researched".
- Any registry errors, and anything an agent could not verify.

If everything is current, blocked or already filed, say that in one line and file nothing.

## Rules

- A filing run never edits source, runs an install, commits or pushes. A task run does all of that except the push, by
  `apply.md`'s rules.
- One upgrade task at a time. Never split it, and never file a follow-up.
- Never start the filed task, and never move it. The user drags it when they want it.
- Every version in a task comes from a brief, and every brief's versions come from the inventory.
- One command per Bash call.
