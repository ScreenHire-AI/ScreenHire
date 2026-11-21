import type { LastUpdate, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatDateTime } from '#lib/dates.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import LastUpdated from './last-updated.svelte';

const now = Date.parse('2026-10-25T13:42:00Z');
const twoHoursAgo = '2026-10-25T11:42:00Z';

const members: TeamMember[] = [
  { id: 'm1', userId: 'u1', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner', customRoleId: null, roleName: 'Owner', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
  { id: 'm2', userId: 'u2', email: 'jordan@example.com', displayName: 'Jordan Ellis', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
];

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
});

afterEach(() => {
  vi.useRealTimers();
});

function words(value: LastUpdate, lead = false) {
  const { container } = render(WithTooltips, { props: { component: LastUpdated, props: { value, members, currentUserId: 'u1', lead } } });
  return container.textContent?.replace(/\s+/g, ' ').trim();
}

describe('last updated', () => {
  it('says Created, by whom, when nothing has changed the object since it was created', () => {
    expect(words({ at: twoHoursAgo, by: 'Member', userId: 'u2', created: true }, true)).toBe('Created:Created 2 hours ago by Jordan Ellis');
  });

  it('says Updated after a change, and names nobody when nobody was recorded', () => {
    expect(words({ at: twoHoursAgo, by: 'Member', userId: 'u1' }, true)).toBe('Last updated:Updated 2 hours ago by you');
    expect(words({ at: twoHoursAgo, by: null }, true)).toBe('Last updated:Updated 2 hours ago');
  });

  it('says a built-in nobody changed is by ScreenHire, in lower case after its verb', () => {
    expect(words({ at: null, by: 'ScreenHire' }, true)).toBe('Last updated:Updated by ScreenHire');
    expect(words({ at: null, by: 'ScreenHire' })).toBe('By ScreenHire');
  });

  it("makes the fact itself the History button, its name the visible words then History (the owner's walk-through, 2026-11-21)", async () => {
    const value: LastUpdate = { at: twoHoursAgo, by: 'Member', userId: 'u1' };
    const opened = vi.fn();
    render(WithTooltips, { props: { component: LastUpdated, props: { value, members, currentUserId: 'u1', lead: true, titleRow: true, onhistory: opened } } });

    const fact = screen.getByRole('button', { name: 'Last updated: 2 hours ago by you. History' });
    expect(fact).toHaveAttribute('aria-haspopup', 'dialog');
    // On a title row the words still give way with an ellipsis, and nothing else sits beside them.
    expect(fact).toHaveClass('truncate');
    expect(screen.getAllByRole('button')).toHaveLength(1);
    await fireEvent.click(fact);
    expect(opened).toHaveBeenCalledOnce();
  });

  it('is plain text when nothing can change the object', () => {
    render(WithTooltips, { props: { component: LastUpdated, props: { value: { at: twoHoursAgo, by: 'Member', userId: 'u1' }, members, currentUserId: 'u1', titleRow: true } } });
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('holds the whole fact and the exact time in a tooltip on a title row, where the words can be cut (F5)', async () => {
    const value: LastUpdate = { at: twoHoursAgo, by: 'Member', userId: 'u2' };
    render(WithTooltips, { props: { component: LastUpdated, props: { value, members, currentUserId: 'u1', titleRow: true } } });

    await fireEvent.click(screen.getByText(/Updated/).closest('span.truncate')!);

    await waitFor(() => expect(document.querySelector('[data-slot="tooltip-content"]')).toHaveTextContent(`Updated ${formatDateTime(twoHoursAgo)} by Jordan Ellis`));
  });

  it('wraps with its verb outside a title row, so a note sheet keeps the whole line', () => {
    const value: LastUpdate = { at: twoHoursAgo, by: 'Member', userId: 'u1' };
    render(WithTooltips, { props: { component: LastUpdated, props: { value, members, currentUserId: 'u1', lead: true, onhistory: () => {} } } });

    expect(screen.getByRole('button', { name: /History$/ })).not.toHaveClass('truncate');
    expect(screen.getByRole('button', { name: /History$/ }).parentElement).toHaveClass('flex-wrap');
  });

  it('is the value alone inside a fact, whose label says Created or Last updated', () => {
    expect(words({ at: twoHoursAgo, by: 'Member', userId: 'u2', created: true })).toBe('2 hours ago by Jordan Ellis');
    expect(words({ at: twoHoursAgo, by: 'ApiKey', apiKeyId: 'k1' })).toBe('2 hours ago by an API key');
  });
});
