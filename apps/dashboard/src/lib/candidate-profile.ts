import type { ApplicationSummary, CandidateHistoryEntry, TeamMember } from '@screenhire/api-client';
import type { Activity } from '#lib/components/activity-list.svelte';
import { actorText, describeEvent, eventIcons } from '#lib/activity.js';
import { keptInMindLabel } from '#lib/talent-pool.js';
import { isTerminal } from '#lib/workflow.js';

/**
 * A candidate's profile (/candidates/{id}): who they are, every application they made in the order they made it, and
 * the history across all of them. Nothing here reads Verification output (ADR-0004).
 */

/** How far an application got and how it ended, in the profile's words. */
export interface Progress {
  /** The last stage it was in before it ended. Undefined while it is open, where the outcome names its stage, and when no move is known. */
  reached?: string;
  /** "Hired", "Rejected", "Withdrawn", "Saved", or the current stage name while it is still open. */
  outcome: string;
  /** When it ended; undefined while it is open. */
  endedAt?: string;
}

/**
 * How far the application got and how it ended. A hired, rejected or withdrawn application sits in a closing stage, so
 * "how far" is the stage it was in before its last move; a saved candidate kept their stage. The history is the
 * source for the move, newest first.
 */
export function progressOf(application: ApplicationSummary, history: readonly CandidateHistoryEntry[] | undefined): Progress {
  if (application.keptInMindAt) {
    return { reached: application.stage.name, outcome: keptInMindLabel, endedAt: application.keptInMindAt };
  }

  if (!isTerminal(application.stage.kind)) {
    return { outcome: application.stage.name };
  }

  const lastMove = history?.find((item) => item.applicationId === application.id && item.entry.type === 'StageChanged');
  return { reached: lastMove?.fromStageName ?? undefined, outcome: application.stage.name, endedAt: application.stageEnteredAt };
}

/** The history as Activity lists it, newest first: no edit lines, no line for a note that was deleted, and the job each entry is in. */
export function historyItems(
  history: readonly CandidateHistoryEntry[],
  members: readonly TeamMember[] | undefined,
  currentUserId: string | undefined,
  jobName: (id: string | null | undefined) => string,
  /** Names the job each entry is in. Left out for a candidate with one application, where it would say the same on every line. */
  inJob = true,
): Activity[] {
  return history
    .filter(({ entry }) => entry.type !== 'NoteEdited' && !(entry.type === 'NoteAdded' && entry.note === null))
    .map((item) => {
      const { entry } = item;
      // Stages belong to each job's workflow, so the API names the ones this entry mentions.
      const stageName = (id: string | null | undefined) => {
        if (!id) return '';
        if (id === entry.fromStageId) return item.fromStageName ?? id;
        if (id === entry.toStageId) return item.toStageName ?? id;
        return id === entry.stageId ? (item.stageName ?? id) : id;
      };
      const who = actorText(entry, members, currentUserId);
      return {
        id: entry.id,
        icon: eventIcons[entry.type],
        event: describeEvent(entry, { stageName, followUpName: () => 'a follow-up questionnaire', jobName }),
        actor: inJob ? (who ? `${who} in ${item.jobTitle}` : `in ${item.jobTitle}`) : (who ?? undefined),
        note: entry.note ?? undefined,
        // A team note can mention a member by id, so the page draws it as the team reads it, not as a quote.
        noteId: entry.type === 'NoteAdded' ? (entry.noteId ?? undefined) : undefined,
        at: entry.occurredAt,
      };
    });
}

/** When the candidate asked to be removed from Saved candidates through the link in an invitation, if they did. */
export const askedToBeRemovedAt = (history: readonly CandidateHistoryEntry[] | undefined): string | undefined =>
  history?.find(({ entry }) => entry.type === 'LeftTalentPool' && entry.poolExit === 'CandidateAsked')?.entry.occurredAt;
