import type { OrganizationTalentPoolSettings } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Nobody has changed the default yet.
const lastUpdated = { at: null, by: 'ScreenHire' as const };
const settings = (permissionMonths: number): OrganizationTalentPoolSettings => ({ permissionMonths, minMonths: 6, maxMonths: 24, lastUpdated });

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/talent-pool') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

function renderPage(current: OrganizationTalentPoolSettings, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/talent-pool-settings': () => Response.json(current),
        'PUT /v1/organization/talent-pool-settings': (init) => {
          const body = JSON.parse(String(init?.body)) as { permissionMonths: number };
          bodies.push(body);
          return Response.json({ ...current, permissionMonths: body.permissionMonths });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

const months = () => screen.findByLabelText('Permission lasts') as Promise<HTMLInputElement>;
const save = () => screen.findByRole('button', { name: 'Save' });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('talent pool settings page', () => {
  it('loads the permission period the organization has, 12 months by default', async () => {
    renderPage(settings(12));

    expect((await months()).value).toBe('12');
    expect(screen.getByText(/6 to 24 months/)).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('loads a period an owner changed', async () => {
    renderPage(settings(18));

    expect((await months()).value).toBe('18');
  });

  it('saves the new period as a number of months', async () => {
    const bodies = renderPage(settings(12));
    await fireEvent.input(await months(), { target: { value: '18' } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ permissionMonths: 18 }]));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it.each([['6'], ['24']])('accepts %s months, the edge of the range', async (value) => {
    const bodies = renderPage(settings(12));
    await fireEvent.input(await months(), { target: { value } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ permissionMonths: Number(value) }]));
  });

  it.each([['5'], ['25'], ['0']])('refuses %s months with a message and sends nothing', async (value) => {
    const bodies = renderPage(settings(12));
    await fireEvent.input(await months(), { target: { value } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter a number from 6 to 24.')).toBeInTheDocument();
    expect(await months()).toHaveAttribute('aria-invalid', 'true');
    expect(bodies).toEqual([]);
    expect(screen.queryByText('Saved.')).toBeNull();
  });

  it('refuses a period that is not a whole number, and sends nothing', async () => {
    const bodies = renderPage(settings(12));
    await fireEvent.input(await months(), { target: { value: '12.5' } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter a whole number of months.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('says why when the API refuses the period', async () => {
    renderPage(settings(12), {
      'PUT /v1/organization/talent-pool-settings': () => Response.json({ detail: 'Only owners and admins can change this.' }, { status: 403 }),
    });

    await fireEvent.click(await save());

    expect(await screen.findByText('Only owners and admins can change this.')).toBeInTheDocument();
  });

  it('shows a load failure with a way to retry', async () => {
    renderPage(settings(12), { 'GET /v1/organization/talent-pool-settings': () => Response.json({}, { status: 500 }) });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load the talent pool settings (HTTP 500).');
  });
});
