import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, roleCatalogue, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/roles') }, navigating: { to: null } }));

function renderPage(roles: () => Response = () => Response.json(roleCatalogue)) {
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/organization/roles': roles })));
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('roles page', () => {
  it('shows what each role can do, from the catalogue the API serves, grouped as it lists them', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;

    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual([
      'Permission',
      'Covers',
      'Owner',
      'Admin',
      'Recruiter',
      'Viewer',
    ]);
    expect(within(table).getAllByRole('rowheader').map((header) => header.textContent?.trim())).toEqual([
      'Hiring',
      'See jobs and candidates',
      'Move candidates',
      'Organization',
      'Manage the team',
      'Manage Owners',
    ]);
    const cells = (name: string) =>
      within(within(table).getByRole('rowheader', { name }).closest('tr')!)
        .getAllByRole('cell')
        .slice(1)
        .map((cell) => cell.textContent?.trim());
    expect(cells('Move candidates')).toEqual(['Yes', 'Yes', 'Yes', 'No']);
    expect(cells('Manage Owners')).toEqual(['Yes', 'No', 'No', 'No']);
    expect(screen.getByText(/An API key has a scope instead of a role/)).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('is a fixed grid: no column sorts (L2)', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;

    expect(within(table).queryAllByRole('button')).toHaveLength(0);
  });

  it('lists the roles that have each permission on a phone', async () => {
    renderPage();

    const item = (await screen.findAllByText('Manage the team')).find((element) => element.closest('li'))!.closest('li')!;
    expect(within(item).getAllByText(/^(Owner|Admin|Recruiter|Viewer)$/).map((pill) => pill.textContent)).toEqual(['Owner', 'Admin']);
  });

  it('says so and offers a retry when the roles cannot load', async () => {
    renderPage(() => new Response(null, { status: 500 }));

    expect(await screen.findByRole('button', { name: /Try again/ })).toBeInTheDocument();
  });
});
