import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import CopyButton from './copy-button.svelte';

const show = () =>
  render(WithTooltips, { props: { component: CopyButton, props: { value: 'casey@example.com', label: 'Copy email address' } } });

afterEach(() => {
  Reflect.deleteProperty(navigator, 'clipboard');
  vi.useRealTimers();
});

describe('copy button', () => {
  it('copies its value, says "Copied" to screen readers and shows the check for two seconds', async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const { container } = show();

    await fireEvent.click(screen.getByRole('button', { name: 'Copy email address' }));

    expect(writeText).toHaveBeenCalledWith('casey@example.com');
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Copied'));
    await expectNoAxeViolations(container);
  });

  it('goes back to idle after two seconds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => {}) }, configurable: true });
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Copy email address' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Copied'));
    await vi.advanceTimersByTimeAsync(2100);

    expect(screen.getByRole('status').textContent).toBe('');
  });

  it('says the copy failed when the clipboard refuses', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => Promise.reject(new Error('denied'))) }, configurable: true });
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Copy email address' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Could not copy'));
  });

  it('says it failed when there is no clipboard at all', async () => {
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Copy email address' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Could not copy'));
  });
});
