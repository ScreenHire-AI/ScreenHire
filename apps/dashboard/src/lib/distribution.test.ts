import type { JobPostingStatus } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { board, now, posting } from '../test-support/fixtures.js';
import { boardAction, boardRows, describePosting, postingCounts, postingProblems } from './distribution.js';

const later = (ms: number) => new Date(now + ms).toISOString();

describe('describePosting', () => {
  const cases: [JobPostingStatus['state'], Partial<JobPostingStatus>, string, string | null, string][] = [
    ['Live', {}, 'Live', null, 'open'],
    ['Queued', { expectedBy: later(20_000) }, 'Queued', '~20s', 'waiting'],
    ['Submitted', { expectedBy: later(90_000) }, 'Submitted', '~2m', 'waiting'],
    ['Submitted', { confirmsLive: false, expectedBy: later(86_400_000) }, 'Sent', '~1d', 'waiting'],
    ['Submitted', { confirmsLive: false }, 'Sent', null, 'waiting'],
    ['Closing', { expectedBy: later(30_000) }, 'Removing', '~30s', 'waiting'],
    ['Closing', { confirmsRemoval: false }, 'Removal requested', null, 'waiting'],
    ['Closed', {}, 'Removed', null, 'notInEffect'],
    ['Rejected', { lastError: 'Title too long.' }, 'Needs fixes', null, 'needsFixes'],
    ['Error', {}, 'Error', null, 'needsFixes'],
    ['Draft', { issues: ['The description needs 100 characters.'] }, 'Needs fixes', null, 'needsFixes'],
  ];

  it.each(cases)('%s %o reads %s, estimate %s, as a %s status', (state, overrides, label, eta, status) => {
    const view = describePosting(posting('board', 'Board', { state, ...overrides }), now);

    expect(view).toMatchObject({ label, eta, status });
  });

  it('says an estimate has passed instead of counting below zero', () => {
    expect(describePosting(posting('sandbox', 'Sandbox', { state: 'Queued', expectedBy: later(-5_000) }), now).eta).toBe('Overdue');
  });

  it("explains a refusal with the board's reason and fixes with the board's issues", () => {
    expect(describePosting(posting('a', 'A', { state: 'Rejected', lastError: 'Title too long.' }), now).detail).toContain('Title too long.');
    expect(describePosting(posting('a', 'A', { state: 'Draft', issues: ['Needs a salary.'] }), now).detail).toContain('Needs a salary.');
  });

  it('reads "not posted" for a board the job is not on', () => {
    expect(describePosting(undefined, now)).toMatchObject({ label: 'Not posted', status: 'notInEffect' });
  });
});

describe('boardAction', () => {
  const perBoard = { canUnpublishPerBoard: true };
  const wholeJobOnly = { canUnpublishPerBoard: false };

  it('offers nothing per board on a draft or a closed job', () => {
    expect(boardAction('Draft', perBoard, undefined)).toBeNull();
    expect(boardAction('Closed', perBoard, posting('a', 'A', { state: 'Closing' }))).toBeNull();
  });

  it('posts an open job to a board it is not on, or was taken down from', () => {
    expect(boardAction('Open', perBoard, undefined)).toBe('post');
    expect(boardAction('Open', perBoard, posting('a', 'A', { state: 'Closed' }))).toBe('post');
  });

  it('retries a posting the board refused or never answered', () => {
    expect(boardAction('Open', perBoard, posting('a', 'A', { state: 'Error' }))).toBe('retry');
    expect(boardAction('Open', perBoard, posting('a', 'A', { state: 'Rejected' }))).toBe('retry');
  });

  it('takes down only from boards that can drop one job while it stays open', () => {
    expect(boardAction('Open', perBoard, posting('a', 'A', { state: 'Live' }))).toBe('takeDown');
    expect(boardAction('Open', wholeJobOnly, posting('careers', 'Careers', { state: 'Live' }))).toBeNull();
    expect(boardAction('Open', perBoard, posting('a', 'A', { state: 'Closing' }))).toBeNull();
  });
});

describe('boardRows', () => {
  it("keeps the API's board order and adds boards a posting is still on after their configuration went", () => {
    const rows = boardRows([board('careers', 'Careers page'), board('sandbox', 'Sandbox board')], [
      posting('sandbox', 'Sandbox board'),
      posting('retired', 'Retired board', { state: 'Closing', canUnpublish: false }),
    ]);

    expect(rows.map((row) => [row.boardId, row.posting?.state ?? null])).toEqual([
      ['careers', null],
      ['sandbox', 'Live'],
      ['retired', 'Closing'],
    ]);
  });
});

describe('postingCounts', () => {
  it('counts the boards a posting is on or on its way to, out of every board', () => {
    const boards = [board('careers', 'Careers page'), board('adzuna', 'Adzuna'), board('jooble', 'Jooble'), board('sandbox', 'Sandbox board')];
    const rows = boardRows(boards, [
      posting('careers', 'Careers page', { state: 'Live' }),
      posting('adzuna', 'Adzuna', { state: 'Draft', issues: ['Adzuna needs a description of at least 100 characters.'] }),
      posting('jooble', 'Jooble', { state: 'Submitted', confirmsLive: false }),
    ]);

    expect(postingCounts(rows)).toEqual({ posted: 2, total: 4 });
  });
});

describe('postingProblems', () => {
  const rowsWith = (...postings: JobPostingStatus[]) => boardRows(postings.map((p) => board(p.boardId, p.boardName)), postings);

  it('says a posting whose checks found issues was not taken, with the issues, never that the board refused it', () => {
    const problems = postingProblems(rowsWith(posting('adzuna', 'Adzuna', { state: 'Draft', issues: ['Adzuna needs a description of at least 100 characters.'] })));

    expect(problems).toEqual([
      { kind: 'notTaken', boards: ['Adzuna'], text: 'Adzuna did not take this job. Adzuna needs a description of at least 100 characters.' },
    ]);
  });

  it("names every board a refusal covers, with each board's reason", () => {
    const problems = postingProblems(
      rowsWith(
        posting('adzuna', 'Adzuna', { state: 'Rejected', lastError: 'Title too long.' }),
        posting('jooble', 'Jooble', { state: 'Rejected', lastError: null }),
        posting('careers', 'Careers page', { state: 'Live' }),
      ),
    );

    expect(problems).toEqual([
      { kind: 'refused', boards: ['Adzuna', 'Jooble'], text: "Adzuna and Jooble refused this job's posting. Adzuna: Title too long." },
    ]);
  });

  it('says a board that did not answer did not answer', () => {
    expect(postingProblems(rowsWith(posting('sandbox', 'Sandbox board', { state: 'Error' })))).toEqual([
      { kind: 'noAnswer', boards: ['Sandbox board'], text: 'Sandbox board did not answer. Retry to send it again.' },
    ]);
  });

  it('finds nothing when every posting is fine', () => {
    expect(postingProblems(rowsWith(posting('careers', 'Careers page', { state: 'Live' }), posting('jooble', 'Jooble', { state: 'Closing' })))).toEqual([]);
  });
});
