import { ApiError } from '@screenhire/api-client/svelte-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import ConfirmDialog from './confirm-dialog.svelte';

const renderDialog = (onconfirm: () => Promise<unknown>, props: Record<string, unknown> = {}) =>
  render(ConfirmDialog, {
    props: {
      open: true,
      title: 'Revoke Payroll sync?',
      description: 'Requests that use it are refused from now on.',
      confirmLabel: 'Revoke key',
      failure: 'Could not revoke the key',
      onconfirm,
      ...props,
    },
  });

describe('confirm dialog', () => {
  it('asks the question with what happens, a way back and the action', async () => {
    renderDialog(async () => {});

    const dialog = await screen.findByRole('dialog', { name: 'Revoke Payroll sync?' });
    expect(within(dialog).getByText('Requests that use it are refused from now on.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Revoke key' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('uses the cancel label it is given', async () => {
    renderDialog(async () => {}, { cancelLabel: 'Keep it' });

    expect(await screen.findByRole('button', { name: 'Keep it' })).toBeInTheDocument();
  });

  it('shows the action icon the caller passes, inside the action', async () => {
    renderDialog(async () => {}, { icon: createRawSnippet(() => ({ render: () => '<svg data-testid="action-icon" aria-hidden="true"></svg>' })) });

    const button = within(await screen.findByRole('dialog')).getByRole('button', { name: 'Revoke key' });
    expect(within(button).getByTestId('action-icon')).toBeInTheDocument();
  });

  it('does the work only on confirm, then closes', async () => {
    const onconfirm = vi.fn(async () => {});
    renderDialog(onconfirm);
    const dialog = await screen.findByRole('dialog');
    expect(onconfirm).not.toHaveBeenCalled();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke key' }));

    await waitFor(() => expect(onconfirm).toHaveBeenCalledOnce());
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('does nothing on cancel', async () => {
    const onconfirm = vi.fn(async () => {});
    renderDialog(onconfirm);

    await fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(onconfirm).not.toHaveBeenCalled();
  });

  it('stays open and shows the API reason when the work fails', async () => {
    renderDialog(async () => {
      throw new ApiError(409, { detail: 'The key was already revoked.' });
    });
    const dialog = await screen.findByRole('dialog');

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke key' }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('The key was already revoked.'));
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Revoke key' })).toBeEnabled();
  });

  it('names what failed when the error carries no reason', async () => {
    renderDialog(async () => {
      throw new Error('boom');
    });
    const dialog = await screen.findByRole('dialog');

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Revoke key' }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('Could not revoke the key'));
  });

  it('disables the action while the work runs, so it cannot be sent twice', async () => {
    let finish!: () => void;
    const onconfirm = vi.fn(() => new Promise<void>((resolve) => (finish = resolve)));
    renderDialog(onconfirm);
    const dialog = await screen.findByRole('dialog');
    const action = within(dialog).getByRole('button', { name: 'Revoke key' });

    await fireEvent.click(action);

    await waitFor(() => expect(action).toBeDisabled());
    expect(onconfirm).toHaveBeenCalledOnce();
    finish();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
});
