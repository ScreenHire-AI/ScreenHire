import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import SecretDialog from './secret-dialog.svelte';

const secret = 'whsec_0123456789abcdef0123456789abcdef';

const renderDialog = (onclose = vi.fn()) => {
  render(SecretDialog, { props: { open: true, title: 'Your signing secret', label: 'Signing secret', value: secret, onclose } });
  return onclose;
};

afterEach(() => {
  Reflect.deleteProperty(navigator, 'clipboard');
});

describe('secret dialog', () => {
  it('shows the secret in a read-only field with the label and the show-once warning', async () => {
    renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Your signing secret' });
    expect(within(dialog).getByLabelText('Signing secret')).toHaveValue(secret);
    expect(within(dialog).getByLabelText('Signing secret')).toHaveAttribute('readonly');
    expect(within(dialog).getByText('Copy it now. You will not see it again.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('copies the secret and says it was copied', async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    renderDialog();

    await fireEvent.click(await screen.findByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenCalledWith(secret);
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Signing secret copied');
  });

  it('keeps the field for a manual copy when the clipboard is refused', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => Promise.reject(new Error('denied'))) }, configurable: true });
    renderDialog();
    const dialog = await screen.findByRole('dialog');

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Copy' }));

    await waitFor(() => expect(within(dialog).getByLabelText('Signing secret')).toHaveFocus());
    expect(within(dialog).queryByRole('button', { name: 'Copied' })).not.toBeInTheDocument();
  });

  it('tells the caller to forget the secret when Done is chosen', async () => {
    const onclose = renderDialog();

    await fireEvent.click(await screen.findByRole('button', { name: 'Done' }));

    expect(onclose).toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('tells the caller to forget the secret when the dialog is closed with Escape', async () => {
    const onclose = renderDialog();
    const dialog = await screen.findByRole('dialog');

    await fireEvent.keyDown(dialog, { key: 'Escape' });

    await waitFor(() => expect(onclose).toHaveBeenCalled());
  });
});
