#!/usr/bin/env node
// Decides whether a push to main needs its tests run again (.github/workflows/ci.yml, the `tested-tree` job).
//
// A pull request run tests refs/pull/<n>/merge and posts its checks on the PR's head. When the squash that lands on main
// has the same tree as that head, main holds exactly the files a green `Gate` already tested, and a second run could only
// find a flake. This compares the pushed tree with the head of the PR it came from and answers `tested` only when the
// trees are equal and every check on that head passed, `Gate` among them.
//
// A squash of a branch that was behind main has a different tree, so it runs in full. That is the case the push to main
// exists for: two green branches that break each other. A push with no merged PR behind it (a direct /merge-back, or
// GitHub not yet listing the PR for the new commit) runs in full too.
//
// One gap, accepted in ADR-0020, decision 12: the PR run tested the head merged with main as it was then, so a change on
// main reverted between that run and the merge leaves equal trees on a tree the run did not test.
//
// Any doubt runs the tests. An API error, a check still running, a head with no passing `Gate`: each answers false, and
// the reason is printed on every run. A required check counts only when GitHub Actions posted it, so another app's check
// of the same name passes nothing.
//
//   node scripts/tested-tree.mjs <sha>
//
// Reads GITHUB_REPOSITORY and GITHUB_TOKEN, and writes `tested=` to GITHUB_OUTPUT. Only .github/actions/tested-tree runs it.
import { appendFileSync, realpathSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** The checks a head must have passed. `Gate` fails unless every test job and shard succeeded, so it stands for them all. */
export const REQUIRED_PASSES = ['Gate'];

const PASSING_CONCLUSIONS = new Set(['success', 'skipped', 'neutral']);

/**
 * @typedef {object} Api
 * @property {(sha: string) => Promise<Array<{ number: number, merged_at: string | null, base: { ref: string }, head: { sha: string } }>>} pullsForCommit
 * @property {(sha: string) => Promise<string>} treeOf
 * @property {(sha: string) => Promise<Array<{ name: string, status: string, conclusion: string | null, app?: { slug: string } | null }>>} checksOn
 */

/**
 * @param {string} sha the pushed commit
 * @param {Api} api
 * @returns {Promise<{ tested: boolean, reason: string }>}
 */
export async function decideTestedTree(sha, api) {
  let pulls;
  try {
    pulls = await api.pullsForCommit(sha);
  } catch (error) {
    return { tested: false, reason: `could not read the PRs for ${sha}: ${describe(error)}` };
  }
  const merged = pulls.filter((pull) => pull.merged_at !== null && pull.base.ref === 'main');
  if (merged.length === 0) {
    return { tested: false, reason: `${sha} has no merged PR behind it` };
  }

  const reasons = [];
  for (const pull of merged) {
    const verdict = await judgePull(sha, pull, api);
    if (verdict.tested) return verdict;
    reasons.push(verdict.reason);
  }
  return { tested: false, reason: reasons.join('; ') };
}

async function judgePull(sha, pull, api) {
  const label = `PR #${pull.number}`;
  try {
    const [pushedTree, headTree] = await Promise.all([api.treeOf(sha), api.treeOf(pull.head.sha)]);
    if (pushedTree !== headTree) {
      return { tested: false, reason: `${label}: the pushed tree differs from its head's` };
    }

    const checks = await api.checksOn(pull.head.sha);
    const unfinished = checks.filter((check) => check.status !== 'completed').map((check) => check.name);
    if (unfinished.length > 0) {
      return { tested: false, reason: `${label}: still running on its head: ${unfinished.join(', ')}` };
    }
    const failing = checks.filter((check) => !PASSING_CONCLUSIONS.has(check.conclusion ?? '')).map((check) => check.name);
    if (failing.length > 0) {
      return { tested: false, reason: `${label}: did not pass on its head: ${failing.join(', ')}` };
    }
    const absent = REQUIRED_PASSES.filter((name) =>
      !checks.some((check) => check.name === name && check.conclusion === 'success' && check.app?.slug === 'github-actions'));
    if (absent.length > 0) {
      return { tested: false, reason: `${label}: no passing run on its head of: ${absent.join(', ')}` };
    }
    return { tested: true, reason: `${label} tested this tree on ${pull.head.sha}` };
  } catch (error) {
    return { tested: false, reason: `${label}: could not read its head: ${describe(error)}` };
  }
}

function describe(error) {
  return error instanceof Error ? error.message : String(error);
}

export function githubApi(repository, token, fetchResponse = fetch) {
  const request = async (route) => {
    const response = await fetchResponse(`https://api.github.com/repos/${repository}/${route}`, {
      headers: {
        Accept: 'application/vnd.github+json',
        Authorization: `Bearer ${token}`,
        'X-GitHub-Api-Version': '2022-11-28',
      },
    });
    if (!response.ok) throw new Error(`${route} answered ${response.status}`);
    return response.json();
  };
  return {
    pullsForCommit: (sha) => request(`commits/${sha}/pulls`),
    treeOf: async (sha) => (await request(`git/commits/${sha}`)).tree.sha,
    checksOn: async (sha) => {
      const checks = [];
      for (let page = 1; ; page += 1) {
        const body = await request(`commits/${sha}/check-runs?filter=latest&per_page=100&page=${page}`);
        checks.push(...body.check_runs);
        if (checks.length >= body.total_count || body.check_runs.length === 0) return checks;
      }
    },
  };
}

async function main() {
  const sha = process.argv[2];
  const { GITHUB_REPOSITORY: repository, GITHUB_TOKEN: token, GITHUB_OUTPUT: outputPath } = process.env;
  const decision = sha && repository && token
    ? await decideTestedTree(sha, githubApi(repository, token))
    : { tested: false, reason: 'the sha, GITHUB_REPOSITORY or GITHUB_TOKEN is missing' };
  console.log(`[tested-tree] ${decision.tested ? 'skipping the tests' : 'running the tests'}: ${decision.reason}`);
  if (outputPath) appendFileSync(outputPath, `tested=${decision.tested}\n`);
}

/**
 * Whether node was asked to run this file, not to import it. Compared by real path, because node resolves the entry
 * module through links. Importing it from `node -e` (no argv[1]) or from a test is not running it.
 */
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
  await main();
}
