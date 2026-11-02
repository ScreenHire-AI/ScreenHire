import type { ApplicationTimelineEntry, WorkflowStage } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { endedStatus, trackedStages, trackerFor } from './pipeline-tracker.js';

// vitest.config.ts pins the timezone to UTC and jsdom's languages are US English.
const now = Date.parse('2026-11-02T12:00:00Z');

const workflow: WorkflowStage[] = [
  { id: 'applied', name: 'Applied', kind: 'Applied' },
  { id: 'screening', name: 'Screening', kind: 'Screening' },
  { id: 'interview', name: 'Interview', kind: 'Interview' },
  { id: 'offer', name: 'Offer', kind: 'Offer' },
  { id: 'hired', name: 'Hired', kind: 'Hired' },
  { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
  { id: 'withdrawn', name: 'Withdrawn', kind: 'Withdrawn' },
];

const entry = (type: 'Submitted' | 'StageChanged', occurredAt: string, toStageId: string, fromStageId: string | null = null) =>
  ({ id: `${type}-${toStageId}-${occurredAt}`, type, occurredAt, toStageId, fromStageId }) as ApplicationTimelineEntry;

const history = [
  entry('Submitted', '2026-10-20T09:00:00Z', 'applied'),
  entry('StageChanged', '2026-10-22T09:00:00Z', 'screening', 'applied'),
  entry('StageChanged', '2026-10-29T09:00:00Z', 'interview', 'screening'),
];

const stageOf = (id: string) => {
  const { name, kind } = workflow.find((stage) => stage.id === id)!;
  return { id, name, kind };
};

const states = (steps: { name: string; state: string }[]) => steps.map((step) => `${step.name}:${step.state}`);

describe('trackerFor', () => {
  it('draws every stage but the ways out, with the stage they are in ringed and the earlier ones passed with their dates', () => {
    const tracker = trackerFor(workflow, { stage: stageOf('screening'), stageEnteredAt: '2026-10-30T09:00:00Z' }, history.slice(0, 2), now);

    expect(states(tracker.steps)).toEqual(['Applied:passed', 'Screening:current', 'Interview:upcoming', 'Offer:upcoming', 'Hired:upcoming']);
    expect(tracker.steps.map((step) => step.note)).toEqual(['Oct 20', 'Here 3 days', undefined, undefined, undefined]);
    expect(tracker).toMatchObject({ position: 2, total: 5, outcome: undefined });
  });

  it('keeps the latest date for a stage they entered twice', () => {
    const again = [...history, entry('StageChanged', '2026-10-30T09:00:00Z', 'screening', 'interview')];
    const tracker = trackerFor(workflow, { stage: stageOf('interview'), stageEnteredAt: '2026-10-31T09:00:00Z' }, again, now);

    expect(tracker.steps[1]).toMatchObject({ name: 'Screening', state: 'passed', note: 'Oct 30' });
  });

  it('fills a stage a move skipped, without a date', () => {
    const skipped = [entry('Submitted', '2026-10-20T09:00:00Z', 'applied'), entry('StageChanged', '2026-10-29T09:00:00Z', 'interview', 'applied')];
    const tracker = trackerFor(workflow, { stage: stageOf('interview'), stageEnteredAt: '2026-10-29T09:00:00Z' }, skipped, now);

    expect(tracker.steps[1]).toEqual({ id: 'screening', name: 'Screening', state: 'passed', note: undefined });
  });

  it('shows a saved candidate left from the stage they kept, and how, with the date', () => {
    const tracker = trackerFor(
      workflow,
      { stage: stageOf('interview'), stageEnteredAt: '2026-10-29T09:00:00Z', keptInMindAt: '2026-10-31T10:00:00Z' },
      history,
      now,
    );

    expect(states(tracker.steps)).toEqual(['Applied:passed', 'Screening:passed', 'Interview:left', 'Offer:upcoming', 'Hired:upcoming']);
    expect(tracker.steps[2]!.note).toBe('Saved Oct 31');
    expect(tracker).toMatchObject({ position: 3, outcome: 'Saved' });
  });

  it.each([
    ['rejected', 'Rejected'],
    ['withdrawn', 'Withdrawn'],
  ])('shows %s as a dark dot on the stage the last move left from', (id, label) => {
    const left = [...history, entry('StageChanged', '2026-10-31T09:00:00Z', id, 'interview')];
    const tracker = trackerFor(workflow, { stage: stageOf(id), stageEnteredAt: '2026-10-31T09:00:00Z' }, left, now);

    expect(states(tracker.steps)).toEqual(['Applied:passed', 'Screening:passed', 'Interview:left', 'Offer:upcoming', 'Hired:upcoming']);
    expect(tracker.steps[2]!.note).toBe(`${label} Oct 31`);
    expect(tracker.outcome).toBe(label);
  });

  it('falls back to the furthest stage reached when the closing move is not in the history', () => {
    const tracker = trackerFor(workflow, { stage: stageOf('rejected'), stageEnteredAt: '2026-10-31T09:00:00Z' }, history, now);

    expect(tracker.steps[2]).toMatchObject({ name: 'Interview', state: 'left' });
  });

  it('draws no ringed or dark dot when nothing says where the candidate was', () => {
    const tracker = trackerFor(workflow, { stage: stageOf('rejected'), stageEnteredAt: '2026-10-31T09:00:00Z' }, undefined, now);

    expect(tracker.steps.every((step) => step.state === 'upcoming')).toBe(true);
    expect(tracker.position).toBe(0);
    expect(tracker.outcome).toBe('Rejected');
  });

  it('draws no ringed dot when the stage is no longer in the workflow', () => {
    const gone = { id: 'removed', name: 'Phone call', kind: 'Custom' as const };
    const tracker = trackerFor(workflow, { stage: gone, stageEnteredAt: '2026-10-31T09:00:00Z' }, history, now);

    expect(tracker.steps.every((step) => step.state === 'upcoming')).toBe(true);
    expect(tracker.position).toBe(0);
  });

  it('ends a hired candidate on the Hired step', () => {
    const tracker = trackerFor(workflow, { stage: stageOf('hired'), stageEnteredAt: '2026-11-01T09:00:00Z' }, history, now);

    expect(tracker.steps.at(-1)).toMatchObject({ name: 'Hired', state: 'left', note: 'Hired Nov 1' });
    expect(tracker).toMatchObject({ position: 5, outcome: 'Hired' });
  });

  it('puts the year on a date from another year', () => {
    const old = [entry('Submitted', '2025-12-20T09:00:00Z', 'applied')];
    const tracker = trackerFor(workflow, { stage: stageOf('screening'), stageEnteredAt: '2026-10-30T09:00:00Z' }, old, now);

    expect(tracker.steps[0]!.note).toBe('Dec 20, 2025');
  });
});

describe('endedStatus', () => {
  it('has no status while the candidate is in the pipeline', () => {
    expect(endedStatus({ stage: stageOf('screening') })).toBeUndefined();
  });

  it.each([
    ['rejected', 'notInEffect', 'Rejected'],
    ['withdrawn', 'notInEffect', 'Withdrawn'],
    ['hired', 'done', 'Hired'],
  ])('names %s with the %s status', (id, status, label) => {
    expect(endedStatus({ stage: stageOf(id) })).toEqual({ status, label });
  });

  it('says Saved for a saved candidate, whatever stage they kept', () => {
    expect(endedStatus({ stage: stageOf('interview'), keptInMindAt: '2026-10-31T10:00:00Z' })).toEqual({ status: 'saved', label: 'Saved' });
  });
});

describe('trackedStages', () => {
  it('leaves out Rejected and Withdrawn only', () => {
    expect(trackedStages(workflow).map((stage) => stage.id)).toEqual(['applied', 'screening', 'interview', 'offer', 'hired']);
  });
});
