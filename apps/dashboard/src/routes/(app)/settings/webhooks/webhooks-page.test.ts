import type { WebhookEndpointEntry } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { expectCountInPanelNotBand } from '../../../../test-support/list-template.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const location = vi.hoisted(() => ({ url: new URL('http://dashboard.test/settings/webhooks') }));
vi.mock('$app/state', () => ({
  page: {
    data: { user },
    get url() {
      return location.url;
    },
  },
  navigating: { to: null },
}));
// Creating an endpoint opens it; superforms needs the rest of the module (beforeNavigate).
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

const endpoint = (id: string, overrides: Partial<WebhookEndpointEntry> = {}): WebhookEndpointEntry => ({
  id,
  url: `https://hooks.example.com/${id}`,
  description: null,
  events: ['application.submitted'],
  enabled: true,
  consecutiveFailures: 0,
  disabledAt: null,
  disabledReason: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: '2026-10-01T09:00:00Z',
  secretRotatedAt: '2026-10-01T09:00:00Z',
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: 'Member', userId: 'u1', created: true },
  ...overrides,
});

const created = endpoint('e9', { url: 'https://hooks.example.com/applicants', description: 'Applicant sync' });
const signingSecret = 'shws_fictional_0123456789abcdef';

function renderPage(endpoints: WebhookEndpointEntry[] | Response, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/webhooks': () => (endpoints instanceof Response ? endpoints : Response.json(endpoints)),
        'GET /v1/webhooks/event-types': () => Response.json(['application.submitted', 'job.closed']),
        'GET /v1/meta': () => Response.json({ name: 'ScreenHire', version: '1.0.0', environment: 'Production', apiOrigin: 'https://api.example.com' }),
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

// The band's Add endpoint opens the sheet, whose form is named for what it makes.
async function openAddSheet() {
  await fireEvent.click(await screen.findByRole('button', { name: 'Add endpoint' }));
  const sheet = await screen.findByRole('dialog', { name: 'Add endpoint' });
  return within(sheet).getByRole('form', { name: 'New webhook endpoint' });
}

async function fillAndSubmit(form: HTMLElement) {
  await fireEvent.input(within(form).getByLabelText('URL'), { target: { value: ' https://hooks.example.com/applicants ' } });
  await fireEvent.input(within(form).getByLabelText(/Description/), { target: { value: 'Applicant sync' } });
  await fireEvent.click(await within(form).findByRole('checkbox', { name: /Application submitted/ }));
  await fireEvent.click(within(form).getByRole('button', { name: 'Add endpoint' }));
}

afterEach(() => {
  vi.unstubAllGlobals();
  navigation.goto.mockClear();
  location.url = new URL('http://dashboard.test/settings/webhooks');
});

describe('webhooks page', () => {
  it('says what webhooks are for, the events, how to check a message and where the reference is', async () => {
    renderPage([endpoint('e1')]);
    const panel = await screen.findByRole('region', { name: 'How webhooks work' });

    expect(await within(panel).findByText('application.submitted')).toBeInTheDocument();
    expect(within(panel).getByText('job.closed')).toBeInTheDocument();
    expect(within(panel).getByText('ScreenHire-Signature: t=…,v1=…')).toBeInTheDocument();
    expect(await within(panel).findByRole('link', { name: 'Webhooks in the API reference (opens in a new tab)' })).toHaveAttribute(
      'href',
      'https://api.example.com/scalar#tag/webhooks',
    );
    await expectNoAxeViolations();
  });

  it('lists each endpoint by its description, or its address when it has none, linked to the endpoint', async () => {
    renderPage([
      endpoint('e1', { description: 'Applicant sync', events: ['application.submitted', 'application.moved'] }),
      endpoint('e2', { url: 'https://hooks.example.com/plain' }),
    ]);

    const described = (await screen.findAllByRole('link', { name: 'Applicant sync' }))[0]!;
    expect(described).toHaveAttribute('href', '/settings/webhooks/e1');
    expect(screen.getAllByRole('link', { name: 'https://hooks.example.com/plain' })[0]).toHaveAttribute('href', '/settings/webhooks/e2');
    expect(screen.getAllByText('2 events').length).toBeGreaterThan(0);
    expect(screen.getAllByText('1 event').length).toBeGreaterThan(0);
    expectCountInPanelNotBand('Endpoints', 2);
    await expectNoAxeViolations();
  });

  it('has a Last updated column with the date of the newest change (T6)', async () => {
    renderPage([endpoint('e1', { lastUpdated: { at: '2026-10-02T09:00:00Z', by: 'Member', userId: 'u1', created: false } })]);

    expect(await screen.findByRole('columnheader', { name: 'Last updated' })).toBeInTheDocument();
    const dates = screen.getByRole('table').querySelectorAll('time');
    expect([...dates].map((time) => time.getAttribute('datetime'))).toEqual(['2026-10-02T09:00:00Z']);
  });

  it('labels an endpoint that is on, one a person turned off, and one the worker turned off after failures', async () => {
    renderPage([
      endpoint('e1'),
      endpoint('e2', { enabled: false }),
      endpoint('e3', { enabled: false, disabledReason: 'Ten deliveries in a row failed.' }),
    ]);

    await screen.findAllByText('Delivering');
    expect(screen.getAllByText('Off').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Turned off').length).toBeGreaterThan(0);
  });

  it('offers to add the first endpoint when there are none', async () => {
    renderPage([]);

    expect(await screen.findByText('No webhooks yet')).toBeInTheDocument();
    // Nothing to search over an empty list (design system section 7).
    expect(screen.queryByLabelText(/^Search (keys|endpoints)$/)).toBeNull();
    // The empty state offers the one action too, beside the header band's (E1), and it opens the same sheet (A16).
    await fireEvent.click(within(screen.getByRole('region', { name: 'Endpoints' })).getByRole('button', { name: 'Add endpoint' }));
    expect(await screen.findByRole('dialog', { name: 'Add endpoint' })).toBeInTheDocument();
  });

  it('says why the endpoints could not load', async () => {
    renderPage(Response.json({ detail: 'Try again later.' }, { status: 500 }));

    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
  });
});

describe('add endpoint sheet', () => {
  it('opens over the list from the band, with one checkbox per event type the API lists', async () => {
    renderPage([endpoint('e1')]);

    const form = await openAddSheet();

    expect(await within(form).findAllByRole('checkbox')).toHaveLength(2);
    expect(within(form).getByText('Job closed')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('creates the endpoint with a trimmed URL, the description and the chosen events', async () => {
    const bodies = renderPage([]);

    await fillAndSubmit(await openAddSheet());

    await waitFor(() =>
      expect(bodies).toEqual([{ url: 'https://hooks.example.com/applicants', description: 'Applicant sync', events: ['application.submitted'] }]),
    );
  });

  it('sends a blank description as null', async () => {
    const bodies = renderPage([]);
    const form = await openAddSheet();

    await fireEvent.input(within(form).getByLabelText('URL'), { target: { value: 'https://hooks.example.com/applicants' } });
    await fireEvent.click(await within(form).findByRole('checkbox', { name: /Job closed/ }));
    await fireEvent.click(within(form).getByRole('button', { name: 'Add endpoint' }));

    await waitFor(() => expect(bodies).toEqual([{ url: 'https://hooks.example.com/applicants', description: null, events: ['job.closed'] }]));
  });

  it('closes, shows the signing secret once, and Done opens the new endpoint', async () => {
    renderPage([]);

    await fillAndSubmit(await openAddSheet());

    const dialog = await screen.findByRole('dialog', { name: 'Your signing secret' });
    expect(within(dialog).getByDisplayValue(signingSecret)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add endpoint' })).not.toBeInTheDocument());
    expect(navigation.goto).not.toHaveBeenCalled();
    // The sheet closing does not take focus back to Add endpoint behind the dialog: it stays in the dialog (WCAG 2.4.3).
    await waitFor(() => expect(dialog).toContainElement(document.activeElement as HTMLElement));

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }));

    await waitFor(() => expect(navigation.goto).toHaveBeenCalledWith('/settings/webhooks/e9'));
    expect(navigation.goto).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByDisplayValue(signingSecret)).not.toBeInTheDocument());
  });

  it('asks for an address and at least one event, and sends nothing', async () => {
    const bodies = renderPage([]);
    const form = await openAddSheet();
    await within(form).findAllByRole('checkbox');

    await fireEvent.click(within(form).getByRole('button', { name: 'Add endpoint' }));

    expect(await within(form).findByText('Enter the address to send events to.')).toBeInTheDocument();
    expect(within(form).getByText('Choose at least one event.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('stays open and shows why the API refused the address, with no secret', async () => {
    renderPage([], { 'POST /v1/webhooks': () => Response.json({ detail: 'The address must be a public https URL.' }, { status: 422 }) });
    const form = await openAddSheet();

    await fillAndSubmit(form);

    expect(await within(form).findByText('The address must be a public https URL.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Add endpoint' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Your signing secret' })).not.toBeInTheDocument();
  });

  it('opens on arrival from the old Add endpoint address (?add), and takes the parameter out of the address', async () => {
    location.url = new URL('http://dashboard.test/settings/webhooks?add=endpoint');
    renderPage([endpoint('e1')]);

    expect(await screen.findByRole('dialog', { name: 'Add endpoint' })).toBeInTheDocument();
    await waitFor(() => expect(navigation.goto).toHaveBeenCalledWith(new URL('http://dashboard.test/settings/webhooks'), { replace: true, reset: false }));
  });

  it('stays closed on the plain address', async () => {
    renderPage([endpoint('e1')]);

    await screen.findByRole('region', { name: 'Endpoints' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(navigation.goto).not.toHaveBeenCalled();
  });
});
