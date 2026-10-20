---
description: Start ScreenHire locally with its demo data through the Aspire CLI, wait until it is usable, report the URLs and logins, and open it in the task's browser pane; rebuild one service after a code change; or stop it. Use for "preview", "run the app", "start the stack", "show me", "look at my change in the app", and when /run looks for a project skill. Arguments: none (keep this clone's data), fresh (a clean demo, nothing deleted), stop.
argument-hint: [fresh | stop]
---

# Preview

Brings up the whole stack (API, worker, the three frontends with hot reload, Postgres, Azurite, Mailpit, Dev SSO) with
Brightwater's demo data, through the Aspire CLI. What the demo data is, and the logins, are in
[`docs/operations/local-demo.md`](../../../docs/operations/local-demo.md); this skill does not repeat them.

**Arguments:** $ARGUMENTS

- none: keep this clone's data volumes, so changes from earlier previews are still there.
- `fresh`: a clean demo with no volumes at all. Nothing is deleted; the next run without `fresh` has the old data back.
- `stop`: go to step 6.

The Aspire CLI is a repo-local tool (`dotnet-tools.json`, pinned to the AppHost's Aspire version), so every command is
`dotnet aspire ...`, run from the repository root. Pass `--non-interactive --nologo` and `--apphost
apps/apphost/ScreenHire.AppHost.csproj` to each. Set `ASPIRE_CLI_TELEMETRY_OPTOUT=true` in the environment of every
call. Never run `aspire agent init`: it writes hooks and MCP settings into the user's machine-wide configuration.

Rules from Aspire's own agent guidance: start with `aspire start`, never `dotnet run` or `aspire run` (both block the
terminal); wait with `aspire wait`, never a curl loop; stop with `aspire stop`, never by killing a process; never
`aspire stop --all`, which stops other clones' stacks, or `--force`, which cleans up persistent resources; do not
`dotnet build` the API or worker while the stack runs (it holds their output files), use `rebuild` (step 4).

## 1. Prerequisites

1. `docker info --format '{{.ServerVersion}}'`. If it fails, Docker Desktop is not running: report that and stop.
2. `pnpm install --frozen-lockfile` and `dotnet tool restore`. Both return in seconds when nothing changed.
3. `dotnet dev-certs https --check --trust`. If it finds no trusted certificate, carry on: the Aspire dashboard warns in
   the browser. Report `dotnet dev-certs https --trust` as an action for a person (trusting needs a confirmation dialog).

## 2. Start

```bash
dotnet aspire start --non-interactive --nologo --isolated --apphost apps/apphost/ScreenHire.AppHost.csproj --format Json
```

- `--isolated` gives every port a random free number and the run its own user secrets, so worktrees never collide and
  no port needs reserving. Use it in a Kangentic worktree (the path contains `.kangentic/worktrees/`). In the main
  checkout leave it out, which keeps the pinned ports from CLAUDE.md "Commands".
- `fresh`: append `-- --ScreenHire:PersistData=false`.
- It returns once the AppHost is up, with JSON holding `dashboardUrl` (the Aspire dashboard, login token included). A
  first start pulls images and builds, so give the call a 10-minute timeout.
- Without `fresh`, a second start (isolated or not) reuses this clone's data volumes: their names come from the
  AppHost's path, not from the run.
  Nothing keeps running in this session's shell, so a Kangentic task's turn-end automations are not held.
- One stack per clone: starting again replaces this clone's running stack (which is how a `fresh` start or an AppHost
  change is picked up). Other worktrees' stacks are other AppHosts and are not touched.

## 3. Wait for ready, without ending the turn

```bash
dotnet aspire wait api --non-interactive --nologo --apphost apps/apphost/ScreenHire.AppHost.csproj --timeout 600
```

Then the same for `dashboard`, `careers` and `mailpit`. The API turns healthy only once the demo data is in, so ready
means the data is there. A first start pulls images and builds, hence the long timeout. If a wait fails, report its
output and `dotnet aspire logs <resource> --non-interactive --nologo --apphost apps/apphost/ScreenHire.AppHost.csproj`.

Ready does **not** wait for the integrity chips: the worker assesses the synthetic applicants after it starts, within
about a minute, and live updates can lag until its backlog drains (`docs/architecture/integrity-engine.md` §10.9).
Step 5 waits for the chips where they show.

## 4. Report

`dotnet aspire describe --non-interactive --nologo --apphost apps/apphost/ScreenHire.AppHost.csproj --format Json` lists
every resource with its state, URLs and volumes. A healthy API means the demo ran, not that every step worked: a failed
step is logged and skipped. `dotnet aspire logs api --non-interactive --nologo --apphost
apps/apphost/ScreenHire.AppHost.csproj --search "could not be seeded"` prints the failures (it does not follow); if it
prints any, report that the demo data is partial, with the exception. Report:

- the dashboard, careers (`/brightwater`), marketing, API reference (`/scalar` on the API) and Mailpit URLs, and the
  Aspire dashboard link from step 2. That link carries its login token: give it in chat only, never in a commit, a
  file, a PR body or a board comment;
- the logins: **Continue with Dev SSO** on the dashboard, then Avery Quinn, Brightwater's Owner (the guide's "Demo
  logins" has the others);
- one line on the data, linking `docs/operations/local-demo.md` ("What the demo data is");
- unless `fresh`, this clone's data volumes (each resource's `volumes`), so a wipe can name them;
- editing while it runs: frontend changes reload on their own. After a change to the API or the worker, run
  `dotnet aspire resource api rebuild --non-interactive --nologo --apphost apps/apphost/ScreenHire.AppHost.csproj`
  (or `worker`): it rebuilds that project and restarts it while the rest keeps running. After an AppHost change, run
  step 2 again;
- how to stop: `/preview stop`.

## 5. Show it

In a Kangentic task, use the task's browser pane (the `kangentic_browser_*` tools):

1. `kangentic_browser_open_pane` with the dashboard's URL plus `/sign-in`.
2. Wait for either `a[href*="/v1/auth/challenge/dev-sso"]` (signed out) or `h1` with text `Jobs` (already signed in:
   browsers share `localhost` cookies across ports, so an earlier preview's session may carry over).
3. Signed out: click `a[href*="/v1/auth/challenge/dev-sso"]`, wait for `form input[value="brightwater-owner"]`, then
   click `form:has(input[value="brightwater-owner"]) button`. That is Avery Quinn. Do not match on text: the
   "Unverified address" button signs in with Avery's email and is refused. Wait for `h1` with text `Jobs`.
4. `kangentic_browser_query_all` on `a[href^="/jobs/"]` with `includeHtml`, take the `href` of "Senior .NET Engineer"
   and `kangentic_browser_navigate` to it. Clicking by text can land on the first job link.
5. Wait up to 60 s, twice if needed, for `[data-integrity-chip="authenticity"][data-level="Verification recommended"]`.
   Text alone matches the sidebar's saved view of the same name.

An offscreen pane (another project is open in Kangentic) screenshots blank; read the page with
`kangentic_browser_query_all` or `kangentic_browser_eval` (`document.querySelector('main').innerText`) instead.

Outside Kangentic, print the dashboard URL and the sign-in steps.

## 6. Stop (`/preview stop`)

```bash
dotnet aspire stop --non-interactive --nologo --apphost apps/apphost/ScreenHire.AppHost.csproj
```

It stops this clone's AppHost gracefully, and Aspire removes its containers within a few seconds; the data volumes
stay. Confirm:

1. `dotnet aspire ps --non-interactive --nologo` no longer lists this clone's AppHost path.
2. For each data volume step 4 reported, `docker ps -a --filter volume=<name> --format '{{.Names}}'` prints nothing.
   A container still listed there mounts this clone's data. Read its labels with `docker inspect <exact name>
   --format '{{json .Config.Labels}}'` to confirm Aspire made it, then remove it by that exact name, one at a time. Never select containers by a name pattern, a port or an image; other worktrees' stacks use the same
   images.

## Leaving it running

Leave it running for a person who asked for it. A board agent that started it to check its own change runs
`/preview stop` before printing a board outcome line, so the worktree can be removed later.

To wipe this clone's persisted demo data: `/preview stop`, then `docker volume rm <name>` for each of the three data
volumes step 4 reported, one by one.
