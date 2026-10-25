import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../../test-support/axe.js';
import { apiStub, interviewerRoleId, roleCatalogue, user } from '../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/roles/compare') }, navigating: { to: null } }));

function renderPage(roles: () => Response = () => Response.json(roleCatalogue)) {
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/organization/roles': roles })));
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('compare roles page', () => {
  it('shows what each role can do, built-in and custom, from the catalogue the API serves, grouped as it lists them', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;

    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual([
      'Permission',
      'Covers',
      'Owner',
      'Admin',
      'Recruiter',
      'Viewer',
      'Interviewer',
    ]);
    expect(within(table).getByRole('link', { name: 'Interviewer' })).toHaveAttribute('href', `/settings/roles/${interviewerRoleId}`);
    expect(within(table).getByRole('link', { name: 'Viewer' })).toHaveAttribute('href', '/settings/roles/viewer');
    expect(within(table).getAllByRole('rowheader').map((header) => header.textContent?.trim())).toEqual([
      'Hiring',
      'See jobs and candidates',
      'Move candidates',
      'Fill in scorecards',
      'Organization',
      'Manage the team',
      'Manage Owners',
    ]);
    const cells = (name: string) =>
      within(within(table).getByRole('rowheader', { name }).closest('tr')!)
        .getAllByRole('cell')
        .slice(1)
        .map((cell) => cell.textContent?.trim());
    expect(cells('Move candidates')).toEqual(['Yes', 'Yes', 'Yes', 'No', 'No']);
    expect(cells('Fill in scorecards')).toEqual(['Yes', 'Yes', 'Yes', 'No', 'Yes']);
    expect(cells('Manage Owners')).toEqual(['Yes', 'No', 'No', 'No', 'No']);
    const keys = screen.getByText(/An API key has a scope instead of a role/);
    expect(within(keys).getByRole('link', { name: 'API keys' })).toHaveAttribute('href', '/settings/api-keys');
    await expectNoAxeViolations();
  });

  it('is a fixed grid: no column sorts (L2)', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;

    expect(within(table).queryAllByRole('button')).toHaveLength(0);
  });

  it('lists the roles that have each permission on a phone', async () => {
    renderPage();

    const item = (await screen.findAllByText('Fill in scorecards')).find((element) => element.closest('li'))!.closest('li')!;
    expect(within(item).getAllByText(/^(Owner|Admin|Recruiter|Viewer|Interviewer)$/).map((pill) => pill.textContent)).toEqual([
      'Owner',
      'Admin',
      'Recruiter',
      'Interviewer',
    ]);
  });

  it('says so and offers a retry when the roles cannot load', async () => {
    renderPage(() => new Response(null, { status: 500 }));

    expect(await screen.findByRole('button', { name: /Try again/ })).toBeInTheDocument();
  });
});
