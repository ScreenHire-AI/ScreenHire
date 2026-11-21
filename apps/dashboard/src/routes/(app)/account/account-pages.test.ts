import type { AccountDetails, AccountSignIn, UpdateProfileRequest } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import { dateSettings } from '#lib/date-settings.svelte.js';
import { preferences } from '#lib/preferences.svelte.js';
import PreferencesPage from './preferences/+page.svelte';
import ProfilePage from './profile/+page.svelte';
import SecurityPage from './security/+page.svelte';

const state = vi.hoisted(() => ({ path: '/account/profile' }));
vi.mock('$app/state', () => ({
  page: {
    data: { user },
    get url() {
      return new URL(`http://dashboard.test${state.path}`);
    },
  },
  navigating: { to: null },
}));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}), invalidateAll: vi.fn(async () => {}) }));

const details = (overrides: Partial<AccountDetails> = {}): AccountDetails => ({
  email: 'avery@example.com',
  name: 'Avery Quinn',
  displayName: 'Avery Quinn',
  photoUrl: null,
  timeZone: 'Europe/London',
  language: null,
  dateFormat: 'Browser',
  shortcutsEnabled: true,
  confirmMoves: true,
  ...overrides,
});

function renderPage(component: typeof ProfilePage, path: string, routes: Parameters<typeof apiStub>[0]) {
  state.path = path;
  const fetchMock = vi.fn(apiStub(routes));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component, props: {} } });
  return fetchMock;
}

describe('Your account', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists its pages in one menu, the current one marked', async () => {
    renderPage(ProfilePage, '/account/profile', { 'GET /v1/account': () => Response.json(details()) });

    const menu = await screen.findByRole('navigation', { name: 'Your account' });
    const links = within(menu).getAllByRole('link');
    // Connected accounts shows only where there is something to connect; the test shell has nothing.
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Profile', 'Notifications', 'Sign-in and security', 'Preferences']);
    expect(within(menu).getByRole('link', { name: 'Profile' })).toHaveAttribute('aria-current', 'page');
  });

  it('shows the sign-in email read only, and saves the name, time zone, language and date format', async () => {
    let body: UpdateProfileRequest | undefined;
    renderPage(ProfilePage, '/account/profile', {
      'GET /v1/account': () => Response.json(details()),
      'PUT /v1/account/profile': (init) => {
        body = JSON.parse(String(init?.body));
        return Response.json(details({ name: body!.name, dateFormat: body!.dateFormat, language: body!.language }));
      },
    });

    // Read only: a value, not an input (E3).
    expect(await screen.findByText('avery@example.com')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Email' })).toBeNull();

    const name = screen.getByRole('textbox', { name: 'Name' });
    await waitFor(() => expect(name).toHaveValue('Avery Quinn'));
    await fireEvent.input(name, { target: { value: 'Avery Q. Quinn' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Date format' }), { target: { value: 'Iso' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'en-GB' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(body).toBeDefined());
    expect(body).toEqual({ name: 'Avery Q. Quinn', timeZone: 'Europe/London', language: 'en-GB', dateFormat: 'Iso' });
    // The dates on screen follow at once.
    await waitFor(() => expect(dateSettings.dateFormat).toBe('Iso'));
  });

  it('asks for a name before saving', async () => {
    const fetchMock = renderPage(ProfilePage, '/account/profile', { 'GET /v1/account': () => Response.json(details()) });

    const name = await screen.findByRole('textbox', { name: 'Name' });
    await waitFor(() => expect(name).toHaveValue('Avery Quinn'));
    await fireEvent.input(name, { target: { value: ' ' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Enter your name.')).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false);
  });

  it('lists how the person signs in and confirms Sign out everywhere first', async () => {
    const signIn: AccountSignIn = { email: 'avery@example.com', emailLinks: true, providers: [{ id: 'google', name: 'Google' }], passkeys: 2, passkeyLimit: 10 };
    const fetchMock = renderPage(SecurityPage, '/account/security', {
      'GET /v1/account/sign-in': () => Response.json(signIn),
      'POST /v1/account/sign-out-everywhere': () => new Response(null, { status: 204 }),
    });

    expect(await screen.findByText('Google')).toBeInTheDocument();
    expect(screen.getByText('2 passkeys')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Manage passkeys' })).toHaveAttribute('href', '/account/passkeys');

    await fireEvent.click(screen.getByRole('button', { name: 'Sign out everywhere' }));
    const dialog = await screen.findByRole('dialog', { name: 'Sign out everywhere else?' });
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Sign out everywhere' }));

    await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/v1/account/sign-out-everywhere' && init?.method === 'POST')).toBe(true));
    expect(await screen.findByText('Every other session is ending.')).toBeInTheDocument();
  });

  it('keeps the theme and reduced motion in this browser, and the habits through the shell', async () => {
    const saved: unknown[] = [];
    preferences.saveHabitsWith((habits) => saved.push(habits));
    renderPage(PreferencesPage, '/account/preferences', {});

    await fireEvent.click(await screen.findByRole('radio', { name: 'Dark' }));
    expect(globalThis.localStorage.getItem('screenhire:theme')).toBe('dark');
    expect(document.documentElement).toHaveClass('dark');

    await fireEvent.click(screen.getByRole('switch', { name: 'Reduce motion' }));
    expect(document.documentElement).toHaveClass('reduce-motion');

    await fireEvent.click(screen.getByRole('switch', { name: 'Single-key shortcuts' }));
    expect(saved).toEqual([{ shortcutsEnabled: false, confirmMoves: preferences.confirmMoves }]);

    preferences.saveHabitsWith(undefined);
    preferences.setShortcutsEnabled(true);
    preferences.setReduceMotion(false);
    preferences.setThemeChoice('light');
  });

  it('has no accessibility violations on Profile', async () => {
    renderPage(ProfilePage, '/account/profile', { 'GET /v1/account': () => Response.json(details()) });
    await screen.findByText('avery@example.com');
    await expectNoAxeViolations(document.body);
  });
});
