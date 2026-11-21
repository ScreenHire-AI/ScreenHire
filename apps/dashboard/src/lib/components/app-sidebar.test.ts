import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { preferences } from '#lib/preferences.svelte.js';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, savedView, user } from '../../test-support/fixtures.js';
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
  addToSidebar: vi.fn<ViewActions['addToSidebar']>(async () => null),
  removeFromSidebar: vi.fn<ViewActions['removeFromSidebar']>(async () => null),
  markTeamViewsOpened: vi.fn<ViewActions['markTeamViewsOpened']>(async () => null),
});

afterEach(() => {
  preferences.setConfirmMoves(true);
  vi.unstubAllGlobals();
});

const main = () => screen.getByRole('navigation', { name: 'Main' });
const menuFor = (name: string) => within(main()).getByRole('button', { name: `Actions for ${name}` });
const open = async (name: string) => {
  await fireEvent.pointerDown(menuFor(name), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
};

describe('the trial line in the sidebar footer', () => {
  const ends = '2026-12-05T09:00:00Z';

  it('is one muted link to Billing above the person block, with the day the trial ends', () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, trialEndsAt: ends } });

    const link = screen.getByRole('link', { name: 'Trial ends December 5' });
    expect(link).toHaveAttribute('href', '/settings/billing');
    expect(link.querySelector('time')).toHaveAttribute('datetime', ends);
    expect(link).toHaveClass('text-muted-foreground');
    const person = screen.getByRole('button', { name: /^Avery Quinn/ });
    expect(link.compareDocumentPosition(person) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(link.closest('[data-sidebar="footer"]')).toBe(person.closest('[data-sidebar="footer"]'));
  });

  it('is not there without an end date: another role, a paid plan, billing off or a trial that is over', () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user } });

    expect(screen.queryByText(/Trial ends/)).not.toBeInTheDocument();
  });

  it('leaves the icon rail alone, where there is no room for a line of words', () => {
    stubViewport(800);
    render(ShellHarness, { props: { user, trialEndsAt: ends } });

    expect(screen.getByRole('button', { name: 'Expand sidebar' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Trial ends December 5' })).toHaveClass('group-data-[collapsible=icon]:hidden');
  });
});

describe("the person's block at the foot of the sidebar", () => {
  // The hairline between the last navigation item and the person's block: where to go, then who you are (section 11).
  const expectDivider = (scope: HTMLElement) => {
    const person = within(scope).getByRole('button', { name: /^Avery Quinn/ });
    const footer = person.closest<HTMLElement>('[data-sidebar="footer"]')!;
    const divider = footer.previousElementSibling;
    expect(divider).toHaveAttribute('data-sidebar', 'separator');
    expect(divider).toHaveClass('bg-sidebar-border', 'data-[orientation=horizontal]:h-px');
    expect(divider).toHaveAttribute('data-orientation', 'horizontal');
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

  it('links to the Passkeys page from the menu of the signed-in person, above Sign out', async () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user } });

    await fireEvent.pointerDown(screen.getByRole('button', { name: /^Avery Quinn/ }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    const names = within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim());
    expect(names.indexOf('Passkeys')).toBeGreaterThan(-1);
    expect(names.indexOf('Passkeys')).toBe(names.indexOf('Sign out') - 1);

    // A real link, so it opens in a new tab and shows where it goes (A5).
    expect(within(menu).getByRole('menuitem', { name: 'Passkeys' })).toHaveAttribute('href', '/account/passkeys');
  });

  it("turns the move confirmation back on from the person's menu, next to the keyboard shortcuts switch", async () => {
    preferences.setConfirmMoves(false);
    stubViewport(1280);
    render(ShellHarness, { props: { user } });

    await fireEvent.pointerDown(screen.getByRole('button', { name: /^Avery Quinn/ }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    const item = within(menu).getByRole('menuitemcheckbox', { name: 'Confirm moves that send emails or create tasks' });
    expect(item).toHaveAttribute('aria-checked', 'false');
    const names = within(menu).getAllByRole('menuitemcheckbox').map((entry) => entry.textContent?.trim());
    expect(names).toEqual(['Single-key shortcuts', 'Confirm moves that send emails or create tasks']);

    await fireEvent.click(item);

    expect(preferences.confirmMoves).toBe(true);
    expect(globalThis.localStorage.getItem('screenhire:confirm-moves')).toBe('on');
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

  it("gives a teammate's view the person added a menu with Remove from sidebar and nothing they may not do", async () => {
    stubViewport(1280);
    const viewActions = actions();
    render(ShellHarness, { props: { user, saved: [savedView({ shared: true, mine: false, canEdit: false })], viewActions } });

    expect(within(main()).getByRole('link', { name: /^Referrals in screening/ })).toBeInTheDocument();
    const menu = await open('Referrals in screening');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Remove from sidebar']);

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from sidebar' }));
    expect(viewActions.removeFromSidebar).toHaveBeenCalledWith(expect.objectContaining({ id: view.id }));
  });

  it("offers a role that edits anyone's views Remove from sidebar first, then Rename, Make private and Delete view", async () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, saved: [savedView({ shared: true, mine: false, canEdit: true })], viewActions: actions() } });

    const menu = await open('Referrals in screening');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual([
      'Remove from sidebar',
      'Rename',
      'Make private',
      'Delete view',
    ]);
  });

  it('leaves a shared view nobody added out of the sidebar, and offers it in Browse team views', async () => {
    stubViewport(1280);
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/applications': () => Response.json([]) })));
    const offered = savedView({ id: 'offered', name: 'Team queue', shared: true, mine: false, canEdit: false, inSidebar: false });
    const teamViews = { views: [offered], seenAt: '2026-10-01T00:00:00Z', jobTitles: {} };
    render(ShellHarness, { props: { user, saved: [offered], viewActions: actions(), teamViews } });

    expect(within(main()).queryByRole('link', { name: /^Team queue/ })).toBeNull();
    await fireEvent.click(within(main()).getByRole('button', { name: 'Browse team views' }));

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    expect(within(sheet).getByRole('heading', { name: 'Team queue' })).toBeInTheDocument();
  });

  it('has no Browse team views button without the views to browse, and hides it in the icon rail', () => {
    stubViewport(1280);
    const { unmount } = render(ShellHarness, { props: { user, viewActions: actions() } });
    expect(within(main()).queryByRole('button', { name: 'Browse team views' })).toBeNull();
    unmount();

    render(ShellHarness, {
      props: { user, viewActions: actions(), teamViews: { views: [], seenAt: '2026-10-01T00:00:00Z', jobTitles: {} } },
    });
    expect(within(main()).getByRole('button', { name: 'Browse team views' }).closest('li')).toHaveClass('group-data-[collapsible=icon]:hidden');
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
