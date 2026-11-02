import type { ApplicationDetail, ApplicationTimelineEntry, WorkflowStage } from '@screenhire/api-client';
import { shortDate, timeSince } from '#lib/dates.js';
import type { StatusName } from '#lib/status.js';
import { keptInMindLabel } from '#lib/talent-pool.js';
import { isTerminal } from '#lib/workflow.js';

/**
 * The pipeline tracker in a candidate's header (design system section 11): every stage of the job's workflow in order,
 * where the candidate has been and where they are now, or how they left. It reads stages, dates and the stage kind
 * only, never Verification output (ADR-0004).
 */

/** Passed: filled, with the date they reached it. Current: ringed, "Here 3 days". Left: dark, "Saved Oct 31". */
export type TrackerState = 'passed' | 'current' | 'upcoming' | 'left';

export interface TrackerStep {
  id: string;
  name: string;
  state: TrackerState;
  /** The date reached, "Here 3 days", or how they left; absent for a stage not yet reached or reached on no known date. */
  note?: string;
}

export interface Tracker {
  steps: TrackerStep[];
  /** The 1-based place of the current (or left-from) step, for "2 of 6"; 0 when the candidate's stage is not in the workflow. */
  position: number;
  total: number;
  /** How the candidate left ("Saved", "Rejected", "Withdrawn", "Hired"); undefined while they are in the pipeline. */
  outcome?: string;
}

/**
 * The status badge beside the name once the candidate has left the job: Saved, Rejected, Withdrawn or Hired. It takes the
 * place of the authenticity level, which is about an application still in the pipeline. Rejected and Withdrawn are "not in
 * effect" (grey, design system section 3.2); Hired is Done. Undefined while they are in the pipeline.
 */
export function endedStatus(detail: Pick<ApplicationDetail, 'stage' | 'keptInMindAt'>): { status: StatusName; label: string } | undefined {
  if (detail.keptInMindAt) return { status: 'saved', label: keptInMindLabel };
  if (!isTerminal(detail.stage.kind)) return undefined;
  return { status: detail.stage.kind === 'Hired' ? 'done' : 'notInEffect', label: detail.stage.name };
}

/** The stages the tracker draws: Rejected and Withdrawn are ways out, not steps along the way. Hired is the last step. */
export const trackedStages = (stages: readonly WorkflowStage[]): WorkflowStage[] =>
  stages.filter((stage) => stage.kind !== 'Rejected' && stage.kind !== 'Withdrawn');

export function trackerFor(
  stages: readonly WorkflowStage[],
  detail: Pick<ApplicationDetail, 'stage' | 'stageEnteredAt' | 'keptInMindAt'>,
  entries: readonly ApplicationTimelineEntry[] | undefined,
  now: number,
): Tracker {
  const steps = trackedStages(stages);
  const moves = (entries ?? []).filter((entry) => entry.type === 'Submitted' || entry.type === 'StageChanged');

  // When each stage was reached; the timeline is oldest first, so a stage entered twice keeps its latest entry.
  const reachedAt = new Map<string, string>();
  for (const move of moves) {
    if (move.toStageId) reachedAt.set(move.toStageId, move.occurredAt);
  }

  const saved = Boolean(detail.keptInMindAt);
  const ended = saved || isTerminal(detail.stage.kind);
  const outcome = saved ? keptInMindLabel : ended ? detail.stage.name : undefined;
  const endedAt = saved ? detail.keptInMindAt! : detail.stageEnteredAt;

  const indexOf = (id: string | null | undefined) => (id ? steps.findIndex((stage) => stage.id === id) : -1);
  let anchor = indexOf(detail.stage.id);
  if (anchor < 0 && ended) {
    // A rejected or withdrawn application sits in a closing stage the tracker does not draw: it left from the stage
    // before its last move into that stage, or else from the furthest stage it is known to have reached.
    const lastMove = moves.findLast((move) => move.type === 'StageChanged' && move.toStageId === detail.stage.id);
    anchor = indexOf(lastMove?.fromStageId);
    if (anchor < 0) anchor = steps.findLastIndex((stage) => reachedAt.has(stage.id));
  }

  const drawn = steps.map<TrackerStep>((stage, index) => {
    const base = { id: stage.id, name: stage.name };
    if (anchor < 0 || index > anchor) return { ...base, state: 'upcoming' };
    if (index < anchor) {
      const at = reachedAt.get(stage.id);
      return { ...base, state: 'passed', note: at ? shortDate(at, now) : undefined };
    }
    return ended
      ? { ...base, state: 'left', note: `${outcome} ${shortDate(endedAt, now)}` }
      : { ...base, state: 'current', note: `Here ${timeSince(detail.stageEnteredAt, now)}` };
  });

  return { steps: drawn, position: anchor + 1, total: drawn.length, outcome };
}
