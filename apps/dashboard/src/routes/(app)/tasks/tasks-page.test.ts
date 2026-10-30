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

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const fetch = vi.fn(
    apiStub({
      'GET /v1/tasks?status=Open&scope=Everyone': () => Response.json(open),
      'GET /v1/tasks?status=Completed&scope=Mine': () => Response.json([]),
      'GET /v1/tasks?status=Completed&scope=Team': () => Response.json(done),
      'GET /v1/tasks?status=Completed&scope=Everyone': () => Response.json(done),
      'GET /v1/organization/members': () => Response.json(members),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetch);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetch;
}

const rows = (panel: HTMLElement) => within(within(panel).getByRole('list', { name: 'Open tasks' })).getAllByRole('listitem');

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('my tasks page', () => {
  it("opens on the person's own tasks, with a count on Mine, For the team and Everyone's", async () => {
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Tasks' });
    const whose = screen.getByRole('group', { name: 'Whose' });

    await waitFor(() => expect(within(whose).getByRole('button', { name: 'Mine 1' })).toHaveAttribute('aria-pressed', 'true'));
    expect(within(whose).getByRole('button', { name: 'For the team 1' })).toBeInTheDocument();
    expect(within(whose).getByRole('button', { name: "Everyone's 3" })).toBeInTheDocument();
    const own = rows(panel);
    expect(own).toHaveLength(1);
    // The row links to where the work is done, and the task is ticked off there, so there is no checkbox here.
    expect(within(own[0]!).getByRole('link', { name: 'Review this application.' })).toHaveAttribute('href', '/applications/01a157d4-0000-7000-8000-000000000410');
    expect(own[0]).toHaveTextContent('Jordan Lee');
    expect(own[0]).toHaveTextContent('Review');
    await waitFor(() => expect(own[0]).toHaveTextContent('For you'));
    expect(within(own[0]!).queryByRole('checkbox')).not.toBeInTheDocument();

    await fireEvent.click(within(whose).getByRole('button', { name: /^For the team/ }));
    const team = rows(panel);
    expect(team).toHaveLength(1);
    expect(team[0]).toHaveTextContent('To-do');
    expect(team[0]).toHaveTextContent('For the team');

    await fireEvent.click(within(whose).getByRole('button', { name: /^Everyone's/ }));
    expect(rows(panel)).toHaveLength(3);
  });

  it('shows done tasks with who finished them, and no checkbox on them', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Tasks' });

    await fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    await fireEvent.click(screen.getByRole('button', { name: /^Everyone's/ }));
    const panel = await screen.findByRole('region', { name: 'Tasks' });
    expect(await within(panel).findByText(/by Robin Ashby/)).toBeInTheDocument();
    expect(within(panel).queryByRole('checkbox')).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: /^Mine/ }));
    expect(await screen.findByText('Nothing done yet')).toBeInTheDocument();
  });

  it("asks for each scope's own done tasks, so a busy team does not crowd out the person's", async () => {
    const fetch = renderPage();
    await screen.findByRole('region', { name: 'Tasks' });

    await fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    const whose = screen.getByRole('group', { name: 'Whose' });
    await waitFor(() => expect(within(whose).getByRole('button', { name: 'Mine 0' })).toBeInTheDocument());
    expect(within(whose).getByRole('button', { name: 'For the team 1' })).toBeInTheDocument();
    expect(within(whose).getByRole('button', { name: "Everyone's 1" })).toBeInTheDocument();
    for (const scope of ['Mine', 'Team', 'Everyone']) {
      expect(fetch.mock.calls.some(([called]) => String(called) === `/v1/tasks?status=Completed&scope=${scope}`)).toBe(true);
    }
  });
});
