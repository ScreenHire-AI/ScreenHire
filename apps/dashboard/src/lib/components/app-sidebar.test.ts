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

describe("the person's block at the foot of the sidebar", () => {
  // The hairline between the last navigation item and the person's block: where to go, then who you are (section 11).
  const expectDivider = (scope: HTMLElement) => {
    const person = within(scope).getByRole('button', { name: /^Avery Quinn/ });
    const footer = person.closest<HTMLElement>('[data-sidebar="footer"]')!;
    const divider = footer.previousElementSibling;
    expect(divider).toHaveAttribute('data-sidebar', 'separator');
    expect(divider).toHaveClass('h-px', 'bg-sidebar-border');
    // Decorative: it draws a line and says nothing to a screen reader.
    expect(divider).toHaveAttribute('aria-hidden', 'true');
    const nav = within(scope).getByRole('navigation', { name: 'Main' });
    expect(nav.compareDocumentPosition(divider!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  };

  it('is set apart from the navigation by a hairline in the full sidebar and the icon rail', () => {
    stubViewport(1280);
    const { unmount } = render(ShellHarness, { props: { user } });
    expectDivider(document.body);
    unmount();

    stubViewport(800);
    render(ShellHarness, { props: { user } });
    expect(screen.getByRole('button', { name: 'Expand sidebar' })).toBeInTheDocument();
    expectDivider(document.body);
  });

  it('keeps the hairline in the sheet on a phone', async () => {
    stubViewport(390);
    render(ShellHarness, { props: { user } });

    await fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));

    expectDivider(await screen.findByRole('dialog', { name: 'Navigation' }));
  });
});

describe('the Views list in the sidebar', () => {
  it('is headed Views, so Saved means saved candidates only, and lists a saved view after the built-in ones', () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, saved: [view] } });

    expect(within(main()).getByRole('heading', { level: 2, name: 'Views' })).toBeInTheDocument();
    expect(within(main()).queryByText('Saved views')).toBeNull();
    const link = within(main()).getByRole('link', { name: /^Referrals in screening/ });
    expect(link).toHaveAttribute('href', `/candidates?view=${view.id}`);
    expect(link).toHaveAttribute('aria-current', 'page');
    // A view has no count (I2, T4): it filters, it does not hold things to do, and no status icon marks it.
    expect(link.querySelector('[data-slot="badge"]')).toBeNull();
    expect(link.querySelector('svg')).not.toBeNull();
    expect(link.querySelector('[data-status-icon]')).toBeNull();
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

  it('gives a tablet a button that opens the full sidebar, where the Views menus are', async () => {
    stubViewport(800);
    render(ShellHarness, { props: { user, viewActions: actions() } });

    const expand = screen.getByRole('button', { name: 'Expand sidebar' });
    expect(expand).toHaveClass('lg:hidden');

    await fireEvent.click(expand);

    expect(await screen.findByRole('button', { name: 'Collapse sidebar' })).toBeInTheDocument();
  });

  it('hides a built-in view from its menu, and lists the hidden ones behind "Show hidden views"', async () => {
    stubViewport(1280);
    const viewActions = actions();
    const { unmount } = render(ShellHarness, { props: { user, viewActions } });

    expect(within(main()).queryByRole('button', { name: /Show hidden views/ })).toBeNull();
    const menu = await open('Overdue');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Hide from sidebar']);
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Hide from sidebar' }));
    expect(viewActions.setHidden).toHaveBeenCalledWith('overdue', true);
    unmount();

    render(ShellHarness, { props: { user, viewActions, hiddenBuiltIns: ['overdue'] } });
    expect(within(main()).queryByRole('link', { name: /^Overdue/ })).toBeNull();
    const toggle = within(main()).getByRole('button', { name: /Show hidden views/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle.querySelector('[data-slot="badge"]')).toBeNull();

    await fireEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(within(main()).getByRole('link', { name: /^Overdue/ })).toBeInTheDocument();
    const shown = await open('Overdue');
    await fireEvent.click(within(shown).getByRole('menuitem', { name: 'Show in sidebar' }));
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
