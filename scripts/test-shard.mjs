#!/usr/bin/env node
// Splits a .NET test project's test classes into shards, for CI's matrix jobs (.github/workflows/ci.yml), and prints
// the xUnit arguments that run one shard: `--filter-class <class>` for each class in it, one argument per line.
//
//   node scripts/test-shard.mjs <test project folder> <shard> <shards>   lists the built project's tests
//   node scripts/test-shard.mjs --list <file> [--durations <file>] <shard> <shards>
//                                                                        a saved `--list-tests` output (the tests)
//   node scripts/test-shard.mjs --record <folder>                       prints tests/shard-durations.json from the
//                                                                        CTRF reports (*.ctrf.json) under <folder>
//
// Why a script: xUnit v3 and Microsoft Testing Platform have no shard option, unlike Playwright and Vitest.
// Meziantou.ShardedTest has no licence (ADR-0008) and builds a VSTest --filter, which `dotnet test` on MTP does not take.
//
// A class stays whole, because a class fixture can boot a whole AppHost of its own (DevelopmentDemoFixture). Each class
// weighs its measured seconds from tests/shard-durations.json (CI's CTRF reports, refreshed with --record); a class
// with no measurement weighs its test count times the median seconds per test of the measured ones. Classes go to the
// lightest shard, heaviest first. The order is fixed (weight, then name), so every job of one run computes the same
// split, and the shards together hold every class exactly once. A shard with no class fails, rather than run the whole
// project with no filter.
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const defaultDurations = join(root, 'tests', 'shard-durations.json');

/** The test classes in `dotnet test --list-tests` output, with their test counts. Theory rows count once each. */
export function classesOf(listing) {
  const counts = new Map();
  for (const line of listing.split(/\r?\n/)) {
    if (!line.startsWith('  ') || line.startsWith('   ')) continue;
    const name = line.trim().split('(')[0];
    const dot = name.lastIndexOf('.');
    if (dot <= 0 || name.includes(' ')) continue;
    const type = name.slice(0, dot);
    counts.set(type, (counts.get(type) ?? 0) + 1);
  }
  return counts;
}

/** Each class's weight: its measured seconds, or its test count times the median measured seconds per test. */
export function weightsOf(counts, durations) {
  const perTest = [...counts]
    .filter(([type]) => durations[type] !== undefined)
    .map(([type, tests]) => durations[type] / tests)
    .sort((a, b) => a - b);
  const median = perTest.length > 0 ? perTest[Math.floor(perTest.length / 2)] : 1;
  return new Map([...counts].map(([type, tests]) => [type, durations[type] ?? tests * median]));
}

/** The classes of shard `index` (1-based) of `total`, with the shard's total weight. */
export function shard(weights, index, total) {
  const buckets = Array.from({ length: total }, () => ({ weight: 0, classes: [] }));
  const ordered = [...weights].sort(([a, x], [b, y]) => y - x || (a < b ? -1 : a > b ? 1 : 0));
  for (const [type, weight] of ordered) {
    const lightest = buckets.reduce((best, bucket) => (bucket.weight < best.weight ? bucket : best));
    lightest.weight += weight;
    lightest.classes.push(type);
  }
  return buckets[index - 1];
}

/** Seconds per test class, summed from every CTRF report under `folder`. */
function record(folder) {
  const seconds = {};
  const visit = (path) => {
    for (const entry of readdirSync(path)) {
      const full = join(path, entry);
      if (statSync(full).isDirectory()) {
        visit(full);
      } else if (entry.endsWith('.ctrf.json')) {
        for (const test of JSON.parse(readFileSync(full, 'utf8')).results.tests) {
          // xUnit's CTRF suite path ends with the class's full name, then the method.
          const type = test.suite?.[test.suite.length - 2];
          if (type) seconds[type] = (seconds[type] ?? 0) + test.duration / 1000;
        }
      }
    }
  };
  visit(folder);
  const sorted = Object.fromEntries(Object.keys(seconds).sort().map((type) => [type, Math.round(seconds[type] * 10) / 10]));
  if (Object.keys(sorted).length === 0) {
    console.error(`No CTRF report (*.ctrf.json) under ${folder}.`);
    process.exit(1);
  }
  console.log(JSON.stringify(sorted, null, 2));
}

function usage() {
  console.error('usage: node scripts/test-shard.mjs <test project folder> <shard> <shards>');
  console.error('       node scripts/test-shard.mjs --list <file> [--durations <file>] <shard> <shards>');
  console.error('       node scripts/test-shard.mjs --record <folder>');
  process.exit(2);
}

function main(args) {
  const options = {};
  const positional = [];
  for (let i = 0; i < args.length; i++) {
    if (['--list', '--durations', '--record'].includes(args[i])) {
      options[args[i].slice(2)] = args[++i];
    } else {
      positional.push(args[i]);
    }
  }

  if (options.record) {
    record(options.record);
    return;
  }

  const [project, indexText, totalText] = options.list ? [null, ...positional] : positional;
  const index = Number(indexText);
  const total = Number(totalText);
  if ((!options.list && !project) || !Number.isInteger(index) || !Number.isInteger(total) || index < 1 || index > total) {
    usage();
  }

  let listing;
  if (options.list) {
    listing = readFileSync(options.list, 'utf8');
  } else {
    const listed = spawnSync('dotnet', ['test', '--project', project, '--no-build', '--list-tests'], { encoding: 'utf8' });
    if (listed.status !== 0) {
      console.error(`Could not list the tests of ${project} (build it first):\n${listed.stdout}${listed.stderr}`);
      process.exit(1);
    }
    listing = listed.stdout;
  }

  const durationsFile = options.durations ?? (options.list ? null : defaultDurations);
  const durations = durationsFile && existsSync(durationsFile) ? JSON.parse(readFileSync(durationsFile, 'utf8')) : {};
  const counts = classesOf(listing);
  const { weight, classes } = shard(weightsOf(counts, durations), index, total);
  if (classes.length === 0) {
    console.error(`Shard ${index}/${total} has no test class: ${counts.size} classes cannot fill ${total} shards.`);
    process.exit(1);
  }

  const measured = classes.filter((type) => durations[type] !== undefined).length;
  console.error(`Shard ${index}/${total}: ${classes.length} of ${counts.size} classes (${measured} measured), about ${Math.round(weight)} s of tests.`);
  for (const type of classes.sort()) {
    console.log('--filter-class');
    console.log(type);
  }
}

main(process.argv.slice(2));
