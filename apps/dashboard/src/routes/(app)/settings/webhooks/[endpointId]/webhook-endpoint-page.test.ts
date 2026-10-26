import type { WebhookDeliveryEntry, WebhookEndpointEntry } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../../test-support/axe.js';
import { apiStub, user } from '../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/webhooks/e1') }, navigating: { to: null } }));
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

const endpointId = 'e1';

const endpoint = (overrides: Partial<WebhookEndpointEntry> = {}): WebhookEndpointEntry => ({
  id: endpointId,
  url: 'https://hooks.example.com/applicants',
  description: 'Applicant sync',
  events: ['application.submitted', 'job.stage_changed'],
  enabled: true,
  consecutiveFailures: 0,
  disabledAt: null,
  disabledReason: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: '2026-10-01T09:00:00Z',
  secretRotatedAt: '2026-10-01T09:00:00Z',
  ...overrides,
});

const delivery = (id: string, status: WebhookDeliveryEntry['status'], eventType: string, overrides: Partial<WebhookDeliveryEntry> = {}): WebhookDeliveryEntry => ({
  id,
  eventId: `event-${id}`,
  eventType,
  status,
  attempts: 1,
  lastStatusCode: null,
  lastError: null,
  replayOfId: null,
  createdAt: '2026-10-20T09:00:00Z',
  lastAttemptAt: null,
  nextAttemptAt: null,
  completedAt: null,
  payload: '{}',
  ...overrides,
});

// One delivery per status, each with its own event type so a replay button is named after exactly one of them.
const log = [
  delivery('d1', 'Succeeded', 'application.submitted', { lastStatusCode: 200 }),
  delivery('d2', 'Failed', 'application.moved', { attempts: 5, lastStatusCode: 500, lastError: 'Receiver unavailable' }),
  delivery('d3', 'Pending', 'job.published'),
  delivery('d4', 'Skipped', 'job.closed'),
];

function renderPage(detail: WebhookEndpointEntry, routes: Parameters<typeof apiStub>[0] = {}, deliveries: WebhookDeliveryEntry[] = log) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/webhooks/${endpointId}`]: () => Response.json(detail),
      [`GET /v1/webhooks/${endpointId}/deliveries`]: () => Response.json(deliveries),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: { params: { endpointId } } } });
  return fetchMock;
}

// bits-ui opens a menu on pointerdown.
async function openMoreActions() {
  await fireEvent.pointerDown(await screen.findByRole('button', { name: 'More actions' }), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

afterEach(() => {
  vi.unstubAllGlobals();
  navigation.goto.mockClear();
});

describe('webhook endpoint page', () => {
  it('shows the delivery log with a status label for each delivery and each event in words, and the events with their codes', async () => {
    renderPage(endpoint());

    const deliveries = await screen.findByRole('region', { name: 'Deliveries' });
    await within(deliveries).findAllByText('Application submitted');
    expect(within(deliveries).queryByText('application.submitted')).toBeNull();
    // Sorted like every table, newest first (L2).
    expect(within(deliveries).getByRole('columnheader', { name: /^Sent/ })).toHaveAttribute('aria-sort', 'descending');
    for (const label of ['Delivered', 'Failed', 'Pending', 'Skipped']) {
      expect(within(deliveries).getAllByText(label).length).toBeGreaterThan(0);
    }
    expect(within(deliveries).getAllByText('HTTP 500, Receiver unavailable').length).toBeGreaterThan(0);

    const events = screen.getByRole('region', { name: 'Events' });
    expect(within(events).getByText('application.submitted')).toBeInTheDocument();
    expect(within(events).getByText('job.stage_changed')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says there are no deliveries yet', async () => {
    renderPage(endpoint(), {}, []);

    expect(await screen.findByText('No deliveries yet.')).toBeInTheDocument();
  });

  it('replays a finished delivery with a POST', async () => {
    const fetchMock = renderPage(endpoint(), { [`POST /v1/webhooks/${endpointId}/deliveries/d2/replay`]: () => Response.json({}, { status: 202 }) });

    await fireEvent.click((await screen.findAllByRole('button', { name: 'Replay the application.moved delivery' }))[0]!);

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(`/v1/webhooks/${endpointId}/deliveries/d2/replay`, expect.objectContaining({ method: 'POST' })),
    );
  });

  it('offers replay on delivered, failed and skipped deliveries but not on a Pending one', async () => {
    renderPage(endpoint());
    await screen.findAllByText('Job published');

    for (const type of ['application.submitted', 'application.moved', 'job.closed']) {
      expect(screen.getAllByRole('button', { name: `Replay the ${type} delivery` }).length).toBeGreaterThan(0);
    }
    expect(screen.queryAllByRole('button', { name: 'Replay the job.published delivery' })).toHaveLength(0);
  });

  it('hides replay while the endpoint is off, and says how to get it back', async () => {
    renderPage(endpoint({ enabled: false }));
    await screen.findAllByText('application.submitted');

    expect(screen.queryAllByRole('button', { name: /^Replay the/ })).toHaveLength(0);
    expect(screen.getByText('Turn the endpoint on to replay deliveries.')).toBeInTheDocument();
  });

  it('says why a replay failed', async () => {
    renderPage(endpoint(), {
      [`POST /v1/webhooks/${endpointId}/deliveries/d2/replay`]: () => Response.json({ detail: 'The event is still being tried.' }, { status: 409 }),
    });

    await fireEvent.click((await screen.findAllByRole('button', { name: 'Replay the application.moved delivery' }))[0]!);

    expect(await screen.findByText('The event is still being tried.')).toBeInTheDocument();
  });

  it('shows why an endpoint the worker turned off is off, with Turn on', async () => {
    renderPage(endpoint({ enabled: false, disabledReason: 'Ten deliveries in a row failed.', disabledAt: '2026-10-19T09:00:00Z' }));

    // A problem to fix, as a notice at the top of the work area (E2).
    const reason = (await screen.findByText(/Ten deliveries in a row failed\./)).closest('[data-slot="notice"]');
    expect(reason).not.toBeNull();
    expect(reason).toHaveTextContent('Turned off');
    expect(screen.getByRole('button', { name: 'Turn on' })).toBeInTheDocument();
  });

  it('turns the endpoint on with a PUT that keeps its settings and sets enabled', async () => {
    const bodies: unknown[] = [];
    renderPage(endpoint({ enabled: false, disabledReason: 'Ten deliveries in a row failed.' }), {
      [`PUT /v1/webhooks/${endpointId}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(endpoint())),
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Turn on' }));

    await waitFor(() =>
      expect(bodies).toEqual([
        { url: 'https://hooks.example.com/applicants', events: ['application.submitted', 'job.stage_changed'], description: 'Applicant sync', enabled: true },
      ]),
    );
  });

  it('says in the deliveries panel when turning the endpoint on fails', async () => {
    renderPage(endpoint({ enabled: false, disabledReason: 'Ten deliveries in a row failed.' }), {
      [`PUT /v1/webhooks/${endpointId}`]: () => Response.json({ detail: 'The address no longer resolves.' }, { status: 409 }),
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Turn on' }));

    const deliveries = screen.getByRole('region', { name: 'Deliveries' });
    expect(await within(deliveries).findByText('The address no longer resolves.')).toBeInTheDocument();
  });

  it('offers no Turn on and no failure reason while the endpoint is on', async () => {
    renderPage(endpoint());
    await screen.findAllByText('application.submitted');

    expect(screen.queryByRole('button', { name: 'Turn on' })).not.toBeInTheDocument();
    expect(document.querySelector('[data-slot="notice"]')).toBeNull();
  });

  it('turns the endpoint off from the More menu with enabled false', async () => {
    const bodies: unknown[] = [];
    renderPage(endpoint(), {
      [`PUT /v1/webhooks/${endpointId}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(endpoint({ enabled: false }))),
    });

    await fireEvent.click(within(await openMoreActions()).getByRole('menuitem', { name: 'Turn off' }));

    await waitFor(() => expect(bodies).toEqual([expect.objectContaining({ enabled: false })]));
  });

  it('rotates the signing secret after a confirmation and shows the new secret once', async () => {
    const secret = 'whsec_0123456789abcdef0123456789abcdef';
    renderPage(endpoint(), { [`POST /v1/webhooks/${endpointId}/rotate-secret`]: () => Response.json({ endpoint: endpoint(), signingSecret: secret }) });

    await fireEvent.click(within(await openMoreActions()).getByRole('menuitem', { name: 'Rotate signing secret' }));
    const confirm = await screen.findByRole('dialog', { name: 'Rotate the signing secret?' });
    await fireEvent.click(within(confirm).getByRole('button', { name: 'Rotate secret' }));

    const dialog = await screen.findByRole('dialog', { name: 'Your new signing secret' });
    expect(within(dialog).getByDisplayValue(secret)).toBeInTheDocument();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByDisplayValue(secret)).not.toBeInTheDocument());
  });

  it('deletes the endpoint after a confirmation and goes back to the list', async () => {
    const fetchMock = renderPage(endpoint(), { [`DELETE /v1/webhooks/${endpointId}`]: () => new Response(null, { status: 204 }) });

    await fireEvent.click(within(await openMoreActions()).getByRole('menuitem', { name: 'Delete endpoint' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete this endpoint?' });
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE')).toHaveLength(0);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete endpoint' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/v1/webhooks/${endpointId}`, expect.objectContaining({ method: 'DELETE' })));
    await waitFor(() => expect(navigation.goto).toHaveBeenCalledWith('/settings/webhooks'));
  });

  it('says the endpoint was not found when the API answers 404', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          [`GET /v1/webhooks/${endpointId}`]: () => new Response(null, { status: 404 }),
          [`GET /v1/webhooks/${endpointId}/deliveries`]: () => new Response(null, { status: 404 }),
        }),
      ),
    );
    render(WithQueryClient, { props: { component: Page, props: { params: { endpointId } } } });

    expect(await screen.findByText('It was deleted, or it belongs to another organization.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to webhooks' })).toHaveAttribute('href', '/settings/webhooks');
  });
});
