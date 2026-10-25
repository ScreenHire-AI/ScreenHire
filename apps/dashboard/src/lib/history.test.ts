import type { HistoryEntry, TeamMember } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { actorName, groupByDay, initials, joinPages, sentenceStart } from './history.js';

const members: TeamMember[] = [
  { id: 'm1', userId: 'u1', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner', joinedAt: '2026-10-01T09:00:00Z' },
  { id: 'm2', userId: 'u2', email: 'jordan@example.com', displayName: 'Jordan Ellis', role: 'Recruiter', joinedAt: '2026-10-01T09:00:00Z' },
];

const entry = (id: string, at: string): HistoryEntry => ({ id, at, by: 'Member', userId: 'u2', apiKeyId: null, lines: [] });

describe('history', () => {
  it('names who made a change as the screen names people', () => {
    expect(actorName({ by: 'Member', userId: 'u2' }, members, 'u1')).toBe('Jordan Ellis');
    expect(actorName({ by: 'Member', userId: 'u1' }, members, 'u1')).toBe('you');
    expect(actorName({ by: 'Member', userId: 'gone' }, members, 'u1')).toBe('a former teammate');
    expect(actorName({ by: 'ScreenHire' }, members, 'u1')).toBe('ScreenHire');
    expect(actorName({ by: 'ApiKey', apiKeyId: 'k1' }, members, 'u1')).toBe('an API key');
    expect(actorName({ by: null }, members, 'u1')).toBeUndefined();
  });

  it('groups entries by the day they happened on, keeping their order', () => {
    const now = Date.parse('2026-10-25T13:00:00Z');
    const days = groupByDay([entry('a', '2026-10-25T12:40:00Z'), entry('b', '2026-10-25T10:00:00Z'), entry('c', '2026-10-22T09:12:00Z')], now);

    expect(days.map((day) => [day.label, day.entries.map((e) => e.id)])).toEqual([
      ['Today', ['a', 'b']],
      ['Oct 22', ['c']],
    ]);
  });

  it('joins pages once each, even when an entry shifts onto the next page', () => {
    const a = entry('a', '2026-10-25T12:00:00Z');
    const b = entry('b', '2026-10-25T11:00:00Z');
    expect(joinPages([{ entries: [a, b], nextBefore: 2 }, { entries: [b], nextBefore: null }, undefined]).map((e) => e.id)).toEqual(['a', 'b']);
  });

  it('starts a sentence with a capital and makes initials', () => {
    expect(sentenceStart('you')).toBe('You');
    expect(initials('Avery Quinn')).toBe('AQ');
    expect(initials('a former teammate')).toBe('AF');
  });
});
