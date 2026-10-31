import type { ApplicationTimelineEntry } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { actorText, describeEvent, kindCounts, visibleEntries } from '#lib/activity.js';

const base: ApplicationTimelineEntry = {
  id: 'e0',
  type: 'Submitted',
  occurredAt: '2026-10-24T14:43:00Z',
  actorUserId: null,
  fromStageId: null,
  toStageId: 'applied',
  note: null,
  tag: null,
  automationRunId: null,
  actorApiKeyId: null,
  scorecardId: null,
  stageId: null,
  recommendation: null,
};
const entry = (id: string, patch: Partial<ApplicationTimelineEntry>): ApplicationTimelineEntry => ({ ...base, id, ...patch });
const names = { stageName: (id: string | null | undefined) => (id === 'applied' ? 'Applied' : (id ?? '')), followUpName: () => 'Availability' };

describe('activity', () => {
  const entries = [
    entry('e1', {}),
    entry('e2', { type: 'NoteAdded', noteId: 'n1', note: 'Kept.' }),
    entry('e3', { type: 'NoteAdded', noteId: 'n2', note: null }),
    entry('e4', { type: 'NoteEdited', noteId: 'n1' }),
    entry('e5', { type: 'NoteDeleted', noteId: 'n2', actorUserId: 'u1' }),
    entry('e6', { type: 'EmailSent', emailSubject: 'Application received' }),
    entry('e7', { type: 'IntegrityAssessed' }),
  ];

  it('lists entries newest first without edit lines or deleted notes', () => {
    expect(visibleEntries(entries).map((item) => item.id)).toEqual(['e7', 'e6', 'e5', 'e2', 'e1']);
  });

  it('counts each kind, and the checks only under All', () => {
    expect(kindCounts(visibleEntries(entries))).toEqual({ all: 5, notes: 2, emails: 1, scorecards: 0, stage: 1 });
  });

  it('says a note was deleted and by whom, never what it said', () => {
    const deleted = entries[4]!;
    expect(describeEvent(deleted, names)).toBe('Deleted a note');
    expect(actorText(deleted, [{ id: 'm', userId: 'u1', email: 'a@example.com', displayName: 'Avery Quinn', role: 'Admin', customRoleId: null, roleName: 'Admin', joinedAt: base.occurredAt, jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 }], 'me')).toBe('by Avery Quinn');
  });

  it("names an API key's changes by the key", () => {
    expect(actorText(entry('k', { type: 'NoteAdded', actorApiKeyId: 'k1', actorApiKeyName: 'ATS sync' }), [], undefined)).toBe('by ATS sync');
    expect(actorText(entry('k', { type: 'NoteAdded', actorApiKeyId: 'k1' }), [], undefined)).toBe('by an API key');
    expect(describeEvent(entries[0]!, names)).toBe('Applied on the careers page, into Applied');
  });

  it('names a created task, with whoever created it, and lists it under All only', () => {
    const task = entry('t', { type: 'TaskCreated', note: 'Check the references', taskId: 't1', actorApiKeyId: 'k1', actorApiKeyName: 'Assistant' });
    expect(describeEvent(task, names)).toBe('Created a task');
    expect(actorText(task, [], undefined)).toBe('by Assistant');
    expect(kindCounts([task])).toEqual({ all: 1, notes: 0, emails: 0, scorecards: 0, stage: 0 });
  });

  describe('talent pool entries', () => {
    const jobNames = { ...names, jobName: (id: string | null | undefined) => (id === 'j2' ? 'Support Engineer' : 'another job') };

    it('says a candidate was kept in mind without a rejection, and lists it under Stage', () => {
      const kept = entry('k', { type: 'KeptInMind', note: 'Strong, wrong timing.' });
      expect(describeEvent(kept, jobNames)).toBe('Kept in mind: left the job without a rejection');
      expect(kindCounts([kept]).stage).toBe(1);
    });

    it.each([
      ['Expired', 'Left the talent pool: their permission ran out'],
      ['Removed', 'Took them out of the talent pool'],
      ['CandidateAsked', 'Took them out of the talent pool at their request'],
      ['AddedToJob', 'Added them to Support Engineer from the talent pool'],
      ['AppliedAgain', 'Left the talent pool: applied for Support Engineer'],
      ['BackInJob', 'Left the talent pool: back in this job'],
      ['KeptFromAnotherJob', 'Left the talent pool here: kept in mind from Support Engineer instead'],
    ] as const)('words leaving the pool by %s', (poolExit, text) => {
      expect(describeEvent(entry('l', { type: 'LeftTalentPool', poolExit, otherJobId: 'j2' }), jobNames)).toBe(text);
    });

    it('words a leave with no recorded reason as a removal, and names no job the reader cannot see', () => {
      expect(describeEvent(entry('l', { type: 'LeftTalentPool', poolExit: null }), jobNames)).toBe('Took them out of the talent pool');
      expect(describeEvent(entry('l', { type: 'LeftTalentPool', poolExit: 'AddedToJob', otherJobId: 'hidden' }), jobNames)).toBe(
        'Added them to another job from the talent pool',
      );
      expect(describeEvent(entry('l', { type: 'LeftTalentPool', poolExit: 'AddedToJob', otherJobId: 'j2' }), names)).toBe(
        'Added them to another job from the talent pool',
      );
    });

    it('names the job an invitation was for, and lists it under Emails', () => {
      const invited = entry('i', { type: 'InvitedToApply', otherJobId: 'j2' });
      expect(describeEvent(invited, jobNames)).toBe('Invited to apply for Support Engineer');
      expect(describeEvent(invited, names)).toBe('Invited to apply for another job');
      expect(kindCounts([invited]).emails).toBe(1);
    });
  });
});
