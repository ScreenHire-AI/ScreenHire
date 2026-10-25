import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, interviewerRoleId, roleCatalogue, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const { goto } = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/roles') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto }));

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(apiStub({ 'GET /v1/organization/roles': () => Response.json(roleCatalogue), ...routes }));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });
  return fetchMock;
}

// bits-ui opens a menu on pointerdown. Rows render in a phone list and a table (no CSS in jsdom), so a row has one menu.
async function openMenu(name: string) {
  const [trigger] = await screen.findAllByRole('button', { name });
  await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

afterEach(() => {
  vi.unstubAllGlobals();
  goto.mockClear();
});

describe('roles page', () => {
  it('lists every role with what it can do, marking only the built-in ones', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;
    const rows = within(table).getAllByRole('row').slice(1);

    expect(rows.map((row) => within(row).getAllByRole('cell')[0]!.textContent?.trim())).toEqual([
      'Owner Built in',
      'Admin Built in',
      'Recruiter Built in',
      'Viewer Built in',
      'Interviewer',
    ]);
    expect(within(table).getByRole('link', { name: 'Interviewer' })).toHaveAttribute('href', `/settings/roles/${interviewerRoleId}`);
    expect(within(table).getByRole('link', { name: 'Viewer' })).toHaveAttribute('href', '/settings/roles/viewer');
    expect(screen.queryByText(/Custom/)).not.toBeInTheDocument();
    expect(within(rows[0]!).getByText('By ScreenHire')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Compare roles' })).toHaveAttribute('href', '/settings/roles/compare');
    await expectNoAxeViolations();
  });

  it('makes a role by copying one, then opens its page', async () => {
    const bodies: unknown[] = [];
    renderPage({
      'POST /v1/organization/roles': (init) => {
        bodies.push(JSON.parse(String(init?.body)));
        return Response.json({ id: 'new-role' }, { status: 201 });
      },
    });
    await screen.findAllByRole('table');

    await fireEvent.click(screen.getByRole('button', { name: 'New role' }));
    const sheet = await screen.findByRole('dialog', { name: 'New role' });
    const sources = within(within(sheet).getByRole('radiogroup', { name: 'Copy permissions from' })).getAllByRole('radio');
    expect(sources.map((radio) => radio.getAttribute('value'))).toEqual(['admin', 'recruiter', 'viewer', interviewerRoleId]);
    expect(within(sheet).getByRole('radio', { name: /^Viewer/ })).toBeChecked();

    await fireEvent.input(within(sheet).getByLabelText('Name'), { target: { value: 'Hiring manager' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Create and edit' }));

    await waitFor(() => expect(bodies).toEqual([{ name: 'Hiring manager', copyFrom: 'Viewer', copyFromRoleId: null }]));
    await waitFor(() => expect(goto).toHaveBeenCalledWith('/settings/roles/new-role'));
  });

  it('copies a custom role by its id', async () => {
    const bodies: unknown[] = [];
    renderPage({
      'POST /v1/organization/roles': (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json({ id: 'copy' }, { status: 201 })),
    });
    const menu = await openMenu('Actions for Interviewer');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Copy role' }));
    const sheet = await screen.findByRole('dialog', { name: 'New role' });
    await waitFor(() => expect(within(sheet).getByRole('radio', { name: /^Interviewer/ })).toBeChecked());
    await fireEvent.input(within(sheet).getByLabelText('Name'), { target: { value: 'Panel interviewer' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Create and edit' }));

    await waitFor(() => expect(bodies).toEqual([{ name: 'Panel interviewer', copyFrom: null, copyFromRoleId: interviewerRoleId }]));
  });

  it('shows the name the API refused under the field', async () => {
    renderPage({
      'POST /v1/organization/roles': () =>
        Response.json({ title: 'One or more validation errors occurred.', errors: { name: ['Another role has this name. Choose another.'] } }, { status: 400 }),
    });
    await screen.findAllByRole('table');
    await fireEvent.click(screen.getByRole('button', { name: 'New role' }));
    const sheet = await screen.findByRole('dialog', { name: 'New role' });

    await fireEvent.input(within(sheet).getByLabelText('Name'), { target: { value: 'interviewer' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Create and edit' }));

    expect(await within(sheet).findByText('Another role has this name. Choose another.')).toBeInTheDocument();
    expect(goto).not.toHaveBeenCalled();
  });

  it('offers to delete only a custom role, and says why the API refused', async () => {
    renderPage({
      'DELETE /v1/organization/roles/01a157d4-0000-7000-8000-0000000000cc': () =>
        Response.json({ title: 'Role in use', detail: '1 person has this role. Give them another role first.', status: 409 }, { status: 409 }),
    });

    const builtIn = await openMenu('Actions for Viewer');
    expect(within(builtIn).queryByRole('menuitem', { name: 'Delete role' })).not.toBeInTheDocument();
    await fireEvent.keyDown(builtIn, { key: 'Escape' });

    const menu = await openMenu('Actions for Interviewer');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Delete role' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete the Interviewer role?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete role' }));

    expect(await within(dialog).findByText(/1 person has this role/)).toBeInTheDocument();
  });

  it('says so and offers a retry when the roles cannot load', async () => {
    renderPage({ 'GET /v1/organization/roles': () => new Response(null, { status: 500 }) });

    expect(await screen.findByRole('button', { name: /Try again/ })).toBeInTheDocument();
  });
});
