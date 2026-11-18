#!/usr/bin/env node
// Where a CI run's time went (.github/workflows/ci.yml): seconds to `Gate`, the job that finished last, each job's
// queue and length, each shard's test step and the steps before it, and job-seconds. It is how a change to the
// workflow is measured (.claude/rules/test-value.md, "Measure a CI change by paired runs") and where the tables in
// docs/process/testing.md, "Time", come from.
//
//   node scripts/ci-timings.mjs <run id>...            reads each run's jobs with `gh api`
//   node scripts/ci-timings.mjs --last 25 [--event pull_request]
//                                                        the newest green runs of that event (default pull_request)
//   node scripts/ci-timings.mjs --file <json>           saved [{ run, jobs }] instead of GitHub
//   ... --json                                           prints the numbers as JSON instead of text
//
// Why a script: `gh run view --json jobs` gives start and end times and nothing derived from them.
//
// A run that queued says nothing about the workflow: the account runs 20 jobs at once and other pull requests share
// them. So a run where any job waited over 20 s is flagged, its seconds to `Gate` are for reading and not for deciding,
// and the numbers to compare are the ones a queue cannot touch: a named step's seconds, a job's seconds, job-seconds.
// Lighthouse is left out of job-seconds and of the last job to finish: it is outside the gate, and it always measures
// on a manual run.
import { spawnSync } from 'node:child_process';
import { readFileSync, realpathSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** Jobs the gate does not wait for, or that are the gate. */
const GATE = 'Gate';
const OUTSIDE_GATE = [/^Lighthouse/, /^Tree already tested$/];
/** A job that waited longer than this for a runner makes its run's total say more about the account than the workflow. */
export const QUEUE_LIMIT_SECONDS = 20;

const seconds = (from, to) => Math.round((Date.parse(to) - Date.parse(from)) / 1000);

/**
 * One run's numbers. `run` is GitHub's workflow run (`id`, `created_at`, `run_started_at`, `run_attempt`, `event`) and
 * `jobs` its jobs with their steps. A job that was skipped, or that an earlier attempt ran, is left out.
 */
export function summarize(run, jobs) {
  const attempt = run.run_attempt ?? 1;
  const ran = jobs.filter((job) => job.conclusion !== 'skipped' && job.started_at && job.completed_at && (job.run_attempt ?? attempt) === attempt);
  const measured = ran.map((job) => {
    const shard = /\s*\((\d+)\/(\d+)\)$/.exec(job.name);
    const steps = (job.steps ?? []).filter((step) => step.started_at && step.completed_at && step.conclusion !== 'skipped');
    const test = steps.find((step) => /\(shard \d+\/\d+\)$/.test(step.name));
    return {
      name: job.name,
      family: shard ? job.name.slice(0, shard.index) : job.name,
      shard: shard ? Number(shard[1]) : null,
      queue: seconds(job.created_at, job.started_at),
      seconds: seconds(job.started_at, job.completed_at),
      completedAt: job.completed_at,
      beforeTests: test ? seconds(job.started_at, test.started_at) : null,
      testStep: test ? seconds(test.started_at, test.completed_at) : null,
      steps: steps
        .map((step) => ({ name: step.name.replace(/ \(shard \d+\/\d+\)$/, ''), seconds: seconds(step.started_at, step.completed_at) }))
        .filter((step) => step.seconds >= 5),
    };
  });

  const gate = measured.find((job) => job.name === GATE);
  const gated = measured.filter((job) => job.name !== GATE && !OUTSIDE_GATE.some((pattern) => pattern.test(job.name)));
  const pole = gated.reduce((last, job) => (!last || Date.parse(job.completedAt) > Date.parse(last.completedAt) ? job : last), null);
  const longestQueue = measured.reduce((most, job) => (!most || job.queue > most.queue ? job : most), null);
  return {
    id: run.id,
    event: run.event,
    createdAt: run.created_at,
    attempt,
    // A rerun's jobs carry the first attempt's times, so only the latest attempt's own jobs are counted, from its start.
    rerun: attempt > 1,
    toGate: gate ? seconds(run.run_started_at ?? run.created_at, gate.completedAt) : null,
    pole: pole && { name: pole.name, queue: pole.queue, seconds: pole.seconds, beforeTests: pole.beforeTests, testStep: pole.testStep },
    longestQueue: longestQueue && { name: longestQueue.name, seconds: longestQueue.queue },
    queued: (longestQueue?.queue ?? 0) > QUEUE_LIMIT_SECONDS,
    jobSeconds: gated.reduce((sum, job) => sum + job.seconds, 0) + (gate?.seconds ?? 0),
    jobs: measured.map(({ completedAt, ...job }) => job),
  };
}

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted.length === 0 ? null : sorted.length % 2 ? sorted[(sorted.length - 1) / 2] : Math.round((sorted[sorted.length / 2 - 1] + sorted[sorted.length / 2]) / 2);
};
const spread = (values) => (values.length === 0 ? null : { median: median(values), min: Math.min(...values), max: Math.max(...values), count: values.length });

/** Every job family over several runs: its jobs' seconds, the seconds before its tests, its test step and its steps. */
export function aggregate(summaries) {
  const families = new Map();
  for (const job of summaries.flatMap((summary) => summary.jobs)) {
    const family = families.get(job.family) ?? families.set(job.family, { jobs: [], steps: new Map() }).get(job.family);
    family.jobs.push(job);
    for (const step of job.steps) family.steps.set(step.name, [...(family.steps.get(step.name) ?? []), step.seconds]);
  }
  const numbers = (jobs, field) => jobs.map((job) => job[field]).filter((value) => value !== null);
  return {
    runs: summaries.length,
    queuedRuns: summaries.filter((summary) => summary.queued).length,
    // Seconds to Gate only from runs that neither queued nor were rerun: the others measure the account or a flake.
    toGate: spread(summaries.filter((summary) => !summary.queued && !summary.rerun && summary.toGate !== null).map((summary) => summary.toGate)),
    jobSeconds: spread(summaries.filter((summary) => !summary.rerun).map((summary) => summary.jobSeconds)),
    poles: Object.fromEntries([...summaries.reduce((counts, summary) => (summary.pole ? counts.set(summary.pole.name, (counts.get(summary.pole.name) ?? 0) + 1) : counts), new Map())].sort((a, b) => b[1] - a[1])),
    families: Object.fromEntries([...families].map(([name, family]) => [name, {
      seconds: spread(numbers(family.jobs, 'seconds')),
      beforeTests: spread(numbers(family.jobs, 'beforeTests')),
      testStep: spread(numbers(family.jobs, 'testStep')),
      shards: Object.fromEntries([...new Set(family.jobs.map((job) => job.shard).filter((shard) => shard !== null))].sort((a, b) => a - b)
        .map((shard) => [shard, spread(numbers(family.jobs.filter((job) => job.shard === shard), 'testStep'))])),
      steps: Object.fromEntries([...family.steps].map(([step, values]) => [step, spread(values)])),
    }])),
  };
}

const range = (value) => (value === null ? '-' : value.count === 1 ? `${value.median}` : `${value.median} (${value.min} to ${value.max})`);

function print(summaries) {
  for (const run of summaries) {
    const notes = [run.rerun ? `attempt ${run.attempt}, its own jobs only` : null, run.queued ? `QUEUED: ${run.longestQueue.name} waited ${run.longestQueue.seconds} s` : null].filter(Boolean);
    console.log(`run ${run.id} (${run.event}, ${run.createdAt}): ${run.toGate ?? '-'} s to Gate, ${run.jobSeconds} job-seconds${notes.length ? `, ${notes.join(', ')}` : ''}`);
    if (run.pole) {
      const inside = run.pole.testStep === null ? '' : `, ${run.pole.beforeTests} s before its tests, ${run.pole.testStep} s of tests`;
      console.log(`  last to finish: ${run.pole.name}, ${run.pole.seconds} s after ${run.pole.queue} s queued${inside}`);
    }
  }

  const all = aggregate(summaries);
  console.log(`\n${all.runs} run(s), ${all.queuedRuns} queued. Seconds are the median, with the range when there is more than one.`);
  console.log(`to Gate (runs that neither queued nor were rerun): ${range(all.toGate)}`);
  console.log(`job-seconds a run, without Lighthouse: ${range(all.jobSeconds)}`);
  console.log(`last to finish: ${Object.entries(all.poles).map(([name, count]) => `${name} x${count}`).join(', ')}`);
  for (const [name, family] of Object.entries(all.families)) {
    console.log(`\n${name}: job ${range(family.seconds)}${family.testStep ? `, before tests ${range(family.beforeTests)}, test step ${range(family.testStep)}` : ''}`);
    for (const [shard, testStep] of Object.entries(family.shards)) console.log(`  shard ${shard}: test step ${range(testStep)}`);
    for (const [step, value] of Object.entries(family.steps)) console.log(`  step "${step}": ${range(value)}`);
  }
}

function gh(args) {
  const result = spawnSync('gh', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (result.status !== 0) {
    console.error(`gh ${args.join(' ')} failed:\n${result.stderr}`);
    process.exit(1);
  }
  return JSON.parse(result.stdout);
}

function main(args) {
  const options = { event: 'pull_request' };
  const ids = [];
  for (let i = 0; i < args.length; i++) {
    if (['--file', '--last', '--event'].includes(args[i])) options[args[i].slice(2)] = args[++i];
    else if (args[i] === '--json') options.json = true;
    else ids.push(args[i]);
  }

  let runs;
  if (options.file) {
    runs = JSON.parse(readFileSync(options.file, 'utf8'));
  } else {
    if (options.last) {
      ids.push(...gh(['run', 'list', '--workflow', 'CI', '--event', options.event, '--status', 'success', '--limit', options.last, '--json', 'databaseId'])
        .map((run) => String(run.databaseId)));
    }
    if (ids.length === 0) {
      console.error('usage: node scripts/ci-timings.mjs <run id>... | --last <n> [--event <event>] | --file <json>   [--json]');
      process.exit(2);
    }
    // `{owner}/{repo}` is gh's own placeholder for the repository of the working directory.
    runs = ids.map((id) => ({
      run: gh(['api', `repos/{owner}/{repo}/actions/runs/${id}`]),
      jobs: gh(['api', '--paginate', '--slurp', `repos/{owner}/{repo}/actions/runs/${id}/jobs?filter=latest&per_page=100`]).flatMap((page) => page.jobs),
    }));
  }

  const summaries = runs.map(({ run, jobs }) => summarize(run, jobs));
  if (options.json) console.log(JSON.stringify({ runs: summaries, all: aggregate(summaries) }, null, 2));
  else print(summaries);
}

/** Whether node was asked to run this file, not to import it (a test imports it). Compared by real path. */
function isEntrypoint() {
  const entry = process.argv[1];
  if (!entry) return false;
  try {
    return realpathSync(fileURLToPath(import.meta.url)) === realpathSync(path.resolve(entry));
  } catch {
    return import.meta.url === pathToFileURL(path.resolve(entry)).href;
  }
}

if (isEntrypoint()) {
  main(process.argv.slice(2));
}
