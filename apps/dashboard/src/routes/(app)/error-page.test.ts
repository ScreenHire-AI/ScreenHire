import { render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import Harness from '../../test-support/error-in-shell-harness.svelte';

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

describe('error page inside the signed-in app', () => {
  it('names a missing page under the top bar, not the brand bar, with the way back, and passes axe', async () => {
    state.status = 404;
    state.message = 'Not Found';
    render(Harness);

    // The shell's top bar (search, bell) is there; the root error page's brand bar is not.
    expect(screen.getByRole('button', { name: /^Search/ })).toBeInTheDocument();
    expect(document.querySelector('[data-slot="brand-bar"]')).toBeNull();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Page not found');
    expect(screen.getByRole('region', { name: 'Error 404' })).toHaveTextContent('Check the address, or go back to your jobs.');
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
    await expectNoAxeViolations(document.body);
  });

  it("shows the error's own message for anything else", () => {
    state.status = 500;
    state.message = 'Could not load the jobs.';
    render(Harness);

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Something went wrong');
    expect(screen.getByRole('region', { name: 'Error 500' })).toHaveTextContent('Could not load the jobs.');
  });
});
