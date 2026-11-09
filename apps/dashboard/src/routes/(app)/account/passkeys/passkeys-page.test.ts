import type { PasskeySummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import { cancelledPrompt, stubWebAuthn, unstubWebAuthn } from '../../../../test-support/webauthn.js';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/account/passkeys') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const passkey = (id: string, name: string, overrides: Partial<PasskeySummary> = {}): PasskeySummary => ({
  id,
  name,
  createdAt: '2026-10-01T09:00:00Z',
  lastUsedAt: null,
  ...overrides,
});

const laptop = passkey('p1', 'Chrome on Windows', { lastUsedAt: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString() });
const phone = passkey('p2', 'Safari on iOS');

const listOf = (passkeys: PasskeySummary[], limit = 10) => Response.json({ passkeys, limit });
const optionsResponse = () => Response.json({ optionsJson: JSON.stringify({ challenge: 'abc' }) });

function renderPage(routes: Parameters<typeof apiStub>[0]) {
  const fetchMock = vi.fn(apiStub(routes));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetchMock;
}

// bits-ui opens a menu on pointerdown.
async function openMenu(name: string) {
  // Rows render in a phone list and a table (no CSS in jsdom), so a row has two buttons.
  const [trigger] = await screen.findAllByRole('button', { name });
  await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

async function openAddDialog() {
  await fireEvent.click(await screen.findByRole('button', { name: 'Add a passkey' }));
  return screen.findByRole('dialog', { name: 'Add a passkey' });
}

afterEach(() => {
  unstubWebAuthn();
});

describe('Passkeys page', () => {
  it('lists each passkey with when it was added and last used, and says how many can be kept', async () => {
    stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([laptop, phone]) });

    expect(await screen.findByRole('heading', { level: 1, name: /Passkeys/ })).toBeInTheDocument();
    const table = await screen.findByRole('table');
    const [header, ...rows] = within(table).getAllByRole('row');
    expect(within(header!).getAllByRole('columnheader').map((cell) => cell.textContent?.trim())).toEqual(['Name', 'Added', 'Last used', 'Actions']);
    // Most recently used first; one never used is last, and says so (a value, not a blank cell: design system L1).
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Chrome on Windows');
    expect(rows[0]).toHaveTextContent('Oct 1, 2026');
    expect(rows[0]).toHaveTextContent('3 hours ago');
    expect(rows[1]).toHaveTextContent('Safari on iOS');
    expect(within(rows[1]!).getByText('Never used')).not.toHaveClass('sr-only');
    // Far from the limit, nothing says so.
    expect(screen.queryByText(/You can keep up to/)).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('lists two-line items on a phone, with the same facts', async () => {
    stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([laptop, phone]) });

    await screen.findByRole('table');
    const items = within(screen.getByRole('main')).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Chrome on Windows');
    expect(items[0]).toHaveTextContent('Added Oct 1, 2026');
    expect(items[0]).toHaveTextContent('Last used 3 hours ago');
    expect(items[1]).toHaveTextContent('Never used');
  });

  it('says how many can be kept once that is close, in the panel footer', async () => {
    stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([laptop, phone], 3) });

    expect(await screen.findByText('You can keep up to 3 passkeys.')).toBeInTheDocument();
  });

  it('explains what a passkey is when there are none, and offers Add only in the header', async () => {
    stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([]) });

    expect(await screen.findByText('No passkeys yet')).toBeInTheDocument();
    expect(screen.getByText(/signs you in with your fingerprint, face or screen lock/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Add a passkey' })).toHaveLength(1);
  });

  it('offers no way to add one in a browser without WebAuthn, and says why', async () => {
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([laptop]) });

    const notice = await screen.findByText('This browser cannot add passkeys. Use a current version of Chrome, Edge, Safari or Firefox.');
    expect(notice.closest('[data-slot="notice"]')).not.toBeNull();
    expect(screen.queryByRole('button', { name: 'Add a passkey' })).not.toBeInTheDocument();
    // What is already there can still be removed.
    expect((await screen.findAllByRole('button', { name: 'Actions for Chrome on Windows' })).length).toBeGreaterThan(0);
  });

  it('turns Add off at the limit and says so', async () => {
    stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([laptop, phone], 2) });

    // The reason sits beside the disabled button, not in the panel footer.
    expect(await screen.findByText('You have 2 passkeys, the most you can keep. Remove one to add another.')).toBeInTheDocument();
    expect(screen.queryByText(/You can keep up to/)).not.toBeInTheDocument();
    for (const button of screen.getAllByRole('button', { name: 'Add a passkey' })) expect(button).toBeDisabled();
  });

  it('says so, with a way to try again, when the list cannot be loaded', async () => {
    stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => Response.json({ title: 'Error' }, { status: 500 }) });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load your passkeys');
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('adds a passkey under the name typed, then shows it in the list', async () => {
    const credentials = stubWebAuthn();
    let added = false;
    const fetchMock = renderPage({
      'GET /v1/auth/passkeys': () => listOf(added ? [laptop, passkey('p3', 'Work laptop')] : [laptop]),
      'POST /v1/auth/passkeys/register/options': optionsResponse,
      'POST /v1/auth/passkeys/register': () => {
        added = true;
        return Response.json(passkey('p3', 'Work laptop'));
      },
    });

    const dialog = await openAddDialog();
    const name = within(dialog).getByLabelText('Name');
    expect(name).not.toHaveValue('');
    await fireEvent.input(name, { target: { value: 'Work laptop' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add passkey' }));

    expect((await screen.findAllByText('Work laptop')).length).toBeGreaterThan(0);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(credentials.create).toHaveBeenCalledOnce();
    const register = fetchMock.mock.calls.find(([input]) => String(input) === '/v1/auth/passkeys/register')!;
    expect(JSON.parse(String(register[1]?.body))).toEqual(expect.objectContaining({ name: 'Work laptop' }));
    expect(screen.getByText('Work laptop was added.')).toBeInTheDocument();
  });

  it('asks for a name before it asks the browser', async () => {
    const credentials = stubWebAuthn();
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([]) });

    const dialog = await openAddDialog();
    await fireEvent.input(within(dialog).getByLabelText('Name'), { target: { value: '   ' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add passkey' }));

    expect(await within(dialog).findByText('Name the passkey.')).toBeInTheDocument();
    expect(credentials.create).not.toHaveBeenCalled();
  });

  it('leaves the dialog open and says nothing when the person closes the browser prompt', async () => {
    const credentials = stubWebAuthn({ create: async () => Promise.reject(cancelledPrompt()) });
    renderPage({ 'GET /v1/auth/passkeys': () => listOf([]), 'POST /v1/auth/passkeys/register/options': optionsResponse });

    const dialog = await openAddDialog();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add passkey' }));

    await waitFor(() => expect(credentials.create).toHaveBeenCalledOnce());
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Add passkey' })).toBeEnabled());
    expect(screen.getByRole('dialog', { name: 'Add a passkey' })).toBeInTheDocument();
    // The failure line is always in the dialog, empty until there is something to say.
    expect(within(dialog).getByRole('alert')).toBeEmptyDOMElement();
  });

  it("shows the API's reason in the dialog when the passkey cannot be added", async () => {
    stubWebAuthn();
    renderPage({
      'GET /v1/auth/passkeys': () => listOf([]),
      'POST /v1/auth/passkeys/register/options': optionsResponse,
      'POST /v1/auth/passkeys/register': () =>
        Response.json({ errors: { credentialJson: ['That passkey could not be added. Try again, or use another device.'] } }, { status: 400 }),
    });

    const dialog = await openAddDialog();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add passkey' }));

    await waitFor(() =>
      expect(within(dialog).getByRole('alert')).toHaveTextContent('That passkey could not be added. Try again, or use another device.'),
    );
  });

  it('removes a passkey after confirming, then refreshes the list', async () => {
    stubWebAuthn();
    let removed = false;
    const fetchMock = renderPage({
      'GET /v1/auth/passkeys': () => listOf(removed ? [laptop] : [laptop, phone]),
      'DELETE /v1/auth/passkeys/p2': () => {
        removed = true;
        return new Response(null, { status: 204 });
      },
    });
    await screen.findAllByText('Safari on iOS');

    const menu = await openMenu('Actions for Safari on iOS');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove passkey' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove Safari on iOS?' });
    // Nothing is removed until it is confirmed.
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'DELETE')).toBe(false);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove passkey' }));

    await waitFor(() => expect(screen.queryAllByText('Safari on iOS')).toHaveLength(0));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getAllByText('Chrome on Windows').length).toBeGreaterThan(0);
    expect(screen.getByText('Safari on iOS was removed.')).toBeInTheDocument();
  });

  it('shows why the last way to sign in cannot be removed, and keeps the passkey', async () => {
    stubWebAuthn();
    renderPage({
      'GET /v1/auth/passkeys': () => listOf([laptop]),
      'DELETE /v1/auth/passkeys/p1': () =>
        Response.json(
          { title: 'Last way to sign in', detail: 'This is the only way you have to sign in. Add another passkey or a sign-in method first.', status: 409 },
          { status: 409 },
        ),
    });
    await screen.findAllByText('Chrome on Windows');

    const menu = await openMenu('Actions for Chrome on Windows');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove passkey' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove Chrome on Windows?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove passkey' }));

    await waitFor(() =>
      expect(within(dialog).getByRole('alert')).toHaveTextContent('This is the only way you have to sign in. Add another passkey or a sign-in method first.'),
    );
    expect(screen.getByRole('dialog', { name: 'Remove Chrome on Windows?' })).toBeInTheDocument();
    expect(screen.getAllByText('Chrome on Windows').length).toBeGreaterThan(0);
  });
});
