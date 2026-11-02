import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { savedView, user } from '../../test-support/fixtures.js';
import ShellHarness from '../../test-support/shell-harness.svelte';
import { stubViewport } from '../../test-support/viewport.js';
import type { ViewActions } from '../views.svelte.js';

const view = savedView();

vi.mock('$app/state', () => ({
  page: { url: new URL('http://dashboard.test/candidates?view=01a157d4-0000-7000-8000-0000000003aa') },
  navigating: { to: null },
}));

const actions = () => ({
  rename: vi.fn<ViewActions['rename']>(async () => null),
  setShared: vi.fn<ViewActions['setShared']>(async () => null),
  remove: vi.fn<ViewActions['remove']>(async () => null),
  setHidden: vi.fn<ViewActions['setHidden']>(async () => null),
});

afterEach(() => vi.unstubAllGlobals());

const main = () => screen.getByRole('navigation', { name: 'Main' });
const menuFor = (name: string) => within(main()).getByRole('button', { name: `Actions for ${name}` });
const open = async (name: string) => {
  await fireEvent.pointerDown(menuFor(name), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
};

describe('the Views list in the sidebar', () => {
  it('is headed Views, so Saved means saved candidates only, and lists a saved view after the built-in ones', () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, saved: [view], viewCounts: { [view.id]: 3 } } });

    expect(within(main()).getByRole('heading', { level: 2, name: 'Views' })).toBeInTheDocument();
    expect(within(main()).queryByText('Saved views')).toBeNull();
    const link = within(main()).getByRole('link', { name: /^Referrals in screening/ });
    expect(link).toHaveAttribute('href', `/candidates?view=${view.id}`);
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(within(link).getByText('3')).toHaveAttribute('data-slot', 'badge');
  });

  it('offers a person their own view Rename, Share with the team and Delete view, each a menu item with a keyboard path', async () => {
    stubViewport(1280);
    const viewActions = actions();
    render(ShellHarness, { props: { user, saved: [view], viewActions } });

    const trigger = menuFor('Referrals in screening');
    // The menu button is the next tab stop after its link, so the keyboard reaches it, and it shows on focus.
    expect(trigger.closest('li')).toContainElement(within(main()).getByRole('link', { name: /^Referrals in screening/ }));
    expect(trigger).toHaveClass('group-focus-within/menu-item:opacity-100');
    await expectNoAxeViolations();
    const menu = await open('Referrals in screening');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Rename', 'Share with the team', 'Delete view']);

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Share with the team' }));
    expect(viewActions.setShared).toHaveBeenCalledWith(expect.objectContaining({ name: 'Referrals in screening' }), true);
  });

  it('offers Make private on a shared view, and deletes at once with no confirmation (Undo reverses it)', async () => {
    stubViewport(1280);
    const viewActions = actions();
    render(ShellHarness, { props: { user, saved: [savedView({ shared: true })], viewActions } });

    let menu = await open('Referrals in screening');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Make private' }));
    expect(viewActions.setShared).toHaveBeenCalledWith(expect.anything(), false);

    menu = await open('Referrals in screening');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Delete view' }));
    expect(viewActions.remove).toHaveBeenCalledOnce();
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });

  it('renames from a dialog and says why when the name is refused', async () => {
    stubViewport(1280);
    const viewActions = actions();
    viewActions.rename.mockResolvedValueOnce('There is already a view with this name here.');
    render(ShellHarness, { props: { user, saved: [view], viewActions } });

    const menu = await open('Referrals in screening');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Rename' }));
    const dialog = await screen.findByRole('dialog', { name: 'Rename view' });
    const name = within(dialog).getByRole('textbox', { name: 'Name' });
    expect(name).toHaveValue('Referrals in screening');
    await fireEvent.input(name, { target: { value: 'Weekly referrals' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Rename view' }));

    expect(await within(dialog).findByText('There is already a view with this name here.')).toBeInTheDocument();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Rename view' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Rename view' })).toBeNull());
    expect(viewActions.rename).toHaveBeenLastCalledWith(expect.objectContaining({ id: view.id }), 'Weekly referrals');
  });

  it('gives a view the person cannot change no menu', () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, saved: [savedView({ shared: true, mine: false, canEdit: false })], viewActions: actions() } });

    expect(within(main()).getByRole('link', { name: /^Referrals in screening/ })).toBeInTheDocument();
    expect(within(main()).queryByRole('button', { name: 'Actions for Referrals in screening' })).toBeNull();
  });

  it('hides a built-in view from its menu, and lists the hidden ones behind "Show hidden views"', async () => {
    stubViewport(1280);
    const viewActions = actions();
    const { unmount } = render(ShellHarness, { props: { user, viewActions } });

    expect(within(main()).queryByRole('button', { name: /Show hidden views/ })).toBeNull();
    const menu = await open('Overdue');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Hide']);
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Hide' }));
    expect(viewActions.setHidden).toHaveBeenCalledWith('overdue', true);
    unmount();

    render(ShellHarness, { props: { user, viewActions, hiddenBuiltIns: ['overdue'] } });
    expect(within(main()).queryByRole('link', { name: /^Overdue/ })).toBeNull();
    const toggle = within(main()).getByRole('button', { name: /Show hidden views/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(within(toggle).getByText('1')).toHaveAttribute('data-slot', 'badge');

    await fireEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(within(main()).getByRole('link', { name: /^Overdue/ })).toBeInTheDocument();
    const shown = await open('Overdue');
    await fireEvent.click(within(shown).getByRole('menuitem', { name: 'Show in list' }));
    expect(viewActions.setHidden).toHaveBeenLastCalledWith('overdue', false);
  });

  it('says why a change failed, under the list', async () => {
    stubViewport(1280);
    const viewActions = actions();
    viewActions.remove.mockResolvedValueOnce('Could not delete the view (HTTP 403).');
    render(ShellHarness, { props: { user, saved: [view], viewActions } });

    const menu = await open('Referrals in screening');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Delete view' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not delete the view (HTTP 403).');
  });
});
