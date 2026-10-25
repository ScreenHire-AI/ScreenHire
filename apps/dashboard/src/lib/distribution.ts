import type { BoardSummary, JobPostingStatus, JobStatus } from '@screenhire/api-client';
import { eta, timeUntil } from '#lib/dates.js';
import type { StatusName } from '#lib/status.js';

export interface PostingView {
  /**
   * The state's status (design system section 3.2): live is open, a board still to act is waiting (grey, nobody needs to
   * act), a refused or failed posting needs fixes, and a board the job is not on is not in effect.
   */
  status: StatusName;
  /** The state: `Live`, `Queued`. */
  label: string;
  /** The board's estimate, for a fact pill: `~20s`, `Overdue`; null when the board gives none. */
  eta: string | null;
  /** The state in a sentence, for screen readers and the tooltip, including what is still uncertain. */
  detail: string;
}

/** What the person can do on one board now. Closing the whole job is the header's job, not a board's. */
export type BoardAction = 'post' | 'retry' | 'takeDown' | null;

function etaOf(expectedBy: string | null, now: number): string | null {
  if (!expectedBy) return null;
  return Date.parse(expectedBy) > now ? eta(expectedBy, now) : 'Overdue';
}

function estimateSentence(expectedBy: string | null, now: number, what: string): string {
  if (!expectedBy) return ` The board gives no estimate for when it ${what}.`;
  return Date.parse(expectedBy) > now
    ? ` Expected in ${timeUntil(expectedBy, now, 'long')}.`
    : ` The board's estimate has passed; we keep checking.`;
}

/**
 * How one board's posting reads in the Distribution panel. It never claims more than the board confirms: a board that
 * does not report back stays "sent" after a publish and "removal requested" after a close, never "live" or "removed"
 * (docs/architecture/job-distribution.md, "What each state means").
 */
export function describePosting(posting: JobPostingStatus | undefined, now: number): PostingView {
  if (!posting) {
    return { status: 'notInEffect', label: 'Not posted', eta: null, detail: 'Not posted to this board.' };
  }

  const { state, expectedBy, confirmsLive, confirmsRemoval } = posting;
  switch (state) {
    case 'Live':
      return { status: 'open', label: 'Live', eta: null, detail: 'Live: the board confirmed the job shows.' };
    case 'Queued':
      return {
        status: 'waiting',
        label: 'Queued',
        eta: etaOf(expectedBy, now),
        detail: `Queued: waiting to be sent to the board.${estimateSentence(expectedBy, now, 'takes it')}`,
      };
    case 'Submitted':
      return confirmsLive
        ? {
            status: 'waiting',
            label: 'Submitted',
            eta: etaOf(expectedBy, now),
            detail: `Submitted: the board took it and has not confirmed it shows yet.${estimateSentence(expectedBy, now, 'shows it')}`,
          }
        : {
            status: 'waiting',
            label: 'Sent',
            eta: etaOf(expectedBy, now),
            detail: `Sent: this board never confirms when a job shows.${estimateSentence(expectedBy, now, 'picks it up')}`,
          };
    case 'Closing':
      return confirmsRemoval
        ? {
            status: 'waiting',
            label: 'Removing',
            eta: etaOf(expectedBy, now),
            detail: `Removing: the board has not confirmed the job is gone yet.${estimateSentence(expectedBy, now, 'removes it')}`,
          }
        : {
            status: 'waiting',
            label: 'Removal requested',
            eta: etaOf(expectedBy, now),
            detail: `Removal requested: this board never confirms removal, so the job may still show until it refreshes.${estimateSentence(expectedBy, now, 'refreshes')}`,
          };
    case 'Closed':
      return { status: 'notInEffect', label: 'Removed', eta: null, detail: 'Removed: the board confirmed the job is gone, or it never reached the board.' };
    case 'Rejected':
      return {
        status: 'needsFixes',
        label: 'Needs fixes',
        eta: null,
        detail: `Rejected by the board${posting.lastError ? `: ${posting.lastError}` : '.'}`,
      };
    case 'Error':
      return {
        status: 'needsFixes',
        label: 'Error',
        eta: null,
        detail: `The board did not answer${posting.lastError ? `: ${posting.lastError}` : '.'} Retry to send it again.`,
      };
    case 'Draft':
      return {
        status: 'needsFixes',
        label: 'Needs fixes',
        eta: null,
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

/**
 * The Distribution tab's count (design system E2, "Distribution 7/8"): the boards the job is on or on its way to, out of
 * every board listed. A pull board never confirms it shows the job, so a posting sent there counts as posted.
 */
export function postingCounts(rows: readonly BoardRow[]): { posted: number; total: number } {
  return { posted: rows.filter((row) => row.posting && active.has(row.posting.state)).length, total: rows.length };
}

/** A problem with the job's postings, for a notice on the job page: what is wrong, naming each board, in words. */
export interface PostingProblem {
  kind: 'notTaken' | 'refused' | 'noAnswer';
  boards: string[];
  text: string;
}

const boardList = new Intl.ListFormat('en', { type: 'conjunction' });

/**
 * The postings that need fixes, one problem per kind, each naming every board it covers. The words never claim more than
 * happened (docs/architecture/job-distribution.md): a posting whose checks found issues was never sent, so the board did
 * not refuse it; only a board's own answer is a refusal, and a push board that did not answer is neither.
 */
export function postingProblems(rows: readonly BoardRow[]): PostingProblem[] {
  const of = (state: JobPostingStatus['state']) => rows.filter((row) => row.posting?.state === state);
  const problems: PostingProblem[] = [];
  const notTaken = of('Draft');
  if (notTaken.length > 0) {
    const issues = notTaken.flatMap((row) => row.posting!.issues);
    problems.push({
      kind: 'notTaken',
      boards: notTaken.map((row) => row.name),
      text: `${boardList.format(notTaken.map((row) => row.name))} did not take this job.${issues.length > 0 ? ` ${issues.join(' ')}` : ''}`,
    });
  }

  const refused = of('Rejected');
  if (refused.length > 0) {
    const reasons = refused.flatMap((row) => (row.posting!.lastError ? [`${row.name}: ${row.posting!.lastError}`] : []));
    problems.push({
      kind: 'refused',
      boards: refused.map((row) => row.name),
      text: `${boardList.format(refused.map((row) => row.name))} refused this job's posting.${reasons.length > 0 ? ` ${reasons.join(' ')}` : ''}`,
    });
  }

  const noAnswer = of('Error');
  if (noAnswer.length > 0) {
    problems.push({
      kind: 'noAnswer',
      boards: noAnswer.map((row) => row.name),
      text: `${boardList.format(noAnswer.map((row) => row.name))} did not answer. Retry to send it again.`,
    });
  }

  return problems;
}
