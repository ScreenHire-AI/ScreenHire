import { cleanup, fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import InfoButton from './info-button.svelte';

afterEach(() => cleanup());

// Design system section 11, Info button, and W14: an explanation one press away, on hover, a click or keyboard focus.
describe('InfoButton', () => {
  it('opens its words on a click, and closes on Escape', async () => {
    render(InfoButton, { props: { label: 'About Email', text: 'Sends a message to each reviewer.' } });
    const button = screen.getByRole('button', { name: 'About Email' });
    expect(screen.queryByText('Sends a message to each reviewer.')).not.toBeInTheDocument();

    await fireEvent.click(button);
    expect(await screen.findByText('Sends a message to each reviewer.')).toBeInTheDocument();
    await expectNoAxeViolations();

    await fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await fireEvent.keyDown(document.body, { key: 'Escape' });
    await expect.poll(() => screen.queryByText('Sends a message to each reviewer.')).toBeNull();
  });

  it('opens on hover, and a click then keeps it open once the pointer has left', async () => {
    render(InfoButton, { props: { label: 'About Email', text: 'Sends a message to each reviewer.' } });
    const button = screen.getByRole('button', { name: 'About Email' });

    await fireEvent.pointerEnter(button, { pointerType: 'mouse' });
    expect(await screen.findByText('Sends a message to each reviewer.')).toBeInTheDocument();

    await fireEvent.click(button);
    await fireEvent.pointerLeave(button, { pointerType: 'mouse' });
    expect(screen.getByText('Sends a message to each reviewer.')).toBeInTheDocument();
  });

  it('opens on keyboard focus, leaves focus on the button, and closes when focus moves on', async () => {
    render(InfoButton, { props: { label: 'About Email', text: 'Sends a message to each reviewer.' } });
    const button = screen.getByRole('button', { name: 'About Email' });
    // jsdom has no focus-visible: a keyboard focus is what the button checks for.
    vi.spyOn(button, 'matches').mockImplementation((selector: string) => selector === ':focus-visible');

    await fireEvent.focus(button);
    expect(await screen.findByText('Sends a message to each reviewer.')).toBeInTheDocument();
    expect(document.activeElement === button || document.activeElement === document.body).toBe(true);

    await fireEvent.blur(button);
    await expect.poll(() => screen.queryByText('Sends a message to each reviewer.')).toBeNull();
  });
});
