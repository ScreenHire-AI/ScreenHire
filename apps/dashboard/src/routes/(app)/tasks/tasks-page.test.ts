import type { TaskListItem, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/tasks') } }));

const robin = '01a157d4-0000-7000-8000-0000000000cc';
const members: TeamMember[] = [
  { id: '01a157d4-0000-7000-8000-000000000401', userId: user.id, email: user.email, displayName: user.displayName, role: 'Owner', customRoleId: null, roleName: 'Owner', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
  { id: '01a157d4-0000-7000-8000-000000000402', userId: robin, email: 'robin@brightwater.example.com', displayName: 'Robin Ashby', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-02T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
];

const task = (overrides: Partial<TaskListItem>): TaskListItem => ({
  id: crypto.randomUUID(),
  applicationId: '01a157d4-0000-7000-8000-000000000410',
  jobId,
  candidateName: 'Casey Morgan',
  jobTitle: 'Senior .NET Engineer',
  kind: 'Task',
  note: 'Call the references.',
  assigneeUserId: null,
  automationRunId: null,
  createdAt: '2026-10-20T10:00:00Z',
  completedAt: null,
  completedByUserId: null,
  ...overrides,
});

// For the team, the person's own, and a teammate's.
const open = [
  task({}),
  task({ kind: 'Review', note: 'Review this application.', assigneeUserId: user.id, candidateName: 'Jordan Lee', createdAt: '2026-10-20T10:05:00Z' }),
  task({ kind: 'Review', note: 'Review the portfolio.', assigneeUserId: robin, candidateName: 'Alex Reed', createdAt: '2026-10-20T10:10:00Z' }),
];
const done = [task({ kind: 'RejectionReview', note: 'Not authorised to work in the UK.', completedAt: '2026-10-20T11:00:00Z', completedByUserId: robin })];
const mine = open[1]!;
const tick = 'Mark done for Jordan Lee: Review this application.';

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const fetch = vi.fn(
    apiStub({
      'GET /v1/tasks?status=Open&scope=Everyone': () => Response.json(open),
      'GET /v1/tasks?status=Completed&scope=Everyone': () => Response.json(done),
      'GET /v1/organization/members': () => Response.json(members),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetch);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetch;
}

const posted = (fetch: ReturnType<typeof renderPage>, url: string) =>
  fetch.mock.calls.some(([called, init]) => String(called) === url && init?.method === 'POST');

const rows = (panel: HTMLElement) => within(within(panel).getByRole('list', { name: 'Open tasks' })).getAllByRole('listitem');

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('my tasks page', () => {
  it("opens on the person's own tasks, with a count on Mine, For the team and Everyone's", async () => {
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Open tasks' });
    const whose = screen.getByRole('group', { name: 'Whose' });

    await waitFor(() => expect(within(whose).getByRole('button', { name: 'Mine 1' })).toHaveAttribute('aria-pressed', 'true'));
    expect(within(whose).getByRole('button', { name: 'For the team 1' })).toBeInTheDocument();
    expect(within(whose).getByRole('button', { name: "Everyone's 3" })).toBeInTheDocument();
    const own = rows(panel);
    expect(own).toHaveLength(1);
    expect(within(own[0]!).getByRole('link', { name: 'Jordan Lee' })).toHaveAttribute('href', '/applications/01a157d4-0000-7000-8000-000000000410');
    expect(own[0]).toHaveTextContent('Review');
    await waitFor(() => expect(own[0]).toHaveTextContent('For you'));
    expect(within(own[0]!).getByRole('checkbox', { name: tick })).not.toBeChecked();

    await fireEvent.click(within(whose).getByRole('button', { name: /^For the team/ }));
    const team = rows(panel);
    expect(team).toHaveLength(1);
    expect(team[0]).toHaveTextContent('To-do');
    expect(team[0]).toHaveTextContent('For the team');

    await fireEvent.click(within(whose).getByRole('button', { name: /^Everyone's/ }));
    expect(rows(panel)).toHaveLength(3);
  });

  it('ticks a task done and keeps it there, ticked, with Undo for 10 seconds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let listed = open;
    const fetch = renderPage({
      'GET /v1/tasks?status=Open&scope=Everyone': () => Response.json(listed),
      [`POST /v1/tasks/${mine.id}/complete`]: () => {
        listed = open.filter((row) => row.id !== mine.id);
        return Response.json({ ...mine, completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id });
      },
    });
    await fireEvent.click(await screen.findByRole('checkbox', { name: tick }));

    await waitFor(() => expect(posted(fetch, `/v1/tasks/${mine.id}/complete`)).toBe(true));
    expect(await screen.findByText('Marked "Review this application." done.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Mine 0' })).toBeInTheDocument());
    expect(screen.getByRole('checkbox', { name: tick })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Undo, Jordan Lee: Review this application.' })).toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(10_000);
    await waitFor(() => expect(screen.queryByRole('checkbox', { name: tick })).not.toBeInTheDocument());
    expect(screen.getByText('Nothing to do')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: "Show everyone's" })).toBeInTheDocument();
  });

  it('opens the task again on Undo, and on unticking it', async () => {
    const fetch = renderPage({
      [`POST /v1/tasks/${mine.id}/complete`]: () => Response.json({ ...mine, completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id }),
      [`POST /v1/tasks/${mine.id}/reopen`]: () => Response.json(mine),
    });

    await fireEvent.click(await screen.findByRole('checkbox', { name: tick }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Undo, Jordan Lee: Review this application.' }));
    await waitFor(() => expect(posted(fetch, `/v1/tasks/${mine.id}/reopen`)).toBe(true));
    expect(await screen.findByText('"Review this application." is open again.')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: tick })).not.toBeChecked();
    expect(screen.queryByRole('button', { name: /^Undo/ })).not.toBeInTheDocument();

    fetch.mockClear();
    await fireEvent.click(screen.getByRole('checkbox', { name: tick }));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: tick })).toBeChecked());
    await fireEvent.click(screen.getByRole('checkbox', { name: tick }));
    await waitFor(() => expect(posted(fetch, `/v1/tasks/${mine.id}/reopen`)).toBe(true));
  });

  it('unticks the task and says why when completing fails', async () => {
    renderPage({ [`POST /v1/tasks/${mine.id}/complete`]: () => new Response(null, { status: 500 }) });

    await fireEvent.click(await screen.findByRole('checkbox', { name: tick }));

    expect((await screen.findAllByText(/Could not complete the task/)).length).toBeGreaterThan(0);
    expect(screen.getByRole('checkbox', { name: tick })).not.toBeChecked();
  });

  it('shows done tasks with who finished them, and no checkbox on them', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Open tasks' });

    await fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    await fireEvent.click(screen.getByRole('button', { name: /^Everyone's/ }));
    const panel = await screen.findByRole('region', { name: 'Done' });
    expect(await within(panel).findByText(/by Robin Ashby/)).toBeInTheDocument();
    expect(within(panel).queryByRole('checkbox')).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: /^Mine/ }));
    expect(await screen.findByText('Nothing done yet')).toBeInTheDocument();
  });
});
