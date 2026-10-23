import type { WebhookEndpointEntry } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../../test-support/axe.js';
import { apiStub, user } from '../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/webhooks/new') }, navigating: { to: null } }));
// Creating an endpoint opens it; superforms needs the rest of the module (beforeNavigate).
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

const created: WebhookEndpointEntry = {
  id: 'e9',
  url: 'https://hooks.example.com/applicants',
  description: 'Applicant sync',
  events: ['application.submitted'],
  enabled: true,
  consecutiveFailures: 0,
  disabledAt: null,
  disabledReason: null,
  createdAt: '2026-10-20T09:00:00Z',
  updatedAt: '2026-10-20T09:00:00Z',
  secretRotatedAt: '2026-10-20T09:00:00Z',
};

const signingSecret = 'whsec_0123456789abcdef0123456789abcdef';

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/webhooks/event-types': () => Response.json(['application.submitted', 'application.moved', 'job.published']),
        'POST /v1/webhooks': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json({ endpoint: created, signingSecret }, { status: 201 });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

async function fillAndSubmit() {
  const form = await screen.findByRole('form', { name: 'New webhook endpoint' });
  await fireEvent.input(within(form).getByLabelText('URL'), { target: { value: ' https://hooks.example.com/applicants ' } });
  await fireEvent.input(within(form).getByLabelText(/Description/), { target: { value: 'Applicant sync' } });
  await fireEvent.click(await within(form).findByRole('checkbox', { name: /Application submitted/ }));
  await fireEvent.click(within(form).getByRole('button', { name: 'Add endpoint' }));
  return form;
}

afterEach(() => {
  vi.unstubAllGlobals();
  navigation.goto.mockClear();
});

describe('new webhook endpoint page', () => {
  it('offers one checkbox per event type the API lists', async () => {
    renderPage();

    const form = await screen.findByRole('form', { name: 'New webhook endpoint' });
    expect(await within(form).findAllByRole('checkbox')).toHaveLength(3);
    expect(within(form).getByText('Job published')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('creates the endpoint with a trimmed URL, the description and the chosen events', async () => {
    const bodies = renderPage();

    await fillAndSubmit();

    await waitFor(() =>
      expect(bodies).toEqual([{ url: 'https://hooks.example.com/applicants', description: 'Applicant sync', events: ['application.submitted'] }]),
    );
  });

  it('sends a blank description as null', async () => {
    const bodies = renderPage();
    const form = await screen.findByRole('form', { name: 'New webhook endpoint' });

    await fireEvent.input(within(form).getByLabelText('URL'), { target: { value: 'https://hooks.example.com/applicants' } });
    await fireEvent.click(await within(form).findByRole('checkbox', { name: /Job published/ }));
    await fireEvent.click(within(form).getByRole('button', { name: 'Add endpoint' }));

    await waitFor(() => expect(bodies).toEqual([{ url: 'https://hooks.example.com/applicants', description: null, events: ['job.published'] }]));
  });

  it('shows the signing secret once, and Done opens the new endpoint', async () => {
    renderPage();

    await fillAndSubmit();

    const dialog = await screen.findByRole('dialog', { name: 'Your signing secret' });
    expect(within(dialog).getByDisplayValue(signingSecret)).toBeInTheDocument();
    expect(navigation.goto).not.toHaveBeenCalled();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }));

    await waitFor(() => expect(navigation.goto).toHaveBeenCalledWith('/settings/webhooks/e9'));
    expect(navigation.goto).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByDisplayValue(signingSecret)).not.toBeInTheDocument());
  });

  it('asks for an address and at least one event, and sends nothing', async () => {
    const bodies = renderPage();
    const form = await screen.findByRole('form', { name: 'New webhook endpoint' });
    await within(form).findAllByRole('checkbox');

    await fireEvent.click(within(form).getByRole('button', { name: 'Add endpoint' }));

    expect(await within(form).findByText('Enter the address to send events to.')).toBeInTheDocument();
    expect(within(form).getByText('Choose at least one event.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('shows why the API refused the address and shows no secret', async () => {
    renderPage({ 'POST /v1/webhooks': () => Response.json({ detail: 'The address must be a public https URL.' }, { status: 422 }) });

    const form = await fillAndSubmit();

    expect(await within(form).findByText('The address must be a public https URL.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
