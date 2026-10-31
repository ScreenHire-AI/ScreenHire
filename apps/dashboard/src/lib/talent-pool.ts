import type { ApplicationSummary, KeptInMind } from '@screenhire/api-client';
import { isTerminal } from '#lib/workflow.js';

/**
 * The talent pool in the dashboard (docs/architecture/talent-pool.md). Keeping a candidate in mind ends their application
 * without a rejection: it keeps its stage, so every place that asks "is this application still in the job?" reads
 * `keptInMindAt` as well as the stage's kind, through these helpers.
 */

/** What a kept application shows where its stage would: it has left the job. */
export const keptInMindLabel = 'Kept in mind';

/** Whether the application has left its job: hired, rejected, withdrawn, or kept in mind. */
export const hasLeftJob = (application: Pick<ApplicationSummary, 'stage' | 'keptInMindAt'>): boolean =>
  Boolean(application.keptInMindAt) || isTerminal(application.stage.kind);

/** The stage as a list or header names it: "Kept in mind" once the team kept the candidate in mind. */
export const stageLabel = (application: Pick<ApplicationSummary, 'stage' | 'keptInMindAt'>): string =>
  application.keptInMindAt ? keptInMindLabel : application.stage.name;

/**
 * Whether the person can keep this application's candidate in mind: it is still in the job and the candidate gave a
 * permission for future roles that still holds. Never reads Verification output (ADR-0004).
 */
export const canKeepInMind = (
  application: Pick<ApplicationSummary, 'stage' | 'keptInMindAt' | 'futureRolesUntil'>,
  now: Date = new Date(),
): boolean => !hasLeftJob(application) && Boolean(application.futureRolesUntil) && new Date(application.futureRolesUntil!) > now;

/** What a keep did, in one line: "Kept 3 in mind. 2 did not allow it." */
export function describeKept(result: KeptInMind, name?: string): string {
  const kept = result.kept === 1 && name ? `Kept ${name} in mind.` : `Kept ${result.kept} in mind.`;
  const notAllowed = result.notAllowed > 0 ? ` ${result.notAllowed} did not allow it.` : '';
  const ended = result.alreadyEnded > 0 ? ` ${result.alreadyEnded} had already left the job.` : '';
  return `${kept}${notAllowed}${ended}`;
}
