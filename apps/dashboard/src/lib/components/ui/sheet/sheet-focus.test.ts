import { render, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import SheetFocusHarness from '../../../../test-support/sheet-focus-harness.svelte';

// A sheet never starts on its Close button: focused first, Close opened its tooltip, and the tooltip took the first click
// outside, so a sheet with nothing to fill took two clicks to close (the owner's walk-through, 2026-11-21).
describe('sheet focus on open', () => {
  it('starts on the sheet itself when it has nothing to fill', async () => {
    render(SheetFocusHarness);
    const sheet = await screen.findByRole('dialog', { name: 'Screening scorecard' });
    await waitFor(() => expect(document.activeElement).toBe(sheet));
    expect(screen.getByRole('button', { name: 'Close' })).not.toHaveFocus();
  });

  it('starts on its first field when it has one', async () => {
    render(SheetFocusHarness, { field: true });
    const note = await screen.findByRole('textbox', { name: 'Note' });
    await waitFor(() => expect(note).toHaveFocus());
  });
});
