import type { TeamMember } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { insertMention, matchMembers, mentionedUserIds, mentionQuery, mentionToken, noteSegments, toDisplay, toStored } from '#lib/mentions.js';

const robin: TeamMember = { id: 'm1', userId: '0199e8a0-0000-7000-8000-0000000000a1', email: 'robin@example.com', displayName: 'Robin Hale', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-01T09:00:00Z' };
const rowan: TeamMember = { id: 'm2', userId: '0199e8a0-0000-7000-8000-0000000000a2', email: 'rowan@example.com', displayName: 'Rowan Ellis', role: 'Viewer', customRoleId: null, roleName: 'Viewer', joinedAt: '2026-10-01T09:00:00Z' };
const avery: TeamMember = { id: 'm3', userId: '0199e8a0-0000-7000-8000-0000000000a3', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner', customRoleId: null, roleName: 'Owner', joinedAt: '2026-10-01T09:00:00Z' };
const members = [robin, rowan, avery];

describe('mentions', () => {
  it('splits a stored note into text and mentions', () => {
    expect(noteSegments(`Ask ${mentionToken(robin.userId)} today.`)).toEqual([
      { kind: 'text', text: 'Ask ' },
      { kind: 'mention', userId: robin.userId },
      { kind: 'text', text: ' today.' },
    ]);
    expect(mentionedUserIds(`${mentionToken(robin.userId)} ${mentionToken(rowan.userId)} ${mentionToken(robin.userId)}`)).toEqual([robin.userId, rowan.userId]);
  });

  it('shows current names for editing and turns them back into tokens on save', () => {
    const stored = `${mentionToken(robin.userId)}, ask ${mentionToken('0199e8a0-0000-7000-8000-0000000000ff')} too.`;

    const display = toDisplay(stored, members);

    expect(display.text).toBe('@Robin Hale, ask @former teammate too.');
    expect(toStored(display.text, display.picked)).toBe(stored);
  });

  it('drops a mention whose name was edited away and never reads a longer name as a shorter one', () => {
    const picked = [
      { userId: robin.userId, name: 'Robin' },
      { userId: rowan.userId, name: 'Robin Hale' },
    ];

    expect(toStored('@Robin Hale and @Robin, then @Robi', picked)).toBe(`${mentionToken(rowan.userId)} and ${mentionToken(robin.userId)}, then @Robi`);
    expect(toStored('@Robin Halek', [{ userId: robin.userId, name: 'Robin Hale' }])).toBe('@Robin Halek');
  });

  it('finds the mention being typed, and never one inside an email address', () => {
    expect(mentionQuery('Can you book it, @Ro', 20)).toEqual({ start: 17, query: 'Ro' });
    expect(mentionQuery('@', 1)).toEqual({ start: 0, query: '' });
    expect(mentionQuery('Write to robin@example.com', 26)).toBeNull();
    expect(mentionQuery('No mention here', 15)).toBeNull();
    expect(mentionQuery('@Robin\nnext line', 16)).toBeNull();
  });

  it('narrows the list by the start of a word, best matches first', () => {
    expect(matchMembers(members, 'ro').map((member) => member.displayName)).toEqual(['Robin Hale', 'Rowan Ellis']);
    expect(matchMembers(members, 'qu').map((member) => member.displayName)).toEqual(['Avery Quinn']);
    expect(matchMembers(members, '')).toHaveLength(3);
    expect(matchMembers(members, 'zz')).toEqual([]);
  });

  it('puts the picked name where the mention was typed', () => {
    expect(insertMention('Ask @Ro about it', 4, 7, 'Robin Hale')).toEqual({ text: 'Ask @Robin Hale  about it', caret: 16 });
  });
});
