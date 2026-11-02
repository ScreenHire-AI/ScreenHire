import type { ApplicationSummary, ApplicationTimelineEntry, CandidateHistoryEntry, TeamMember } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { eventIcons } from '#lib/activity.js';
import { askedToBeRemovedAt, historyItems, inTimeOrder, progressOf } from '#lib/candidate-profile.js';

const me = '01a157d4-0000-7000-8000-0000000000aa';
const robin = '01a157d4-0000-7000-8000-0000000000cc';
const members: TeamMember[] = [
  { id: 'm1', userId: robin, email: 'robin@brightwater.example.com', displayName: 'Robin Ashby', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-02T09:00:00Z', jobAccess: 'All', seesAllJobs: true, jobCount: 0 },
];

type StageKind = ApplicationSummary['stage']['kind'];

function application(id: string, submittedAt: string, stage: { name: string; kind: StageKind }, overrides: Partial<ApplicationSummary> = {}): ApplicationSummary {
  return {
    id,
    jobId: `job-${id}`,
    candidateId: 'candidate-1',
    candidateName: 'Casey Morgan',
    stage: { id: stage.name.toLowerCase(), ...stage },
    submittedAt,
    stageEnteredAt: submittedAt,
    lastActivityAt: submittedAt,
    jobTitle: `Job ${id}`,
    source: 'CareersSite',
    tags: [],
    ...overrides,
  };
}

function entry(overrides: Partial<ApplicationTimelineEntry>): ApplicationTimelineEntry {
  return {
    id: crypto.randomUUID(),
    type: 'Submitted',
    occurredAt: '2026-10-20T09:00:00Z',
    actorUserId: null,
    fromStageId: null,
    toStageId: null,
    note: null,
    tag: null,
    automationRunId: null,
    actorApiKeyId: null,
    scorecardId: null,
    stageId: null,
    recommendation: null,
    ...overrides,
  };
}

const item = (applicationId: string, jobTitle: string, e: ApplicationTimelineEntry, names: Partial<CandidateHistoryEntry> = {}): CandidateHistoryEntry => ({
  applicationId,
  jobId: `job-${applicationId}`,
  jobTitle,
  entry: e,
  ...names,
});

describe('inTimeOrder', () => {
  it('sorts applications oldest first by when they were made, not by the order the API sent', () => {
    const newest = application('c', '2026-10-20T12:00:00Z', { name: 'Applied', kind: 'Applied' });
    const oldest = application('a', '2025-09-01T12:00:00Z', { name: 'Applied', kind: 'Applied' });
    const middle = application('b', '2026-03-01T12:00:00Z', { name: 'Applied', kind: 'Applied' });

    expect(inTimeOrder([newest, oldest, middle]).map((a) => a.id)).toEqual(['a', 'b', 'c']);
  });

  it('compares instants, so an offset does not change the order', () => {
    // 10:00 at +05:00 is 05:00 UTC, earlier than 08:00 UTC although "10" sorts after "08" as text.
    const earlier = application('early', '2026-10-20T10:00:00+05:00', { name: 'Applied', kind: 'Applied' });
    const later = application('late', '2026-10-20T08:00:00Z', { name: 'Applied', kind: 'Applied' });

    expect(inTimeOrder([later, earlier]).map((a) => a.id)).toEqual(['early', 'late']);
  });

  it('does not change the list it is given', () => {
    const input = [application('b', '2026-03-01T12:00:00Z', { name: 'Applied', kind: 'Applied' }), application('a', '2025-09-01T12:00:00Z', { name: 'Applied', kind: 'Applied' })];

    inTimeOrder(input);

    expect(input.map((a) => a.id)).toEqual(['b', 'a']);
  });
});

describe('progressOf', () => {
  it('names the current stage for an application that is still open, with no end date', () => {
    const open = application('open', '2026-10-01T12:00:00Z', { name: 'Interview', kind: 'Interview' });

    expect(progressOf(open, [])).toEqual({ outcome: 'Interview' });
  });

  it('reads how far a hired application got from the stage before its last move, and ends it when it entered Hired', () => {
    const hired = application('h', '2026-10-01T12:00:00Z', { name: 'Hired', kind: 'Hired' }, { stageEnteredAt: '2026-10-15T12:00:00Z' });
    const history = [
      // Another application's later move must not be read as this one's.
      item('other', 'Other job', entry({ type: 'StageChanged' }), { fromStageName: 'Wrong stage' }),
      item('h', 'Job h', entry({ type: 'StageChanged' }), { fromStageName: 'Offer', toStageName: 'Hired' }),
      item('h', 'Job h', entry({ type: 'StageChanged' }), { fromStageName: 'Interview', toStageName: 'Offer' }),
    ];

    expect(progressOf(hired, history)).toEqual({ reached: 'Offer', outcome: 'Hired', endedAt: '2026-10-15T12:00:00Z' });
  });

  it('reads a rejected application the same way', () => {
    const rejected = application('r', '2026-10-01T12:00:00Z', { name: 'Rejected', kind: 'Rejected' }, { stageEnteredAt: '2026-10-08T12:00:00Z' });
    const history = [item('r', 'Job r', entry({ type: 'StageChanged' }), { fromStageName: 'Screening', toStageName: 'Rejected' })];

    expect(progressOf(rejected, history)).toEqual({ reached: 'Screening', outcome: 'Rejected', endedAt: '2026-10-08T12:00:00Z' });
  });

  it('skips history entries that are not a stage move when looking for the last move', () => {
    const rejected = application('r', '2026-10-01T12:00:00Z', { name: 'Rejected', kind: 'Rejected' });
    const history = [
      item('r', 'Job r', entry({ type: 'NoteAdded', note: 'Sorry to see this.' }), { fromStageName: 'Not a move' }),
      item('r', 'Job r', entry({ type: 'StageChanged' }), { fromStageName: 'Screening', toStageName: 'Rejected' }),
    ];

    expect(progressOf(rejected, history).reached).toBe('Screening');
  });

  it('leaves out how far a withdrawn application got when the history holds no move for it', () => {
    const withdrawn = application('w', '2026-10-01T12:00:00Z', { name: 'Withdrawn', kind: 'Withdrawn' }, { stageEnteredAt: '2026-10-02T12:00:00Z' });

    expect(progressOf(withdrawn, [item('other', 'Other job', entry({ type: 'StageChanged' }), { fromStageName: 'Screening' })])).toEqual({
      outcome: 'Withdrawn',
      endedAt: '2026-10-02T12:00:00Z',
    });
  });

  it('falls back to the closing stage while the history has not loaded', () => {
    const hired = application('h', '2026-10-01T12:00:00Z', { name: 'Hired', kind: 'Hired' });

    expect(progressOf(hired, undefined).reached).toBeUndefined();
  });

  it('reports a candidate kept in mind as Kept in mind, at the stage they kept, ended when they were kept', () => {
    const kept = application('k', '2026-10-01T12:00:00Z', { name: 'Interview', kind: 'Interview' }, { keptInMindAt: '2026-10-12T12:00:00Z' });

    expect(progressOf(kept, [])).toEqual({ reached: 'Interview', outcome: 'Kept in mind', endedAt: '2026-10-12T12:00:00Z' });
  });
});

describe('historyItems', () => {
  const jobName = (id: string | null | undefined) => (id === 'job-support' ? 'Support Engineer' : 'another job');
  const items = (history: CandidateHistoryEntry[], currentUserId: string | undefined = me) => historyItems(history, members, currentUserId, jobName);

  it('names each stage with the name its own job gave it, though both jobs use the same stage id', () => {
    const history = [
      item('a', 'Senior .NET Engineer', entry({ type: 'StageChanged', fromStageId: 'applied', toStageId: 'screening' }), { fromStageName: 'Applied', toStageName: 'Screening' }),
      item('b', 'Support Engineer', entry({ type: 'StageChanged', fromStageId: 'applied', toStageId: 'screening' }), { fromStageName: 'New', toStageName: 'Phone screen' }),
    ];

    expect(items(history).map((i) => i.event)).toEqual(['Moved from Applied to Screening', 'Moved from New to Phone screen']);
  });

  it('names the stage of a scorecard and falls back to the id when the API sent no name', () => {
    const history = [
      item('a', 'Senior .NET Engineer', entry({ type: 'ScorecardSubmitted', stageId: 'interview' }), { stageName: 'Final interview' }),
      item('a', 'Senior .NET Engineer', entry({ type: 'ScorecardSubmitted', stageId: 'offer' })),
    ];

    expect(items(history).map((i) => i.event)).toEqual(['Scorecard for Final interview', 'Scorecard for offer']);
  });

  it('drops edit lines and the line of a note that was deleted, and keeps everything else in order', () => {
    const history = [
      item('a', 'Senior .NET Engineer', entry({ id: 'deleted-line', type: 'NoteDeleted', actorUserId: robin })),
      item('a', 'Senior .NET Engineer', entry({ id: 'edit-line', type: 'NoteEdited', actorUserId: me })),
      item('a', 'Senior .NET Engineer', entry({ id: 'gone-note', type: 'NoteAdded', note: null, actorUserId: robin })),
      item('a', 'Senior .NET Engineer', entry({ id: 'kept-note', type: 'NoteAdded', note: 'Strong portfolio.', actorUserId: robin })),
    ];

    const result = items(history);

    expect(result.map((i) => i.id)).toEqual(['deleted-line', 'kept-note']);
    expect(result[0]!.event).toBe('Deleted a note');
    expect(result[1]).toMatchObject({ event: 'Note', note: 'Strong portfolio.' });
  });

  it('says who did it and in which job: you, a teammate, an automation, an API key, or nobody', () => {
    const history = [
      item('a', 'Senior .NET Engineer', entry({ type: 'TagAdded', tag: 'a', actorUserId: me })),
      item('a', 'Senior .NET Engineer', entry({ type: 'TagAdded', tag: 'b', actorUserId: robin })),
      item('a', 'Senior .NET Engineer', entry({ type: 'TagAdded', tag: 'c', automationRunId: 'run-1' })),
      item('b', 'Support Engineer', entry({ type: 'TagAdded', tag: 'd', actorApiKeyId: 'key-1', actorApiKeyName: 'ATS sync' })),
      item('b', 'Support Engineer', entry({ type: 'Submitted', toStageId: 'applied' }), { toStageName: 'Applied' }),
    ];

    expect(items(history).map((i) => i.actor)).toEqual([
      'by you in Senior .NET Engineer',
      'by Robin Ashby in Senior .NET Engineer',
      'by an automation in Senior .NET Engineer',
      'by ATS sync in Support Engineer',
      'in Support Engineer',
    ]);
  });

  it('calls a person who left the team a former teammate', () => {
    const history = [item('a', 'Senior .NET Engineer', entry({ type: 'TagAdded', tag: 'a', actorUserId: 'someone-gone' }))];

    expect(items(history)[0]!.actor).toBe('by a former teammate in Senior .NET Engineer');
  });

  it('carries the entry id, time, icon and the note quoted under the event', () => {
    const history = [item('a', 'Senior .NET Engineer', entry({ id: 'e1', type: 'StageChanged', occurredAt: '2026-10-20T11:00:00Z', note: 'Great call.', fromStageId: 'x', toStageId: 'y' }), { fromStageName: 'X', toStageName: 'Y' })];

    expect(items(history)[0]).toMatchObject({ id: 'e1', at: '2026-10-20T11:00:00Z', note: 'Great call.', icon: eventIcons.StageChanged });
  });

  it('names another job for the talent pool entries, and says another job when the reader cannot see it', () => {
    const history = [
      item('a', 'Senior .NET Engineer', entry({ type: 'InvitedToApply', otherJobId: 'job-support' })),
      item('a', 'Senior .NET Engineer', entry({ type: 'InvitedToApply', otherJobId: 'job-hidden' })),
    ];

    expect(items(history).map((i) => i.event)).toEqual(['Invited to apply for Support Engineer', 'Invited to apply for another job']);
  });

  it('gives an empty list for an empty history', () => {
    expect(items([])).toEqual([]);
  });
});

describe('askedToBeRemovedAt', () => {
  it('returns when the candidate asked, through the link in an invitation', () => {
    const history = [
      item('a', 'Job a', entry({ type: 'StageChanged' })),
      item('a', 'Job a', entry({ type: 'LeftTalentPool', poolExit: 'CandidateAsked', occurredAt: '2026-10-12T12:00:00Z' })),
    ];

    expect(askedToBeRemovedAt(history)).toBe('2026-10-12T12:00:00Z');
  });

  it('ignores other ways of leaving the talent pool, such as the team removing them', () => {
    const history = [
      item('a', 'Job a', entry({ type: 'LeftTalentPool', poolExit: 'Removed', occurredAt: '2026-10-12T12:00:00Z' })),
      item('a', 'Job a', entry({ type: 'LeftTalentPool', poolExit: 'Expired', occurredAt: '2026-10-13T12:00:00Z' })),
    ];

    expect(askedToBeRemovedAt(history)).toBeUndefined();
  });

  it('returns the newest request when there are two, as the history is newest first', () => {
    const history = [
      item('b', 'Job b', entry({ type: 'LeftTalentPool', poolExit: 'CandidateAsked', occurredAt: '2026-10-20T12:00:00Z' })),
      item('a', 'Job a', entry({ type: 'LeftTalentPool', poolExit: 'CandidateAsked', occurredAt: '2026-10-12T12:00:00Z' })),
    ];

    expect(askedToBeRemovedAt(history)).toBe('2026-10-20T12:00:00Z');
  });

  it('is undefined before the history loads and when it is empty', () => {
    expect(askedToBeRemovedAt(undefined)).toBeUndefined();
    expect(askedToBeRemovedAt([])).toBeUndefined();
  });
});
