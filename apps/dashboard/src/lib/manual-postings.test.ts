import type { ManualPostingStatus } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  applicationsBySource,
  copyFields,
  describeManualPosting,
  isUsableKey,
  localToday,
  needsTakingDown,
  plainText,
  salaryText,
  sourceKeyFor,
  trackedUrl,
} from './manual-postings.js';

const posting = (overrides: Partial<ManualPostingStatus> = {}): ManualPostingStatus => ({
  id: 'p1',
  boardName: 'LinkedIn',
  sourceKey: 'linkedin',
  trackedUrl: 'https://acme.careers.example.com/jobs/x?src=linkedin',
  listingUrl: null,
  postedOn: '2026-11-01',
  endsOn: null,
  contentAt: '2026-11-01T09:00:00Z',
  takenDownAt: null,
  changedFields: [],
  createdAt: '2026-11-01T09:00:00Z',
  updatedAt: '2026-11-01T09:00:00Z',
  ...overrides,
});

describe('sourceKeyFor', () => {
  // The same cases as ManualPostingTests.Source_key_for_a_board_name on the server: the key the dashboard sends is the one
  // the server would make, so a link copied before saving counts once the posting is saved.
  it.each([
    ['LinkedIn', 'linkedin'],
    ['Riverside Jobs Weekly', 'riverside-jobs-weekly'],
    ['  Indeed (free listing)  ', 'indeed-free-listing'],
    ['Café Emplois', 'cafe-emplois'],
    ['Talent.com', 'talent-com'],
    ['***', ''],
  ])('makes %j into %j', (name, key) => {
    expect(sourceKeyFor(name)).toBe(key);
  });

  it('cuts a long name to 40 characters without a trailing hyphen', () => {
    const key = sourceKeyFor('A very long board name that goes on and on for ever');
    expect(key.length).toBeLessThanOrEqual(40);
    expect(key.endsWith('-')).toBe(false);
  });
});

describe('isUsableKey', () => {
  it("refuses an empty key and a board ScreenHire posts to itself", () => {
    expect(isUsableKey('', ['careers'])).toBe(false);
    expect(isUsableKey('adzuna', ['careers', 'adzuna'])).toBe(false);
    expect(isUsableKey('linkedin', ['careers', 'adzuna'])).toBe(true);
  });
});

describe('trackedUrl', () => {
  it('adds src to the careers address, replacing one already there', () => {
    expect(trackedUrl('https://acme.careers.example.com/jobs/x', 'linkedin')).toBe('https://acme.careers.example.com/jobs/x?src=linkedin');
    expect(trackedUrl('https://acme.careers.example.com/jobs/x?src=old&a=1', 'indeed')).toBe('https://acme.careers.example.com/jobs/x?src=indeed&a=1');
  });
});

describe('plainText', () => {
  it('keeps headings, paragraphs and lists as lines, with bullets and numbers', () => {
    const html = '<h2>About the role</h2><p>You will <strong>build</strong> things.<br>Remote is fine.</p><ul><li>C#</li><li>Postgres</li></ul><ol><li>Apply</li><li>Talk</li></ol>';
    expect(plainText(html)).toBe('About the role\n\nYou will build things.\nRemote is fine.\n\n- C#\n- Postgres\n\n1. Apply\n2. Talk');
  });

  it('keeps the words of a link and drops its address', () => {
    expect(plainText('<p>See <a href="https://example.com">our site</a>.</p>')).toBe('See our site.');
  });
});

describe('salaryText', () => {
  it('writes a range, a minimum and a maximum in full words for the period', () => {
    expect(salaryText({ min: 70000, max: 85000, currency: 'GBP', period: 'Year' })).toMatch(/^£70,000 to £85,000 a year$/);
    expect(salaryText({ min: 30000, max: null, currency: 'GBP', period: 'Year' })).toMatch(/^From £30,000 a year$/);
    expect(salaryText({ min: null, max: 14, currency: 'USD', period: 'Hour' })).toMatch(/^Up to \$14 an hour$/);
  });
});

describe('copyFields', () => {
  it('offers each field a listing needs, and leaves out a salary the job does not have', () => {
    const fields = copyFields({ title: 'Engineer', location: 'Leeds, United Kingdom', employmentType: 'PartTime', salary: null, description: '<p>Hi.</p>' });
    expect(fields.map((field) => [field.field, field.text])).toEqual([
      ['Title', 'Engineer'],
      ['Location', 'Leeds, United Kingdom'],
      ['Type', 'Part-time'],
      ['Description', 'Hi.'],
    ]);
  });
});

describe('needsTakingDown and describeManualPosting', () => {
  const today = '2026-11-22';

  it('reads Posted by you while the job is open and the end date has not passed', () => {
    expect(describeManualPosting(posting({ endsOn: '2026-11-22' }), 'Open', today)).toMatchObject({ status: 'open', label: 'Posted by you' });
  });

  it('reads Still posted by you once the job is closed or the end date has passed', () => {
    expect(needsTakingDown(posting(), 'Closed', today)).toBe(true);
    expect(needsTakingDown(posting({ endsOn: '2026-11-21' }), 'Open', today)).toBe(true);
    expect(describeManualPosting(posting({ endsOn: '2026-11-21' }), 'Open', today)).toMatchObject({ status: 'stillPosted', label: 'Still posted by you' });
  });

  it('reads Taken down once marked, whatever the job', () => {
    expect(needsTakingDown(posting({ takenDownAt: '2026-11-20T10:00:00Z' }), 'Closed', today)).toBe(false);
    expect(describeManualPosting(posting({ takenDownAt: '2026-11-20T10:00:00Z' }), 'Closed', today)).toMatchObject({ status: 'notInEffect', label: 'Taken down' });
  });
});

describe('localToday', () => {
  it("is the person's own calendar day", () => {
    expect(localToday(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05');
  });
});

describe('applicationsBySource', () => {
  it('counts each link, zero for one with none, and nothing for the boards that send candidates to the careers page', () => {
    const count = applicationsBySource([
      { source: 'careers', applications: 4 },
      { source: 'linkedin', applications: 2 },
    ]);
    expect(count('careers')).toBe(4);
    expect(count('linkedin')).toBe(2);
    expect(count('adzuna')).toBe(0);
    expect(count('google')).toBeNull();
    expect(count('job-board-api')).toBeNull();
  });
});
