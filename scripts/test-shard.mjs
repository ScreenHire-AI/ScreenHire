#!/usr/bin/env node
// Splits a .NET test project's test classes into shards, for CI's matrix jobs (.github/workflows/ci.yml), and prints
// the xUnit arguments that run one shard: `--filter-class <class>` for each class in it, one argument per line.
//
//   node scripts/test-shard.mjs <test project folder> <shard> <shards>   lists the built project's tests
//   node scripts/test-shard.mjs --list <file> [--durations <file>] <shard> <shards>
//                                                                        a saved `--list-tests` output (the tests)
//   node scripts/test-shard.mjs --record <folder> [--durations <file>]  rewrites tests/shard-durations.json from the
//                                                                        CTRF reports (*.ctrf.json) under <folder>
//   node scripts/test-shard.mjs --slowest <folder> [count]              prints the slowest tests in the CTRF reports
//                                                                        under <folder>, 15 unless told otherwise
//   node scripts/test-shard.mjs --check <namespace>=<shards>... [--durations <file>] [--classes <file>]
//                                                                        prints what breaks the limits below, one per
//                                                                        line, and exits 1 if anything does
//
// Why a script: xUnit v3 and Microsoft Testing Platform have no shard option, unlike Playwright and Vitest.
// Meziantou.ShardedTest has no licence (ADR-0008) and builds a VSTest --filter, which `dotnet test` on MTP does not take.
//
// A class stays whole, because a class fixture can boot a whole AppHost of its own (DevelopmentDemoFixture). Classes go
// to the lightest shard, heaviest first. The order is fixed (weight, then name), so every job of one run computes the
// same split, and the shards together hold every class exactly once. A shard with no class fails, rather than run the
// whole project with no filter, and so does a listing whose tests the script cannot all place: a line it cannot read
// would otherwise be a test no shard runs.
//
// What a class weighs (tests/shard-durations.json, measured on CI and refreshed with --record):
//   - `seconds`: its tests' seconds, summed. xUnit runs `parallelism` tests at once on the runners (--parallel all on 4
//     cores), so a shard's parallel tests take about their summed seconds divided by that.
//   - `alone`: the seconds of the shard's clock a class takes when it runs alone, after the parallel tests, the AppHost
//     it boots for itself included (a collection with DisableParallelization). It weighs those seconds times
//     `parallelism`, which puts it in the same unit as the others: nothing else runs beside it. The keys of `alone` say
//     which classes these are; TestShardPlanTests fails when they differ from the collections in the test sources.
//   - a class with no measurement weighs its test count times the median seconds per test of the measured ones.
//   - `longest`: each class's longest test. A shard cannot end before its longest test, however light the shard is.
//
// The limits --check holds (.claude/rules/test-value.md): no class heavier than an average shard, unless it runs alone
// and so cannot be split; no test over a quarter longer than an average shard's parallel tests take; and, given the
// classes on disk, at most a quarter of a project's classes without a measurement.
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const defaultDurations = join(root, 'tests', 'shard-durations.json');
const refresh = 'gh run download <run-id> -p "durations-*" -D TestResults/durations, then node scripts/test-shard.mjs --record TestResults/durations';

// How far past an average shard's parallel tests one test may run. The same quarter a shard may run past its suite's
// average before its weights are refreshed, and it covers the estimate: the runners run 3.6 to 4 tests at once, not 4.
const LONGEST_TEST_TOLERANCE = 1.25;

function fail(message) {
  console.error(message);
  process.exit(1);
}

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

/** How many tests the listing says it found ("Discovered N tests in assembly"), or null when it does not say. */
export function discoveredOf(listing) {
  const found = [...listing.matchAll(/^\s*Discovered (\d+) tests? in assembly/gm)];
  return found.length === 0 ? null : found.reduce((sum, match) => sum + Number(match[1]), 0);
}

/** The measurements: `{ parallelism, seconds, longest, alone }`, empty when there is no file. Another shape fails. */
export function readDurations(file) {
  if (!file || !existsSync(file)) return { parallelism: 4, seconds: {}, longest: {}, alone: {} };
  const data = JSON.parse(readFileSync(file, 'utf8'));
  if (typeof data.seconds !== 'object' || data.seconds === null || !Number.isFinite(data.parallelism) || data.parallelism <= 0) {
    fail(`${file} is not in the shape --record writes ({ parallelism, seconds, longest, alone }). Refresh it: ${refresh}.`);
  }
  return { parallelism: data.parallelism, seconds: data.seconds, longest: data.longest ?? {}, alone: data.alone ?? {} };
}

/** What a measured class weighs, in test-seconds: see the header. Undefined for a class with no measurement. */
function measuredWeight(type, durations) {
  return durations.alone[type] !== undefined ? durations.alone[type] * durations.parallelism : durations.seconds[type];
}

/** Each class's weight: its measured weight, or its test count times the median measured seconds per test. */
export function weightsOf(counts, durations) {
  const perTest = [...counts]
    .filter(([type]) => durations.seconds[type] !== undefined)
    .map(([type, tests]) => durations.seconds[type] / tests)
    .sort((a, b) => a - b);
  const median = perTest.length > 0 ? perTest[Math.floor(perTest.length / 2)] : 1;
  return new Map([...counts].map(([type, tests]) => [type, measuredWeight(type, durations) ?? tests * median]));
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

/**
 * The measurements from CTRF reports, one array of tests per report (`type`, `stop` and `duration` in milliseconds).
 * A class's numbers are the mean over the reports that ran it, so several runs' reports give one run's seconds. Only
 * `stop` and `duration` are read: xUnit stamps `start` when a test is queued, not when it begins. `known` is the file
 * being refreshed: its `alone` keys say which classes run alone, and one that no report ran keeps its number.
 */
export function recordOf(reports, known) {
  const perReport = { seconds: new Map(), longest: new Map(), alone: new Map() };
  const add = (map, type, value) => map.set(type, [...(map.get(type) ?? []), value]);
  for (const tests of reports) {
    const classes = new Map();
    for (const test of tests) classes.set(test.type, [...(classes.get(test.type) ?? []), test]);
    for (const [type, own] of classes) {
      add(perReport.seconds, type, own.reduce((sum, test) => sum + test.duration, 0) / 1000);
      add(perReport.longest, type, Math.max(...own.map((test) => test.duration)) / 1000);
      if (known.alone[type] !== undefined) {
        // From the last test that ended before this class's first test began, so the wait for its own stack counts.
        const began = Math.min(...own.map((test) => test.stop - test.duration));
        const before = tests.filter((test) => test.type !== type && test.stop <= began).map((test) => test.stop);
        const from = before.length > 0 ? Math.max(...before) : began;
        add(perReport.alone, type, (Math.max(...own.map((test) => test.stop)) - from) / 1000);
      }
    }
  }

  const mean = (map) => Object.fromEntries([...map].sort(([a], [b]) => (a < b ? -1 : 1))
    .map(([type, values]) => [type, Math.round((values.reduce((sum, value) => sum + value, 0) / values.length) * 10) / 10]));
  const alone = { ...known.alone, ...mean(perReport.alone) };
  return {
    parallelism: known.parallelism,
    seconds: mean(perReport.seconds),
    longest: mean(perReport.longest),
    alone: Object.fromEntries(Object.keys(alone).sort().map((type) => [type, alone[type]])),
  };
}

/**
 * The slowest tests in CTRF reports, slowest first: each test's mean seconds over the reports that ran it. It is what
 * to read when a shard runs long or --check names a class whose longest test is over the limit.
 */
export function slowestOf(reports, count) {
  const seconds = new Map();
  for (const test of reports.flat()) seconds.set(test.name, [...(seconds.get(test.name) ?? []), test.duration / 1000]);
  return [...seconds]
    .map(([name, values]) => ({ name, seconds: Math.round((values.reduce((sum, value) => sum + value, 0) / values.length) * 10) / 10 }))
    .sort((a, b) => b.seconds - a.seconds || (a.name < b.name ? -1 : 1))
    .slice(0, count);
}

/** Every CTRF report under `folder`, as `recordOf` takes them. */
function reportsUnder(folder) {
  const reports = [];
  const visit = (path) => {
    for (const entry of readdirSync(path)) {
      const full = join(path, entry);
      if (statSync(full).isDirectory()) {
        visit(full);
      } else if (entry.endsWith('.ctrf.json')) {
        // xUnit's CTRF suite path ends with the class's full name, then the method.
        reports.push(JSON.parse(readFileSync(full, 'utf8')).results.tests
          .map((test) => ({ type: test.suite?.[test.suite.length - 2], name: test.name, stop: test.stop, duration: test.duration }))
          .filter((test) => test.type));
      }
    }
  };
  visit(folder);
  return reports;
}

/**
 * What breaks the limits, one sentence each. `shardsByProject` maps a test project's root namespace to its shard
 * count in ci.yml; `classesOnDisk`, when given, is every test class in the sources, for the staleness limit.
 */
export function check(durations, shardsByProject, classesOnDisk) {
  const problems = [];
  const round = (value) => Math.round(value * 10) / 10;
  for (const [project, shards] of Object.entries(shardsByProject)) {
    const inProject = (type) => type.startsWith(`${project}.`);
    const types = Object.keys(durations.seconds).filter(inProject);
    if (types.length === 0) {
      problems.push(`tests/shard-durations.json has no measurement of a ${project} class, so its shards are cut blind. Refresh it: ${refresh}.`);
      continue;
    }

    const average = types.reduce((sum, type) => sum + measuredWeight(type, durations), 0) / shards;
    const phase = average / durations.parallelism;
    for (const type of types.filter((type) => durations.alone[type] === undefined)) {
      if (durations.seconds[type] > average) {
        problems.push(`${type} weighs ${round(durations.seconds[type])} test-seconds, more than an average shard of ${project} (${round(average)} over ${shards} shards), so its shard always runs long. Split the class.`);
      }

      if ((durations.longest[type] ?? 0) > phase * LONGEST_TEST_TOLERANCE) {
        problems.push(`${type} has a test of ${round(durations.longest[type])} s, over a quarter longer than an average shard's parallel tests take in ${project} (${round(phase)} s), and a shard cannot end before its longest test. Split the test, or make it a theory over groups.`);
      }
    }

    if (classesOnDisk) {
      const onDisk = classesOnDisk.filter(inProject);
      const unmeasured = onDisk.filter((type) => durations.seconds[type] === undefined);
      if (onDisk.length === 0) {
        problems.push(`No test class of ${project} was found in the sources, so nothing was compared.`);
      } else if (unmeasured.length * 4 > onDisk.length) {
        problems.push(`${unmeasured.length} of ${onDisk.length} test classes of ${project} have no measurement in tests/shard-durations.json, over a quarter, so its shards are cut on guesses. Refresh it from a green run whose shards ran: ${refresh}.`);
      }
    }
  }

  return problems;
}

function usage() {
  console.error('usage: node scripts/test-shard.mjs <test project folder> <shard> <shards>');
  console.error('       node scripts/test-shard.mjs --list <file> [--durations <file>] <shard> <shards>');
  console.error('       node scripts/test-shard.mjs --record <folder> [--durations <file>]');
  console.error('       node scripts/test-shard.mjs --slowest <folder> [count]');
  console.error('       node scripts/test-shard.mjs --check <namespace>=<shards>... [--durations <file>] [--classes <file>]');
  process.exit(2);
}

function main(args) {
  const options = {};
  const positional = [];
  for (let i = 0; i < args.length; i++) {
    if (['--list', '--durations', '--record', '--classes', '--slowest'].includes(args[i])) {
      options[args[i].slice(2)] = args[++i];
    } else if (args[i] === '--check') {
      options.check = true;
    } else {
      positional.push(args[i]);
    }
  }

  if (options.record) {
    const file = options.durations ?? defaultDurations;
    const reports = reportsUnder(options.record);
    if (reports.length === 0) fail(`No CTRF report (*.ctrf.json) under ${options.record}.`);
    const recorded = recordOf(reports, readDurations(file));
    // Written here and not through the shell's `>`, which would empty the file before its `alone` keys are read.
    writeFileSync(file, `${JSON.stringify(recorded, null, 2)}\n`);
    console.error(`${file}: ${Object.keys(recorded.seconds).length} classes from ${reports.length} reports.`);
    return;
  }

  if (options.slowest) {
    const count = positional.length > 0 ? Number(positional[0]) : 15;
    if (!Number.isInteger(count) || count < 1) usage();
    const reports = reportsUnder(options.slowest);
    if (reports.length === 0) fail(`No CTRF report (*.ctrf.json) under ${options.slowest}.`);
    for (const test of slowestOf(reports, count)) console.log(`${test.seconds.toFixed(1).padStart(7)} s  ${test.name}`);
    return;
  }

  if (options.check) {
    const shardsByProject = Object.fromEntries(positional.map((pair) => pair.split('=')).map(([project, shards]) => [project, Number(shards)]));
    if (positional.length === 0 || Object.values(shardsByProject).some((shards) => !Number.isInteger(shards) || shards < 1)) usage();
    const classes = options.classes ? readFileSync(options.classes, 'utf8').split(/\r?\n/).map((line) => line.trim()).filter(Boolean) : null;
    const problems = check(readDurations(options.durations ?? defaultDurations), shardsByProject, classes);
    for (const problem of problems) console.log(problem);
    process.exit(problems.length > 0 ? 1 : 0);
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
      fail(`Could not list the tests of ${project} (build it first):\n${listed.stdout}${listed.stderr}`);
    }
    listing = listed.stdout;
  }

  const durations = readDurations(options.durations ?? (options.list ? null : defaultDurations));
  const counts = classesOf(listing);
  const placed = [...counts.values()].reduce((sum, tests) => sum + tests, 0);
  const discovered = discoveredOf(listing);
  if (discovered === null) {
    fail('The test listing has no "Discovered N tests in assembly" line, so nothing proves every test is in a shard.');
  }

  if (placed !== discovered) {
    fail(`The test listing says it discovered ${discovered} tests, and ${placed} of its lines name a class and a method. A test this script cannot place would run in no shard.`);
  }

  const { weight, classes } = shard(weightsOf(counts, durations), index, total);
  if (classes.length === 0) {
    fail(`Shard ${index}/${total} has no test class: ${counts.size} classes cannot fill ${total} shards.`);
  }

  const measured = classes.filter((type) => durations.seconds[type] !== undefined).length;
  console.error(`Shard ${index}/${total}: ${classes.length} of ${counts.size} classes (${measured} measured), about ${Math.round(weight)} test-seconds, about ${Math.round(weight / durations.parallelism)} s on the clock once the stack is up.`);
  for (const type of classes.sort()) {
    console.log('--filter-class');
    console.log(type);
  }
}

main(process.argv.slice(2));
