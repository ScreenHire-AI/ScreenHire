import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { user, userAs } from '../../test-support/fixtures.js';
import ShellHarness from '../../test-support/shell-harness.svelte';
import { stubViewport } from '../../test-support/viewport.js';
import { preferences } from '../preferences.svelte.js';

vi.mock('$app/state', () => ({ page: { url: new URL('http://dashboard.test/candidates?view=needs-review') } }));

const sidebar = () => document.querySelector<HTMLElement>('[data-slot="sidebar"]');
const tooltip = () => document.querySelector('[data-slot="tooltip-content"]');
const main = () => screen.getByRole('navigation', { name: 'Main' });
// The fixture user is an Owner, so the sidebar has Verification after Reports and ends with the one Settings item, which
// opens the Settings home; the settings themselves are listed there, and in each group's menu.
const views = ['To review', 'To verify', 'Overdue', 'New today', 'Not screened', 'Interviewing'];

// jsdom does not navigate; the sheet closing is what these tests watch, not the page change.
const stopNavigation = (event: MouseEvent) => event.preventDefault();
beforeEach(() => document.addEventListener('click', stopNavigation));

afterEach(() => {
  document.removeEventListener('click', stopNavigation);
  vi.unstubAllGlobals();
  preferences.setShortcutsEnabled(true);
});

// The frame every signed-in page sits in, at each size class of the design system (section 8).
describe('app shell', () => {
  it('shows the full sidebar from lg: icons and labels, plain view icons, no view counts, one Settings item', async () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, openTasks: 3 } });

    expect(sidebar()).toHaveAttribute('data-state', 'expanded');
    const links = within(main()).getAllByRole('link');
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Jobs', 'Candidates', 'Templates', 'My tasks 3', 'Reports', 'Verification', ...views, 'Settings']);
    // Navigation pairs an icon with its label (I1); no view has a status icon, so I2 holds even for To verify and Overdue.
    expect(main().querySelector('[data-status-icon]')).toBeNull();
    expect(links.every((link) => link.querySelector('svg'))).toBe(true);
    expect(screen.getByRole('link', { name: 'Verification' })).toHaveAttribute('href', '/verification');
    expect(screen.getByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings');
    // Settings and its groups are not items of their own, and neither is Saved candidates (a choice on the Candidates page).
    for (const name of ['Organization', 'Hiring setup', 'Careers site', 'Integrations', 'Team', 'Saved candidates']) {
      expect(within(main()).queryByRole('link', { name })).toBeNull();
    }
    // Only My tasks has a count, in the grey count badge after its label.
    expect([...main().querySelectorAll('[data-slot="badge"]')].map((badge) => badge.textContent)).toEqual(['3']);
    expect(within(links[3]!).getByText('3')).toHaveAttribute('data-slot', 'badge');
    expect(screen.getByRole('link', { name: 'To review' })).toHaveAttribute('aria-current', 'page');
    expect(screen.queryByRole('button', { name: 'Open navigation' })).toBeNull();
    await expectNoAxeViolations();
  });

  it('is an icon rail at md, with each label in a tooltip', async () => {
    stubViewport(768);
    render(ShellHarness, { props: { user } });

    expect(sidebar()).toHaveAttribute('data-state', 'collapsed');
    expect(sidebar()).toHaveAttribute('data-collapsible', 'icon');
    // The labels stay in the accessible name; only their text hides.
    const candidates = within(main()).getByRole('link', { name: 'Candidates' });
    expect(within(candidates).getByText('Candidates')).toHaveClass('group-data-[collapsible=icon]:sr-only');
    expect(screen.queryByRole('button', { name: 'Open navigation' })).toBeNull();

    await fireEvent.focus(candidates);

    await waitFor(() => expect(tooltip()).toHaveTextContent('Candidates'));
    expect([...tooltip()!.querySelectorAll('kbd')].map((key) => key.textContent)).toEqual(['G', 'C']);
    await expectNoAxeViolations();
  });

  it('puts the sidebar in a sheet behind the menu button on phones, which a link closes', async () => {
    stubViewport(390);
    render(ShellHarness, { props: { user } });

    expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));

    const sheet = await screen.findByRole('dialog', { name: 'Navigation' });
    expect(within(sheet).getAllByRole('link', { name: 'To review' })).toHaveLength(1);
    await expectNoAxeViolations();

    await fireEvent.click(within(sheet).getByRole('link', { name: 'Jobs' }));

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Navigation' })).toBeNull());
  });

  it('shows Back, a level up the breadcrumb, in place of the menu button on a phone detail page', () => {
    stubViewport(390);
    const crumbs = [{ label: 'Candidates', href: '/candidates' }, { label: 'Senior product designer', href: '/jobs/1' }, { label: 'Jordan Lee' }];
    render(ShellHarness, { props: { user, crumbs, back: true } });

    expect(screen.getByRole('link', { name: 'Back to Senior product designer' })).toHaveAttribute('href', '/jobs/1');
    expect(screen.queryByRole('button', { name: 'Open navigation' })).toBeNull();
  });

  it('keeps Back for phones: wider screens have the sidebar', () => {
    stubViewport(768);
    render(ShellHarness, { props: { user, crumbs: [{ label: 'Candidates', href: '/candidates' }, { label: 'Jordan Lee' }], back: true } });

    expect(screen.queryByRole('link', { name: 'Back to Candidates' })).toBeNull();
  });

  it('has a breadcrumb, search with its key caps, the theme switch and a keyboard button that opens the shortcuts dialog', async () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, crumbs: [{ label: 'Jobs', href: '/' }, { label: 'Senior product designer' }] } });

    const breadcrumb = screen.getByRole('navigation', { name: 'Breadcrumb' });
    expect(within(breadcrumb).getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/');
    expect(within(breadcrumb).getByText('Senior product designer').closest('[aria-current="page"]')).not.toBeNull();
    const search = screen.getByRole('button', { name: /^Search/ });
    expect([...search.querySelectorAll('kbd[data-slot="kbd"]')].map((key) => key.textContent)).toEqual(['Ctrl', 'K']);
    expect(screen.getByRole('button', { name: 'Switch to dark theme' })).toBeInTheDocument();

    const keyboard = screen.getByRole('button', { name: 'Keyboard shortcuts' });
    await fireEvent.focus(keyboard);
    await waitFor(() => expect(tooltip()).toHaveTextContent('Keyboard shortcuts'));
    expect(tooltip()!.querySelector('kbd')).toHaveTextContent('?');
    await fireEvent.click(keyboard);

    const dialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' });
    expect(within(dialog).getByRole('switch', { name: 'Single-key shortcuts' })).toBeChecked();
    await expectNoAxeViolations();
  });

  it('drops single-key hints from tooltips once single-key shortcuts are off', async () => {
    preferences.setShortcutsEnabled(false);
    stubViewport(768);
    render(ShellHarness, { props: { user } });

    await fireEvent.focus(screen.getByRole('button', { name: 'Keyboard shortcuts' }));
    await waitFor(() => expect(tooltip()).toHaveTextContent('Keyboard shortcuts'));
    expect(tooltip()!.querySelector('kbd')).toBeNull();
    expect(within(main()).getByRole('link', { name: 'Jobs' })).not.toHaveAttribute('aria-keyshortcuts');
  });

  it('opens the shortcuts dialog from the user menu, which phones reach through the sheet', async () => {
    stubViewport(390);
    render(ShellHarness, { props: { user } });
    await fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
    const sheet = await screen.findByRole('dialog', { name: 'Navigation' });

    await fireEvent.pointerDown(within(sheet).getByRole('button', { name: /^Avery Quinn/ }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: /^Keyboard shortcuts/ }));

    expect(await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })).toBeInTheDocument();
    // The dialog opens over the sheet; closing it returns focus to the sheet, never to the page body (WCAG 2.4.3).
    await fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Keyboard shortcuts' })).toBeNull());
    await waitFor(() => expect(sheet).toContainElement(document.activeElement as HTMLElement));
  });

  it('opens the Notifications sheet from the user menu', async () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user } });

    await fireEvent.pointerDown(screen.getByRole('button', { name: /^Avery Quinn/ }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Notifications' }));

    expect(await screen.findByText('Notifications sheet open')).toBeInTheDocument();
  });

  it('keeps the sheet shut after the window widens past phone size and narrows again', async () => {
    const viewport = stubViewport(390);
    render(ShellHarness, { props: { user } });
    await fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
    await screen.findByRole('dialog', { name: 'Navigation' });

    viewport.resize(800);
    await waitFor(() => expect(sidebar()).toHaveAttribute('data-collapsible', 'icon'));
    viewport.resize(390);

    await screen.findByRole('button', { name: 'Open navigation' });
    expect(screen.queryByRole('dialog', { name: 'Navigation' })).toBeNull();
  });

  it('shows no count after My tasks while it loads or at none', () => {
    stubViewport(1280);
    const { unmount } = render(ShellHarness, { props: { user } });
    expect(main().querySelector('[data-slot="badge"]')).toBeNull();
    unmount();

    render(ShellHarness, { props: { user, openTasks: 0 } });
    expect(main().querySelector('[data-slot="badge"]')).toBeNull();
  });

  it('keeps Settings one item in the icon rail, with its label in a tooltip', async () => {
    stubViewport(768);
    render(ShellHarness, { props: { user } });

    const settings = within(main()).getByRole('link', { name: 'Settings' });
    await fireEvent.focus(settings);

    await waitFor(() => expect(tooltip()).toHaveTextContent('Settings'));
    expect(within(main()).getByRole('link', { name: 'Verification' })).toBeInTheDocument();
  });

  it.each(['Recruiter', 'Viewer'] as const)('shows a %s Verification but no Settings: no permission of theirs opens Settings', (role) => {
    stubViewport(1280);
    render(ShellHarness, { props: { user: userAs(role) } });

    // Everyone who can see verification has the area (read-only for them); only Settings needs a permission.
    expect(within(main()).queryByRole('link', { name: 'Settings' })).toBeNull();
    // The sections (My tasks, Reports and Verification included) and the six views.
    expect(within(main()).getAllByRole('link').map((link) => link.textContent?.trim())).toEqual([
      'Jobs', 'Candidates', 'Templates', 'My tasks', 'Reports', 'Verification', ...views,
    ]);
  });

  it('shows a label cut with an ellipsis in full in a tooltip in the full sidebar (F5)', async () => {
    // jsdom does no layout: make the long saved view's label wider than its box.
    const scrollWidth = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollWidth')!;
    Object.defineProperty(HTMLElement.prototype, 'scrollWidth', {
      configurable: true,
      get(this: HTMLElement) {
        return this.textContent === 'To verify' ? 2000 : 0;
      },
    });
    try {
      stubViewport(1280);
      render(ShellHarness, { props: { user } });

      await fireEvent.focus(within(main()).getByRole('link', { name: 'To verify' }));
      await waitFor(() => expect(tooltip()).toHaveTextContent('To verify'));
      expect(tooltip()).not.toHaveAttribute('hidden');

      await fireEvent.focus(within(main()).getByRole('link', { name: 'New today' }));
      await waitFor(() => expect(document.querySelector('[data-slot="tooltip-content"]:not([hidden])')).toBeNull());
    } finally {
      delete (HTMLElement.prototype as { scrollWidth?: number }).scrollWidth;
      Object.defineProperty(Element.prototype, 'scrollWidth', scrollWidth);
    }
  });
});
