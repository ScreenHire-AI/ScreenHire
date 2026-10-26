import { render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../test-support/axe.js';
import WithTooltips from '../test-support/with-tooltips.svelte';
import Page from './+error.svelte';

const state = vi.hoisted(() => ({ status: 404, message: 'Not Found' }));
vi.mock(import('$app/state'), async (importOriginal) => ({
  ...(await importOriginal()),
  page: {
    get status() {
      return state.status;
    },
    get error() {
      return { message: state.message };
    },
  } as never,
}));

const show = () => render(WithTooltips, { props: { component: Page } });

describe('error page', () => {
  it('names a missing page under the brand bar, with the way back, and passes axe', async () => {
    state.status = 404;
    state.message = 'Not Found';
    show();

    expect(document.querySelector('[data-slot="brand-bar"]')).toHaveTextContent('ScreenHire');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Page not found');
    expect(screen.getByRole('region', { name: 'Error 404' })).toHaveTextContent('Check the address, or go back to your jobs.');
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
    await expectNoAxeViolations(document.body);
  });

  it("shows the error's own message for anything else", () => {
    state.status = 500;
    state.message = 'Could not load the ways to sign in.';
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Something went wrong');
    expect(screen.getByRole('region', { name: 'Error 500' })).toHaveTextContent('Could not load the ways to sign in.');
  });
});
