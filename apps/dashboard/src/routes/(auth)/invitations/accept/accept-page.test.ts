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
      return new URL(`http://localhost:4323/invitations/accept${state.hash}`);
    },
  } as never,
}));

const show = () => render(WithTooltips, { props: { component: Page } });

describe('invitation accept page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    navigation.goto.mockClear();
  });

  it('asks for a click before using the invitation, and passes axe', async () => {
    state.hash = '#invitation=abc';
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Join your team');
    expect(screen.getByRole('region', { name: 'Invitation' })).toHaveTextContent('This signs you in on this device');
    expect(screen.getByRole('button', { name: 'Accept invitation' })).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('sends the token and opens the dashboard once accepted', async () => {
    state.hash = '#invitation=abc';
    const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ id: 'u', email: 'e', displayName: 'e', organization: null }));
    vi.stubGlobal('fetch', fetch);
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Accept invitation' }));

    await vi.waitFor(() => expect(navigation.goto).toHaveBeenCalledWith('/', { invalidateAll: true, replaceState: true }));
    const [input, init] = fetch.mock.calls[0]!;
    expect(String(input)).toContain('/v1/auth/invitations/accept');
    expect(JSON.parse(String(init?.body))).toEqual({ token: 'abc' });
  });

  it('explains a link without a token', () => {
    state.hash = '#token=abc';
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent("This invitation doesn't work");
    expect(screen.getByRole('link', { name: 'Go to ScreenHire' })).toHaveAttribute('href', '/');
  });

  it('moves focus to the new title when the invitation is refused', async () => {
    state.hash = '#invitation=used';
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 400 })));
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Accept invitation' }));

    const heading = await screen.findByRole('heading', { level: 1, name: "This invitation doesn't work" });
    await vi.waitFor(() => expect(heading).toHaveFocus());
    expect(navigation.goto).not.toHaveBeenCalled();
  });
});
