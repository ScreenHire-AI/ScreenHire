import type { ApplicationTaskEntry, ApplicationTimelineEntry, ScorecardEntry, ScorecardTemplateSummary, TeamMember } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { askedFor, type ScorecardChoice, type ScorecardRequest } from './scorecard-choices.js';
import { readable, rosterRows, roleOf, scorecardTitle, waitingRows } from './scorecard-roster.js';

const me = '01a157d4-0000-7000-8000-0000000000a1';
const robin = '01a157d4-0000-7000-8000-0000000000a2';
const members = [
  { userId: me, displayName: 'Avery Quinn', roleName: 'Owner' },
  { userId: robin, displayName: 'Robin Ashby', roleName: 'Interviewer' },
] as TeamMember[];
const stageName = (id: string) => (id === 'interview' ? 'Interview' : id);

const given = (overrides: Partial<ScorecardEntry> = {}) =>
  ({
    id: crypto.randomUUID(),
    stageId: 'interview',
    reviewerUserId: robin,
    answers: [],
    rating: 4,
    ratings: [],
    recommendation: 'Yes',
    note: null,
    submittedAt: '2026-11-04T10:00:00Z',
    scorecardTemplateId: 'interview',
    scorecardName: 'Team interview',
    withheld: false,
    ...overrides,
  }) as ScorecardEntry;

describe('rosterRows', () => {
  it('names the reviewer only, "You" for the reader, and ranks the recommendation for sorting', () => {
    const rows = rosterRows([given({ reviewerUserId: me, recommendation: 'StrongYes' }), given({ recommendation: 'StrongNo' })], { stageName, members, currentUserId: me });

    expect(rows.map((row) => row.reviewer)).toEqual(['You', 'Robin Ashby']);
    expect(rows.map((row) => row.recommendationRank)).toEqual([3, 0]);
    expect(rows[0]).toMatchObject({ stage: 'Interview', rating: 4 });
  });

  it('leaves a withheld scorecard without a rank or a rating, so its cells are blank and it sorts last', () => {
    const [row] = rosterRows([given({ withheld: true, recommendation: null, rating: null })], { stageName, members, currentUserId: me });

    expect(row!.recommendationRank).toBeUndefined();
    expect(row!.rating).toBeUndefined();
    expect(row!.reviewer).toBe('Robin Ashby');
  });
});

describe('readable and titles', () => {
  it('steps only through the scorecards that can be read, in the table order', () => {
    const rows = rosterRows([given({ id: 'a' }), given({ id: 'b', withheld: true, recommendation: null }), given({ id: 'c' })], { stageName, members, currentUserId: me });

    expect(readable(rows).map((row) => row.id)).toEqual(['a', 'c']);
  });

  it("titles a sheet with the scorecard's name, or its stage's when it predates names", () => {
    expect(scorecardTitle(given(), stageName)).toBe('Team interview scorecard');
    expect(scorecardTitle(given({ scorecardName: 'Interview scorecard' }), stageName)).toBe('Interview scorecard');
    expect(scorecardTitle(given({ scorecardName: null }), stageName)).toBe('Interview scorecard');
  });

  it("takes the reviewer's role from the team, and none for someone who left", () => {
    expect(roleOf(robin, members)).toBe('Interviewer');
    expect(roleOf('01a157d4-0000-7000-8000-0000000000ff', members)).toBe('');
  });
});

describe('waitingRows', () => {
  const task = (overrides: Partial<ApplicationTaskEntry>) =>
    ({
      id: crypto.randomUUID(),
      applicationId: 'app',
      kind: 'Scorecard',
      note: 'Fill in the Interview scorecard.',
      assigneeUserId: robin,
      createdAt: '2026-11-05T09:00:00Z',
      completedAt: null,
      completedByUserId: null,
      scorecardTemplateId: 'interview',
      ...overrides,
    }) as ApplicationTaskEntry;
  const library = [{ id: 'interview', name: 'Team interview' }] as ScorecardTemplateSummary[];

  it('lists the open requested scorecards, oldest first, with who asked from the timeline', () => {
    const first = task({ id: 'first' });
    const later = task({ id: 'later', createdAt: '2026-11-06T09:00:00Z', scorecardTemplateId: 'unknown' });
    const timeline = [{ type: 'TaskCreated', taskId: 'first', actorUserId: me }] as ApplicationTimelineEntry[];

    const rows = waitingRows([later, first], timeline, library);

    expect(rows.map((row) => row.taskId)).toEqual(['first', 'later']);
    expect(rows[0]).toMatchObject({ assigneeUserId: robin, scorecard: 'Team interview scorecard', askedByUserId: me });
    // A request the timeline has no actor for (a key or a workflow) names no one.
    expect(rows[1]!.askedByUserId).toBeNull();
  });

  it('leaves out reviews, to-dos, finished requests and requests for no one in particular', () => {
    const rows = waitingRows(
      [
        task({ kind: 'Review' }),
        task({ kind: 'Task' }),
        task({ completedAt: '2026-11-06T09:00:00Z' }),
        task({ assigneeUserId: null }),
      ],
      [],
      library,
    );

    expect(rows).toEqual([]);
  });
});

describe('askedFor', () => {
  const request = (overrides: Partial<ScorecardRequest>): ScorecardRequest => ({ kind: 'Scorecard', scorecardTemplateId: 'interview', stageId: null, askedAt: '2026-11-05T09:00:00Z', askedBy: 'Avery Quinn', ...overrides });
  const stageChoice = { value: 'stage:interview', stageId: 'interview', key: 'interview' } as ScorecardChoice;
  const libraryChoice = { value: 'library:phone-screen', key: 'phone-screen' } as ScorecardChoice;

  it("matches a requested scorecard to the choice that is that scorecard, the stage's copy of it included", () => {
    expect(askedFor(stageChoice, [request({})])?.askedBy).toBe('Avery Quinn');
    expect(askedFor(libraryChoice, [request({ scorecardTemplateId: 'phone-screen', askedBy: 'Robin Ashby' })])?.askedBy).toBe('Robin Ashby');
    expect(askedFor(libraryChoice, [request({})])).toBeUndefined();
  });

  it('matches a review to a stage choice only, since any scorecard of theirs finishes it', () => {
    const review = request({ kind: 'Review', scorecardTemplateId: null, askedBy: undefined });

    expect(askedFor(stageChoice, [review])).toBe(review);
    expect(askedFor(libraryChoice, [review])).toBeUndefined();
    expect(askedFor(undefined, [review])).toBeUndefined();
  });

  it("matches a review for a stage only to that stage's scorecard", () => {
    const review = request({ kind: 'Review', scorecardTemplateId: null, stageId: 'interview', askedBy: undefined });
    const screening = { value: 'stage:screening', stageId: 'screening', key: 'screening' } as ScorecardChoice;

    expect(askedFor(stageChoice, [review])).toBe(review);
    expect(askedFor(screening, [review])).toBeUndefined();
  });
});
