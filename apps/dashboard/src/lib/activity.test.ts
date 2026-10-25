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
    expect(actorText(deleted, [{ id: 'm', userId: 'u1', email: 'a@example.com', displayName: 'Avery Quinn', role: 'Admin', joinedAt: base.occurredAt }], 'me')).toBe('by Avery Quinn');
  });

  it("names an API key's changes by the key", () => {
    expect(actorText(entry('k', { type: 'NoteAdded', actorApiKeyId: 'k1', actorApiKeyName: 'ATS sync' }), [], undefined)).toBe('by ATS sync');
    expect(actorText(entry('k', { type: 'NoteAdded', actorApiKeyId: 'k1' }), [], undefined)).toBe('by an API key');
    expect(describeEvent(entries[0]!, names)).toBe('Applied on the careers page, into Applied');
  });
});
