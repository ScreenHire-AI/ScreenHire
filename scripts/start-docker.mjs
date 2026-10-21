#!/usr/bin/env node
// Starts Docker Desktop when the Docker engine is down, then waits for it. Run with `pnpm docker:start`; agents use it
// instead of launching Docker Desktop themselves, which scripts/guard-processes.mjs refuses.
//
// A process started from a Kangentic task's shell belongs to that task: it inherits KANGENTIC_TASK_ID and works in the
// task's worktree. When the task reaches Done, Kangentic's cleanup keeps Docker (its window runs under it) but cannot
// delete a folder a live process works in, so the worktree stays on disk. This script starts Docker Desktop with the
// tag removed and Docker's install folder as the working directory. Either change alone keeps the cleanup from
// treating Docker as the task's; the tag goes because Docker serves the whole machine, and the working directory moves
// so the worktree folder can be deleted. Anything a task owns, a dev server for one, keeps its tag so Done stops it.
//
// Docker Desktop's launcher (this script) exits, so it is one of the processes the Aspire teardown note in
// docs/process/board-pipeline.md ("Processes on the dev machine") describes.
//
// Exits 0 once `docker info` answers, at once when it already does. Exits 1 if the engine does not answer within
// three minutes, if Docker Desktop is not installed where expected, or on a platform other than Windows.
import { spawn, spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';

const READY_TIMEOUT_MS = 3 * 60_000;
const POLL_MS = 2_000;
const TASK_TAG = 'KANGENTIC_TASK_ID';

/** The engine's version when `docker info` answers, otherwise null. */
function engineVersion() {
  const result = spawnSync('docker', ['info', '--format', '{{.ServerVersion}}'], { encoding: 'utf8', timeout: 20_000 });
  if (result.error?.code === 'ENOENT') {
    // Without the CLI no poll can ever answer, so do not start Docker Desktop and wait three minutes for nothing.
    console.error('docker:start: the docker CLI is not on PATH. Report it as infrastructure.');
    process.exit(1);
  }
  return result.status === 0 ? result.stdout.trim() : null;
}

const running = engineVersion();
if (running) {
  console.log(`docker:start: the Docker engine is running (${running}).`);
  process.exit(0);
}

if (process.platform !== 'win32') {
  console.error('docker:start: the Docker engine is not running. This script starts Docker Desktop on Windows only; start Docker yourself.');
  process.exit(1);
}

const installFolder = join(process.env.ProgramW6432 ?? process.env.ProgramFiles ?? 'C:\\Program Files', 'Docker', 'Docker');
const desktop = join(installFolder, 'Docker Desktop.exe');
if (!existsSync(desktop)) {
  console.error(`docker:start: the Docker engine is not running and Docker Desktop is not at ${desktop}.`);
  process.exit(1);
}

// Windows environment names ignore case, so match the tag the same way.
const environment = Object.fromEntries(Object.entries(process.env).filter(([name]) => name.toUpperCase() !== TASK_TAG));
console.log(`docker:start: starting ${desktop}`);
spawn(desktop, [], { cwd: installFolder, env: environment, detached: true, stdio: 'ignore' }).unref();

const deadline = Date.now() + READY_TIMEOUT_MS;
while (Date.now() < deadline) {
  await sleep(POLL_MS);
  const version = engineVersion();
  if (version) {
    console.log(`docker:start: the Docker engine is running (${version}).`);
    process.exit(0);
  }
}

console.error(
  `docker:start: the Docker engine did not answer within ${READY_TIMEOUT_MS / 60_000} minutes. Report it as infrastructure; do not start Docker Desktop another way.`,
);
process.exit(1);
