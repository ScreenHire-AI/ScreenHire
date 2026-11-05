import type { CustomRoleDetail, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../../test-support/axe.js';
import { apiStub, interviewerRoleId, roleCatalogue, user } from '../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const { goto } = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/roles/x') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto }));

const detail: CustomRoleDetail = {
  id: interviewerRoleId,
  name: 'Interviewer',
  description: 'See jobs and candidates and fill in scorecards.',
  permissions: ['ViewHiring', 'FillInScorecards'],
  people: 1,
  copiedFrom: 'Viewer',
  createdAt: '2026-10-25T09:00:00Z',
  lastUpdated: { at: '2026-10-25T09:00:00Z', by: 'Member', userId: user.id, apiKeyId: null },
};

const team: TeamMember[] = [
  { id: 'm-1', userId: user.id, email: user.email, displayName: user.displayName, role: 'Owner', customRoleId: null, roleName: 'Owner', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
  {
    id: 'm-2',
    userId: 'user-robin',
    email: 'robin@brightwater.example.com',
    displayName: 'Robin Hale',
    role: 'Custom',
    customRoleId: interviewerRoleId,
    roleName: 'Interviewer',
    joinedAt: '2026-10-02T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0,
  },
];

function renderPage(roleId: string, routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/organization/roles': () => Response.json(roleCatalogue),
      [`GET /v1/organization/roles/${interviewerRoleId}`]: () => Response.json(detail),
      'GET /v1/organization/members': () => Response.json(team),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: { params: { roleId } } } });
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
  goto.mockClear();
});

describe('role page', () => {
  it('edits a custom role: its name and a checkbox per permission, with locked rows that say why', async () => {
    renderPage(interviewerRoleId);

    expect(await screen.findByRole('heading', { level: 1, name: 'Interviewer' })).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Interviewer');
    expect(screen.getByRole('checkbox', { name: /Fill in scorecards/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /Move candidates/ })).not.toBeChecked();
    expect(screen.queryByRole('checkbox', { name: /See jobs and candidates/ })).not.toBeInTheDocument();
    expect(screen.getByText('Every role has it.')).toBeInTheDocument();
    expect(screen.getAllByText('Owner only. A custom role cannot have it.')).toHaveLength(2);
    // A plain count, never "2 of 4", which reads as a score (E2).
    expect(within(screen.getByRole('region', { name: 'What this role can do' })).getByText('2')).toBeInTheDocument();
    expect(screen.queryByText(/\d of \d/)).not.toBeInTheDocument();
    expect(screen.getByText('Viewer')).toBeInTheDocument();
    expect(screen.getAllByText('Robin Hale').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'History' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('saves the name and the permissions ticked', async () => {
    const bodies: unknown[] = [];
    renderPage(interviewerRoleId, {
      [`PUT /v1/organization/roles/${interviewerRoleId}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(detail)),
    });
    await screen.findByLabelText('Name');

    await fireEvent.input(screen.getByLabelText('Name'), { target: { value: 'Panel interviewer' } });
    await fireEvent.click(screen.getByRole('checkbox', { name: /Move candidates/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(JSON.stringify(bodies[0])).toBe(JSON.stringify({ name: 'Panel interviewer', permissions: ['ViewHiring', 'MoveCandidates', 'FillInScorecards'] }));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('deletes the role through the More menu and a confirmation, then goes back to Roles', async () => {
    const fetchMock = renderPage(interviewerRoleId, { [`DELETE /v1/organization/roles/${interviewerRoleId}`]: () => new Response(null, { status: 204 }) });
    const [trigger] = await screen.findAllByRole('button', { name: 'More actions' });

    await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
    await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Delete role' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete the Interviewer role?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete role' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/v1/organization/roles/${interviewerRoleId}`, expect.objectContaining({ method: 'DELETE' })));
    await waitFor(() => expect(goto).toHaveBeenCalledWith('/settings/roles'));
  });

  it('shows a built-in role read only, with Copy role and no History', async () => {
    renderPage('viewer');

    expect(await screen.findByRole('heading', { level: 1, name: 'Viewer' })).toBeInTheDocument();
    expect(screen.getByText('Built in')).toBeInTheDocument();
    expect(screen.getByText(/ScreenHire supplies this role, so it cannot be changed/)).toBeInTheDocument();
    // Counted as the form counts: a plain number, never "N of M" (E2).
    expect(within(screen.getByRole('region', { name: 'What this role can do' })).getByText('1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy role' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'History' })).not.toBeInTheDocument();
    expect(screen.getByText('ScreenHire')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('offers no copy of the Owner role, in words or as a button', async () => {
    renderPage('owner');

    expect(await screen.findByText(/ScreenHire supplies this role, so it cannot be changed/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copy role' })).toBeNull();
    expect(screen.queryByText(/Copy it to make one of your own/)).toBeNull();
  });

  it('says why a built-in role is read only and offers the copy below the list, not under the title', async () => {
    renderPage('viewer');

    const reason = await screen.findByText(/Copy it to make one of your own/);
    const list = within(screen.getByRole('region', { name: 'What this role can do' })).getByText('See jobs and candidates');
    expect(list.compareDocumentPosition(reason) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('lists the people with a role in a sortable table, each with when they joined and Edit access in the row menu', async () => {
    renderPage(interviewerRoleId);

    const people = within(await screen.findByRole('region', { name: 'People with this role' }));
    const table = within(await people.findByRole('table'));
    for (const column of ['Name', 'Email', 'Jobs', 'Joined']) expect(table.getByRole('button', { name: new RegExp(column) })).toBeInTheDocument();
    expect(table.getByText('robin@brightwater.example.com')).toBeInTheDocument();
    expect(people.queryByRole('link')).toBeNull();

    await fireEvent.pointerDown(table.getByRole('button', { name: 'Actions for Robin Hale' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Edit access' }));
    expect(goto).toHaveBeenCalledWith('/settings/team?access=m-2');
    await expectNoAxeViolations();
  });

  it('offers no Edit access on your own row', async () => {
    renderPage('owner');

    const people = within(await screen.findByRole('region', { name: 'People with this role' }));
    expect(within(await people.findByRole('table')).getByText(user.email)).toBeInTheDocument();
    expect(people.queryByRole('button', { name: `Actions for ${user.displayName}` })).toBeNull();
  });

  it('says the role is gone when the API answers 404', async () => {
    renderPage('01a157d4-0000-7000-8000-0000000000dd', {
      'GET /v1/organization/roles/01a157d4-0000-7000-8000-0000000000dd': () => Response.json({ title: 'Not Found', status: 404 }, { status: 404 }),
    });

    expect(await screen.findByText('It was deleted, or it belongs to another organization.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to roles' })).toHaveAttribute('href', '/settings/roles');
  });
});
