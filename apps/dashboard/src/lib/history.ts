import type { ChangeActorKind, HistoryEntry, HistoryPage, TeamMember } from '@screenhire/api-client';
import { dayKey, dayLabel } from '#lib/dates.js';
import { personName } from '#lib/team.js';

// Change history (design system T6, docs/architecture/change-history.md): who changed a template or a job, when, and
// what, in the History sheet and the Last updated fact.

/** Who made a change, as the API names them: by kind, with a member's or an API key's id. */
export interface ChangeActor {
  by: ChangeActorKind | null;
  userId?: string | null;
  apiKeyId?: string | null;
}

/**
 * Who made a change, as the screen names them: "ScreenHire" for an automation or a built-in, "you", a member's current
 * name, "a former teammate", or "an API key". Undefined when nobody was recorded, for a change made before history was.
 */
export function actorName(actor: ChangeActor, members: readonly TeamMember[] | undefined, currentUserId: string | undefined): string | undefined {
  if (actor.by === 'ScreenHire') return 'ScreenHire';
  if (actor.by === 'Member' && actor.userId) return personName(actor.userId, members, currentUserId);
  if (actor.by === 'ApiKey') return 'an API key';
  return undefined;
}

/** The first letter in capitals, for a name that starts a sentence ("You changed the subject"). */
export const sentenceStart = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

export interface HistoryDay {
  key: string;
  label: string;
  entries: HistoryEntry[];
}

/** Entries, newest first, under the day each happened on in the browser's time zone (`Today`, `Oct 22`). */
export function groupByDay(entries: readonly HistoryEntry[], now: number): HistoryDay[] {
  const days: HistoryDay[] = [];
  for (const entry of entries) {
    const key = dayKey(entry.at);
    const last = days.at(-1);
    if (last?.key === key) last.entries.push(entry);
    else days.push({ key, label: dayLabel(entry.at, now), entries: [entry] });
  }
  return days;
}

/** The loaded pages' entries in order, each once: while a refetch after a save is in flight, two pages can share an entry. */
export function joinPages(pages: readonly (HistoryPage | undefined)[]): HistoryEntry[] {
  const seen = new Set<string>();
  return pages.flatMap((page) => page?.entries ?? []).filter((entry) => !seen.has(entry.id) && Boolean(seen.add(entry.id)));
}
