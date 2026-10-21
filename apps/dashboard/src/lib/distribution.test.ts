import type { JobPostingStatus } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { board, now, posting } from '../test-support/fixtures.js';
import { boardAction, boardRows, describePosting } from './distribution.js';

const later = (ms: number) => new Date(now + ms).toISOString();

describe('describePosting', () => {
  const cases: [JobPostingStatus['state'], Partial<JobPostingStatus>, string, string][] = [
    ['Live', {}, 'Live', 'circle'],
    ['Queued', { expectedBy: later(20_000) }, 'Queued · ~20s', 'diamond'],
    ['Submitted', { expectedBy: later(90_000) }, 'Submitted · ~2m', 'diamond'],
    ['Submitted', { confirmsLive: false, expectedBy: later(86_400_000) }, 'Sent · ~1d', 'diamond'],
    ['Submitted', { confirmsLive: false }, 'Sent', 'diamond'],
    ['Closing', { expectedBy: later(30_000) }, 'Removing · ~30s', 'diamond'],
    ['Closing', { confirmsRemoval: false }, 'Removal requested', 'diamond'],
    ['Closed', {}, 'Removed', 'ring'],
    ['Rejected', { lastError: 'Title too long.' }, 'Rejected', 'square'],
    ['Error', {}, 'Error', 'square'],
    ['Draft', { issues: ['The description needs 100 characters.'] }, 'Needs fixes', 'square'],
  ];

  it.each(cases)('%s %o reads %s with a %s mark', (state, overrides, label, shape) => {
    const view = describePosting(posting('board', 'Board', { state, ...overrides }), now);

    expect(view.label).toBe(label);
    expect(view.shape).toBe(shape);
  });

  it('says an estimate has passed instead of counting below zero', () => {
    expect(describePosting(posting('sandbox', 'Sandbox', { state: 'Queued', expectedBy: later(-5_000) }), now).label).toBe('Queued · overdue');
  });

  it("explains a refusal with the board's reason and fixes with the board's issues", () => {
    expect(describePosting(posting('a', 'A', { state: 'Rejected', lastError: 'Title too long.' }), now).detail).toContain('Title too long.');
    expect(describePosting(posting('a', 'A', { state: 'Draft', issues: ['Needs a salary.'] }), now).detail).toContain('Needs a salary.');
  });

  it('reads "not posted" for a board the job is not on', () => {
    expect(describePosting(undefined, now)).toMatchObject({ label: 'Not posted', shape: 'ring' });
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
