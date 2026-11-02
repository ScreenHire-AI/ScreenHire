import { describe, expect, it } from 'vitest';
import { canKeepInMind, describeKept, hasLeftJob, keptInMindLabel, stageLabel } from '#lib/talent-pool.js';

type Stage = { id: string; name: string; kind: 'Applied' | 'Screening' | 'Interview' | 'Offer' | 'Hired' | 'Rejected' | 'Withdrawn' };

const screening: Stage = { id: 'screening', name: 'Screening', kind: 'Screening' };
const rejected: Stage = { id: 'rejected', name: 'Not a fit', kind: 'Rejected' };

describe('saved candidate helpers', () => {
  describe('hasLeftJob', () => {
    it('is false for an open application in an open stage', () => {
      expect(hasLeftJob({ stage: screening, keptInMindAt: null })).toBe(false);
      expect(hasLeftJob({ stage: screening })).toBe(false);
    });

    it('is true once the candidate is saved, whatever the stage', () => {
      expect(hasLeftJob({ stage: screening, keptInMindAt: '2026-10-30T10:00:00Z' })).toBe(true);
    });

    it.each(['Hired', 'Rejected', 'Withdrawn'] as const)('is true for a %s application', (kind) => {
      expect(hasLeftJob({ stage: { id: 'end', name: 'End', kind }, keptInMindAt: null })).toBe(true);
    });
  });

  describe('stageLabel', () => {
    it('names the stage while the candidate is in the job', () => {
      expect(stageLabel({ stage: screening, keptInMindAt: null })).toBe('Screening');
    });

    it('says Saved in place of the stage once they were saved', () => {
      expect(stageLabel({ stage: screening, keptInMindAt: '2026-10-30T10:00:00Z' })).toBe('Saved');
      expect(keptInMindLabel).toBe('Saved');
    });
  });

  describe('canKeepInMind', () => {
    it('is true for an open application whose candidate allowed future roles', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: null, allowsFutureRoles: true })).toBe(true);
    });

    it('is false when the candidate never allowed it, or took it back', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: null, allowsFutureRoles: false })).toBe(false);
      expect(canKeepInMind({ stage: screening })).toBe(false);
    });

    it('is false for an application already saved', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: '2026-10-30T10:00:00Z', allowsFutureRoles: true })).toBe(false);
    });

    it('is false for a rejected application, even with permission', () => {
      expect(canKeepInMind({ stage: rejected, keptInMindAt: null, allowsFutureRoles: true })).toBe(false);
    });
  });

  describe('describeKept', () => {
    it('names the candidate when exactly one was saved and a name is given', () => {
      expect(describeKept({ kept: 1, notAllowed: 0, alreadyEnded: 0 }, 'Casey Morgan')).toBe('Saved Casey Morgan.');
    });

    it('counts them when several were saved, or when no name is given', () => {
      expect(describeKept({ kept: 3, notAllowed: 0, alreadyEnded: 0 }, 'Casey Morgan')).toBe('Saved 3.');
      expect(describeKept({ kept: 1, notAllowed: 0, alreadyEnded: 0 })).toBe('Saved 1.');
    });

    it('adds who did not allow it and who had already left the job', () => {
      expect(describeKept({ kept: 3, notAllowed: 2, alreadyEnded: 0 })).toBe('Saved 3. 2 did not allow it.');
      expect(describeKept({ kept: 2, notAllowed: 1, alreadyEnded: 4 })).toBe('Saved 2. 1 did not allow it. 4 had already left the job.');
      expect(describeKept({ kept: 0, notAllowed: 0, alreadyEnded: 1 })).toBe('Saved 0. 1 had already left the job.');
    });
  });
});
