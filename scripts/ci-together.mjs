#!/usr/bin/env node
// Runs commands that read nothing of each other's side by side, for the steps of a CI shard that come before its
// tests (.github/workflows/ci.yml): pulling the AppHost's images, building the frontends, building the tests. In a row
// they were the larger part of the about 175 s an end-to-end shard spent before its first test, on every shard.
//
//   node scripts/ci-together.mjs "<name>" "<command>" ["<name>" "<command>"]...
//
// The last command prints as it runs, in a log group. The others print when everything has ended, each in its own
// group, so their lines never interleave. Every command's seconds are printed, because the workflow's step now times
// them together. The script exits 1 when any command failed, after all of them have ended, so one run shows every
// failure. A run that is cancelled or times out prints what the waiting commands had written so far.
//
// Why a script: a `run:` step runs one line after another, and shell job control would be the same lines again in each
// job with no test. Node, because every runner has it and the unit tests can run this on Windows too.
import { spawn } from 'node:child_process';

const args = process.argv.slice(2);
if (args.length < 4 || args.length % 2 !== 0) {
  console.error('usage: node scripts/ci-together.mjs "<name>" "<command>" "<name>" "<command>" [...]');
  process.exit(2);
}

const commands = [];
for (let i = 0; i < args.length; i += 2) commands.push({ name: args[i], command: args[i + 1], log: '', done: false });
const last = commands[commands.length - 1];

function start(entry) {
  const began = Date.now();
  const streams = entry === last;
  return new Promise((resolve) => {
    const child = spawn(entry.command, { shell: true, stdio: streams ? 'inherit' : ['ignore', 'pipe', 'pipe'] });
    if (!streams) {
      child.stdout.on('data', (data) => { entry.log += data; });
      child.stderr.on('data', (data) => { entry.log += data; });
    }

    const end = (code) => {
      entry.code = code;
      entry.seconds = Math.round((Date.now() - began) / 1000);
      entry.done = true;
      resolve();
    };
    child.on('error', (error) => { entry.log += `${error.message}\n`; end(1); });
    child.on('close', (code) => end(code ?? 1));
  });
}

/** The log of each command that did not print as it ran, in its own group. */
function printLogs() {
  for (const entry of commands.filter((entry) => entry !== last)) {
    console.log(`::group::${entry.name}${entry.done ? '' : ' (still running)'}`);
    process.stdout.write(entry.log.endsWith('\n') || entry.log === '' ? entry.log : `${entry.log}\n`);
    console.log('::endgroup::');
  }
}

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    console.log('::endgroup::');
    printLogs();
    process.exit(1);
  });
}

console.log(`::group::${last.name}`);
const running = commands.map(start);
await running[running.length - 1];
console.log('::endgroup::');
await Promise.all(running);
printLogs();
for (const entry of commands) {
  console.log(`${entry.code === 0 ? 'passed' : 'FAILED'}: ${entry.name}, ${entry.seconds} s${entry.code === 0 ? '' : ` (exit ${entry.code})`}`);
}

process.exit(commands.some((entry) => entry.code !== 0) ? 1 : 0);
