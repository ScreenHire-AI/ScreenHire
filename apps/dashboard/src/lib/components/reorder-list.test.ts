import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import ReorderHarness from '../../test-support/reorder-harness.svelte';

const names = (label: string) =>
  within(screen.getByRole('list', { name: label }))
    .getAllByRole('listitem')
    .map((item) => item.getAttribute('aria-label'));
const grip = (name: string) => screen.getByRole('button', { name: `Reorder ${name}` });

describe('reorder grip', () => {
  it('opens Move up, Move down, Move to top and Move to bottom on click, leaving out moves that do not apply', async () => {
    render(ReorderHarness);

    await fireEvent.click(grip('Your CV'));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move down', 'Move to bottom']);
    await fireEvent.keyDown(document.activeElement!, { key: 'Escape' });

    await fireEvent.click(grip('Right to work'));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move up', 'Move down', 'Move to top', 'Move to bottom']);
  });

  it('moves an item from the menu and gives the focus back to its grip', async () => {
    const onreorder = vi.fn();
    render(ReorderHarness, { props: { onreorder } });

    await fireEvent.click(grip('Notice period'));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Move to top' }));

    expect(onreorder.mock.calls).toEqual([['Questions', ['Notice period', 'Your CV', 'Right to work']]]);
    expect(names('Questions')).toEqual(['Notice period', 'Your CV', 'Right to work']);
    await waitFor(() => expect(grip('Notice period')).toHaveFocus());
  });

  it('opens the menu with Enter', async () => {
    render(ReorderHarness);

    grip('Your CV').focus();
    await fireEvent.keyDown(grip('Your CV'), { key: 'Enter' });

    expect(await screen.findByRole('menuitem', { name: 'Move down' })).toBeInTheDocument();
  });

  it('picks the item up with Space, moves it with the arrow keys and drops it with Space', async () => {
    const onreorder = vi.fn();
    render(ReorderHarness, { props: { onreorder } });

    grip('Your CV').focus();
    await fireEvent.keyDown(grip('Your CV'), { key: ' ' });
    await waitFor(() => expect(document.activeElement).toHaveAttribute('aria-label', 'Your CV'));
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    await waitFor(() => expect(names('Questions')).toEqual(['Right to work', 'Notice period', 'Your CV']));
    await fireEvent.keyDown(document.activeElement!, { key: ' ' });

    await waitFor(() => expect(grip('Your CV')).toHaveFocus());
    // The list reports the order it ends on.
    expect(onreorder).toHaveBeenLastCalledWith('Questions', ['Right to work', 'Notice period', 'Your CV']);
    // Picked up again, the arrow keys still move it: the grip switched the drag back on.
    await fireEvent.keyDown(grip('Your CV'), { key: ' ' });
    await waitFor(() => expect(document.activeElement).toHaveAttribute('aria-label', 'Your CV'));
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowUp' });
    await waitFor(() => expect(names('Questions')).toEqual(['Right to work', 'Your CV', 'Notice period']));
    await fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
  });

  it('keeps two lists apart: moving in one never changes the other', async () => {
    render(ReorderHarness);

    grip('Morning').focus();
    await fireEvent.keyDown(grip('Morning'), { key: ' ' });
    await waitFor(() => expect(document.activeElement).toHaveAttribute('aria-label', 'Morning'));
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    await waitFor(() => expect(names('Slots')).toEqual(['Afternoon', 'Morning']));
    await fireEvent.keyDown(document.activeElement!, { key: ' ' });

    expect(names('Questions')).toEqual(['Your CV', 'Right to work', 'Notice period']);
    // A press on a grip in one list does not let the other list's items drag.
    await fireEvent.click(grip('Right to work'));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Move up' }));
    expect(names('Questions')).toEqual(['Right to work', 'Your CV', 'Notice period']);
    expect(names('Slots')).toEqual(['Afternoon', 'Morning']);
  });

  it('has no grip on a list of one, and passes axe', async () => {
    render(ReorderHarness, { props: { second: ['Morning'] } });

    expect(screen.queryByRole('button', { name: 'Reorder Morning' })).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });
});
