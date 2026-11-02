import { describe, expect, it } from 'vitest';
import type { ApplicationSummary } from '@screenhire/api-client';
import { applications, savedView, stages } from '../test-support/fixtures.js';
import { defaultSorts, differsFromView, filtersOf, sameFilters, sortOf, unfiltered, verificationOf, viewMatcher } from './view-filters.js';

const now = Date.parse('2026-10-20T12:00:00Z');
const withVerification = (application: ApplicationSummary, authenticity: 'NoConcerns' | 'ReviewSuggested'): ApplicationSummary => ({
  ...application,
  verification: {
    authenticity,
    aiAssistance: 'NotIndicated',
    authenticityReasons: [],
    aiAssistanceReasons: [],
    authenticityDismissed: false,
    aiAssistanceDismissed: false,
  } as ApplicationSummary['verification'],
});

describe('what a saved view holds', () => {
  it('writes the table state as the filters of a view, with the sort always explicit', () => {
    const filters = filtersOf({
      baseView: 'needs-review',
      jobIds: ['job-1'],
      stages: ['Screening'],
      verification: { authenticity: 'Pending', aiAssistance: null },
      tag: 'Referral',
      sort: { id: 'candidate', desc: false },
    });

    expect(filters).toEqual({
      baseView: 'needs-review',
      jobIds: ['job-1'],
      stages: ['Screening'],
      authenticity: 'Pending',
      aiAssistance: null,
      tag: 'Referral',
      sortColumn: 'candidate',
      sortDescending: false,
    });
  });

  it('reads a verification filter back, and takes a level this build does not know as no filter', () => {
    expect(verificationOf({ ...unfiltered('Candidates'), authenticity: 'ReviewSuggested', aiAssistance: 'SomeIndicators' })).toEqual({
      authenticity: 'ReviewSuggested',
      aiAssistance: 'SomeIndicators',
    });
    expect(verificationOf({ ...unfiltered('Candidates'), authenticity: 'Pending' }).authenticity).toBe('Pending');
    expect(verificationOf({ ...unfiltered('Candidates'), authenticity: 'Fraud', aiAssistance: 'toString' })).toEqual({ authenticity: null, aiAssistance: null });
  });

  it('opens each table on its own default sort when the view holds none', () => {
    expect(sortOf({ ...unfiltered('Candidates'), sortColumn: null }, 'Candidates')).toEqual(defaultSorts.Candidates);
    expect(sortOf({ ...unfiltered('Job'), sortColumn: null }, 'Job')).toEqual({ id: 'stage', desc: false });
    expect(sortOf({ ...unfiltered('Job'), sortColumn: 'applied', sortDescending: true }, 'Job')).toEqual({ id: 'applied', desc: true });
  });

  it('compares views by what they pick, whatever order a list was picked in', () => {
    const a = filtersOf({ jobIds: ['x', 'y'], stages: ['Screening', 'Interview'], sort: defaultSorts.Candidates });
    const b = filtersOf({ jobIds: ['y', 'x'], stages: ['Interview', 'Screening'], sort: defaultSorts.Candidates });

    expect(sameFilters(a, b, 'Candidates')).toBe(true);
    expect(sameFilters(a, { ...b, tag: 'Referral' }, 'Candidates')).toBe(false);
    expect(sameFilters(a, { ...b, sortDescending: false }, 'Candidates')).toBe(false);
    expect(sameFilters({ ...a, sortColumn: null }, a, 'Candidates')).toBe(true);
  });

  it('says a table differs from its view once its filters or sort do (the Save view button)', () => {
    const none = unfiltered('Candidates');
    expect(differsFromView(none, 'Candidates', null)).toBe(false);
    expect(differsFromView({ ...none, tag: 'Referral' }, 'Candidates', null)).toBe(true);
    expect(differsFromView({ ...none, sortColumn: 'candidate', sortDescending: false }, 'Candidates', null)).toBe(true);

    const builtIn = { baseView: 'needs-review' };
    expect(differsFromView(unfiltered('Candidates', 'needs-review'), 'Candidates', builtIn)).toBe(false);
    expect(differsFromView({ ...none, tag: 'Referral', baseView: 'needs-review' }, 'Candidates', builtIn)).toBe(true);

    const saved = savedView({ filters: { ...none, tag: 'Referral', stages: ['Screening'] } });
    expect(differsFromView(saved.filters, 'Candidates', { filters: saved.filters })).toBe(false);
    expect(differsFromView({ ...saved.filters, stages: [] }, 'Candidates', { filters: saved.filters })).toBe(true);
  });
});

describe('the rows a saved view picks', () => {
  it('narrows a built-in view by tag, job, stage and verification level', () => {
    const rows = [
      withVerification(applications[0]!, 'ReviewSuggested'),
      applications[1]!,
      { ...applications[2]!, jobId: 'other-job', jobTitle: 'Support Engineer' },
    ];
    const names = (filters: Parameters<typeof viewMatcher>[0]) =>
      rows.filter(viewMatcher(filters, 'Candidates', rows, now)).map((application) => application.candidateName);
    const none = unfiltered('Candidates');

    expect(names(none)).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);
    expect(names({ ...none, tag: 'Referral' })).toEqual(['Jordan Lee']);
    expect(names({ ...none, jobIds: ['other-job'] })).toEqual(['Sam Taylor']);
    expect(names({ ...none, stages: ['Screening'] })).toEqual(['Jordan Lee']);
    expect(names({ ...none, authenticity: 'ReviewSuggested' })).toEqual(['Casey Morgan']);
    expect(names({ ...none, baseView: 'needs-review' })).toEqual(['Casey Morgan']);
    expect(names({ ...none, baseView: 'not-screened' })).toEqual(['Casey Morgan', 'Sam Taylor']);
  });

  it('drops a stage the picked jobs no longer have, as the Candidates page does', () => {
    const none = unfiltered('Candidates');
    const matches = viewMatcher({ ...none, jobIds: ['other-job'], stages: ['Screening'] }, 'Candidates', applications, now);

    expect(applications.filter((application) => matches({ ...application, jobId: 'other-job' }))).toHaveLength(applications.length);
  });

  it('matches a job view by stage id, never a candidate kept in mind', () => {
    const none = unfiltered('Job');
    const screening = stages[1]!.id;
    const kept = { ...applications[1]!, keptInMindAt: '2026-10-19T13:00:00Z' };
    const matches = viewMatcher({ ...none, stages: [screening] }, 'Job', applications, now);

    expect(applications.filter(matches).map((application) => application.candidateName)).toEqual(['Jordan Lee']);
    expect(matches(kept)).toBe(false);
  });
});
