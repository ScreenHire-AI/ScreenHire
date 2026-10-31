#!/usr/bin/env node
// Splits a .NET test project's test classes into shards, for CI's matrix jobs (.github/workflows/ci.yml), and prints
// the xUnit arguments that run one shard: `--filter-class <class>` for each class in it, one argument per line.
//
//   node scripts/test-shard.mjs <test project folder> <shard> <shards>      lists the built project's tests
//   node scripts/test-shard.mjs --list <file> <shard> <shards>              reads a saved `--list-tests` output (tests)
//
// Why a script: xUnit v3 and Microsoft Testing Platform have no shard option, unlike Playwright and Vitest.
// Meziantou.ShardedTest has no licence (ADR-0008) and builds a VSTest --filter, which `dotnet test` on MTP does not take.
//
// A class stays whole, because a class fixture can boot a whole AppHost of its own (DevelopmentDemoFixture). Classes go
// to the shard with the fewest tests so far, largest first, the way Playwright balances its shards: by test count, not
// time. The order is fixed (count, then name), so every job of one run computes the same split, and the shards together
// hold every class exactly once. A shard with no class fails, rather than run the whole project with no filter.
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

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

/** The classes of shard `index` (1-based) of `total`. */
export function shard(counts, index, total) {
  const buckets = Array.from({ length: total }, () => ({ tests: 0, classes: [] }));
  const ordered = [...counts].sort(([a, x], [b, y]) => y - x || (a < b ? -1 : a > b ? 1 : 0));
  for (const [type, tests] of ordered) {
    const lightest = buckets.reduce((best, bucket) => (bucket.tests < best.tests ? bucket : best));
    lightest.tests += tests;
    lightest.classes.push(type);
  }
  return buckets[index - 1];
}

function main(args) {
  const [source, sourceValue, ...rest] = args;
  const [indexText, totalText] = source === '--list' ? rest : [sourceValue, rest[0]];
  const index = Number(indexText);
  const total = Number(totalText);
  if (!source || !Number.isInteger(index) || !Number.isInteger(total) || index < 1 || index > total) {
    console.error('usage: node scripts/test-shard.mjs <test project folder> <shard> <shards>');
    console.error('       node scripts/test-shard.mjs --list <file> <shard> <shards>');
    process.exit(2);
  }

  let listing;
  if (source === '--list') {
    listing = readFileSync(sourceValue, 'utf8');
  } else {
    const listed = spawnSync('dotnet', ['test', '--project', source, '--no-build', '--list-tests'], { encoding: 'utf8' });
    if (listed.status !== 0) {
      console.error(`Could not list the tests of ${source} (build it first):\n${listed.stdout}${listed.stderr}`);
      process.exit(1);
    }
    listing = listed.stdout;
  }

  const counts = classesOf(listing);
  const { tests, classes } = shard(counts, index, total);
  if (classes.length === 0) {
    console.error(`Shard ${index}/${total} has no test class: ${counts.size} classes cannot fill ${total} shards.`);
    process.exit(1);
  }

  console.error(`Shard ${index}/${total}: ${classes.length} of ${counts.size} classes, ${tests} tests.`);
  for (const type of classes.sort()) {
    console.log('--filter-class');
    console.log(type);
  }
}

main(process.argv.slice(2));
