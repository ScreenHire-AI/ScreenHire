import type { HistoryEntry, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import HistorySheet from './history-sheet.svelte';

const now = Date.parse('2026-10-25T13:42:00Z');

const members: TeamMember[] = [
  { id: 'm1', userId: 'u1', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner', customRoleId: null, roleName: 'Owner', joinedAt: '2026-10-01T09:00:00Z' },
  { id: 'm2', userId: 'u2', email: 'jordan@example.com', displayName: 'Jordan Ellis', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-01T09:00:00Z' },
];

const entries: HistoryEntry[] = [
  {
    id: 'e1',
    at: '2026-10-25T13:22:00Z',
    by: 'Member',
    userId: 'u2',
    apiKeyId: null,
    lines: [
      {
        text: 'changed .NET and C# from 1 to 5 to 1 to 10',
        parts: [
          { text: 'changed ', strong: false },
          { text: '.NET and C#', strong: true },
          { text: ' from 1 to 5 to 1 to 10', strong: false },
        ],
        details: [
          { label: 'Answer', before: '1 to 5', after: '1 to 10', kind: 'Text' },
          { label: 'Required', before: 'on', after: 'on', kind: 'Text' },
        ],
      },
    ],
  },
  {
    id: 'e2',
    at: '2026-10-22T09:05:00Z',
    by: 'ScreenHire',
    userId: null,
    apiKeyId: null,
    lines: [{ text: 'created the template', parts: [{ text: 'created the template', strong: false }], details: [] }],
  },
];

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
});

afterEach(() => {
  vi.useRealTimers();
});

function renderSheet(props: Partial<{ entries: HistoryEntry[] | undefined; hasMore: boolean; onmore: () => void; error: string }> = {}) {
  render(WithTooltips, {
    props: {
      component: HistorySheet,
      props: { open: true, name: 'Technical interview', entries, members, currentUserId: 'u1', ...props },
    },
  });
  return screen.getByRole('dialog', { name: 'History' });
}

describe('history sheet', () => {
  it('lists changes newest first under their day, the person first and the time ago', async () => {
    const sheet = renderSheet();

    expect(within(sheet).getByText('Technical interview')).toBeInTheDocument();
    const days = within(sheet).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent?.trim());
    expect(days).toEqual(['Today', 'Oct 22']);
    const [first, second] = within(sheet).getAllByRole('listitem');
    expect(first).toHaveTextContent('Jordan Ellis changed .NET and C# from 1 to 5 to 1 to 10');
    expect(within(first!).getByText('20 minutes ago')).toBeInTheDocument();
    expect(second).toHaveTextContent('ScreenHire created the template');
    expect(within(second!).queryByRole('button', { name: 'Show the change' })).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('shows the values before and after on request', async () => {
    const sheet = renderSheet();
    const show = within(sheet).getByRole('button', { name: 'Show the change' });
    expect(show).toHaveAttribute('aria-expanded', 'false');

    await fireEvent.click(show);

    const hide = within(sheet).getByRole('button', { name: 'Hide the change' });
    expect(hide).toHaveAttribute('aria-expanded', 'true');
    const terms = within(sheet).getAllByRole('term').map((term) => term.textContent);
    expect(terms).toEqual(['Answer', 'Required']);
    const values = within(sheet).getAllByRole('definition').map((value) => value.textContent?.replace(/\s+/g, ' ').trim());
    expect(values).toEqual(['1 to 5 to 1 to 10', 'on Unchanged']);
  });

  it('offers older changes when there are more', async () => {
    const onmore = vi.fn();
    const sheet = renderSheet({ hasMore: true, onmore });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Show older changes' }));
    expect(onmore).toHaveBeenCalled();
  });

  it('says when there are no changes yet', () => {
    expect(renderSheet({ entries: [] })).toHaveTextContent('No changes yet.');
  });
});
