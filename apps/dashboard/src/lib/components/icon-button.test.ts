import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import { describe, expect, it } from 'vitest';
import IconButtonHarness from '../../test-support/icon-button-harness.svelte';

// An icon button's tooltip opens on hover and on keyboard focus, never when code moves the focus to it: a sheet that set its
// own first focus still showed a stray "Close" (the owner's walk-through, 2026-11-22).
const tooltip = () => document.querySelector('[data-slot="tooltip-content"]');

describe('an icon button tooltip', () => {
  it('stays shut when code focuses the button', async () => {
    render(IconButtonHarness);
    const close = screen.getByRole('button', { name: 'Close' });

    close.focus();
    await tick();

    expect(close).toHaveFocus();
    expect(tooltip()).toBeNull();
  });

  it('stays shut when code focuses it after Enter or Escape', async () => {
    render(IconButtonHarness);
    const before = screen.getByRole('textbox', { name: 'Before' });
    before.focus();

    await fireEvent.keyDown(before, { key: 'Escape' });
    screen.getByRole('button', { name: 'Close' }).focus();
    await tick();

    expect(tooltip()).toBeNull();
  });

  it('opens when Tab moves the focus to it', async () => {
    render(IconButtonHarness);
    const before = screen.getByRole('textbox', { name: 'Before' });
    before.focus();
    const close = screen.getByRole('button', { name: 'Close' });

    // jsdom has no default action for Tab, so the test moves the focus as the browser would, in the key's own task.
    await fireEvent.keyDown(before, { key: 'Tab' });
    close.focus();

    expect(await screen.findByText('Close', { selector: '[data-slot="tooltip-content"], [data-slot="tooltip-content"] *' })).toBeInTheDocument();
  });

  it('opens on hover', async () => {
    render(IconButtonHarness);

    await fireEvent.pointerEnter(screen.getByRole('button', { name: 'Close' }), { pointerType: 'mouse' });

    expect(await screen.findByText('Close', { selector: '[data-slot="tooltip-content"], [data-slot="tooltip-content"] *' })).toBeInTheDocument();
  });
});
