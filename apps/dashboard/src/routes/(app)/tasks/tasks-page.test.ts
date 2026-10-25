import type { TaskListItem, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/tasks') } }));

const robin = '01a157d4-0000-7000-8000-0000000000cc';
const members: TeamMember[] = [
  { id: '01a157d4-0000-7000-8000-000000000401', userId: user.id, email: user.email, displayName: user.displayName, role: 'Owner', joinedAt: '2026-10-01T09:00:00Z' },
  { id: '01a157d4-0000-7000-8000-000000000402', userId: robin, email: 'robin@brightwater.example.com', displayName: 'Robin Ashby', role: 'Recruiter', joinedAt: '2026-10-02T09:00:00Z' },
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

const open = [task({}), task({ kind: 'Review', note: 'Review this application.', assigneeUserId: user.id, candidateName: 'Jordan Lee' })];
const done = [task({ kind: 'RejectionReview', note: 'Not authorised to work in the UK.', completedAt: '2026-10-20T11:00:00Z', completedByUserId: robin })];

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const fetch = vi.fn(
    apiStub({
      'GET /v1/tasks?status=Open&scope=Mine': () => Response.json(open),
      'GET /v1/tasks?status=Completed&scope=Mine': () => Response.json(done),
      'GET /v1/tasks?status=Open&scope=Everyone': () => Response.json([]),
      'GET /v1/organization/members': () => Response.json(members),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetch);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetch;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('my tasks page', () => {
  it("lists the person's and the team's open tasks with the candidate, kind and who each is for", async () => {
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Open tasks' });

    const list = await within(panel).findByRole('list', { name: 'Open tasks' });
    const rows = within(list).getAllByRole('listitem');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', '/applications/01a157d4-0000-7000-8000-000000000410');
    expect(rows[0]).toHaveTextContent('To-do');
    expect(rows[0]).toHaveTextContent('For the team');
    expect(rows[1]).toHaveTextContent('Review');
    await waitFor(() => expect(rows[1]).toHaveTextContent('For you'));
    expect(within(rows[1]!).getByRole('button', { name: 'Mark done for Jordan Lee: Review this application.' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('2', { selector: '[data-slot="badge"]' })).toHaveTextContent('2 tasks'));
  });

  it('completes a task', async () => {
    const fetch = renderPage({
      [`POST /v1/tasks/${open[0]!.id}/complete`]: () => Response.json({ ...open[0], completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id }),
    });
    await fireEvent.click(await screen.findByRole('button', { name: 'Mark done for Casey Morgan: Call the references.' }));

    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url) === `/v1/tasks/${open[0]!.id}/complete` && init?.method === 'POST')).toBe(true));
    expect(await screen.findByText('Marked "Call the references." done.')).toBeInTheDocument();
  });

  it("shows done tasks with who finished them, and says so when nobody has anything open", async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Open tasks' });

    await fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    const panel = await screen.findByRole('region', { name: 'Done' });
    expect(await within(panel).findByText(/by Robin Ashby/)).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: /^Mark done/ })).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));
    await fireEvent.click(screen.getByRole('button', { name: "Everyone's" }));
    expect(await screen.findByText('Nothing to do')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: "Show everyone's" })).not.toBeInTheDocument();
  });
});
