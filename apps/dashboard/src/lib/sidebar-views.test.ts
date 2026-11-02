import { describe, expect, it } from 'vitest';
import { applications, jobId, savedView } from '../test-support/fixtures.js';
import { sidebarViews, viewHref } from './sidebar-views.js';

const now = Date.parse('2026-10-20T12:00:00Z');
const input = { saved: [], hiddenBuiltIns: [], applications, now, jobTitles: {} };

describe('the sidebar views', () => {
  it('lists the built-in views first, then the saved ones, each with the rows it picks', () => {
    const referrals = savedView({ filters: { ...savedView().filters, tag: 'Referral' } });
    const { visible, hidden } = sidebarViews({ ...input, saved: [referrals] });

    expect(visible.map((view) => view.key)).toEqual([
      'needs-review',
      'verification-recommended',
      'overdue',
      'new-today',
      'not-screened',
      'interviewing',
      referrals.id,
    ]);
    expect(hidden).toEqual([]);
    expect(visible.at(-1)).toMatchObject({ label: 'Referrals in screening', builtIn: false, count: 1, href: `/candidates?view=${referrals.id}` });
    expect(visible.find((view) => view.key === 'not-screened')).toMatchObject({ builtIn: true, count: 2 });
  });

  it('moves the built-in views a person hid behind "Show hidden views"', () => {
    const { visible, hidden } = sidebarViews({ ...input, hiddenBuiltIns: ['overdue', 'new-today'] });

    expect(visible.map((view) => view.key)).not.toContain('overdue');
    expect(hidden.map((view) => view.key)).toEqual(['overdue', 'new-today']);
  });

  it('has no counts while the applications load', () => {
    const { visible } = sidebarViews({ ...input, applications: undefined, saved: [savedView()] });

    expect(visible.every((view) => view.count === undefined)).toBe(true);
  });

  it('counts a job view over that job only and opens it on the job candidates tab, naming the job', () => {
    const view = savedView({ scope: 'Job', jobId, shared: true, mine: false, filters: { ...savedView().filters, stages: ['screening'] } });
    const otherJob = { ...applications[1]!, id: 'other', jobId: 'other-job' };
    const { visible } = sidebarViews({ ...input, applications: [...applications, otherJob], saved: [view], jobTitles: { [jobId]: 'Senior .NET Engineer' } });

    expect(visible.at(-1)).toMatchObject({
      count: 1,
      jobTitle: 'Senior .NET Engineer',
      href: `/jobs/${jobId}?tab=candidates&view=${view.id}`,
    });
    expect(viewHref(savedView())).toBe(`/candidates?view=${savedView().id}`);
  });

  it('shows a view a teammate shared with the team icon, and your own with the ordinary view icon', () => {
    const mine = savedView({ id: 'a', name: 'Mine' });
    const theirs = savedView({ id: 'b', name: 'Theirs', shared: true, mine: false, canEdit: false });
    const { visible } = sidebarViews({ ...input, saved: [mine, theirs] });

    expect(visible.find((view) => view.key === 'a')!.icon).not.toBe(visible.find((view) => view.key === 'b')!.icon);
  });
});
