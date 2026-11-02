import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { UndoOffer } from '../undo.svelte.js';
import UndoNotice from './undo-notice.svelte';

describe('undo notice', () => {
  it('is an empty live region until there is something to undo', async () => {
    render(UndoNotice, { props: { offer: new UndoOffer() } });

    expect(screen.getByRole('status')).toBeEmptyDOMElement();
    expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
  });

  it('says what was done, with an Undo button that reverses it and goes', async () => {
    const offer = new UndoOffer();
    const undo = vi.fn(async () => null);
    render(UndoNotice, { props: { offer } });

    offer.offer('Deleted "Weekly".', undo);
    const status = await screen.findByRole('status');
    expect(status).toHaveTextContent('Deleted "Weekly".');
    await expectNoAxeViolations();

    await fireEvent.click(screen.getByRole('button', { name: 'Undo' }));

    expect(undo).toHaveBeenCalledOnce();
    await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull());
  });

  it('says why Undo failed', async () => {
    const offer = new UndoOffer();
    render(UndoNotice, { props: { offer } });
    offer.offer('Deleted "Weekly".', async () => 'Another view now has this name.');

    await fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

    expect(await screen.findByText('Another view now has this name.')).toBeInTheDocument();
  });
});
