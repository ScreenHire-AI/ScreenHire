import { describe, expect, it } from 'vitest';
import { canKeepInMind, describeKept, hasLeftJob, keptInMindLabel, stageLabel } from '#lib/talent-pool.js';

type Stage = { id: string; name: string; kind: 'Applied' | 'Screening' | 'Interview' | 'Offer' | 'Hired' | 'Rejected' | 'Withdrawn' };

const screening: Stage = { id: 'screening', name: 'Screening', kind: 'Screening' };
const rejected: Stage = { id: 'rejected', name: 'Not a fit', kind: 'Rejected' };
const now = new Date('2026-10-30T12:00:00Z');
const later = '2027-10-30T12:00:00Z';
const earlier = '2026-10-29T12:00:00Z';

describe('talent pool helpers', () => {
  describe('hasLeftJob', () => {
    it('is false for an open application in an open stage', () => {
      expect(hasLeftJob({ stage: screening, keptInMindAt: null })).toBe(false);
      expect(hasLeftJob({ stage: screening })).toBe(false);
    });

    it('is true once the candidate is kept in mind, whatever the stage', () => {
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

    it('says Kept in mind in place of the stage once they were kept', () => {
      expect(stageLabel({ stage: screening, keptInMindAt: '2026-10-30T10:00:00Z' })).toBe('Kept in mind');
      expect(keptInMindLabel).toBe('Kept in mind');
    });
  });

  describe('canKeepInMind', () => {
    it('is true for an open application whose permission is still in the future', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: null, futureRolesUntil: later }, now)).toBe(true);
    });

    it('is false when the candidate never gave permission', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: null, futureRolesUntil: null }, now)).toBe(false);
      expect(canKeepInMind({ stage: screening }, now)).toBe(false);
    });

    it('is false when the permission has run out', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: null, futureRolesUntil: earlier }, now)).toBe(false);
    });

    it('is false for an application already kept in mind', () => {
      expect(canKeepInMind({ stage: screening, keptInMindAt: '2026-10-30T10:00:00Z', futureRolesUntil: later }, now)).toBe(false);
    });

    it('is false for a rejected application, even with permission', () => {
      expect(canKeepInMind({ stage: rejected, keptInMindAt: null, futureRolesUntil: later }, now)).toBe(false);
    });
  });

  describe('describeKept', () => {
    it('names the candidate when exactly one was kept and a name is given', () => {
      expect(describeKept({ kept: 1, notAllowed: 0, alreadyEnded: 0 }, 'Casey Morgan')).toBe('Kept Casey Morgan in mind.');
    });

    it('counts them when several were kept, or when no name is given', () => {
      expect(describeKept({ kept: 3, notAllowed: 0, alreadyEnded: 0 }, 'Casey Morgan')).toBe('Kept 3 in mind.');
      expect(describeKept({ kept: 1, notAllowed: 0, alreadyEnded: 0 })).toBe('Kept 1 in mind.');
    });

    it('adds who did not allow it and who had already left the job', () => {
      expect(describeKept({ kept: 3, notAllowed: 2, alreadyEnded: 0 })).toBe('Kept 3 in mind. 2 did not allow it.');
      expect(describeKept({ kept: 2, notAllowed: 1, alreadyEnded: 4 })).toBe('Kept 2 in mind. 1 did not allow it. 4 had already left the job.');
      expect(describeKept({ kept: 0, notAllowed: 0, alreadyEnded: 1 })).toBe('Kept 0 in mind. 1 had already left the job.');
    });
  });
});
