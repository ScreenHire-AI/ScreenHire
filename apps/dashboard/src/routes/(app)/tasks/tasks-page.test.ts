import type { TaskListItem, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, user } from '../../../test-support/fixtures.js';
import { stubViewport } from '../../../test-support/viewport.js';
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
  task({ kind: 'Review', note: 'Give your scorecard.', assigneeUserId: user.id, candidateName: 'Jordan Lee', createdAt: '2026-10-20T10:05:00Z' }),
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

// The body's rows, without the header row.
const rows = (panel: HTMLElement) => within(within(panel).getByRole('table', { name: 'Open tasks' })).getAllByRole('row').slice(1);

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
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
    expect(within(own[0]!).getByRole('link', { name: 'Give your scorecard.' })).toHaveAttribute('href', '/applications/01a157d4-0000-7000-8000-000000000410?tab=scorecards&fill=stage');
    expect(own[0]).toHaveTextContent('Jordan Lee');
    // The Type column says Scorecard: "Review" only means the verification state.
    expect(own[0]).toHaveTextContent('Scorecard');
    // Task and Type say what to do and what kind it is, so no other column repeats them (W7).
    expect(within(panel).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Task', 'Candidate', 'Job', 'Type', 'Created']);
    // Mine already says whose they are, so there is no For column (W7).
    expect(within(panel).queryByRole('columnheader', { name: 'For' })).not.toBeInTheDocument();
    expect(within(own[0]!).queryByRole('checkbox')).not.toBeInTheDocument();

    await fireEvent.click(within(whose).getByRole('button', { name: /^For the team/ }));
    const team = rows(panel);
    expect(team).toHaveLength(1);
    expect(team[0]).toHaveTextContent('To-do');
    expect(within(panel).queryByRole('columnheader', { name: 'For' })).not.toBeInTheDocument();

    await fireEvent.click(within(whose).getByRole('button', { name: /^Everyone's/ }));
    expect(rows(panel)).toHaveLength(3);
    // Everyone's is the one list where whose task it is varies, so it has the column.
    expect(within(panel).getByRole('columnheader', { name: /^For/ })).toBeInTheDocument();
    await waitFor(() => expect(rows(panel)[1]).toHaveTextContent('You'));
    expect(rows(panel)[0]).toHaveTextContent('The team');
    expect(rows(panel)[2]).toHaveTextContent('Robin Ashby');
  });

  it('links a requested scorecard and a review to the Scorecards tab with the sheet open, and every other task to the candidate', async () => {
    renderPage({
      'GET /v1/tasks?status=Open&scope=Everyone': () =>
        Response.json([
          ...open,
          task({
            kind: 'Scorecard',
            note: 'Fill in the Interview scorecard.',
            assigneeUserId: user.id,
            scorecardTemplateId: 'interview',
            applicationId: '01a157d4-0000-7000-8000-000000000411',
            createdAt: '2026-10-20T10:20:00Z',
          }),
        ]),
    });
    const panel = await screen.findByRole('region', { name: 'Tasks' });

    await waitFor(() => expect(rows(panel)).toHaveLength(2));
    expect(within(panel).getByRole('link', { name: 'Fill in the Interview scorecard.' })).toHaveAttribute(
      'href',
      '/applications/01a157d4-0000-7000-8000-000000000411?tab=scorecards&scorecard=interview',
    );
    // A review is finished by any scorecard, so it opens the sheet on the scorecard for the candidate's stage.
    expect(within(panel).getByRole('link', { name: 'Give your scorecard.' })).toHaveAttribute(
      'href',
      '/applications/01a157d4-0000-7000-8000-000000000410?tab=scorecards&fill=stage',
    );
    expect(rows(panel)[1]).toHaveTextContent('Scorecard');
  });

  it('shows done tasks with who finished them, and no checkbox on them', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Tasks' });

    await fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    await fireEvent.click(screen.getByRole('button', { name: /^Everyone's/ }));
    const panel = await screen.findByRole('region', { name: 'Tasks' });
    // Who finished it and when are columns of their own, with the date and not a time ago (T5).
    expect(await within(panel).findByText('Robin Ashby')).toBeInTheDocument();
    expect(within(panel).getByRole('columnheader', { name: /^Done by/ })).toBeInTheDocument();
    expect(within(panel).getByText('Oct 20, 2026', { selector: 'time[datetime="2026-10-20T11:00:00Z"]' })).toBeInTheDocument();
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

  it('sorts by a column and says which way, opening on the task that has waited longest (L2)', async () => {
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Tasks' });
    await fireEvent.click(within(screen.getByRole('group', { name: 'Whose' })).getByRole('button', { name: /^Everyone's/ }));

    expect(within(panel).getByRole('columnheader', { name: /^Created/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(rows(panel)[0]).toHaveTextContent('Call the references.');

    await fireEvent.click(within(panel).getByRole('button', { name: /^Candidate/ }));
    expect(within(panel).getByRole('columnheader', { name: /^Candidate/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(rows(panel)[0]).toHaveTextContent('Alex Reed');
    await fireEvent.click(within(panel).getByRole('button', { name: /^Candidate/ }));
    expect(rows(panel)[0]).toHaveTextContent('Jordan Lee');
  });

  it('opens on its default when a remembered sort names a column the scope does not have', async () => {
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {});
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Tasks' });
    const whose = screen.getByRole('group', { name: 'Whose' });
    await fireEvent.click(within(whose).getByRole('button', { name: /^Everyone's/ }));
    await fireEvent.click(within(within(panel).getByRole('columnheader', { name: /^For/ })).getByRole('button'));
    expect(within(panel).getByRole('columnheader', { name: /^For/ })).toHaveAttribute('aria-sort', 'ascending');

    // Mine has no For column, so the sort falls back to the oldest task first instead of sorting on nothing.
    await fireEvent.click(within(whose).getByRole('button', { name: /^For the team/ }));
    await waitFor(() => expect(within(panel).getByRole('columnheader', { name: /^Created/ })).toHaveAttribute('aria-sort', 'ascending'));
    expect(rows(panel)).toHaveLength(1);
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('sorts by the names once the team has loaded, not by what was known before it', async () => {
    let sendMembers: (response: Response) => void = () => {};
    const pendingMembers = new Promise<Response>((resolve) => (sendMembers = resolve));
    renderPage({ 'GET /v1/organization/members': () => pendingMembers });
    const panel = await screen.findByRole('region', { name: 'Tasks' });
    await fireEvent.click(within(screen.getByRole('group', { name: 'Whose' })).getByRole('button', { name: /^Everyone's/ }));
    await waitFor(() => expect(rows(panel)).toHaveLength(3));

    await fireEvent.click(within(within(panel).getByRole('columnheader', { name: /^For/ })).getByRole('button'));
    // A name that sorts after "The team", where the placeholder for a name not loaded yet sorts before it.
    sendMembers(Response.json(members.map((member) => (member.userId === robin ? { ...member, displayName: 'Zoe Ashby' } : member))));

    await waitFor(() => expect(rows(panel).map((row) => within(row).getAllByRole('cell')[4]!.textContent?.trim())).toEqual(['The team', 'You', 'Zoe Ashby']));
  });

  it('lists the tasks as items on a phone, naming whose each is with a name as it is written', async () => {
    stubViewport(360);
    renderPage();
    await screen.findByRole('region', { name: 'Tasks' });
    await fireEvent.click(within(screen.getByRole('group', { name: 'Whose' })).getByRole('button', { name: /^Everyone's/ }));

    const list = await screen.findByRole('list', { name: 'Open tasks' });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await waitFor(() => expect(list).toHaveTextContent('For Robin Ashby'));
    expect(list).toHaveTextContent('For the team');
    expect(list).toHaveTextContent('For you');
    expect(list).not.toHaveTextContent('robin ashby');
    expect(within(screen.getByRole('region', { name: 'Tasks' })).getByRole('button', { name: 'Sort' })).toBeInTheDocument();
  });
});
