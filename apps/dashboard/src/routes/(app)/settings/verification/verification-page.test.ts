import type { OrganizationVerificationSettings } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Nobody has changed the defaults yet (T6).
const lastUpdated = { at: null, by: 'ScreenHire' as const };

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/integrity') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

function renderPage(settings: OrganizationVerificationSettings, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/verification-settings': () => Response.json(settings),
        'PUT /v1/organization/verification-settings': (init) => {
          const body = JSON.parse(String(init?.body)) as Omit<OrganizationVerificationSettings, 'tokensUsedThisMonth'>;
          bodies.push(body);
          return Response.json({ ...body, tokensUsedThisMonth: settings.tokensUsedThisMonth });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

const check = () => screen.findByRole('switch', { name: /Compare each application's sources/ });
const budget = () => screen.findByLabelText(/Monthly token budget/) as Promise<HTMLInputElement>;
const save = () => screen.findByRole('button', { name: 'Save' });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('verification settings page', () => {
  it('loads the switch and the budget', async () => {
    renderPage({ llmConsistencyCheck: true, monthlyTokenBudget: 250000, tokensUsedThisMonth: 420, lastUpdated });

    expect(await check()).toBeChecked();
    expect((await budget()).value).toBe('250000');
    await expectNoAxeViolations();
  });

  it('loads a switch that is off and an empty budget', async () => {
    renderPage({ llmConsistencyCheck: false, monthlyTokenBudget: null, tokensUsedThisMonth: 0, lastUpdated });

    expect(await check()).not.toBeChecked();
    expect((await budget()).value).toBe('');
  });

  it('shows the tokens used this month', async () => {
    renderPage({ llmConsistencyCheck: true, monthlyTokenBudget: null, tokensUsedThisMonth: 420, lastUpdated });

    expect(await screen.findByText('Tokens used')).toBeInTheDocument();
    expect(screen.getByText('420')).toBeInTheDocument();
  });

  it('saves the switch and the budget as a number', async () => {
    const bodies = renderPage({ llmConsistencyCheck: false, monthlyTokenBudget: null, tokensUsedThisMonth: 0, lastUpdated });
    await fireEvent.click(await check());
    await fireEvent.input(await budget(), { target: { value: '120000' } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ llmConsistencyCheck: true, monthlyTokenBudget: 120000 }]));
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
  });

  it('saves an empty budget as null', async () => {
    const bodies = renderPage({ llmConsistencyCheck: true, monthlyTokenBudget: 250000, tokensUsedThisMonth: 0, lastUpdated });
    await fireEvent.input(await budget(), { target: { value: '' } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ llmConsistencyCheck: true, monthlyTokenBudget: null }]));
  });

  it('turns the check off', async () => {
    const bodies = renderPage({ llmConsistencyCheck: true, monthlyTokenBudget: null, tokensUsedThisMonth: 0, lastUpdated });
    await fireEvent.click(await check());

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ llmConsistencyCheck: false, monthlyTokenBudget: null }]));
  });

  it('refuses a budget that is not a whole number, and sends nothing', async () => {
    const bodies = renderPage({ llmConsistencyCheck: true, monthlyTokenBudget: null, tokensUsedThisMonth: 0, lastUpdated });
    await fireEvent.input(await budget(), { target: { value: '12.5k' } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter a whole number of tokens, or leave it empty.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('says why when the API refuses the settings', async () => {
    renderPage(
      { llmConsistencyCheck: true, monthlyTokenBudget: null, tokensUsedThisMonth: 0, lastUpdated },
      { 'PUT /v1/organization/verification-settings': () => Response.json({ detail: 'Only owners and admins can change this.' }, { status: 403 }) },
    );

    await fireEvent.click(await save());

    expect(await screen.findByText('Only owners and admins can change this.')).toBeInTheDocument();
  });
});
