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

  describe('when the person must type words first', () => {
    const typedField = (dialog: HTMLElement) => within(dialog).getByRole('textbox', { name: /^Type Casey Morgan to confirm$/ });
    const type = (dialog: HTMLElement, value: string) => fireEvent.input(typedField(dialog), { target: { value } });
    const renderTyped = (onconfirm: () => Promise<unknown> = async () => {}) =>
      renderDialog(onconfirm, { confirmText: 'Casey Morgan', confirmLabel: 'Erase candidate' });

    it('has no field, and an action that is on, when no words are asked for', async () => {
      renderDialog(async () => {});
      const dialog = await screen.findByRole('dialog');

      expect(within(dialog).queryByRole('textbox')).not.toBeInTheDocument();
      expect(within(dialog).getByRole('button', { name: 'Revoke key' })).toBeEnabled();
    });

    it('keeps the action off until the typed words match, and does not run the work on a click while it is off', async () => {
      const onconfirm = vi.fn(async () => {});
      renderTyped(onconfirm);
      const dialog = await screen.findByRole('dialog');
      const action = within(dialog).getByRole('button', { name: 'Erase candidate' });
      expect(action).toBeDisabled();

      await type(dialog, 'Casey Morga');
      expect(action).toBeDisabled();
      // The element's own click(), which a disabled button ignores as in a browser; fireEvent.click dispatches regardless.
      action.click();
      expect(onconfirm).not.toHaveBeenCalled();

      await type(dialog, 'Casey Morgan');
      await waitFor(() => expect(action).toBeEnabled());
    });

    it('matches ignoring case and spaces at the ends, but not spaces inside', async () => {
      renderTyped();
      const dialog = await screen.findByRole('dialog');
      const action = within(dialog).getByRole('button', { name: 'Erase candidate' });

      await type(dialog, '  casey morgan  ');
      await waitFor(() => expect(action).toBeEnabled());

      await type(dialog, 'CASEY MORGAN');
      expect(action).toBeEnabled();

      await type(dialog, 'Casey  Morgan');
      await waitFor(() => expect(action).toBeDisabled());
    });

    it('runs the work once the words match', async () => {
      const onconfirm = vi.fn(async () => {});
      renderTyped(onconfirm);
      const dialog = await screen.findByRole('dialog');

      await type(dialog, 'casey morgan ');
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Erase candidate' }));

      await waitFor(() => expect(onconfirm).toHaveBeenCalledOnce());
    });

    it('has a labelled field with no axe violations', async () => {
      renderTyped();
      const dialog = await screen.findByRole('dialog');

      expect(typedField(dialog)).toBeInTheDocument();
      await expectNoAxeViolations();
    });

    it('clears the field and turns the action off again when the dialog is opened a second time', async () => {
      const { rerender } = renderTyped();
      let dialog = await screen.findByRole('dialog');
      await type(dialog, 'Casey Morgan');
      await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Erase candidate' })).toBeEnabled());

      await rerender({ open: false });
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      await rerender({ open: true });

      dialog = await screen.findByRole('dialog');
      expect(typedField(dialog)).toHaveValue('');
      expect(within(dialog).getByRole('button', { name: 'Erase candidate' })).toBeDisabled();
    });
  });
});
