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
    expect(screen.getByRole('region', { name: 'Email link' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('explains a link without a token and offers a new one', () => {
    state.hash = '';
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent("This link doesn't work");
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
