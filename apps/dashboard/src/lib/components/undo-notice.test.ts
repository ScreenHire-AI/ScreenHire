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

  describe('the clock', () => {
    const shortOffer = () => new UndoOffer(250);
    const undoButton = () => screen.findByRole('button', { name: 'Undo' });

    it('runs while Undo holds the focus it was given, so the card goes for someone who does nothing', async () => {
      const offer = shortOffer();
      render(UndoNotice, { props: { offer } });
      offer.offer('Deleted "Weekly".', async () => null);

      const button = await undoButton();
      await vi.waitFor(() => expect(button).toHaveFocus());
      await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull(), { timeout: 1000 });
    });

    it('moves focus to the page title when the focused Undo leaves with the card', async () => {
      const title = document.createElement('h1');
      title.tabIndex = -1;
      document.body.append(title);
      const offer = shortOffer();
      render(UndoNotice, { props: { offer } });
      offer.offer('Deleted "Weekly".', async () => null);

      const button = await undoButton();
      await vi.waitFor(() => expect(button).toHaveFocus());
      await vi.waitFor(() => expect(title).toHaveFocus(), { timeout: 1000 });
      title.remove();
    });

    it('waits while the pointer is on the card, and starts again when it leaves', async () => {
      const offer = shortOffer();
      render(UndoNotice, { props: { offer } });
      offer.offer('Deleted "Weekly".', async () => null);
      const button = await undoButton();

      await fireEvent.pointerEnter(button.closest('[data-undo-notice]')!);
      await new Promise((resolve) => setTimeout(resolve, 400));
      expect(screen.getByRole('button', { name: 'Undo' })).toBeInTheDocument();

      await fireEvent.pointerLeave(button.closest('[data-undo-notice]')!);
      await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull(), { timeout: 1000 });
    });

    it('waits once the person moves focus to the card themselves', async () => {
      const offer = shortOffer();
      render(UndoNotice, { props: { offer } });
      offer.offer('Deleted "Weekly".', async () => null);
      const button = await undoButton();
      await vi.waitFor(() => expect(button).toHaveFocus());

      button.blur();
      button.focus();
      await new Promise((resolve) => setTimeout(resolve, 400));

      expect(screen.getByRole('button', { name: 'Undo' })).toBeInTheDocument();
    });

    it('closes on Escape', async () => {
      const offer = new UndoOffer();
      render(UndoNotice, { props: { offer } });
      offer.offer('Deleted "Weekly".', async () => null);

      await fireEvent.keyDown(await undoButton(), { key: 'Escape' });

      await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull());
    });
  });

  it('says why Undo failed', async () => {
    const offer = new UndoOffer();
    render(UndoNotice, { props: { offer } });
    offer.offer('Deleted "Weekly".', async () => 'Another view now has this name.');

    await fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

    expect(await screen.findByText('Another view now has this name.')).toBeInTheDocument();
  });
});
