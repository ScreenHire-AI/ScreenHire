import type { BoardSummary, JobPostingStatus, JobStatus } from '@screenhire/api-client';
import { eta } from '#lib/format.js';

/** A status mark's shape (design-system section 2.2): circle done, diamond waiting, square needs a person, ring none. */
export type MarkShape = 'circle' | 'diamond' | 'square' | 'ring';
/**
 * Status tones, plus `primary` for marks that are not a status, such as the "New today" view, and `ai` for AI
 * assistance, which is information rather than a status (design-system section 2.2).
 */
export type MarkTone = 'clear' | 'review' | 'verify' | 'neutral' | 'primary' | 'ai';

export interface PostingView {
  shape: MarkShape;
  tone: MarkTone;
  /** The mono state, upper case, with the board's estimate when there is one: `LIVE`, `QUEUED · ~20S`. */
  label: string;
  /** The same in a sentence, for screen readers and the tooltip, including what is still uncertain. */
  detail: string;
}

/** What the person can do on one board now. Closing the whole job is the header's job, not a board's. */
export type BoardAction = 'post' | 'retry' | 'takeDown' | null;

function withEta(label: string, expectedBy: string | null, now: number): string {
  if (!expectedBy) return label;
  return Date.parse(expectedBy) > now ? `${label} · ${eta(expectedBy, now).toUpperCase()}` : `${label} · OVERDUE`;
}

function estimateSentence(expectedBy: string | null, now: number, what: string): string {
  if (!expectedBy) return ` The board gives no estimate for when it ${what}.`;
  return Date.parse(expectedBy) > now
    ? ` Expected in ${eta(expectedBy, now).slice(1)}.`
    : ` The board's estimate has passed; we keep checking.`;
}

/**
 * How one board's posting reads in the distribution strip. It never claims more than the board confirms: a board that
 * does not report back stays "sent" after a publish and "removal requested" after a close, never "live" or "removed"
 * (docs/architecture/job-distribution.md, "What each state means").
 */
export function describePosting(posting: JobPostingStatus | undefined, now: number): PostingView {
  if (!posting) {
    return { shape: 'ring', tone: 'neutral', label: 'NOT POSTED', detail: 'Not posted to this board.' };
  }

  const { state, expectedBy, confirmsLive, confirmsRemoval } = posting;
  switch (state) {
    case 'Live':
      return { shape: 'circle', tone: 'clear', label: 'LIVE', detail: 'Live: the board confirmed the job shows.' };
    case 'Queued':
      return {
        shape: 'diamond',
        tone: 'review',
        label: withEta('QUEUED', expectedBy, now),
        detail: `Queued: waiting to be sent to the board.${estimateSentence(expectedBy, now, 'takes it')}`,
      };
    case 'Submitted':
      return confirmsLive
        ? {
            shape: 'diamond',
            tone: 'review',
            label: withEta('SUBMITTED', expectedBy, now),
            detail: `Submitted: the board took it and has not confirmed it shows yet.${estimateSentence(expectedBy, now, 'shows it')}`,
          }
        : {
            shape: 'diamond',
            tone: 'review',
            label: withEta('SENT', expectedBy, now),
            detail: `Sent: this board never confirms when a job shows.${estimateSentence(expectedBy, now, 'picks it up')}`,
          };
    case 'Closing':
      return confirmsRemoval
        ? {
            shape: 'diamond',
            tone: 'review',
            label: withEta('REMOVING', expectedBy, now),
            detail: `Removing: the board has not confirmed the job is gone yet.${estimateSentence(expectedBy, now, 'removes it')}`,
          }
        : {
            shape: 'diamond',
            tone: 'review',
            label: withEta('REMOVAL REQUESTED', expectedBy, now),
            detail: `Removal requested: this board never confirms removal, so the job may still show until it refreshes.${estimateSentence(expectedBy, now, 'refreshes')}`,
          };
    case 'Closed':
      return { shape: 'ring', tone: 'neutral', label: 'REMOVED', detail: 'Removed: the board confirmed the job is gone, or it never reached the board.' };
    case 'Rejected':
      return {
        shape: 'square',
        tone: 'verify',
        label: 'REJECTED',
        detail: `Rejected by the board${posting.lastError ? `: ${posting.lastError}` : '.'}`,
      };
    case 'Error':
      return {
        shape: 'square',
        tone: 'verify',
        label: 'ERROR',
        detail: `The board did not answer${posting.lastError ? `: ${posting.lastError}` : '.'} Retry to send it again.`,
      };
    case 'Draft':
      return {
        shape: 'square',
        tone: 'verify',
        label: 'NEEDS FIXES',
        detail: `Not sent: ${posting.issues.length > 0 ? posting.issues.join(' ') : 'the board found problems with the job.'}`,
      };
  }
}

/** Postings that are on a board, or on their way there. */
const active = new Set<JobPostingStatus['state']>(['Queued', 'Submitted', 'Live']);

/**
 * The one action a board offers. Only an open job is posted per board (a draft is published from the header, to every
 * board at once); a board that cannot drop one job alone (the careers page, Google) offers no take-down, because only
 * closing the job removes it there.
 */
export function boardAction(jobStatus: JobStatus, board: Pick<BoardSummary, 'canUnpublishPerBoard'>, posting: JobPostingStatus | undefined): BoardAction {
  if (jobStatus !== 'Open') return null;
  if (!posting || posting.state === 'Closed') return 'post';
  if (posting.state === 'Error' || posting.state === 'Rejected' || posting.state === 'Draft') return 'retry';
  if (active.has(posting.state) && board.canUnpublishPerBoard) return 'takeDown';
  return null;
}

export interface BoardRow {
  boardId: string;
  name: string;
  board: Pick<BoardSummary, 'canUnpublishPerBoard'>;
  posting: JobPostingStatus | undefined;
}

/**
 * One row per board the organization may use, in the API's order, plus any board a posting is on that is no longer
 * offered (its configuration was removed), so nothing the job is still on disappears from view.
 */
export function boardRows(boards: readonly BoardSummary[], postings: readonly JobPostingStatus[]): BoardRow[] {
  const byBoard = new Map(postings.map((posting) => [posting.boardId, posting]));
  const rows: BoardRow[] = boards.map((board) => ({ boardId: board.id, name: board.name, board, posting: byBoard.get(board.id) }));
  for (const posting of postings) {
    if (!boards.some((board) => board.id === posting.boardId)) {
      rows.push({ boardId: posting.boardId, name: posting.boardName, board: { canUnpublishPerBoard: posting.canUnpublish }, posting });
    }
  }

  return rows;
}
