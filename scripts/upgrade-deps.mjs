#!/usr/bin/env node
// The mechanical half of /upgrade-deps (.claude/skills/upgrade-deps/SKILL.md). Reads every pnpm and NuGet dependency
// the repo declares, asks the registries what exists now, and says per lockstep group what can move and whether earlier
// research still answers it. Nothing here depends on the date: a target is the newest release, and research is reused
// while the versions match, however old it is.
//
// The native tools do what they can: `pnpm outdated` says which npm packages moved and `pnpm audit` which carry
// advisories, and this reads both. Neither they nor `dotnet package list --outdated` can do the rest, which is why this
// script exists:
// - The dotnet command lists only what a project references, after a restore. It never sees the transitive pins in
//   Directory.Packages.props or the local tools in dotnet-tools.json.
// - Both report one `latest`. When .NET 11 and Aspire 17 shipped, `latest` became a new major for whole groups, and a
//   10.0.x security patch must not wait on that decision. So a group whose newest release crosses a major splits:
//   `<group>` takes the newest release on the major we run, and `<group>-major` takes the newest.
// - Packages that must match move as one group: a `$(Property)` version in Directory.Packages.props, and the sets in
//   LOCKSTEP_GROUPS.
// - A target is checked against everything else we pin. That covers an npm peer range, and a NuGet dependency floor
//   above a central pin, which CentralPackageTransitivePinningEnabled turns into a restore error.
// - It flags what supply chain attacks and licence changes look like (supplyChainSignals).
// - It caches research per group.
//
// Usage: node scripts/upgrade-deps.mjs inventory [--refresh]
//        node scripts/upgrade-deps.mjs record-blocker <group> --reason <text> --recheck <text> [--blocked-on <package>=<version>]...
//        node scripts/upgrade-deps.mjs lockdiff [<package>@<version>]...
//        node scripts/upgrade-deps.mjs cache-dir
//   inventory       prints JSON: the groups that can move, and audit counts. The registries are read on every run. Only
//                   research is cached. --refresh marks every group for fresh research.
//   record-blocker  marks a group's brief blocked, so the next run does not file it again. A task agent runs this from
//                   its worktree, which cannot write the root checkout's cache with a file tool but can run this.
//                   A NuGet package in --blocked-on is written `nuget:<Id>`.
//   lockdiff        prints what the working tree changed against HEAD in pnpm-lock.yaml, tools/lighthouse/pnpm-lock.yaml,
//                   Directory.Packages.props and dotnet-tools.json, and whether each named package resolved to the
//                   version given everywhere it is declared. Exit 1 when one did not, or when a direct dependency
//                   nobody named moved.
//   cache-dir       prints the folder that holds the research.
//
// The cache is one JSON brief per group at <root checkout>/.upgrade-deps/briefs/<group>.json. It is gitignored and sits
// in the ROOT checkout, so every worktree of a clone reaches the same folder. It has its own folder because `.kangentic/`
// is the board's (a project delete removes it) and `.claude/` is a protected path that background agents cannot write
// to unattended. Losing it costs a research pass and nothing else. The brief's shape is in
// .claude/agents/upgrade-researcher.md.
//
// Exit 0 with a report, including one that lists registry errors. Exit 2 for a usage error.
import { exec, execFile } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { promisify } from 'node:util';
import semver from 'semver';

const execAsync = promisify(exec);
const execFileAsync = promisify(execFile);
const REPOSITORY_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const NPM_REGISTRY = 'https://registry.npmjs.org';
const NUGET_REGISTRATION = 'https://api.nuget.org/v3/registration5-gz-semver2';
const REGISTRY_CONCURRENCY = 8;
const COMMAND_OUTPUT_LIMIT_BYTES = 64 * 1024 * 1024;
const INSTALL_SCRIPTS = ['preinstall', 'install', 'postinstall'];
const SEVERITY_ORDER = ['info', 'low', 'moderate', 'high', 'critical'];
const NUGET_SEVERITY = ['low', 'moderate', 'high', 'critical'];

/** The research cache's folder in the root checkout. Must stay gitignored: briefs are not for the public repo. */
export const CACHE_FOLDER = '.upgrade-deps';

/**
 * The pnpm installs. The workspace is one. tools/lighthouse installs outside it, with its own lockfile, so a worktree's
 * install skips it (CLAUDE.md, repo layout).
 */
const PNPM_ROOTS = [
  { location: '.', lockfile: 'pnpm-lock.yaml', scope: '-r' },
  { location: 'tools/lighthouse', lockfile: 'tools/lighthouse/pnpm-lock.yaml', scope: '--dir tools/lighthouse --ignore-workspace' },
];

/** A `$(Property)` version in Directory.Packages.props is a group: one edit moves every package that names it. */
const PROPERTY_GROUPS = { AspireVersion: 'aspire', DotNetVersion: 'dotnet' };

/**
 * Packages that release together or must match, so they move in one commit. Tooling that merely peers on another
 * package stays apart: a blocked major of one must not hold another's patch, and the conflict check reports it when two
 * do collide.
 *
 * `agree` says what members' targets must share: `version` for a set published under one number, `minor` for a set
 * that tracks another's minors. The Aspire Community Toolkit's 13.6.0 targets Aspire 13.6, and `aspire.cli` and
 * `dotnet-ef` are the local tools of their groups (both `rollForward: false`).
 */
const LOCKSTEP_GROUPS = [
  { group: 'tailwind', ecosystem: 'npm', pattern: /^(tailwindcss|@tailwindcss\/.+)$/, agree: 'version' },
  { group: 'tiptap', ecosystem: 'npm', pattern: /^@tiptap\//, agree: 'version' },
  { group: 'fontsource', ecosystem: 'npm', pattern: /^@fontsource-variable\// },
  { group: 'aspire', ecosystem: 'nuget', pattern: /^(aspire\.cli|CommunityToolkit\.Aspire\..+)$/i, agree: 'minor' },
  { group: 'dotnet', ecosystem: 'nuget', pattern: /^dotnet-ef$/i, agree: 'version' },
  { group: 'wolverine', ecosystem: 'nuget', pattern: /^WolverineFx(\..+)?$/i, agree: 'version' },
  { group: 'opentelemetry', ecosystem: 'nuget', pattern: /^OpenTelemetry\./i },
  { group: 'extensions-ai', ecosystem: 'nuget', pattern: /^Microsoft\.Extensions\.AI(\..+)?$/i, agree: 'version' },
  { group: 'modelcontextprotocol', ecosystem: 'nuget', pattern: /^ModelContextProtocol(\..+)?$/i, agree: 'version' },
  { group: 'xunit', ecosystem: 'nuget', pattern: /^xunit\.v3(\..+)?$/i, agree: 'version' },
];

/** The suffix of a group that holds the move to a new major (see the header). */
export const MAJOR_SUFFIX = '-major';

/**
 * The group a package moves with. A `$(Property)` version is its own group, a `@types/x` package moves with `x`, and
 * everything else is a group of one, named in lower case so two spellings of a NuGet id share a brief.
 */
export function groupOf({ name, ecosystem, property }) {
  if (property) return PROPERTY_GROUPS[property] ?? property.replace(/Version$/, '').toLowerCase();
  for (const { group, ecosystem: groupEcosystem, pattern } of LOCKSTEP_GROUPS) {
    if (groupEcosystem === ecosystem && pattern.test(name)) return group;
  }
  const typesMatch = ecosystem === 'npm' ? /^@types\/(.+)$/.exec(name) : null;
  const baseName = typesMatch ? typesMatch[1] : name;
  return baseName.replace(/^@/, '').replace(/\//g, '-').toLowerCase();
}

/**
 * A version under NuGet's rules: up to four numbers, then a SemVer 2.0 prerelease label compared without case. They
 * accept every npm version too, so one comparison serves both registries. npm ranges still go through `semver`.
 *
 * @returns {{ numbers: number[], prerelease: string[] } | null}
 */
export function parseVersion(version) {
  const match = /^v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/.exec(String(version ?? '').trim());
  if (!match) return null;
  return {
    numbers: [match[1], match[2], match[3], match[4]].map((part) => Number(part ?? 0)),
    prerelease: match[5] ? match[5].split('.') : [],
  };
}

function compareIdentifiers(left, right) {
  const leftNumeric = /^\d+$/.test(left);
  const rightNumeric = /^\d+$/.test(right);
  if (leftNumeric && rightNumeric) return Number(left) - Number(right);
  if (leftNumeric !== rightNumeric) return leftNumeric ? -1 : 1;
  const leftLower = left.toLowerCase();
  const rightLower = right.toLowerCase();
  return leftLower < rightLower ? -1 : leftLower > rightLower ? 1 : 0;
}

/** Negative, zero or positive, as `left` sorts before, with or after `right`. Unparseable versions sort first. */
export function compareVersions(left, right) {
  const a = parseVersion(left);
  const b = parseVersion(right);
  if (!a || !b) return (a ? 1 : 0) - (b ? 1 : 0);
  for (let index = 0; index < 4; index += 1) {
    if (a.numbers[index] !== b.numbers[index]) return Math.sign(a.numbers[index] - b.numbers[index]);
  }
  if (a.prerelease.length === 0 || b.prerelease.length === 0) return Math.sign(b.prerelease.length - a.prerelease.length);
  for (let index = 0; index < Math.min(a.prerelease.length, b.prerelease.length); index += 1) {
    const order = compareIdentifiers(a.prerelease[index], b.prerelease[index]);
    if (order !== 0) return Math.sign(order);
  }
  return Math.sign(a.prerelease.length - b.prerelease.length);
}

const isPrerelease = (version) => (parseVersion(version)?.prerelease.length ?? 0) > 0;
const releaseOf = (version) => parseVersion(version)?.numbers.join('.');
const majorOf = (version) => parseVersion(version)?.numbers[0];
const newestFirst = (left, right) => compareVersions(right, left);

/** `major` also covers a 0.x minor and a 0.0.x patch, which semver treats as breaking. */
export function changeKind(installed, target) {
  const [installedMajor, installedMinor, installedPatch] = parseVersion(installed).numbers;
  const [targetMajor, targetMinor, targetPatch] = parseVersion(target).numbers;
  if (installedMajor !== targetMajor) return 'major';
  if (installedMajor === 0 && installedMinor !== targetMinor) return 'major';
  if (installedMajor === 0 && installedMinor === 0 && installedPatch !== targetPatch) return 'major';
  if (releaseOf(installed) === releaseOf(target)) return 'prerelease';
  return installedMinor !== targetMinor ? 'minor' : 'patch';
}

/**
 * The releases worth moving to: `latest`, the newest, and `line`, the newest that is not a major. Nothing at or below
 * `installed` ever qualifies, nor a deprecated or unlisted release. A stable install never takes a prerelease or
 * crosses its ceiling. On npm the ceiling is the `latest` dist-tag, or a stable dist-tag on the installed major when
 * that is higher, because a project can trail `latest` behind a line it still ships. NuGet has no dist-tags. A
 * prerelease install takes another prerelease only of the same release, so once the stable one ships it moves there and
 * not on to the next line's beta. Renovate's `ignoreUnstable` draws the same line.
 *
 * @param {{ installed: string, distTags?: Record<string, string>, versions: Record<string, { deprecated?: unknown, listed?: boolean }> }} input
 * @returns {{ latest: string | null, line: string | null }}
 */
export function chooseTargets({ installed, distTags = {}, versions }) {
  const installedRelease = isPrerelease(installed) ? releaseOf(installed) : null;
  const ceiling = Object.entries(distTags)
    .filter(([tag, version]) => parseVersion(version) && !isPrerelease(version)
      && (tag === 'latest' || majorOf(version) === majorOf(installed)))
    .map(([, version]) => version)
    .sort(newestFirst)[0];
  const candidates = Object.entries(versions)
    .filter(([version, meta]) => {
      if (!parseVersion(version) || compareVersions(version, installed) <= 0) return false;
      if (meta.deprecated || meta.listed === false) return false;
      if (isPrerelease(version)) return releaseOf(version) === installedRelease;
      return ceiling === undefined || compareVersions(version, ceiling) <= 0;
    })
    .map(([version]) => version)
    .sort(newestFirst);
  return {
    latest: candidates[0] ?? null,
    line: candidates.find((version) => changeKind(installed, version) !== 'major') ?? null,
  };
}

/**
 * What changed between two releases that supply chain attacks change, plus the licence. A compromised axios release
 * differed from the last good one only by a new dependency and a publish with no provenance, and Phantom Gyp ran code
 * through a `binding.gyp` with no install script at all. A NuGet package runs code at build time through MSBuild
 * `.props` and `.targets` files and through analyzers. A licence change is how FluentAssertions 8 and MassTransit 9 left
 * the ADR-0008 policy. Each one is a reason to look, not a verdict.
 *
 * @typedef {{ dependencies?: Record<string, string>, installScripts?: string[], gypfile?: boolean, buildFiles?: string[],
 *   attested?: boolean, publisher?: string, licence?: string | null }} ReleaseFacts
 * @param {ReleaseFacts | undefined} from
 * @param {ReleaseFacts | undefined} to
 * @returns {string[]} one line per signal
 */
export function supplyChainSignals(from, to) {
  if (!from || !to) return [];
  const signals = [];
  const newOnes = (after = [], before = []) => after.filter((item) => !before.includes(item));
  const added = newOnes(Object.keys(to.dependencies ?? {}), Object.keys(from.dependencies ?? {}));
  if (added.length > 0) signals.push(`new dependencies (${added.length}): ${added.slice(0, 5).join(', ')}`);
  const scripts = newOnes(to.installScripts, from.installScripts);
  if (scripts.length > 0) signals.push(`new install scripts: ${scripts.join(', ')}`);
  if (to.gypfile && !from.gypfile) signals.push('a binding.gyp build appeared');
  const buildFiles = newOnes(to.buildFiles, from.buildFiles);
  if (buildFiles.length > 0) signals.push(`new build-time files (${buildFiles.length}): ${buildFiles.slice(0, 5).join(', ')}`);
  if (from.attested && !to.attested) signals.push('provenance dropped');
  if (from.publisher && to.publisher && from.publisher !== to.publisher) {
    signals.push(`publisher changed: ${from.publisher} to ${to.publisher}`);
  }
  if (from.licence !== undefined && to.licence !== undefined && from.licence !== to.licence) {
    signals.push(`licence changed: ${from.licence ?? 'none declared'} to ${to.licence ?? 'none declared'}`);
  }
  return signals;
}

/**
 * Whether a group's targets share what its `agree` asks for, and whether the members that share a `$(Property)` share
 * one version, as they must to be installed by one edit. Only moving members are compared.
 */
export function targetsAgree(group, members) {
  const baseGroup = group.endsWith(MAJOR_SUFFIX) ? group.slice(0, -MAJOR_SUFFIX.length) : group;
  const agree = LOCKSTEP_GROUPS.find((entry) => entry.group === baseGroup)?.agree;
  const keyOf = agree === 'minor'
    ? ({ target }) => parseVersion(target).numbers.slice(0, 2).join('.')
    : ({ target }) => target;
  if (agree && new Set(members.map(keyOf)).size > 1) return false;
  const byProperty = new Map();
  for (const member of members.filter(({ property }) => property)) {
    byProperty.set(member.property, (byProperty.get(member.property) ?? new Set()).add(member.target));
  }
  return [...byProperty.values()].every((targets) => targets.size === 1);
}

/**
 * Whether a NuGet version range allows `version`. `1.2` means at least 1.2, and interval notation (`[1.2, )`,
 * `[1.2, 2.0)`, `(, 3.0]`, `[1.2]`) bounds it as written. Returns null for a range it cannot read.
 */
export function satisfiesNuGetRange(version, range) {
  const text = String(range ?? '').trim();
  if (parseVersion(text)) return compareVersions(version, text) >= 0;
  const match = /^([[(])\s*([^,\])]*?)\s*(?:,\s*([^\])]*?)\s*)?([\])])$/.exec(text);
  if (!match) return null;
  const [, open, lower, upper, close] = match;
  if (upper === undefined) return open === '[' && close === ']' && compareVersions(version, lower) === 0;
  if (lower && compareVersions(version, lower) < (open === '[' ? 0 : 1)) return false;
  if (upper && compareVersions(version, upper) > (close === ']' ? 0 : -1)) return false;
  return true;
}

const npmSatisfies = (version, range) => (
  semver.validRange(range) ? semver.satisfies(version, range, { includePrerelease: true }) : null
);

/**
 * The requirements a plan breaks: a package whose range for another planned package excludes that package's planned
 * version. Each conflict's `requirement` leaves out the planned version, so one the tree has stays one when the
 * dependency moves.
 */
function conflictsIn(plan, requirementsOf, satisfies, keyOf) {
  const conflicts = [];
  for (const [key, { name, version }] of Object.entries(plan)) {
    for (const [dependency, range] of Object.entries(requirementsOf(key, version))) {
      const planned = plan[keyOf(dependency)];
      if (planned === undefined) continue;
      if (satisfies(planned.version, range) === false) {
        conflicts.push({ requirement: `${name}@${version} needs ${dependency} ${range}`, planned: planned.version });
      }
    }
  }
  return conflicts;
}

/**
 * The conflicts moving ONE group would add. Each group is checked alone against what is installed, so a blocked major
 * reports against its own group and not against a neighbour's patch, and a conflict the tree already has is not news.
 * npm requirements are peer ranges. NuGet requirements are dependency ranges, which CentralPackageTransitivePinningEnabled
 * turns into a restore error when a central pin sits below a dependency's floor.
 *
 * @param {Record<string, { name: string, version: string }>} installed key to installed version. npm keys are names,
 *   NuGet keys are `nuget:<lower-case id>`.
 * @param {Record<string, { name: string, version: string }>} groupTargets the same, for the group's targets
 * @param {(key: string, version: string) => Record<string, string>} requirementsOf
 * @returns {string[]}
 */
export function newConflicts(installed, groupTargets, requirementsOf) {
  const keyOf = (ecosystem) => (ecosystem === 'nuget' ? (name) => `nuget:${name.toLowerCase()}` : (name) => name);
  const check = (plan) => ['npm', 'nuget'].flatMap((ecosystem) => {
    const inEcosystem = Object.fromEntries(Object.entries(plan).filter(([key]) => key.startsWith('nuget:') === (ecosystem === 'nuget')));
    return conflictsIn(inEcosystem, requirementsOf, ecosystem === 'nuget' ? satisfiesNuGetRange : npmSatisfies, keyOf(ecosystem));
  });
  const baseline = new Set(check(installed).map(({ requirement }) => requirement));
  return check({ ...installed, ...groupTargets })
    .filter(({ requirement }) => !baseline.has(requirement))
    .map(({ requirement, planned }) => `${requirement}, planned ${planned}`);
}

/** The NuGet dependency group restore would pick for `framework`: the same one, the nearest older .NET, then .NET Standard. */
export function nearestDependencyGroup(groups, framework) {
  const netVersion = (moniker) => {
    const match = /^net(?:coreapp)?(\d+)\.(\d+)$/.exec(moniker);
    return match ? Number(match[1]) + Number(match[2]) / 10 : null;
  };
  const wanted = netVersion(framework);
  const entries = (groups ?? []).map((group) => ({
    group,
    moniker: String(group.targetFramework ?? '').replace(/^\./, '').toLowerCase(),
  }));
  const net = entries
    .filter(({ moniker }) => netVersion(moniker) !== null && netVersion(moniker) <= wanted)
    .sort((left, right) => netVersion(right.moniker) - netVersion(left.moniker));
  return net[0]?.group
    ?? entries.find(({ moniker }) => moniker === 'netstandard2.1')?.group
    ?? entries.find(({ moniker }) => moniker === 'netstandard2.0')?.group
    ?? entries.find(({ moniker }) => moniker === '')?.group
    ?? null;
}

/** A NuGet package's files that run during a build, with the target-framework folder left out so a retarget is not news. */
export function buildFilesOf(packageEntries) {
  const files = new Set();
  for (const { fullName } of packageEntries ?? []) {
    const parts = String(fullName).replace(/\\/g, '/').split('/');
    const top = parts[0].toLowerCase();
    const file = parts.at(-1);
    if (['build', 'buildtransitive', 'buildmultitargeting'].includes(top) && /\.(props|targets)$/i.test(file)) files.add(`${parts[0]}/${file}`);
    else if (top === 'analyzers' && /\.dll$/i.test(file)) files.add(`analyzers/${file}`);
    else if (top === 'tools' && /\.ps1$/i.test(file)) files.add(`tools/${file}`);
  }
  return [...files].sort();
}

function sameVersions(left, right) {
  const leftNames = Object.keys(left);
  return leftNames.length === Object.keys(right).length && leftNames.every((name) => left[name] === right[name]);
}

function isBrief(candidate) {
  return typeof candidate === 'object' && candidate !== null
    && candidate.draft !== true
    && typeof candidate.targets === 'object' && candidate.targets !== null
    && typeof candidate.evaluatedAt === 'string' && !Number.isNaN(Date.parse(candidate.evaluatedAt))
    && (candidate.verdict === 'ready' || candidate.verdict === 'blocked');
}

/**
 * What earlier research is worth for a group, given what the registries say today. Research for a version never
 * expires: the release notes for a published version do not change, so only a version moving, or `--refresh`, sends a
 * group back to an agent. A second run costs one inventory.
 *
 *   research  no usable brief: full research.
 *   delta     a brief exists for other versions: read it and cover only the new releases.
 *   recheck   blocked, and what it was waiting on has moved: test the blocker alone.
 *   reuse     same versions: no research.
 *   blocked   same versions, same blocker: no research, reported as blocked.
 *   filed     a board task already carries this brief: confirm it is open.
 */
export function researchState({ brief, targets, watchedVersions, refresh }) {
  if (!isBrief(brief) || refresh) return 'research';
  if (!sameVersions(brief.targets, targets)) return 'delta';
  if (brief.task) return 'filed';
  if (brief.verdict !== 'blocked') return 'reuse';
  const watched = Object.entries(brief.blocker?.blockedOn ?? {});
  return watched.every(([name, version]) => watchedVersions[name] === version) ? 'blocked' : 'recheck';
}

/** What a blocker compares against: the newest release on the line it recorded, or the newest release at all. */
export function watchedVersion(recorded, { distTags = {}, versions }) {
  if (!parseVersion(recorded)) {
    return distTags.latest ?? Object.keys(versions).filter((version) => !isPrerelease(version)).sort(newestFirst)[0];
  }
  return chooseTargets({ installed: recorded, distTags, versions }).latest ?? recorded;
}

/** A brief marked blocked by a task agent that could not land the group. */
export function withBlocker(brief, { reason, recheck, blockedOn }, now) {
  return {
    ...brief,
    verdict: 'blocked',
    task: null,
    evaluatedAt: new Date(now).toISOString(),
    blocker: { reason, recheck, blockedOn },
  };
}

const KIND_ORDER = ['prerelease', 'patch', 'minor', 'major', 'deprecated'];

function widestKind(members) {
  return members.reduce((widest, { kind }) => (KIND_ORDER.indexOf(kind) > KIND_ORDER.indexOf(widest) ? kind : widest), 'prerelease');
}

/** The key a record is found under in `registry`: npm names as they are, NuGet ids as `nuget:<lower-case id>`. */
export function registryKey({ ecosystem, name }) {
  return ecosystem === 'nuget' ? `nuget:${name.toLowerCase()}` : name;
}

/**
 * The report.
 *
 * `declared` holds one record per package: `{ name, ecosystem, installed, locations, pinned?, property?, tool?,
 * transitivePin?, current? }`. `current` means `pnpm outdated` did not list it, so its registry document was not read.
 * `registry` maps a registryKey to `{ distTags, versions, times }`, where each version holds `deprecated`, `listed`,
 * `requirements` (npm peer ranges or the NuGet dependency group for our framework) and the ReleaseFacts the signals
 * compare. It may also hold packages nothing declares, read because a brief's blocker waits on them. `installedRequirements`
 * maps a registryKey to the requirements of the installed version, for packages whose document was not read (npm peers
 * come from the lockfile). `advisories` maps a name to the severity of an advisory against that package itself, and
 * `inheritedAdvisories` is the same for one that reaches a direct dependency from below it, which moving that package
 * may or may not clear.
 *
 * A group moves whole, and splits in two when a member's newest release is a major (see the header). Publishing a set
 * takes minutes, so a run can catch members on different numbers: `targetsDisagree` flags that, and research picks a
 * set that matches. A package whose installed or newest release is deprecated with nothing to move to is reported as a
 * group of kind `deprecated`, targeted at what is installed so its brief still caches.
 */
export function buildInventory({
  declared, registry, installedRequirements = {}, advisories = {}, inheritedAdvisories = {}, briefs, now, refresh,
}) {
  const installed = Object.fromEntries(declared.map((record) => [registryKey(record), { name: record.name, version: record.installed }]));
  const requirementsOf = (key, version) => registry[key]?.versions[version]?.requirements
    ?? (installed[key]?.version === version ? installedRequirements[key] : undefined)
    ?? {};
  const watchedVersions = (brief) => Object.fromEntries(
    Object.entries(brief?.blocker?.blockedOn ?? {}).flatMap(([name, recorded]) => {
      const entry = registry[name.startsWith('nuget:') ? name.toLowerCase() : name];
      return entry ? [[name, watchedVersion(recorded, entry)]] : [];
    }),
  );
  const movable = new Map();
  const add = (group, member) => movable.set(group, [...(movable.get(group) ?? []), member]);
  let currentCount = 0;

  for (const record of declared) {
    const entry = record.current ? undefined : registry[registryKey(record)];
    if (!entry) {
      currentCount += 1;
      continue;
    }
    const { latest, line } = chooseTargets({ installed: record.installed, distTags: entry.distTags, versions: entry.versions });
    const deprecated = entry.versions[record.installed]?.deprecated ?? entry.versions[latest ?? entry.distTags?.latest]?.deprecated ?? null;
    if (latest === null && !deprecated) {
      currentCount += 1;
      continue;
    }
    const memberFor = (target, kind) => ({
      ...Object.fromEntries(Object.entries(record).filter(([key]) => key !== 'current')),
      target,
      kind,
      published: entry.times?.[target] ?? null,
      advisory: advisories[record.name] ?? null,
      inheritedAdvisory: inheritedAdvisories[record.name] ?? null,
      deprecated: deprecated ? String(deprecated).slice(0, 200) : null,
      signals: supplyChainSignals(entry.versions[record.installed], entry.versions[target]),
    });
    const group = groupOf(record);
    if (latest === null) {
      add(group, memberFor(record.installed, 'deprecated'));
    } else if (changeKind(record.installed, latest) === 'major') {
      add(`${group}${MAJOR_SUFFIX}`, memberFor(latest, 'major'));
      if (line !== null) add(group, memberFor(line, changeKind(record.installed, line)));
    } else {
      add(group, memberFor(latest, changeKind(record.installed, latest)));
    }
  }

  // A member that moves within its major also belongs to its group's `-major` set, so that set shows every member.
  for (const [group, members] of movable) {
    if (!group.endsWith(MAJOR_SUFFIX) || !movable.has(group.slice(0, -MAJOR_SUFFIX.length))) continue;
    const named = new Set(members.map(({ name }) => name));
    const inLine = movable.get(group.slice(0, -MAJOR_SUFFIX.length)).filter(({ name, kind }) => !named.has(name) && kind !== 'deprecated');
    movable.set(group, [...members, ...inLine]);
  }

  const groups = [...movable.entries()].map(([group, members]) => {
    const targets = Object.fromEntries(members.map(({ name, target }) => [name, target]));
    const planned = Object.fromEntries(members.map((member) => [registryKey(member), { name: member.name, version: member.target }]));
    const brief = briefs[group];
    return {
      group,
      kind: widestKind(members),
      pinned: members.some(({ pinned }) => pinned),
      advisory: members.some(({ advisory }) => advisory !== null),
      signals: members.some(({ signals }) => signals.length > 0),
      targetsDisagree: !targetsAgree(group, members),
      research: researchState({ brief, targets, watchedVersions: watchedVersions(brief), refresh }),
      hasBrief: brief !== undefined,
      conflicts: newConflicts(installed, planned, requirementsOf),
      blocker: brief?.blocker ?? null,
      task: brief?.task ?? null,
      members,
    };
  });

  return {
    generatedAt: new Date(now).toISOString(),
    groups: groups.sort((left, right) => left.group.localeCompare(right.group)),
    currentCount,
  };
}

/**
 * The parts of a pnpm lockfile (version 9) this script needs: each importer's direct dependencies and each package's
 * peer ranges. pnpm has no command that prints its lockfile as data, and the file is machine-written with a fixed
 * layout, so this reads it line by line.
 *
 * @returns {{ importers: Record<string, Record<string, { specifier: string, version: string, block: string }>>,
 *   packages: Record<string, { peerDependencies: Record<string, string> }> }}
 */
export function readPnpmLockfile(text) {
  const importers = {};
  const packages = {};
  const unquote = (value) => value.trim().replace(/^'(.*)'$/, '$1').replace(/^"(.*)"$/, '$1');
  let section = null;
  let importer = null;
  let block = null;
  let dependency = null;
  let packageKey = null;
  let inPeers = false;
  for (const line of text.split(/\r?\n/)) {
    const topLevel = /^(\S[^:]*):/.exec(line);
    if (topLevel) {
      section = topLevel[1];
      continue;
    }
    if (section === 'importers') {
      let match;
      if ((match = /^ {2}(\S.*?):( \{\})?$/.exec(line))) {
        importer = unquote(match[1]);
        importers[importer] = {};
      } else if ((match = /^ {4}(dependencies|devDependencies|optionalDependencies):$/.exec(line))) {
        block = match[1];
      } else if ((match = /^ {4}\S/.exec(line))) {
        block = null;
      } else if (block && (match = /^ {6}(\S.*):$/.exec(line))) {
        dependency = unquote(match[1]);
        importers[importer][dependency] = { specifier: '', version: '', block };
      } else if (block && dependency && (match = /^ {8}(specifier|version): (.+)$/.exec(line))) {
        const value = unquote(match[2]);
        importers[importer][dependency][match[1]] = match[1] === 'version' ? value.replace(/\(.*$/, '') : value;
      }
    } else if (section === 'packages') {
      let match;
      if ((match = /^ {2}(\S.*):$/.exec(line))) {
        packageKey = unquote(match[1]);
        packages[packageKey] = { peerDependencies: {} };
        inPeers = false;
      } else if ((match = /^ {4}(\S[^:]*):/.exec(line))) {
        inPeers = match[1] === 'peerDependencies';
      } else if (inPeers && packageKey && (match = /^ {6}(\S.*?): (.+)$/.exec(line))) {
        packages[packageKey].peerDependencies[unquote(match[1])] = unquote(match[2]);
      }
    }
  }
  return { importers, packages };
}

/** Splits `name@version` at its last `@`, so a scoped name keeps its own. */
export function splitSpec(spec) {
  const separator = spec.lastIndexOf('@');
  return separator > 0 ? [spec.slice(0, separator), spec.slice(separator + 1)] : [spec, ''];
}

/**
 * Directory.Packages.props as data: its properties, and each PackageVersion with its effective version, the property
 * it reads (if any) and its ItemGroup label. Comments are dropped first, because they name packages too.
 */
export function readPackagesProps(text) {
  const stripped = text.replace(/<!--[\s\S]*?-->/g, '');
  const properties = {};
  for (const [, group] of stripped.matchAll(/<PropertyGroup[^>]*>([\s\S]*?)<\/PropertyGroup>/g)) {
    for (const [, name, value] of group.matchAll(/<(\w+)>([^<]*)<\/\1>/g)) properties[name] = value.trim();
  }
  const packages = [];
  for (const [, attributes, body] of stripped.matchAll(/<ItemGroup([^>]*)>([\s\S]*?)<\/ItemGroup>/g)) {
    const label = /\bLabel="([^"]*)"/.exec(attributes)?.[1] ?? '';
    for (const [, element] of body.matchAll(/<PackageVersion\b([^>]*?)\/?>/g)) {
      const name = /\bInclude="([^"]+)"/.exec(element)?.[1];
      const spec = /\bVersion="([^"]+)"/.exec(element)?.[1];
      if (!name || !spec) continue;
      const property = /^\$\((\w+)\)$/.exec(spec)?.[1] ?? null;
      packages.push({ name, spec, version: property ? properties[property] : spec, property, label });
    }
  }
  return { properties, packages };
}

/** dotnet-tools.json as `{ name, version }` per tool. */
export function readToolsManifest(text) {
  const manifest = JSON.parse(text);
  return Object.entries(manifest.tools ?? {}).map(([name, { version }]) => ({ name, version }));
}

/**
 * Every registry dependency the pnpm lockfiles declare, one record per package name. A workspace link or a non-registry
 * spec has nothing to upgrade. A name declared in two places reports the lowest installed version, so the older copy
 * is what the move is measured from.
 *
 * @param {Array<{ location: string, lockfile: ReturnType<typeof readPnpmLockfile> }>} roots
 */
export function collectNpmDeclared(roots) {
  const declared = new Map();
  for (const { location, lockfile } of roots) {
    for (const [importer, dependencies] of Object.entries(lockfile.importers)) {
      const where = path.posix.join(location, importer);
      for (const [name, { specifier, version }] of Object.entries(dependencies)) {
        if (!parseVersion(version) || !semver.validRange(specifier)) continue;
        const record = declared.get(name) ?? { name, ecosystem: 'npm', installed: version, pinned: false, locations: [] };
        if (compareVersions(version, record.installed) < 0) record.installed = version;
        record.pinned = record.pinned || semver.valid(specifier) !== null;
        record.locations.push(where);
        declared.set(name, record);
      }
    }
  }
  return [...declared.values()];
}

/** Every NuGet package and local tool the repo pins, from Directory.Packages.props and dotnet-tools.json. */
export function collectNuGetDeclared(props, tools) {
  return [
    ...props.packages.filter(({ version }) => parseVersion(version)).map(({ name, version, property, label }) => ({
      name,
      ecosystem: 'nuget',
      installed: version,
      locations: ['Directory.Packages.props'],
      ...(property ? { property } : {}),
      ...(label === 'Transitive pins' ? { transitivePin: true } : {}),
    })),
    ...tools.filter(({ version }) => parseVersion(version)).map(({ name, version }) => ({
      name, ecosystem: 'nuget', installed: version, locations: ['dotnet-tools.json'], tool: true,
    })),
  ];
}

/**
 * Splits `pnpm audit --json` for the report. Each finding's path starts at an importer, then the direct dependency.
 * A path of two steps is an advisory against that package itself. A longer one reached it from below, which moving the
 * direct dependency may or may not clear.
 *
 * @param {{ advisories?: Record<string, { severity: string, findings?: Array<{ paths?: string[] }> }> }} report
 */
export function splitPnpmAudit(report) {
  const advisories = {};
  const inheritedAdvisories = {};
  let transitiveCount = 0;
  const raise = (map, name, severity) => {
    if (SEVERITY_ORDER.indexOf(severity) > SEVERITY_ORDER.indexOf(map[name] ?? 'info')) map[name] = severity;
  };
  for (const { severity, findings = [] } of Object.values(report.advisories ?? {})) {
    let direct = false;
    for (const finding of findings) {
      for (const findingPath of finding.paths ?? []) {
        const steps = findingPath.split('>').map((step) => step.trim());
        if (steps.length < 2) continue;
        const [name] = splitSpec(steps[1]);
        if (steps.length === 2) {
          raise(advisories, name, severity);
          direct = true;
        } else {
          raise(inheritedAdvisories, name, severity);
        }
      }
    }
    if (!direct) transitiveCount += 1;
  }
  return { advisories, inheritedAdvisories, transitiveCount };
}

/** pnpm outdated and pnpm audit exit 1 when they find anything, so their JSON is read from either outcome. */
async function pnpmJson(args) {
  let output;
  try {
    ({ stdout: output } = await execAsync(`pnpm ${args}`, { cwd: REPOSITORY_ROOT, maxBuffer: COMMAND_OUTPUT_LIMIT_BYTES }));
  } catch (error) {
    output = error.stdout;
  }
  const text = String(output ?? '').trim();
  return text === '' ? {} : JSON.parse(text);
}

async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json();
}

function npmLicence(manifest) {
  const licence = manifest.license ?? manifest.licenses;
  if (typeof licence === 'string') return licence;
  if (Array.isArray(licence)) return licence.map((entry) => entry?.type ?? entry).join(' OR ');
  return licence?.type ?? null;
}

async function readPackument(name) {
  const document = await fetchJson(`${NPM_REGISTRY}/${name.replace('/', '%2F')}`);
  const versions = {};
  for (const [version, manifest] of Object.entries(document.versions ?? {})) {
    versions[version] = {
      deprecated: manifest.deprecated,
      requirements: manifest.peerDependencies ?? {},
      dependencies: manifest.dependencies ?? {},
      installScripts: INSTALL_SCRIPTS.filter((script) => manifest.scripts?.[script]),
      gypfile: manifest.gypfile === true,
      attested: Boolean(manifest.dist?.attestations),
      publisher: manifest._npmUser?.name,
      licence: npmLicence(manifest),
    };
  }
  return { distTags: document['dist-tags'] ?? {}, versions, times: document.time ?? {} };
}

/**
 * One NuGet package's registration from `from` upwards: pages that end below it are skipped. Deprecation, vulnerabilities,
 * the licence expression and the dependency group for our framework come with each version.
 */
async function readRegistration(id, from, framework) {
  const index = await fetchJson(`${NUGET_REGISTRATION}/${id.toLowerCase()}/index.json`);
  const versions = {};
  const times = {};
  for (const page of index.items ?? []) {
    if (parseVersion(from) && compareVersions(page.upper, from) < 0) continue;
    const items = page.items ?? (await fetchJson(page['@id'])).items ?? [];
    for (const { catalogEntry: entry } of items) {
      const group = nearestDependencyGroup(entry.dependencyGroups, framework);
      const dependencies = Object.fromEntries((group?.dependencies ?? []).map(({ id: dependency, range }) => [dependency, range ?? '']));
      const deprecation = entry.deprecation;
      versions[entry.version] = {
        deprecated: deprecation ? (deprecation.message || (deprecation.reasons ?? []).join(', ') || 'deprecated') : undefined,
        listed: entry.listed !== false,
        requirements: dependencies,
        dependencies,
        licence: entry.licenseExpression || null,
        vulnerabilities: (entry.vulnerabilities ?? []).map(({ severity }) => NUGET_SEVERITY[Number(severity)] ?? 'low'),
        catalogUrl: entry['@id'],
      };
      times[entry.version] = entry.published;
    }
  }
  return { distTags: {}, versions, times };
}

/** Adds the build-time files of the versions a move compares, from each version's catalog leaf. */
async function addBuildFiles(entry, wanted) {
  await Promise.all([...new Set(wanted)].filter((version) => entry.versions[version]?.catalogUrl).map(async (version) => {
    const leaf = await fetchJson(entry.versions[version].catalogUrl);
    entry.versions[version].buildFiles = buildFilesOf(leaf.packageEntries);
  }));
}

async function inParallel(items, work) {
  const queue = [...items];
  const errors = [];
  async function worker() {
    for (let item = queue.shift(); item !== undefined; item = queue.shift()) {
      try {
        await work(item);
      } catch (error) {
        errors.push(`${item.label ?? item}: ${error instanceof Error ? error.message : String(error)}`);
      }
    }
  }
  await Promise.all(Array.from({ length: REGISTRY_CONCURRENCY }, worker));
  return errors;
}

async function cacheDirectory() {
  const { stdout } = await execFileAsync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: REPOSITORY_ROOT });
  return path.join(path.dirname(stdout.trim()), CACHE_FOLDER);
}

function readText(relativePath) {
  return fs.readFileSync(path.join(REPOSITORY_ROOT, relativePath), 'utf8');
}

function readBriefs(briefsDirectory) {
  const briefs = {};
  if (!fs.existsSync(briefsDirectory)) return briefs;
  for (const fileName of fs.readdirSync(briefsDirectory)) {
    if (!fileName.endsWith('.json')) continue;
    try {
      briefs[fileName.slice(0, -'.json'.length)] = JSON.parse(fs.readFileSync(path.join(briefsDirectory, fileName), 'utf8'));
    } catch {
      // An unreadable brief is the same as no brief: the group gets researched again.
    }
  }
  return briefs;
}

function targetFramework() {
  return /<TargetFramework>([^<]+)<\/TargetFramework>/.exec(readText('Directory.Build.props'))?.[1].trim() ?? 'net10.0';
}

async function runInventory(args) {
  const errors = [];
  const framework = targetFramework();
  const lockfiles = PNPM_ROOTS.map((root) => ({ ...root, lockfile: readPnpmLockfile(readText(root.lockfile)) }));
  const npmDeclared = collectNpmDeclared(lockfiles);
  const nugetDeclared = collectNuGetDeclared(readPackagesProps(readText('Directory.Packages.props')), readToolsManifest(readText('dotnet-tools.json')));
  const cacheDir = await cacheDirectory();
  const briefs = readBriefs(path.join(cacheDir, 'briefs'));

  // pnpm outdated is the filter: a package it does not list is current, and its registry document is never read.
  const outdated = new Set();
  const audits = [];
  await Promise.all(PNPM_ROOTS.map(async ({ location, scope }) => {
    try {
      for (const [name, { latest, wanted, isDeprecated }] of Object.entries(await pnpmJson(`outdated ${scope} --format json`))) {
        if (isDeprecated || (latest && wanted && compareVersions(latest, wanted) > 0)) outdated.add(name);
      }
    } catch (error) {
      errors.push(`pnpm outdated in ${location}: ${error instanceof Error ? error.message : String(error)}`);
    }
    try {
      audits.push(splitPnpmAudit(await pnpmJson(`audit ${scope.replace('-r', '').trim()} --json`)));
    } catch (error) {
      errors.push(`pnpm audit in ${location}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }));

  const installedRequirements = {};
  for (const { lockfile } of lockfiles) {
    for (const [key, { peerDependencies }] of Object.entries(lockfile.packages)) {
      const [name, version] = splitSpec(key);
      const record = npmDeclared.find((candidate) => candidate.name === name && candidate.installed === version);
      if (record) installedRequirements[name] = peerDependencies;
    }
  }
  for (const record of npmDeclared) record.current = !outdated.has(record.name);

  const registry = {};
  const watched = Object.values(briefs).flatMap((brief) => Object.entries(brief?.blocker?.blockedOn ?? {}));
  const npmNames = new Set([...npmDeclared.filter(({ current }) => !current).map(({ name }) => name),
    ...watched.map(([name]) => name).filter((name) => !name.startsWith('nuget:'))]);
  const nugetReads = new Map();
  for (const { name, installed } of nugetDeclared) nugetReads.set(name.toLowerCase(), { id: name, from: installed });
  for (const [name, recorded] of watched.filter(([key]) => key.startsWith('nuget:'))) {
    const id = name.slice('nuget:'.length);
    if (!nugetReads.has(id.toLowerCase())) nugetReads.set(id.toLowerCase(), { id, from: recorded });
  }
  errors.push(...await inParallel([
    ...[...npmNames].map((name) => ({ label: name, read: async () => { registry[name] = await readPackument(name); } })),
    ...[...nugetReads.values()].map(({ id, from }) => ({
      label: id,
      read: async () => { registry[`nuget:${id.toLowerCase()}`] = await readRegistration(id, from, framework); },
    })),
  ], (item) => item.read()));

  // The build-time files need one catalog read per version compared, so only moving NuGet packages pay for them.
  errors.push(...await inParallel(nugetDeclared.filter((record) => registry[registryKey(record)]).map((record) => ({
    label: record.name,
    read: async () => {
      const entry = registry[registryKey(record)];
      const { latest, line } = chooseTargets({ installed: record.installed, versions: entry.versions });
      if (latest !== null) await addBuildFiles(entry, [record.installed, latest, line].filter(Boolean));
    },
  })), (item) => item.read()));

  const advisories = {};
  const inheritedAdvisories = {};
  let transitiveCount = 0;
  for (const audit of audits) {
    Object.assign(advisories, audit.advisories);
    Object.assign(inheritedAdvisories, audit.inheritedAdvisories);
    transitiveCount += audit.transitiveCount;
  }
  for (const record of nugetDeclared) {
    const severities = registry[registryKey(record)]?.versions[record.installed]?.vulnerabilities ?? [];
    if (severities.length > 0) advisories[record.name] = severities.sort((left, right) => NUGET_SEVERITY.indexOf(right) - NUGET_SEVERITY.indexOf(left))[0];
  }

  const inventory = buildInventory({
    declared: [...npmDeclared, ...nugetDeclared],
    registry,
    installedRequirements,
    advisories,
    inheritedAdvisories,
    briefs,
    now: Date.now(),
    refresh: args.includes('--refresh'),
  });
  // Absent means no: a null, false or empty field is dropped, so the report stays small in a transcript.
  const dropEmpty = (key, value) => (
    value === null || value === false || (Array.isArray(value) && value.length === 0) ? undefined : value
  );
  console.log(JSON.stringify({ cacheDir, ...inventory, transitiveAdvisories: transitiveCount, errors }, dropEmpty));
}

function readTextOption(args, flag) {
  const index = args.indexOf(flag);
  return index === -1 ? undefined : args[index + 1];
}

async function runRecordBlocker([group, ...args]) {
  const reason = readTextOption(args, '--reason');
  const recheck = readTextOption(args, '--recheck');
  if (!group || !reason || !recheck) {
    console.error('usage: node scripts/upgrade-deps.mjs record-blocker <group> --reason <text> --recheck <text> [--blocked-on <package>=<version>]...');
    process.exit(2);
  }
  const blockedOn = {};
  args.forEach((argument, index) => {
    if (argument !== '--blocked-on') return;
    const pair = args[index + 1] ?? '';
    const separator = pair.lastIndexOf('=');
    if (separator > 0) blockedOn[pair.slice(0, separator)] = pair.slice(separator + 1);
  });
  const briefPath = path.join(await cacheDirectory(), 'briefs', `${group}.json`);
  if (!fs.existsSync(briefPath)) {
    console.error(`No brief for ${group} at ${briefPath}. Put the blocker in your report instead.`);
    process.exit(2);
  }
  const blocked = withBlocker(JSON.parse(fs.readFileSync(briefPath, 'utf8')), { reason, recheck, blockedOn }, Date.now());
  fs.writeFileSync(briefPath, `${JSON.stringify(blocked, null, 2)}\n`);
  console.log(`Recorded ${group} as blocked.`);
}

/**
 * What moved between two states of the repo's dependency files. `before` and `after` each hold `pnpm` (one parsed
 * lockfile per location) and `nuget` (the effective NuGet versions, lower-case id to `{ name, version }`).
 */
export function diffDependencies(before, after) {
  const packageSets = (state) => {
    const sets = new Map();
    for (const lockfile of Object.values(state.pnpm)) {
      for (const key of Object.keys(lockfile.packages)) {
        const [name, version] = splitSpec(key);
        sets.set(name, (sets.get(name) ?? new Set()).add(version));
      }
    }
    return sets;
  };
  const beforeSets = packageSets(before);
  const afterSets = packageSets(after);
  const changed = [];
  const added = [];
  const removed = [];
  for (const name of new Set([...beforeSets.keys(), ...afterSets.keys()])) {
    const was = [...(beforeSets.get(name) ?? [])].sort(compareVersions);
    const is = [...(afterSets.get(name) ?? [])].sort(compareVersions);
    if (was.join() === is.join()) continue;
    if (was.length === 0) added.push(`${name}@${is.join(', ')}`);
    else if (is.length === 0) removed.push(`${name}@${was.join(', ')}`);
    else changed.push(`${name} ${was.join(', ')} -> ${is.join(', ')}`);
  }

  const directMoves = [];
  for (const [location, lockfile] of Object.entries(after.pnpm)) {
    for (const [importer, dependencies] of Object.entries(lockfile.importers)) {
      for (const [name, { version }] of Object.entries(dependencies)) {
        const previous = before.pnpm[location]?.importers[importer]?.[name]?.version;
        if (previous !== version) {
          directMoves.push({ ecosystem: 'npm', where: path.posix.join(location, importer), name, from: previous ?? null, to: version });
        }
      }
    }
  }
  for (const [id, { name, version, where }] of Object.entries(after.nuget)) {
    const previous = before.nuget[id]?.version;
    if (previous !== version) directMoves.push({ ecosystem: 'nuget', where, name, from: previous ?? null, to: version });
  }
  for (const [id, { name, version, where }] of Object.entries(before.nuget)) {
    if (!after.nuget[id]) directMoves.push({ ecosystem: 'nuget', where, name, from: version, to: null });
  }
  return { changed, added, removed, directMoves };
}

/**
 * Whether each named package resolved to its version everywhere it is declared: every pnpm importer that lists it, or
 * its NuGet pin. A name found nowhere fails too.
 */
export function checkExpected(expected, state) {
  return expected.map((spec) => {
    const [name, wanted] = splitSpec(spec);
    const resolved = [];
    for (const [location, lockfile] of Object.entries(state.pnpm)) {
      for (const [importer, dependencies] of Object.entries(lockfile.importers)) {
        if (dependencies[name]) resolved.push({ where: path.posix.join(location, importer), version: dependencies[name].version });
      }
    }
    const nuget = state.nuget[name.toLowerCase()];
    if (nuget) resolved.push({ where: nuget.where, version: nuget.version });
    return { name, wanted, resolved, ok: resolved.length > 0 && resolved.every(({ version }) => version === wanted) };
  });
}

function nugetState(propsText, toolsText) {
  const state = {};
  for (const { name, version } of readPackagesProps(propsText).packages) state[name.toLowerCase()] = { name, version, where: 'Directory.Packages.props' };
  for (const { name, version } of readToolsManifest(toolsText)) state[name.toLowerCase()] = { name, version, where: 'dotnet-tools.json' };
  return state;
}

async function headText(relativePath) {
  try {
    const { stdout } = await execFileAsync('git', ['show', `HEAD:${relativePath}`], { cwd: REPOSITORY_ROOT, maxBuffer: COMMAND_OUTPUT_LIMIT_BYTES });
    return stdout;
  } catch {
    return null;
  }
}

async function runLockdiff(expected) {
  const before = { pnpm: {}, nuget: {} };
  const after = { pnpm: {}, nuget: {} };
  for (const { location, lockfile } of PNPM_ROOTS) {
    const was = await headText(lockfile);
    if (was !== null) before.pnpm[location] = readPnpmLockfile(was);
    after.pnpm[location] = readPnpmLockfile(readText(lockfile));
  }
  const [propsBefore, toolsBefore] = await Promise.all([headText('Directory.Packages.props'), headText('dotnet-tools.json')]);
  if (propsBefore !== null && toolsBefore !== null) before.nuget = nugetState(propsBefore, toolsBefore);
  after.nuget = nugetState(readText('Directory.Packages.props'), readText('dotnet-tools.json'));

  const diff = diffDependencies(before, after);
  const print = (label, lines) => {
    console.log(`${label} (${lines.length})`);
    for (const line of lines) console.log(`  ${line}`);
  };
  print('npm packages changed', diff.changed);
  print('npm packages added', diff.added);
  print('npm packages removed', diff.removed);
  print('direct moves', diff.directMoves.map(({ ecosystem, where, name, from, to }) => `[${ecosystem} ${where}] ${name} ${from ?? 'none'} -> ${to ?? 'none'}`));

  const checks = checkExpected(expected, after);
  for (const { name, wanted, resolved, ok } of checks) {
    const where = resolved.map(({ where: place, version }) => `${place} ${version}`).join(', ') || 'not declared anywhere';
    console.log(`${ok ? 'OK ' : 'BAD'} ${name}: wanted ${wanted}, resolved ${where}`);
  }
  const named = new Set(checks.map(({ name }) => name.toLowerCase()));
  const unplanned = diff.directMoves.filter(({ name }) => !named.has(name.toLowerCase()));
  for (const { ecosystem, where, name, from, to } of unplanned) {
    console.log(`UNPLANNED [${ecosystem} ${where}] ${name} ${from ?? 'none'} -> ${to ?? 'none'}`);
  }
  if (checks.some(({ ok }) => !ok) || unplanned.length > 0) process.exitCode = 1;
}

async function main() {
  const [command, ...args] = process.argv.slice(2);
  if (command === 'inventory') {
    await runInventory(args);
  } else if (command === 'record-blocker') {
    await runRecordBlocker(args);
  } else if (command === 'lockdiff') {
    await runLockdiff(args);
  } else if (command === 'cache-dir') {
    console.log(await cacheDirectory());
  } else {
    console.error('usage: node scripts/upgrade-deps.mjs inventory|record-blocker|lockdiff|cache-dir');
    process.exit(2);
  }
}

/** Whether node was started on this file, rather than importing it (the tests import it). */
function isEntrypoint() {
  const entry = process.argv[1];
  if (!entry) return false;
  try {
    return fs.realpathSync(fileURLToPath(import.meta.url)) === fs.realpathSync(path.resolve(entry));
  } catch {
    return import.meta.url === pathToFileURL(path.resolve(entry)).href;
  }
}

if (isEntrypoint()) {
  await main();
}
