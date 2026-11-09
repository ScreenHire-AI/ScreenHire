import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import ShowMore from './show-more.svelte';

describe('show more', () => {
  it('says how many of how many are shown and asks for the next page', async () => {
    const onmore = vi.fn();
    const { container } = render(ShowMore, { props: { shown: 50, total: 210, onmore } });

    expect(screen.getByRole('status').textContent).toBe('Showing 50 of 210');
    await fireEvent.click(screen.getByRole('button', { name: 'Show more' }));
    expect(onmore).toHaveBeenCalledOnce();
    await expectNoAxeViolations(container);
  });

  it('draws nothing once everything is shown', () => {
    const { container } = render(ShowMore, { props: { shown: 40, total: 40, onmore: () => {} } });

    expect(screen.queryByRole('button')).toBeNull();
    expect(container.textContent?.trim()).toBe('');
  });
});
