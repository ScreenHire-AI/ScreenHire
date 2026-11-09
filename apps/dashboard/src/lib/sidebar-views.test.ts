import { describe, expect, it } from 'vitest';
import { jobId, savedView } from '../test-support/fixtures.js';
import { sidebarViews, viewHref } from './sidebar-views.js';

const input = { saved: [], hiddenBuiltIns: [], jobTitles: {} };

describe('the sidebar views', () => {
  it('lists the built-in views first, then the saved ones', () => {
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
    expect(visible.at(-1)).toMatchObject({ label: 'Referrals in screening', builtIn: false, href: `/candidates?view=${referrals.id}` });
    expect(visible.find((view) => view.key === 'not-screened')).toMatchObject({ builtIn: true });
  });

  it('moves the built-in views a person hid behind "Show hidden views"', () => {
    const { visible, hidden } = sidebarViews({ ...input, hiddenBuiltIns: ['overdue', 'new-today'] });

    expect(visible.map((view) => view.key)).not.toContain('overdue');
    expect(hidden.map((view) => view.key)).toEqual(['overdue', 'new-today']);
  });

  it('has no count on any view, because a view only filters (T4)', () => {
    const { visible, hidden } = sidebarViews({ ...input, hiddenBuiltIns: ['overdue'], saved: [savedView()] });

    for (const view of [...visible, ...hidden]) expect(view).not.toHaveProperty('count');
  });

  it('gives every view a plain icon and never a status icon (I2)', () => {
    const { visible } = sidebarViews({ ...input, saved: [savedView()] });

    for (const view of visible) {
      expect(view.icon).toBeDefined();
      expect(view).not.toHaveProperty('status');
    }
  });

  it('opens a job view on the job candidates tab, naming the job', () => {
    const view = savedView({ scope: 'Job', jobId, shared: true, mine: false, filters: { ...savedView().filters, stages: ['screening'] } });
    const { visible } = sidebarViews({ ...input, saved: [view], jobTitles: { [jobId]: 'Senior .NET Engineer' } });

    expect(visible.at(-1)).toMatchObject({
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
