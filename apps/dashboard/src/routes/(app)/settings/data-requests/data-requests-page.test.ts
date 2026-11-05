import type { DataRequestEntry, OrganizationRetention } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/data-requests') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const day = 24 * 60 * 60 * 1000;
const iso = (offsetDays: number) => new Date(Date.now() + offsetDays * day).toISOString();

const request = (id: string, overrides: Partial<DataRequestEntry> = {}): DataRequestEntry => ({
  id,
  kind: 'Copy',
  status: 'Open',
  receivedAt: iso(-10),
  dueAt: iso(20),
  overdue: false,
  resolvedAt: null,
  copySentAt: null,
  declineReason: null,
  candidate: { id: `c-${id}`, name: 'Casey Morgan', email: 'casey@example.com' },
  ...overrides,
});

// Open and due in 20 days: a candidate asked for a copy.
const copyRequest = request('r1');
// Open and 3 days past due: a candidate asked to be erased.
const erasureRequest = request('r2', {
  kind: 'Erasure',
  receivedAt: iso(-33),
  dueAt: iso(-3),
  overdue: true,
  candidate: { id: 'c-r2', name: 'Robin Ashby', email: 'robin@example.com' },
});
// Answered: the copy went out.
const sentRequest = request('r3', {
  status: 'Completed',
  receivedAt: iso(-40),
  dueAt: iso(-10),
  resolvedAt: iso(-35),
  copySentAt: iso(-35),
  candidate: { id: 'c-r3', name: 'Jordan Ellis', email: 'jordan@example.com' },
});
// Answered: erased, so the row keeps no candidate.
const erasedRequest = request('r4', { kind: 'Erasure', status: 'Completed', receivedAt: iso(-50), dueAt: iso(-20), resolvedAt: iso(-45), candidate: null });
// Declined with a reason.
const declinedRequest = request('r5', {
  status: 'Declined',
  receivedAt: iso(-30),
  dueAt: iso(0),
  resolvedAt: iso(-28),
  declineReason: 'We could not confirm who sent it.',
  candidate: { id: 'c-r5', name: 'Sam Rivera', email: 'sam@example.com' },
});

const all = [copyRequest, erasureRequest, sentRequest, erasedRequest, declinedRequest];

const retentionOff: OrganizationRetention = { months: null, lastUpdated: { at: null, by: 'ScreenHire', userId: null, apiKeyId: null }, lastRun: null };
const retentionOn = (months: number, overrides: Partial<OrganizationRetention> = {}): OrganizationRetention => ({
  ...retentionOff,
  months,
  lastUpdated: { at: iso(-1), by: 'Member', userId: user.id, apiKeyId: null },
  ...overrides,
});

type Routes = Parameters<typeof apiStub>[0];

function renderPage(
  items: DataRequestEntry[] = all,
  { retention = retentionOff, previews = {}, routes = {} }: { retention?: OrganizationRetention; previews?: Record<number, { applications: number; candidates: number }>; routes?: Routes } = {},
) {
  const previewRoutes: Routes = Object.fromEntries(
    Object.entries(previews).map(([months, counts]) => [`GET /v1/organization/data-retention/preview?months=${months}`, () => Response.json(counts)]),
  );
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/data-requests': () => Response.json({ items, openCount: items.filter((item) => item.status === 'Open').length }),
      'GET /v1/organization/data-retention': () => Response.json(retention),
      ...previewRoutes,
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetchMock;
}

const posts = (fetchMock: ReturnType<typeof vi.fn>) => fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT');

// bits-ui opens a menu on pointerdown. Rows render in a phone list and a table (no CSS in jsdom), so a row has two buttons.
async function openMenu(name: string) {
  const [trigger] = await screen.findAllByRole('button', { name });
  await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

async function choose(menuName: string, item: string) {
  const menu = await openMenu(menuName);
  await fireEvent.click(within(menu).getByRole('menuitem', { name: item }));
}

beforeEach(() => localStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe('Data requests page: the list', () => {
  it('lists open requests first by due date, then answered ones latest first, status first, and shows how each answered one ended', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;
    await within(table).findByText('Jordan Ellis');

    expect(within(table).getByRole('columnheader', { name: /Due/ })).toHaveAttribute('aria-sort', 'ascending');
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1]!.textContent?.trim())).toEqual([
      expect.stringContaining('Robin Ashby'),
      expect.stringContaining('Casey Morgan'),
      expect.stringContaining('Sam Rivera'),
      expect.stringContaining('Jordan Ellis'),
      expect.stringContaining('Erased candidate'),
    ]);
    // An answered request has no due date to show.
    expect(within(rows[2]!).getAllByRole('cell')[4]).toHaveTextContent('No due date');
    expect(within(table).getAllByText(/^Copy sent /)).toHaveLength(1);
    expect(within(table).getByText('Declined: We could not confirm who sent it.')).toBeInTheDocument();
    expect(within(table).getByText(/^Erased (?!candidate)/)).toBeInTheDocument();
    expect(within(table).getAllByText('Copy of data').length).toBe(3);
    expect(within(table).getAllByText('Erasure').length).toBe(2);
    await expectNoAxeViolations();
  });

  it('counts the open requests beside the title and marks an overdue one with the Overdue badge', async () => {
    renderPage();
    await screen.findAllByText('Robin Ashby');

    expect(document.querySelector('[data-slot="header-band"]')).toHaveTextContent(/Data requests\s*2\s*open/);
    expect(screen.getAllByText(/^Overdue by 3 days/).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Open').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Completed').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Declined').length).toBeGreaterThan(0);
  });

  it('links an existing candidate to their profile and shows an erased one as quiet text', async () => {
    renderPage();
    await screen.findAllByText('Robin Ashby');

    expect(screen.getAllByRole('link', { name: 'Robin Ashby' })[0]).toHaveAttribute('href', '/candidates/c-r2');
    expect(screen.getAllByText('Erased candidate').length).toBeGreaterThan(0);
    expect(screen.queryByRole('link', { name: 'Erased candidate' })).not.toBeInTheDocument();
  });

  it('offers Send a copy only on an open copy request, Erase only on an open erasure request, Decline on either, and nothing on an answered one', async () => {
    renderPage();
    await screen.findAllByText('Casey Morgan');

    const copyMenu = await openMenu('Actions for Casey Morgan, copy of data');
    expect(within(copyMenu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Send a copy', 'Decline request']);
    await fireEvent.keyDown(copyMenu, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());

    const eraseMenu = await openMenu('Actions for Robin Ashby, erasure');
    expect(within(eraseMenu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Erase candidate', 'Decline request']);

    for (const answered of ['Jordan Ellis', 'Sam Rivera', 'erased candidate']) {
      expect(screen.queryAllByRole('button', { name: new RegExp(`^Actions for ${answered}`) })).toHaveLength(0);
    }
  });

  it('says why the list is empty when no candidate has asked', async () => {
    renderPage([]);

    expect(await screen.findByText('No data requests yet')).toBeInTheDocument();
    expect(screen.getByText(/appears here with a due date/)).toBeInTheDocument();
    expect(document.querySelector('[data-slot="header-band"]')).toHaveTextContent(/Data requests\s*0\s*open/);
  });

  it('says it could not load the requests, with a retry', async () => {
    renderPage(all, { routes: { 'GET /v1/data-requests': () => Response.json({}, { status: 500 }) } });

    expect(await screen.findByText('Could not load the data requests (HTTP 500).')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});

describe('Data requests page: answering a request', () => {
  it('sends a copy only after a confirmation that says the candidate is emailed a link that expires', async () => {
    const fetchMock = renderPage(all, { routes: { 'POST /v1/data-requests/r1/send-copy': () => new Response(null, { status: 202 }) } });
    await choose('Actions for Casey Morgan, copy of data', 'Send a copy');

    const dialog = await screen.findByRole('dialog', { name: 'Send Casey Morgan a copy of their data?' });
    expect(dialog).toHaveTextContent(/emails them a link/);
    expect(dialog).toHaveTextContent(/The link expires/);
    expect(posts(fetchMock)).toHaveLength(0);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Send copy' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/data-requests/r1/send-copy', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect((await screen.findAllByText('Preparing copy')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('menuitem', { name: 'Send a copy' })).not.toBeInTheDocument();
  });

  it('sends nothing when the person cancels', async () => {
    const fetchMock = renderPage();
    await choose('Actions for Casey Morgan, copy of data', 'Send a copy');
    const dialog = await screen.findByRole('dialog');

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(posts(fetchMock)).toHaveLength(0);
  });

  it('erases only once the candidate name is typed, and posts it as the confirmation', async () => {
    const bodies: unknown[] = [];
    const fetchMock = renderPage(all, {
      routes: {
        'POST /v1/data-requests/r2/erase': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return new Response(null, { status: 204 });
        },
      },
    });
    await choose('Actions for Robin Ashby, erasure', 'Erase candidate');

    const dialog = await screen.findByRole('dialog', { name: 'Erase this candidate?' });
    expect(dialog).toHaveTextContent(/Robin Ashby and all of their applications/);
    expect(dialog).toHaveTextContent(/not emailed/);
    const erase = within(dialog).getByRole('button', { name: 'Erase candidate' });
    expect(erase).toBeDisabled();

    await fireEvent.input(within(dialog).getByRole('textbox'), { target: { value: 'robin' } });
    expect(erase).toBeDisabled();
    await fireEvent.input(within(dialog).getByRole('textbox'), { target: { value: 'robin ashby' } });
    expect(erase).toBeEnabled();
    expect(posts(fetchMock)).toHaveLength(0);
    await fireEvent.click(erase);

    await waitFor(() => expect(bodies).toEqual([{ confirmation: 'Robin Ashby' }]));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('declines only with a reason, and posts the reason', async () => {
    const bodies: unknown[] = [];
    const fetchMock = renderPage(all, {
      routes: {
        'POST /v1/data-requests/r1/decline': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return new Response(null, { status: 204 });
        },
      },
    });
    await choose('Actions for Casey Morgan, copy of data', 'Decline request');
    const dialog = await screen.findByRole('dialog', { name: 'Decline this request?' });
    expect(dialog).toHaveTextContent(/Casey Morgan is emailed your reason/);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Decline request' }));

    expect(await within(dialog).findByText('Say why you are declining.')).toBeInTheDocument();
    expect(posts(fetchMock)).toHaveLength(0);

    await fireEvent.input(within(dialog).getByLabelText('Reason'), { target: { value: '  We could not match the address to an application.  ' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Decline request' }));

    await waitFor(() => expect(bodies).toEqual([{ reason: 'We could not match the address to an application.' }]));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('refuses a reason over 500 characters before sending anything', async () => {
    const fetchMock = renderPage();
    await choose('Actions for Casey Morgan, copy of data', 'Decline request');
    const dialog = await screen.findByRole('dialog');

    await fireEvent.input(within(dialog).getByLabelText('Reason'), { target: { value: 'x'.repeat(501) } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Decline request' }));

    expect(await within(dialog).findByText('Keep the reason to 500 characters or fewer.')).toBeInTheDocument();
    expect(posts(fetchMock)).toHaveLength(0);
  });

  it.each([
    ['sending a copy', 'Actions for Casey Morgan, copy of data', 'Send a copy', 'Send copy', 'POST /v1/data-requests/r1/send-copy'],
    ['declining', 'Actions for Casey Morgan, copy of data', 'Decline request', 'Decline request', 'POST /v1/data-requests/r1/decline'],
  ])('says the request was already handled when %s meets a 409', async (_, menuName, item, button, route) => {
    renderPage(all, { routes: { [route]: () => Response.json({ title: 'Conflict' }, { status: 409 }) } });
    await choose(menuName, item);
    const dialog = await screen.findByRole('dialog');
    if (item === 'Decline request') await fireEvent.input(within(dialog).getByLabelText('Reason'), { target: { value: 'No match.' } });

    await fireEvent.click(within(dialog).getByRole('button', { name: button }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('This request was already handled.'));
    expect(dialog).toBeInTheDocument();
  });

  it('says the request was already handled when erasing meets a 409', async () => {
    renderPage(all, { routes: { 'POST /v1/data-requests/r2/erase': () => Response.json({ title: 'Conflict' }, { status: 409 }) } });
    await choose('Actions for Robin Ashby, erasure', 'Erase candidate');
    const dialog = await screen.findByRole('dialog');
    await fireEvent.input(within(dialog).getByRole('textbox'), { target: { value: 'Robin Ashby' } });

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Erase candidate' }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('This request was already handled.'));
  });
});

describe('Data requests page: record retention', () => {
  const switchName = 'Delete ended applications';
  // The panel is a skeleton until the setting loads, so wait for its switch.
  const retentionPanel = async () => (await screen.findByRole('switch', { name: switchName })).closest('section') as HTMLElement;
  const switchIn = (panel: HTMLElement) => within(panel).getByRole('switch', { name: switchName });

  it('is off by default, with no months field, and says what ended means and that it has not run', async () => {
    renderPage([]);
    const panel = await retentionPanel();

    expect(switchIn(panel)).toHaveAttribute('aria-checked', 'false');
    expect(within(panel).queryByLabelText('Keep for (months)')).not.toBeInTheDocument();
    expect(within(panel).getByText(/Ended means hired, rejected or withdrawn, or a closed job, counted from when it ended\./)).toBeInTheDocument();
    expect(within(panel).getByText('Has not run yet.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says when it last ran and how many applications it removed', async () => {
    renderPage([], { retention: retentionOn(12, { lastRun: { at: iso(-2), applicationsRemoved: 12 } }), previews: { 12: { applications: 0, candidates: 0 } } });
    const panel = await retentionPanel();

    await waitFor(() => expect(panel).toHaveTextContent('Last run 2 days ago: removed 12 applications.'));
  });

  it('shows the saved setting, and Last updated with History in its panel header row, not the page header', async () => {
    renderPage([], { retention: retentionOn(12), previews: { 12: { applications: 0, candidates: 0 } } });
    const panel = await retentionPanel();

    await waitFor(() => expect(switchIn(panel)).toHaveAttribute('aria-checked', 'true'));
    expect(within(panel).getByLabelText('Keep for (months)')).toHaveValue(12);
    const fact = await waitFor(() => {
      const found = panel.querySelector('[data-last-updated]');
      expect(found).not.toBeNull();
      return found!;
    });
    expect(fact).toHaveTextContent(/^Last updated:\s*Updated yesterday by you/);
    expect(within(panel).getByRole('button', { name: /^History/ })).toBeInTheDocument();
    expect(document.querySelector('[data-slot="header-band"] [data-last-updated]')).toBeNull();
  });

  it('counts what turning it on would remove, before anything is saved', async () => {
    const fetchMock = renderPage([], { previews: { 6: { applications: 14, candidates: 11 } } });
    const panel = await retentionPanel();

    await fireEvent.click(switchIn(panel));
    await fireEvent.input(within(panel).getByLabelText('Keep for (months)'), { target: { value: '6' } });

    expect(await within(panel).findByText('Saving this removes 14 applications from 11 candidates now. Saved candidates are never removed.', {}, { timeout: 3000 })).toBeInTheDocument();
    expect(posts(fetchMock)).toHaveLength(0);
    await expectNoAxeViolations();
  });

  it('says nothing would be removed, and uses singular words for one', async () => {
    renderPage([], { previews: { 6: { applications: 0, candidates: 0 }, 7: { applications: 1, candidates: 1 } } });
    const panel = await retentionPanel();

    await fireEvent.click(switchIn(panel));
    const months = within(panel).getByLabelText('Keep for (months)');
    await fireEvent.input(months, { target: { value: '6' } });
    expect(await within(panel).findByText('Nothing would be removed now.', {}, { timeout: 3000 })).toBeInTheDocument();

    await fireEvent.input(months, { target: { value: '7' } });
    expect(await within(panel).findByText('Saving this removes 1 application from 1 candidate now. Saved candidates are never removed.', {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it('says it could not count when the dry run fails, and still lets the person save', async () => {
    renderPage([], { routes: { 'GET /v1/organization/data-retention/preview?months=6': () => Response.json({ detail: 'Counting failed.' }, { status: 500 }) } });
    const panel = await retentionPanel();

    await fireEvent.click(switchIn(panel));
    await fireEvent.input(within(panel).getByLabelText('Keep for (months)'), { target: { value: '6' } });

    expect(await within(panel).findByText(/Counting failed|Could not count what this would remove/, {}, { timeout: 5000 })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  it('asks to confirm, stating the count, before turning it on, and saves only after', async () => {
    const bodies: unknown[] = [];
    const fetchMock = renderPage([], {
      previews: { 6: { applications: 14, candidates: 11 } },
      routes: {
        'PUT /v1/organization/data-retention': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(retentionOn(6));
        },
      },
    });
    const panel = await retentionPanel();
    await fireEvent.click(switchIn(panel));
    await fireEvent.input(within(panel).getByLabelText('Keep for (months)'), { target: { value: '6' } });
    await within(panel).findByText(/Saving this removes 14 applications/, {}, { timeout: 3000 });

    await fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));

    const dialog = await screen.findByRole('dialog', { name: 'Delete ended applications after 6 months?' });
    expect(dialog).toHaveTextContent('Saving removes 14 applications from 11 candidates now, for good');
    expect(posts(fetchMock)).toHaveLength(0);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Save and delete' }));

    await waitFor(() => expect(bodies).toEqual([{ months: 6 }]));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(await within(panel).findByText('Saved.')).toBeInTheDocument();
  });

  it('saves without a question when nothing would be removed', async () => {
    const bodies: unknown[] = [];
    renderPage([], {
      previews: { 6: { applications: 0, candidates: 0 } },
      routes: {
        'PUT /v1/organization/data-retention': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(retentionOn(6));
        },
      },
    });
    const panel = await retentionPanel();
    await fireEvent.click(switchIn(panel));
    await fireEvent.input(within(panel).getByLabelText('Keep for (months)'), { target: { value: '6' } });
    await within(panel).findByText('Nothing would be removed now.', {}, { timeout: 3000 });

    await fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toEqual([{ months: 6 }]));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('asks again when the months are shortened, but not when they are lengthened', async () => {
    const bodies: unknown[] = [];
    renderPage([], {
      retention: retentionOn(12),
      previews: { 12: { applications: 0, candidates: 0 }, 6: { applications: 5, candidates: 4 }, 24: { applications: 9, candidates: 8 } },
      routes: {
        'PUT /v1/organization/data-retention': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(retentionOn(24));
        },
      },
    });
    const panel = await retentionPanel();
    await waitFor(() => expect(within(panel).getByLabelText('Keep for (months)')).toHaveValue(12));
    const months = within(panel).getByLabelText('Keep for (months)');

    await fireEvent.input(months, { target: { value: '6' } });
    await within(panel).findByText(/Saving this removes 5 applications from 4 candidates now/, {}, { timeout: 3000 });
    await fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete ended applications after 6 months?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Keep them' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(bodies).toEqual([]);

    await fireEvent.input(months, { target: { value: '24' } });
    await within(panel).findByText(/Saving this removes 9 applications from 8 candidates now/, {}, { timeout: 3000 });
    await fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toEqual([{ months: 24 }]));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('turns it off with no confirmation, sending null', async () => {
    const bodies: unknown[] = [];
    renderPage([], {
      retention: retentionOn(12),
      previews: { 12: { applications: 3, candidates: 2 } },
      routes: {
        'PUT /v1/organization/data-retention': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(retentionOff);
        },
      },
    });
    const panel = await retentionPanel();
    await waitFor(() => expect(switchIn(panel)).toHaveAttribute('aria-checked', 'true'));

    await fireEvent.click(switchIn(panel));
    await fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toEqual([{ months: null }]));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await within(panel).findByText('Saved. Ended applications are kept.')).toBeInTheDocument();
  });

  it.each(['', '0', '121', '2.5'])('asks for a whole number of months from 1 to 120 when the field holds "%s", and sends nothing', async (value) => {
    const fetchMock = renderPage();
    const panel = await retentionPanel();
    await fireEvent.click(switchIn(panel));
    await fireEvent.input(within(panel).getByLabelText('Keep for (months)'), { target: { value } });

    await fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));

    expect(await within(panel).findByText('Enter a whole number of months from 1 to 120.')).toBeInTheDocument();
    expect(posts(fetchMock)).toHaveLength(0);
  });

  it('says it could not load the setting, with a retry, and leaves the requests alone', async () => {
    renderPage(all, { routes: { 'GET /v1/organization/data-retention': () => Response.json({}, { status: 500 }) } });

    expect(await screen.findByText('Could not load the retention setting (HTTP 500).')).toBeInTheDocument();
    expect((await screen.findAllByText('Casey Morgan')).length).toBeGreaterThan(0);
  });
});
