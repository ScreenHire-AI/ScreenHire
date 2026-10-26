import type { TeamMember } from '@screenhire/api-client';

/**
 * Mentions in a note. The API stores one as `@[userId]`, the member's user id (`NoteMentions` in the domain), so a
 * teammate who is renamed shows their current name. The note field shows `@Robin Hale` instead and keeps the people
 * picked from the list, so it can turn the text back into the stored form when the note is saved.
 */

const token = /@\[([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\]/gi;

/** A person picked in the note field, by the name the field shows. */
export interface PickedMention {
  userId: string;
  name: string;
}

/** A piece of a stored note: plain text, or a mention of a member. */
export type NoteSegment = { kind: 'text'; text: string } | { kind: 'mention'; userId: string };

/** The stored form of a mention. */
export const mentionToken = (userId: string) => `@[${userId}]`;

/** A stored note as text and mentions, in order. */
export function noteSegments(stored: string): NoteSegment[] {
  const segments: NoteSegment[] = [];
  let last = 0;
  for (const match of stored.matchAll(token)) {
    if (match.index > last) segments.push({ kind: 'text', text: stored.slice(last, match.index) });
    segments.push({ kind: 'mention', userId: match[1]!.toLowerCase() });
    last = match.index + match[0].length;
  }
  if (last < stored.length) segments.push({ kind: 'text', text: stored.slice(last) });
  return segments;
}

/** The user ids a stored note mentions, each once. */
export const mentionedUserIds = (stored: string): string[] => [
  ...new Set(noteSegments(stored).flatMap((segment) => (segment.kind === 'mention' ? [segment.userId] : []))),
];

/** A member's name in a mention; someone no longer on the team reads "former teammate". */
export const mentionName = (userId: string, members: readonly TeamMember[] | undefined): string =>
  members?.find((member) => member.userId.toLowerCase() === userId.toLowerCase())?.displayName ?? 'former teammate';

/** A stored note as the field shows it ("@Robin Hale"), with the people it mentions, for editing. */
export function toDisplay(stored: string, members: readonly TeamMember[] | undefined): { text: string; picked: PickedMention[] } {
  const picked: PickedMention[] = [];
  const text = noteSegments(stored)
    .map((segment) => {
      if (segment.kind === 'text') return segment.text;
      const name = mentionName(segment.userId, members);
      if (!picked.some((mention) => mention.userId === segment.userId)) picked.push({ userId: segment.userId, name });
      return `@${name}`;
    })
    .join('');
  return { text, picked };
}

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/**
 * The field's text in the stored form: each "@Name" of a person picked becomes their token. A name the text no longer
 * holds whole (someone deleted part of it) is plain text again, so the mention is gone. Longer names go first, so
 * "@Robin Hale" is never read as "@Robin" followed by " Hale".
 */
export function toStored(display: string, picked: readonly PickedMention[]): string {
  let stored = display;
  for (const mention of picked.toSorted((a, b) => b.name.length - a.name.length)) {
    stored = stored.replace(new RegExp(`@${escape(mention.name)}(?![\\p{L}\\p{N}])`, 'gu'), mentionToken(mention.userId));
  }
  return stored;
}

/**
 * The mention being typed before the caret: the position of its "@" and what follows it, or null when the caret is not
 * in one. An "@" starts a mention at the start of the text or after a space or punctuation, never inside a word (an
 * email address), and a mention is at most a few words on one line.
 */
export function mentionQuery(text: string, caret: number): { start: number; query: string } | null {
  const before = text.slice(0, caret);
  const at = before.lastIndexOf('@');
  if (at < 0) return null;
  if (at > 0 && /[\p{L}\p{N}_]/u.test(before[at - 1]!)) return null;
  const query = before.slice(at + 1);
  if (query.length > 40 || /[\n@]/.test(query) || /\s{2}/.test(query) || query.split(' ').length > 3) return null;
  return { start: at, query };
}

/** Members whose name has a word starting with `query`, ignoring case, best first, at most `limit`. */
export function matchMembers(members: readonly TeamMember[], query: string, limit = 8): TeamMember[] {
  const wanted = query.trim().toLowerCase();
  if (!wanted) return members.slice(0, limit);
  const starts = (member: TeamMember) => member.displayName.toLowerCase().startsWith(wanted);
  const word = (member: TeamMember) =>
    member.displayName
      .toLowerCase()
      .split(/\s+/)
      .some((part) => part.startsWith(wanted)) || member.displayName.toLowerCase().includes(` ${wanted}`);
  return [...members.filter(starts), ...members.filter((member) => !starts(member) && word(member))].slice(0, limit);
}

/** The field's text with the mention at `start` (up to the caret) replaced by `@Name `, and where the caret goes. */
export function insertMention(text: string, start: number, caret: number, name: string): { text: string; caret: number } {
  const inserted = `@${name} `;
  return { text: text.slice(0, start) + inserted + text.slice(caret), caret: start + inserted.length };
}
