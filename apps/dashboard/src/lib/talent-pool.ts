import type { ApplicationSummary, KeptInMind } from '@screenhire/api-client';
import { isTerminal } from '#lib/workflow.js';

/**
 * Saved candidates in the dashboard (docs/architecture/talent-pool.md). Saving a candidate ends their application
 * without a rejection: it keeps its stage, so every place that asks "is this application still in the job?" reads
 * `keptInMindAt` as well as the stage's kind, through these helpers.
 */

/** The most applications one save request takes (KeepInMindRequestValidator.MaxApplications in the API). */
export const keepBatchSize = 200;

/** What a saved application shows where its stage would: it has left the job. */
export const keptInMindLabel = 'Saved';

/** Whether the application has left its job: hired, rejected, withdrawn, or saved. */
export const hasLeftJob = (application: Pick<ApplicationSummary, 'stage' | 'keptInMindAt'>): boolean =>
  Boolean(application.keptInMindAt) || isTerminal(application.stage.kind);

/** The stage as a list or header names it: "Saved" once the team saved the candidate. */
export const stageLabel = (application: Pick<ApplicationSummary, 'stage' | 'keptInMindAt'>): string =>
  application.keptInMindAt ? keptInMindLabel : application.stage.name;

/**
 * Whether the person can save this application's candidate: it is still in the job and the candidate allowed the team
 * to keep their details for future roles, and has not taken that back. Never reads Verification output (ADR-0004).
 */
export const canKeepInMind = (application: Pick<ApplicationSummary, 'stage' | 'keptInMindAt' | 'allowsFutureRoles'>): boolean =>
  !hasLeftJob(application) && Boolean(application.allowsFutureRoles);

/** What a save did, in one line: "Saved 3. 2 did not allow it." */
export function describeKept(result: KeptInMind, name?: string): string {
  const kept = result.kept === 1 && name ? `Saved ${name}.` : `Saved ${result.kept}.`;
  const notAllowed = result.notAllowed > 0 ? ` ${result.notAllowed} did not allow it.` : '';
  const ended = result.alreadyEnded > 0 ? ` ${result.alreadyEnded} had already left the job.` : '';
  return `${kept}${notAllowed}${ended}`;
}
