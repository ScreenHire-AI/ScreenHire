import CircleSlashIcon from '~icons/lucide/circle-slash';
import ClipboardPenIcon from '~icons/lucide/clipboard-pen';
import KeyRoundIcon from '~icons/lucide/key-round';
import MessageSquareCheckIcon from '~icons/lucide/message-square-check';
import ScanSearchIcon from '~icons/lucide/scan-search';
import Undo2Icon from '~icons/lucide/undo-2';
import type { VerificationReviewEntry } from '@screenhire/api-client';
import type { Component } from 'svelte';
import { dismissReasonLabels, explainReasonLabels, reviewerName } from '#lib/verification.js';

/**
 * What people and API keys did to an application's verification, as entries for its Activity (design system section 16):
 * every dismissal, explained finding, undo and note, each read through an API key with the Verification read scope
 * (ADR-0021), by key, and the evidence a re-check added or took away (written by ScreenHire, with its reason, so a level
 * never moves without one). The API returns them as `reviewLog`, oldest first.
 */

/** One entry of the log, and with `reads` above 1, the run of reads by one key it stands for. */
export interface ReviewLogRow {
  entry: VerificationReviewEntry;
  reads: number;
}

/**
 * The log's entries (oldest first) as rows: consecutive reads through the same API key become one row at the latest
 * read, with how many there were, so an assistant that reads a hundred applications does not bury the people's actions
 * (ADR-0021). Every read stays in the API; only the list folds them.
 */
export function groupKeyReads(entries: readonly VerificationReviewEntry[]): ReviewLogRow[] {
  const rows: ReviewLogRow[] = [];
  for (const entry of entries) {
    const last = rows.at(-1);
    if (entry.type === 'ReadByKey' && last?.entry.type === 'ReadByKey' && last.entry.apiKeyId === entry.apiKeyId) {
      last.entry = entry;
      last.reads += 1;
    } else {
      rows.push({ entry, reads: 1 });
    }
  }
  return rows;
}

const icons: Record<VerificationReviewEntry['type'], Component> = {
  Dismissed: CircleSlashIcon,
  NoteAdded: ClipboardPenIcon,
  ReasonExplained: MessageSquareCheckIcon,
  ExplanationUndone: Undo2Icon,
  ReadByKey: KeyRoundIcon,
  EvidenceChanged: ScanSearchIcon,
};

/** One Activity line for a log row; the shape of an `Activity` in `activity-list.svelte`. */
export interface ReviewActivity {
  id: string;
  icon: Component;
  event: string;
  actor: string;
  note?: string;
  at: string;
}

function describe({ entry, reads }: ReviewLogRow): string {
  if (entry.type === 'ReadByKey') return `Verification read through the API key ${entry.apiKeyName ?? 'an unknown key'}${reads > 1 ? `, ${reads} times` : ''}`;
  const about = entry.assessmentKind === 'AiAssistance' ? 'the AI help result' : 'the verification result';
  const finding = entry.signalTitle ?? 'a finding';
  if (entry.type === 'Dismissed') return `Dismissed ${about}: ${entry.dismissReason ? dismissReasonLabels[entry.dismissReason] : 'no reason'}`;
  if (entry.type === 'ReasonExplained') return `Explained "${finding}": ${entry.explainReason ? explainReasonLabels[entry.explainReason] : 'no reason'}`;
  if (entry.type === 'ExplanationUndone') return `Undid the explanation of "${finding}"`;
  // The reason is the entry's note, in words that name no other application, job or candidate (ADR-0015).
  if (entry.type === 'EvidenceChanged') return entry.note ?? 'Verification evidence changed';
  // A note names the result it was written on, as the old log did; one written before results had a kind reads plainly.
  return entry.assessmentKind ? `Note on ${about}` : 'Verification note';
}

function actorOf({ entry }: ReviewLogRow, currentUserId: string | undefined): string {
  if (entry.type === 'ReadByKey') return entry.keyCreatedByName ? `key made by ${entry.keyCreatedByName}` : 'key made by someone who has left';
  return entry.actorUserId ? `by ${reviewerName(entry.actorUserId, entry.actorName, currentUserId)}` : 'by ScreenHire';
}

/** The log as Activity lines, newest first. A reviewer's words are quoted under their line; a system reason is the line itself. */
export function reviewActivities(entries: readonly VerificationReviewEntry[], currentUserId: string | undefined): ReviewActivity[] {
  return groupKeyReads(entries)
    .toReversed()
    .map((row) => ({
      id: row.entry.id,
      icon: icons[row.entry.type],
      event: describe(row),
      actor: actorOf(row, currentUserId),
      note: row.entry.type === 'EvidenceChanged' ? undefined : (row.entry.note ?? undefined),
      at: row.entry.occurredAt,
    }));
}
