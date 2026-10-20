#!/usr/bin/env node
// One-time, machine-wide install of the browser the end-to-end tests drive (Chromium). Run with `pnpm e2e:install`.
//
// It builds tests/ScreenHire.E2E and runs the Playwright driver bundled with the Microsoft.Playwright NuGet package,
// so the browser revision always matches the .NET package version (the npm `playwright` CLI may download a different
// one). Browsers land in Playwright's per-user cache (%LOCALAPPDATA%\ms-playwright, ~/.cache/ms-playwright, or
// PLAYWRIGHT_BROWSERS_PATH), so every worktree on this machine shares them. Re-run after bumping Microsoft.Playwright.
// Linux also needs the browser's system libraries once: run the same driver with `install-deps chromium` (needs sudo).
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const project = join(root, 'tests', 'ScreenHire.E2E');

function run(command, args) {
  const result = spawnSync(command, args, { cwd: root, stdio: 'inherit' });
  if (result.error) {
    console.error(`Could not run ${command}: ${result.error.message}`);
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

run('dotnet', ['build', project, '--nologo', '-v', 'quiet']);

const driver = join(project, 'bin', 'Debug', 'net10.0', '.playwright');
const node = join(driver, 'node', `${process.platform}_${process.arch}`, process.platform === 'win32' ? 'node.exe' : 'node');
const cli = join(driver, 'package', 'cli.js');
if (!existsSync(cli)) {
  console.error(`Playwright driver not found at ${cli}; did the build of ${project} succeed?`);
  process.exit(1);
}

// The bundled Node build matches the driver; fall back to this Node (>= 22, per package.json engines) if it is absent.
run(existsSync(node) ? node : process.execPath, [cli, 'install', 'chromium']);
