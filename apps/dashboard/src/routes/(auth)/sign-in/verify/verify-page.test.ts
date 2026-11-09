import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import WithTooltips from '../../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';

const state = vi.hoisted(() => ({ hash: '' }));
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));
vi.mock(import('$app/state'), async (importOriginal) => ({
  ...(await importOriginal()),
  page: {
    get url() {
      return new URL(`http://localhost:4323/sign-in/verify${state.hash}`);
    },
  } as never,
}));

const show = () => render(WithTooltips, { props: { component: Page } });

describe('magic link verify page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    navigation.goto.mockClear();
  });

  it('asks for a click before using the link, and passes axe', async () => {
    state.hash = '#token=abc';
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Finish signing in');
    // The sign-in page's card: the title, one line on what the click does, and the button.
    const card = document.querySelector('[data-slot="sign-in-card"]');
    expect(card).toHaveTextContent('This link signs you in on this device.');
    expect(card).toContainElement(screen.getByRole('button', { name: 'Sign in' }));
    await expectNoAxeViolations(document.body);
  });

  it('opens the dashboard once the link is used', async () => {
    state.hash = '#token=abc';
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await vi.waitFor(() => expect(navigation.goto).toHaveBeenCalledWith('/', { invalidateAll: true, replaceState: true }));
    expect(JSON.parse(String(fetch.mock.calls[0]![1]?.body))).toEqual({ token: 'abc' });
  });

  it('opens where the person was going, read from the fragment beside the token', async () => {
    state.hash = `#token=abc&returnUrl=${encodeURIComponent('/oauth/authorize?client_id=abc&state=xyz')}`;
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await vi.waitFor(() =>
      expect(navigation.goto).toHaveBeenCalledWith('/oauth/authorize?client_id=abc&state=xyz', { invalidateAll: true, replaceState: true }),
    );
    // Only the token is sent to the API.
    expect(JSON.parse(String(fetch.mock.calls[0]![1]?.body))).toEqual({ token: 'abc' });
  });

  it('ignores a return address on another site', async () => {
    state.hash = `#token=abc&returnUrl=${encodeURIComponent('https://evil.example.com/')}`;
    vi.stubGlobal('fetch', vi.fn<typeof globalThis.fetch>(async () => new Response(null, { status: 204 })));
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await vi.waitFor(() => expect(navigation.goto).toHaveBeenCalledWith('/', { invalidateAll: true, replaceState: true }));
  });

  it('explains a link without a token and offers a new one', () => {
    state.hash = '';
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent("This link doesn't work");
    expect(screen.getByRole('alert')).toHaveTextContent('This sign-in link has expired or has already been used.');
    expect(screen.getByRole('link', { name: 'Get a new link' })).toHaveAttribute('href', '/sign-in');
  });

  it('moves focus to the new title when the link has expired', async () => {
    state.hash = '#token=used';
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 400 })));
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    const heading = await screen.findByRole('heading', { level: 1, name: "This link doesn't work" });
    await vi.waitFor(() => expect(heading).toHaveFocus());
    expect(navigation.goto).not.toHaveBeenCalled();
  });
});
