import type { ApiKeySummary, CurrentUser, OrganizationRole, PendingInvitation, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, interviewerRoleId, roleCatalogue, user, userAs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/team') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const member = (id: string, displayName: string, role: Exclude<OrganizationRole, 'Custom'>, userId = `user-${id}`): TeamMember => ({
  id,
  userId,
  email: `${displayName.split(' ')[0]!.toLowerCase()}@brightwater.example.com`,
  displayName,
  role,
  customRoleId: null,
  roleName: role,
  joinedAt: '2026-09-01T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0,
});

const asAdmin = (teammate: TeamMember): TeamMember => ({ ...teammate, role: 'Admin', roleName: 'Admin' });

// The signed-in person is `user` (Avery Quinn); the others are fictional colleagues.
const owner = member('m-1', 'Avery Quinn', 'Owner', user.id);
const otherOwner = member('m-2', 'Blake Rowan', 'Owner');
const admin = member('m-3', 'Casey Morgan', 'Admin');
const recruiter = member('m-4', 'Jordan Lee', 'Recruiter');

const invitations: PendingInvitation[] = [
  { id: 'i-1', email: 'sam@example.com', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', createdAt: '2026-10-18T09:00:00Z', expiresAt: '2026-10-25T09:00:00Z', jobAccess: 'All' as const },
  { id: 'i-2', email: 'robin@example.com', role: 'Owner', customRoleId: null, roleName: 'Owner', createdAt: '2026-10-18T09:00:00Z', expiresAt: '2026-10-25T09:00:00Z', jobAccess: 'All' as const },
];

// Nothing has changed the team since Avery created it.
const teamUpdated = { at: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(), by: 'Member', userId: user.id, apiKeyId: null, created: true };

const as = (role: Exclude<OrganizationRole, 'Custom'>): { data: { user: CurrentUser } } => ({ data: { user: userAs(role) } });

// The signed-in person is the owner by default; pass the team to put them elsewhere.
function renderPage(role: Exclude<OrganizationRole, 'Custom'>, routes: Parameters<typeof apiStub>[0] = {}, team: TeamMember[] = [owner, otherOwner, admin, recruiter]) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/organization/members': () => Response.json(team),
      'GET /v1/organization/invitations': () => Response.json(invitations),
      'GET /v1/organization/api-keys': () => Response.json([]),
      'GET /v1/organization/roles': () => Response.json(roleCatalogue),
      'GET /v1/organization/members/m-4/jobs': () => Response.json([{ id: 'j-1', title: 'Senior .NET Engineer', status: 'Open' }]),
      'GET /v1/organization/team/last-update': () => Response.json(teamUpdated),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: as(role) } });
  return fetchMock;
}

// bits-ui opens a menu on pointerdown. Rows render in a phone list and a table (no CSS in jsdom), so a row has two buttons.
async function openMenu(name: string) {
  const [trigger] = await screen.findAllByRole('button', { name });
  await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

const writes = (fetchMock: ReturnType<typeof renderPage>, method: string) => fetchMock.mock.calls.filter(([, init]) => init?.method === method);

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('team page', () => {
  it('lists the members with their roles and the open invitations', async () => {
    renderPage('Owner');

    expect((await screen.findAllByText('Casey Morgan')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('casey@brightwater.example.com').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Jordan Lee').length).toBeGreaterThan(0);
    expect((await screen.findAllByText('sam@example.com')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('robin@example.com').length).toBeGreaterThan(0);
    expect(screen.getAllByText('You').length).toBeGreaterThan(0);
    await expectNoAxeViolations();
  });

  it('says so when there are no open invitations', async () => {
    renderPage('Owner', { 'GET /v1/organization/invitations': () => Response.json([]) });

    expect(await screen.findByText('No open invitations.')).toBeInTheDocument();
  });

  it('sorts the members by name, and by access from the Role header (L2)', async () => {
    localStorage.clear();
    const viewer = member('m-5', 'Bailey Fox', 'Viewer');
    renderPage('Owner', {}, [recruiter, viewer, admin, owner]);
    await screen.findAllByText('Casey Morgan');
    const table = screen.getAllByRole('table')[0]!;
    const names = () => within(table).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell')[0]!.textContent?.replace('You', '').trim());

    expect(within(table).getByRole('columnheader', { name: /Name/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(names()).toEqual(['Avery Quinn', 'Bailey Fox', 'Casey Morgan', 'Jordan Lee']);

    await fireEvent.click(within(table).getByRole('button', { name: 'Role' }));

    expect(within(table).getByRole('columnheader', { name: /Role/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(names()).toEqual(['Avery Quinn', 'Casey Morgan', 'Jordan Lee', 'Bailey Fox']);
  });

  it('gives an Owner actions on everyone but themselves', async () => {
    renderPage('Owner');
    await screen.findAllByText('Casey Morgan');

    for (const name of ['Blake Rowan', 'Casey Morgan', 'Jordan Lee']) {
      expect(screen.getAllByRole('button', { name: `Actions for ${name}` }).length).toBeGreaterThan(0);
    }
    expect(screen.queryAllByRole('button', { name: 'Actions for Avery Quinn' })).toHaveLength(0);
  });

  it("gives an Admin no actions on an Owner's row, and none on their own", async () => {
    // Avery is the Admin here; Blake is the Owner.
    renderPage('Admin', {}, [asAdmin(owner), otherOwner, admin, recruiter]);
    await screen.findAllByText('Casey Morgan');

    expect(screen.queryAllByRole('button', { name: 'Actions for Avery Quinn' })).toHaveLength(0);
    expect(screen.queryAllByRole('button', { name: 'Actions for Blake Rowan' })).toHaveLength(0);
    expect(screen.getAllByRole('button', { name: 'Actions for Casey Morgan' }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('button', { name: 'Actions for Jordan Lee' }).length).toBeGreaterThan(0);
  });

  it("offers an Admin no menu on an Owner's open invitation", async () => {
    renderPage('Admin');
    await screen.findAllByText('sam@example.com');

    expect(screen.getAllByRole('button', { name: 'Actions for the invitation to sam@example.com' }).length).toBeGreaterThan(0);
    expect(screen.queryAllByRole('button', { name: 'Actions for the invitation to robin@example.com' })).toHaveLength(0);
  });

  it('gives a Recruiter no actions at all', async () => {
    renderPage('Recruiter');
    await screen.findAllByText('Casey Morgan');

    expect(screen.queryAllByRole('button', { name: /^Actions for/ })).toHaveLength(0);
  });

  // Edit access, from the row's menu: the sheet opens once the menu has closed.
  async function openAccessSheet(name: string) {
    const menu = await openMenu(`Actions for ${name}`);
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Edit access' }));
    return screen.findByRole('dialog', { name: `${name}'s access` });
  }

  const roleOptions = (sheet: HTMLElement) =>
    within(within(sheet).getByRole('combobox', { name: 'Role' })).getAllByRole('option').map((option) => option.textContent?.trim());

  it('says which jobs each member sees, in words: All jobs, or how many', async () => {
    renderPage('Owner', {}, [owner, { ...recruiter, jobAccess: 'HiringTeamOnly', seesAllJobs: false, jobCount: 2 }]);
    await screen.findAllByText('Jordan Lee');
    const table = screen.getAllByRole('table')[0]!;

    expect(within(table).getByRole('columnheader', { name: /Jobs/ })).toBeInTheDocument();
    expect(within(within(table).getAllByRole('row')[1]!).getByText('All jobs')).toBeInTheDocument();
    expect(within(table).getByText('2 jobs')).toBeInTheDocument();
  });

  it('offers an Owner every role to give a member in the access sheet', async () => {
    renderPage('Owner');
    const sheet = await openAccessSheet('Jordan Lee');

    expect(roleOptions(sheet)).toEqual(['Owner', 'Admin', 'Recruiter', 'Viewer', 'Interviewer']);
    expect(within(sheet).getByRole('combobox', { name: 'Role' })).toHaveValue('recruiter');
    expect(await within(sheet).findByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', '/jobs/j-1');
    await expectNoAxeViolations();
  });

  it('offers an Admin no Owner role to give a member', async () => {
    renderPage('Admin', {}, [asAdmin(owner), otherOwner, admin, recruiter]);

    expect(roleOptions(await openAccessSheet('Jordan Lee'))).toEqual(['Admin', 'Recruiter', 'Viewer', 'Interviewer']);
  });

  it('shows a custom role by its name alone, and gives it by its id', async () => {
    const robin: TeamMember = { ...member('m-6', 'Robin Hale', 'Viewer'), role: 'Custom', customRoleId: interviewerRoleId, roleName: 'Interviewer' };
    const bodies: unknown[] = [];
    renderPage('Owner', {
      'PUT /v1/organization/members/m-4/role': (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(recruiter)),
    }, [owner, recruiter, robin]);

    expect((await screen.findAllByText('Interviewer')).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Custom/)).not.toBeInTheDocument();
    const sheet = await openAccessSheet('Jordan Lee');
    await fireEvent.change(within(sheet).getByRole('combobox', { name: 'Role' }), { target: { value: interviewerRoleId } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save access' }));

    await waitFor(() => expect(bodies).toEqual([{ role: 'Custom', customRoleId: interviewerRoleId }]));
  });

  it("changes a member's role and job access from the access sheet, then closes it", async () => {
    const roles: unknown[] = [];
    const access: unknown[] = [];
    renderPage('Owner', {
      'PUT /v1/organization/members/m-4/role': (init) => (roles.push(JSON.parse(String(init?.body))), Response.json({ ...recruiter, role: 'Viewer' })),
      'PUT /v1/organization/members/m-4/job-access': (init) => (access.push(JSON.parse(String(init?.body))), Response.json(recruiter)),
    });
    const sheet = await openAccessSheet('Jordan Lee');

    await fireEvent.change(within(sheet).getByRole('combobox', { name: 'Role' }), { target: { value: 'viewer' } });
    await fireEvent.click(within(sheet).getByRole('radio', { name: /^Only jobs they're on/ }));
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save access' }));

    await waitFor(() => expect(roles).toEqual([{ role: 'Viewer', customRoleId: null }]));
    await waitFor(() => expect(access).toEqual([{ jobAccess: 'HiringTeamOnly' }]));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('says Owners and Admins see all jobs instead of offering the choice', async () => {
    renderPage('Owner');
    const sheet = await openAccessSheet('Jordan Lee');

    await fireEvent.change(within(sheet).getByRole('combobox', { name: 'Role' }), { target: { value: 'admin' } });

    expect(within(sheet).getByText('Owners and Admins see all jobs.')).toBeInTheDocument();
    expect(within(sheet).queryByRole('radiogroup', { name: 'Jobs' })).not.toBeInTheDocument();
  });

  describe('inviting', () => {
    async function openInviteSheet() {
      await screen.findAllByText('Casey Morgan');
      await fireEvent.click(screen.getByRole('button', { name: 'Invite' }));
      return screen.findByRole('dialog', { name: 'Invite a colleague' });
    }

    const roleChoices = (sheet: HTMLElement) => within(within(sheet).getByRole('radiogroup', { name: 'Role' })).getAllByRole('radio');
    const roleNames = (sheet: HTMLElement) => roleChoices(sheet).map((radio) => radio.getAttribute('value'));

    it('offers an Owner every role in the invite sheet, starting at Viewer, the least access', async () => {
      renderPage('Owner');
      const sheet = await openInviteSheet();

      expect(roleNames(sheet)).toEqual(['owner', 'admin', 'recruiter', 'viewer', interviewerRoleId]);
      expect(within(sheet).getByRole('radio', { name: /^Viewer/ })).toBeChecked();
      await expectNoAxeViolations();
    });

    it('describes each role from the permission catalogue and links to Compare roles', async () => {
      renderPage('Owner');
      const sheet = await openInviteSheet();

      expect(await within(sheet).findByRole('radio', { name: 'Viewer Sees jobs and candidates and changes nothing.' })).toBeInTheDocument();
      expect(within(sheet).getByRole('link', { name: 'Compare roles' })).toHaveAttribute('href', '/settings/roles/compare');
    });

    it('offers an Admin no Owner option in the invite sheet', async () => {
      renderPage('Admin');

      expect(roleNames(await openInviteSheet())).toEqual(['admin', 'recruiter', 'viewer', interviewerRoleId]);
    });

    it('posts the email and the role chosen, then closes the sheet', async () => {
      const bodies: unknown[] = [];
      renderPage('Owner', {
        'POST /v1/organization/invitations': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(invitations[0], { status: 201 });
        },
      });
      const sheet = await openInviteSheet();

      await fireEvent.input(within(sheet).getByLabelText('Email'), { target: { value: 'taylor@example.com' } });
      await fireEvent.click(within(sheet).getByRole('radio', { name: /^Recruiter/ }));
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Send invitation' }));

      await waitFor(() => expect(bodies).toEqual([{ email: 'taylor@example.com', role: 'Recruiter', customRoleId: null, jobAccess: 'All' }]));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('starts an invitation at All jobs, with Only jobs they are on one click away', async () => {
      const bodies: unknown[] = [];
      renderPage('Owner', {
        'POST /v1/organization/invitations': (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(invitations[0], { status: 201 })),
      });
      const sheet = await openInviteSheet();

      expect(within(sheet).getByRole('radio', { name: /^All jobs/ })).toBeChecked();
      await fireEvent.input(within(sheet).getByLabelText('Email'), { target: { value: 'taylor@example.com' } });
      await fireEvent.click(within(sheet).getByRole('radio', { name: /^Only jobs they're on/ }));
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Send invitation' }));

      await waitFor(() => expect(bodies).toEqual([{ email: 'taylor@example.com', role: 'Viewer', customRoleId: null, jobAccess: 'HiringTeamOnly' }]));
    });

    it('asks for an email address before sending anything', async () => {
      const fetchMock = renderPage('Owner');
      const dialog = await openInviteSheet();

      await fireEvent.click(within(dialog).getByRole('button', { name: 'Send invitation' }));

      expect(await within(dialog).findByText('Enter an email address.')).toBeInTheDocument();
      expect(writes(fetchMock, 'POST')).toHaveLength(0);
    });

    it('says email is not set up when the API answers 404', async () => {
      renderPage('Owner', { 'POST /v1/organization/invitations': () => new Response(null, { status: 404 }) });
      const dialog = await openInviteSheet();

      await fireEvent.input(within(dialog).getByLabelText('Email'), { target: { value: 'taylor@example.com' } });
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Send invitation' }));

      expect(await within(dialog).findByText('Email is not set up here, so invitations cannot be sent.')).toBeInTheDocument();
      expect(screen.getByRole('dialog', { name: 'Invite a colleague' })).toBeInTheDocument();
    });
  });

  it('removes a member through the menu and a confirmation, with a DELETE', async () => {
    const fetchMock = renderPage('Owner', { 'DELETE /v1/organization/members/m-4': () => new Response(null, { status: 204 }) });
    const menu = await openMenu('Actions for Jordan Lee');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove Jordan Lee from the team?' });
    expect(writes(fetchMock, 'DELETE')).toHaveLength(0);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove from team' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/members/m-4', expect.objectContaining({ method: 'DELETE' })));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  describe('the API keys a removed member created', () => {
    const key = (id: string, createdByUserId: string | null, revokedAt: string | null = null): ApiKeySummary => ({
      id,
      name: `Key ${id}`,
      prefix: `shk_${id}`,
      scopes: ['Read'],
      permitsPerMinute: null,
      createdAt: '2026-10-01T09:00:00Z',
      lastUsedAt: null,
      revokedAt,
      createdByUserId,
    });
    // Jordan created two active keys and one already revoked; Avery created another.
    const keys = [key('k-1', recruiter.userId), key('k-2', recruiter.userId), key('k-3', recruiter.userId, '2026-10-02T09:00:00Z'), key('k-4', owner.userId)];
    const routes = {
      'GET /v1/organization/api-keys': () => Response.json(keys),
      'DELETE /v1/organization/members/m-4': () => new Response(null, { status: 204 }),
      'POST /v1/organization/api-keys/k-1/revoke': () => Response.json({ ...keys[0], revokedAt: '2026-10-23T09:00:00Z' }),
      'POST /v1/organization/api-keys/k-2/revoke': () => Response.json({ ...keys[1], revokedAt: '2026-10-23T09:00:00Z' }),
    };
    const revoked = (fetchMock: ReturnType<typeof renderPage>) =>
      writes(fetchMock, 'POST')
        .map(([url]) => String(url))
        .filter((url) => url.endsWith('/revoke'));

    it('are revoked with the member unless the person unticks it', async () => {
      const fetchMock = renderPage('Owner', routes);
      await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/api-keys', expect.anything()));
      const menu = await openMenu('Actions for Jordan Lee');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
      const dialog = await screen.findByRole('dialog', { name: 'Remove Jordan Lee from the team?' });

      const choice = within(dialog).getByRole('checkbox', { name: 'Also revoke the 2 API keys they created' });
      expect(choice).toBeChecked();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove from team' }));

      await waitFor(() => expect(revoked(fetchMock).toSorted()).toEqual(['/v1/organization/api-keys/k-1/revoke', '/v1/organization/api-keys/k-2/revoke']));
      expect(fetchMock).toHaveBeenCalledWith('/v1/organization/members/m-4', expect.objectContaining({ method: 'DELETE' }));
    });

    it('stay when the person unticks the choice', async () => {
      const fetchMock = renderPage('Owner', routes);
      await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/api-keys', expect.anything()));
      const menu = await openMenu('Actions for Jordan Lee');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
      const dialog = await screen.findByRole('dialog', { name: 'Remove Jordan Lee from the team?' });

      await fireEvent.click(within(dialog).getByRole('checkbox', { name: /Also revoke/ }));
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove from team' }));

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(fetchMock).toHaveBeenCalledWith('/v1/organization/members/m-4', expect.objectContaining({ method: 'DELETE' }));
      expect(revoked(fetchMock)).toEqual([]);
    });

    it('are not mentioned for a member who created none', async () => {
      renderPage('Owner', { ...routes, 'DELETE /v1/organization/members/m-3': () => new Response(null, { status: 204 }) });
      const menu = await openMenu('Actions for Casey Morgan');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
      const dialog = await screen.findByRole('dialog', { name: 'Remove Casey Morgan from the team?' });

      expect(within(dialog).queryByRole('checkbox')).toBeNull();
    });

    it('are flagged when the keys could not be loaded, so none is left working unseen', async () => {
      const fetchMock = renderPage('Owner', { ...routes, 'GET /v1/organization/api-keys': () => new Response(null, { status: 403 }) });
      await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/api-keys', expect.anything()));
      const menu = await openMenu('Actions for Jordan Lee');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
      const dialog = await screen.findByRole('dialog', { name: 'Remove Jordan Lee from the team?' });

      expect(await within(dialog).findByText(/Their API keys could not be checked/)).toBeInTheDocument();
      expect(within(dialog).queryByRole('checkbox')).toBeNull();
    });
  });

  it('removes nobody when the person backs out of the confirmation', async () => {
    const fetchMock = renderPage('Owner');
    const menu = await openMenu('Actions for Jordan Lee');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove Jordan Lee from the team?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(writes(fetchMock, 'DELETE')).toHaveLength(0);
  });

  it('keeps the confirmation open and says why when the API refuses the removal', async () => {
    renderPage('Owner', {
      'DELETE /v1/organization/members/m-4': () => Response.json({ detail: 'An organization needs one owner.' }, { status: 409 }),
    });
    const menu = await openMenu('Actions for Jordan Lee');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from team' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove from team' }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('An organization needs one owner.'));
    expect(dialog).toBeInTheDocument();
  });

  it('revokes an invitation through the menu and a confirmation', async () => {
    const fetchMock = renderPage('Owner', { 'DELETE /v1/organization/invitations/i-1': () => new Response(null, { status: 204 }) });
    const menu = await openMenu('Actions for the invitation to sam@example.com');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Revoke invitation' }));
    const dialog = await screen.findByRole('dialog', { name: 'Revoke the invitation to sam@example.com?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke invitation' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/invitations/i-1', expect.objectContaining({ method: 'DELETE' })));
  });

  it('says on the title row that Avery created the team, and History lists its changes in words (T6, S8)', async () => {
    const fetchMock = renderPage('Owner', {
      'GET /v1/organization/team/history': () =>
        Response.json({
          entries: [
            {
              id: 'c-1', at: teamUpdated.at, by: 'Member', userId: user.id, apiKeyId: null,
              lines: [{ text: 'created the team', parts: [{ text: 'created the team', strong: false }], details: [] }],
            },
          ],
          nextBefore: null,
        }),
    });

    const updated = await waitFor(() => {
      const fact = document.querySelector('[data-last-updated]');
      expect(fact).not.toBeNull();
      return fact!;
    });
    expect(updated).toHaveTextContent(/^Created:\s*Created 2 hours ago by you/);
    expect(updated.closest('[data-slot="header-band"]')?.querySelector('dl')).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: /^History/ }));
    const sheet = await screen.findByRole('dialog', { name: 'History' });
    expect(await within(sheet).findByText('created the team')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith('/v1/organization/team/history', expect.anything());
  });

  it('reads Last updated again after a change to the team', async () => {
    const fetchMock = renderPage('Owner', { 'DELETE /v1/organization/invitations/i-1': () => new Response(null, { status: 204 }) });
    const reads = () => fetchMock.mock.calls.filter(([url, init]) => url === '/v1/organization/team/last-update' && (init?.method ?? 'GET') === 'GET').length;
    await waitFor(() => expect(reads()).toBe(1));

    await fireEvent.click(within(await openMenu('Actions for the invitation to sam@example.com')).getByRole('menuitem', { name: 'Revoke invitation' }));
    const dialog = await screen.findByRole('dialog', { name: 'Revoke the invitation to sam@example.com?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke invitation' }));

    await waitFor(() => expect(reads()).toBe(2));
  });
});
