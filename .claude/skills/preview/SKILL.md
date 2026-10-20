---
description: Start ScreenHire locally with its demo data, wait until it is usable, report the URLs and logins, and open it in the task's browser pane; or stop it. Use for "preview", "run the app", "start the stack", "show me", "look at my change in the app", and when /run looks for a project skill. Arguments: none (keep this clone's data), fresh (a clean demo, nothing deleted), stop.
argument-hint: [fresh | stop]
---

# Preview

Brings up the whole stack (API, worker, the three frontends with hot reload, Postgres, Azurite, Mailpit, Dev SSO) with
Brightwater's demo data, on ports no other worktree uses. What the demo data is, and every URL and login, is in
[`docs/operations/local-demo.md`](../../../docs/operations/local-demo.md); this skill does not repeat it.

**Arguments:** $ARGUMENTS

- none: keep this clone's data volumes, so changes from earlier previews are still there.
- `fresh`: a clean demo with no volumes at all. Nothing is deleted; the next run without `fresh` has the old data back.
- `stop`: go to step 7.

Run long commands with `run_in_background` and stop them with TaskStop. Never stop a process by name, port or process
tree; `scripts/guard-processes.mjs` refuses it.

## 1. Prerequisites

1. `docker info --format '{{.ServerVersion}}'`. If it fails, Docker Desktop is not running: report that and stop.
2. `pnpm install --frozen-lockfile`. It returns in seconds when nothing changed.
3. `dotnet dev-certs https --check --trust`. If it finds no trusted certificate, carry on: the Aspire dashboard warns in
   the browser. Report `dotnet dev-certs https --trust` as an action for a person (trusting needs a confirmation dialog).

## 2. Ports

Nine ports, in this order: Api, Marketing, Careers, Dashboard, Mailpit, DevSso, AspireDashboard, AspireOtlp,
AspireResourceService.

- **In a Kangentic worktree** (the path contains `.kangentic/worktrees/`): `kangentic_get_current_task` with the
  working directory, then `kangentic_check_dev_ports` with its id. If the task already has nine reserved ports, use them;
  otherwise `kangentic_reserve_dev_ports` with `count: 9`, one call. Reservations last as long as the task, so every
  preview of this task gets the same ports. Map them to the names above in ascending order.
- **In the root checkout:** the pinned defaults 5080, 4321, 4322, 4323, 8025, 5090, 17113, 21055, 22161.

If `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:<Api>/health` answers 200, this clone's stack is already up.
Without `fresh`, skip to step 5. With `fresh`, carry on: starting again replaces the running instance (step 3).

## 3. Start

One command, with `run_in_background`, from the repository root (`<P:Name>` is the port from step 2):

```bash
dotnet run --project apps/apphost --no-launch-profile -- --environment Development --ASPNETCORE_URLS=https://localhost:<P:AspireDashboard> --ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=https://localhost:<P:AspireOtlp> --ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=https://localhost:<P:AspireResourceService> --ScreenHire:Ports:Api=<P:Api> --ScreenHire:Ports:Marketing=<P:Marketing> --ScreenHire:Ports:Careers=<P:Careers> --ScreenHire:Ports:Dashboard=<P:Dashboard> --ScreenHire:Ports:Mailpit=<P:Mailpit> --ScreenHire:Ports:DevSso=<P:DevSso>
```

For `fresh`, add `--ScreenHire:PersistData=false` after `--environment Development`.

- Why not `pnpm dev`: `apps/apphost/Properties/launchSettings.json` pins the Aspire dashboard's own ports, and a launch
  profile's values win over the shell's. `--no-launch-profile` drops them, and command-line settings, unlike environment
  variables, do not leak into the build (the API's OpenAPI step starts the API, which fails in Development without a
  database).
- Aspire runs one instance per AppHost project: starting it again from the same clone prints "Stopping previous
  instance" and replaces the running stack cleanly. Other worktrees' stacks are other projects and are not touched.
- Then, in a Kangentic worktree, call `kangentic_mark_background_service` with the shell id at once. Without it the
  task shows as busy and its column's turn-end automations wait until the stack exits.

## 4. Wait for ready, without ending the turn

Ready means: the API's `/health` answers 200, which with the demo on happens only after the demo data is in, and the
dashboard, careers and Mailpit answer. Ready does **not** wait for the integrity chips: the worker assesses the
synthetic applicants after it starts, within about a minute, and live updates can lag until its backlog drains
(`docs/architecture/integrity-engine.md` §10.9). Step 6 waits for the chips where they show.

Use `127.0.0.1`, not `localhost`: curl tries IPv6 first and loses two seconds per attempt.

```bash
curl -fs -o /dev/null -w "%{http_code}\n" --retry 60 --retry-delay 2 --retry-all-errors --retry-max-time 120 --max-time 5 http://127.0.0.1:<P:Api>/health
```

If it exits non-zero, read the end of the background output file. Stop and report if the shell has exited or the output
shows an error (`error`, `Unhandled exception`, `port is already allocated`). Otherwise run it again: up to five rounds,
since a first start pulls images and builds. Then check the rest the same way, one URL each:
`http://127.0.0.1:<P:Dashboard>/`, `http://127.0.0.1:<P:Careers>/brightwater`, `http://127.0.0.1:<P:Mailpit>/`.

## 5. Report

- The URLs with the actual ports: dashboard, careers (`/brightwater`), marketing, API reference (`/scalar` on the API),
  Mailpit, and the Aspire dashboard's sign-in link, from the output:
  `grep -o 'https://localhost:[0-9]*/login?t=[0-9a-f]*' <output file>`.
- Logins: **Continue with Dev SSO** on the dashboard, then Avery Quinn (Brightwater's Owner) or another user from the
  guide's "Demo logins".
- One line on the data, linking `docs/operations/local-demo.md` ("What the demo data is").
- This stack's containers and, unless `fresh`, its data volumes, so step 7 and a later wipe use exact names. The
  containers carry the label `com.microsoft.developer.usvc-dev.creatorProcessId`, whose value is this stack's DCP
  controller. In PowerShell:

  ```powershell
  $root = (git rev-parse --show-toplevel) -replace '/', '\'
  $apphost = Get-CimInstance Win32_Process -Filter "Name='ScreenHire.AppHost.exe'" | Where-Object { $_.CommandLine -like "*$root\apps\apphost\*" -and $_.CommandLine -like '*--ScreenHire:Ports:Api=<P:Api> *' }
  $server = Get-CimInstance Win32_Process -Filter "Name='dcp.exe'" | Where-Object { $_.CommandLine -match "start-apiserver --monitor $($apphost.ProcessId) " }
  $controllers = Get-CimInstance Win32_Process -Filter "Name='dcp.exe'" | Where-Object { $_.CommandLine -match "run-controllers .*--monitor $($server.ProcessId) " }
  docker ps --filter "label=com.microsoft.developer.usvc-dev.creatorProcessId=$($controllers.ProcessId)" --format '{{.Names}} {{.Mounts}}'
  ```

  Four containers: `postgres-*`, `storage-*`, `mailpit-*`, `dev-sso-*`. The volumes are
  `screenhire.apphost-<hash>-{postgres,storage,mailpit}-data`; `docker inspect --format '{{range .Mounts}}{{.Name}} {{end}}' <name>`
  prints a full name.
- How to stop: `/preview stop`.
- Editing while it runs: frontend changes reload on their own. A .NET change needs a restart: run `/preview` again
  (step 3 replaces the instance), or restart the resource in the Aspire dashboard.

## 6. Show it

In a Kangentic task, use the task's browser pane (the `kangentic_browser_*` tools):

1. `kangentic_browser_open_pane` with `http://localhost:<P:Dashboard>/sign-in`.
2. `kangentic_browser_wait` for selector `a[href*="/v1/auth/challenge/dev-sso"]`, then `kangentic_browser_click` it.
3. Wait for `form input[value="brightwater-owner"]`, then click `form:has(input[value="brightwater-owner"]) button`.
   That is Avery Quinn. Do not match on text: the "Unverified address" button signs in with Avery's email and is refused.
4. Wait for selector `h1` with text `Jobs`.
5. `kangentic_browser_query_all` on `a[href^="/jobs/"]` with `includeHtml`, take the `href` of "Senior .NET Engineer"
   and `kangentic_browser_navigate` to it. Clicking by text can land on the first job link.
6. Wait up to 60 s, twice if needed, for `[data-integrity-chip="authenticity"][data-level="Verification recommended"]`.
   Text alone matches the sidebar's saved view of the same name.

An offscreen pane (another project is open in Kangentic) screenshots blank; read the page with
`kangentic_browser_query_all` or `kangentic_browser_eval` (`document.querySelector('main').innerText`) instead.

Outside Kangentic, print the dashboard URL and the sign-in steps.

## 7. Stop (`/preview stop`)

1. If step 5 has not listed this stack's containers yet in this session, list them now, with the snippet above. The
   label points at a live process only while the stack runs.
2. `TaskStop` the shell from step 3. It ends the whole process tree at once, DCP included, so Aspire cannot remove its
   containers.
3. Check the API port is free: `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:<P:Api>/health` prints `000`.
4. For each container from step 1, still listed by `docker ps -a --format '{{.Names}}'`: read its labels with
   `docker inspect --format '{{index .Config.Labels "com.microsoft.developer.usvc-dev.creatorProcessId"}}' <name>`, and if
   it is this stack's value, `docker rm -f -v <name>`. One container per command, by exact name; `-v` removes only the
   anonymous volumes a fresh stack's images create, never the named data volumes.
5. Report what was removed and what is left.

A stack this session did not start (another session's shell) cannot be stopped from here. Say so; its own session stops
it, or a person does with Ctrl+C.

## Leaving it running

Leave it running for a person who asked for it. A board agent that started it to check its own change runs
`/preview stop` before printing a board outcome line, so the worktree can be removed later.

To wipe this clone's persisted demo data: `/preview stop`, then `docker volume rm <name>` for each of the three data
volumes step 5 reported, one by one.
