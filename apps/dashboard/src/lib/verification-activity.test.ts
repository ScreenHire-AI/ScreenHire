import type { VerificationReviewEntry } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { groupKeyReads, reviewActivities } from './verification-activity.js';

describe('verification activity', () => {
  const entry = (id: string, overrides: Partial<VerificationReviewEntry>): VerificationReviewEntry => ({
    id,
    type: 'NoteAdded',
    assessmentKind: null,
    assessmentId: null,
    dismissReason: null,
    note: null,
    actorUserId: 'user-1',
    actorName: 'Avery Quinn',
    occurredAt: '2026-10-20T10:00:00Z',
    signalId: null,
    signalTitle: null,
    explainReason: null,
    apiKeyId: null,
    apiKeyName: null,
    keyCreatedByName: null,
    ...overrides,
  });
  const read = (id: string, apiKeyId: string, occurredAt: string) =>
    entry(id, { type: 'ReadByKey', actorUserId: null, actorName: null, apiKeyId, apiKeyName: apiKeyId, occurredAt });

  it('folds consecutive reads by one key into the latest, with how many there were', () => {
    const rows = groupKeyReads([read('a', 'k1', '2026-10-20T10:00:00Z'), read('b', 'k1', '2026-10-20T10:05:00Z'), read('c', 'k1', '2026-10-20T10:09:00Z')]);

    expect(rows.map((row) => [row.entry.id, row.reads])).toEqual([['c', 3]]);
  });

  it('keeps a different key, and a person\'s action between two reads, as rows of their own', () => {
    const rows = groupKeyReads([read('a', 'k1', '2026-10-20T10:00:00Z'), read('b', 'k2', '2026-10-20T10:01:00Z'), entry('n', {}), read('c', 'k2', '2026-10-20T10:02:00Z')]);

    expect(rows.map((row) => [row.entry.id, row.reads])).toEqual([['a', 1], ['b', 1], ['n', 1], ['c', 1]]);
  });

  it('changes nothing for a log with no key reads', () => {
    const entries = [entry('x', {}), entry('y', { type: 'Dismissed' })];

    expect(groupKeyReads(entries).map((row) => row.entry)).toEqual(entries);
    expect(groupKeyReads([])).toEqual([]);
  });

  it('words each kind of entry for Activity, newest first, quoting a reviewer and never a system reason', () => {
    const lines = reviewActivities(
      [
        entry('n', { type: 'NoteAdded', note: 'Called the number.', occurredAt: '2026-10-20T10:00:00Z' }),
        entry('e', { type: 'ReasonExplained', signalTitle: 'Internet phone number', explainReason: 'ExpectedForRole', occurredAt: '2026-10-20T10:01:00Z' }),
        entry('u', { type: 'ExplanationUndone', signalTitle: 'Internet phone number', occurredAt: '2026-10-20T10:02:00Z' }),
        entry('d', { type: 'Dismissed', assessmentKind: 'Authenticity', dismissReason: 'VerifiedOutsideScreenHire', note: 'Video call.', occurredAt: '2026-10-20T10:03:00Z' }),
        entry('c', { type: 'EvidenceChanged', actorUserId: null, actorName: null, note: 'New evidence: a shared device.', occurredAt: '2026-10-20T10:04:00Z' }),
        read('r1', 'k1', '2026-10-20T10:05:00Z'),
        read('r2', 'k1', '2026-10-20T10:06:00Z'),
      ],
      'user-1',
    );

    expect(lines.map((line) => line.id)).toEqual(['r2', 'c', 'd', 'u', 'e', 'n']);
    expect(lines.map((line) => line.event)).toEqual([
      'Verification read through the API key k1, 2 times',
      'New evidence: a shared device.',
      'Dismissed the verification result: Verified outside ScreenHire',
      'Undid the explanation of "Internet phone number"',
      'Explained "Internet phone number": Expected for this role',
      'Verification note',
    ]);
    expect(lines.map((line) => line.actor)).toEqual(['key made by someone who has left', 'by ScreenHire', 'by you', 'by you', 'by you', 'by you']);
    expect(lines.find((line) => line.id === 'd')?.note).toBe('Video call.');
    expect(lines.find((line) => line.id === 'c')?.note).toBeUndefined();
  });
});
