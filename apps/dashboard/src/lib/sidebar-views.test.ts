import { describe, expect, it } from 'vitest';
import { jobId, savedView } from '../test-support/fixtures.js';
import { sidebarViews, viewHref } from './sidebar-views.js';
import { statuses } from './status.js';

const input = { saved: [], hiddenBuiltIns: [], sidebarOrder: [], jobTitles: {} };

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

  it("gives every built-in view its own icon, a status view its status's mark, so the icon rail tells them apart (I2)", () => {
    const { visible } = sidebarViews({ ...input, saved: [savedView()] });
    const builtIn = visible.filter((view) => view.builtIn);

    expect(new Set(builtIn.map((view) => view.icon)).size).toBe(builtIn.length);
    expect(builtIn.find((view) => view.key === 'needs-review')?.icon).toBe(statuses.review.icon);
    expect(builtIn.find((view) => view.key === 'verification-recommended')?.icon).toBe(statuses.verification.icon);
    expect(builtIn.find((view) => view.key === 'overdue')?.icon).toBe(statuses.overdue.icon);
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

  it('leaves a shared view out until the person adds it: sharing is an offer', () => {
    const mine = savedView({ id: 'a', name: 'Mine' });
    const offered = savedView({ id: 'b', name: 'Offered', shared: true, mine: false, canEdit: false, inSidebar: false });
    const { visible } = sidebarViews({ ...input, saved: [mine, offered] });

    expect(visible.map((view) => view.key)).toContain('a');
    expect(visible.map((view) => view.key)).not.toContain('b');
  });

  it('lists the views a person owns, then the shared ones they added in the order they added them', () => {
    const shared = (id: string, name: string) => savedView({ id, name, shared: true, mine: false, canEdit: false, inSidebar: true });
    const first = shared('first', 'First added');
    const second = shared('second', 'Second added');
    const mine = savedView({ id: 'mine', name: 'Mine, saved last' });
    // The server lists views oldest first, so the added ones can arrive in either order.
    const { visible } = sidebarViews({ ...input, saved: [first, second, mine], sidebarOrder: ['second', 'first'] });

    expect(visible.slice(-3).map((view) => view.key)).toEqual(['mine', 'second', 'first']);
  });
});
