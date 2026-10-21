import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { user } from '../../test-support/fixtures.js';
import ShellHarness from '../../test-support/shell-harness.svelte';
import { stubViewport } from '../../test-support/viewport.js';
import { preferences } from '../preferences.svelte.js';

vi.mock('$app/state', () => ({ page: { url: new URL('http://dashboard.test/candidates?view=needs-review') } }));

const viewCounts = { 'needs-review': 2, 'verification-recommended': 1, 'new-today': 4, 'not-screened': 3, interviewing: 0 };
const sidebar = () => document.querySelector<HTMLElement>('[data-slot="sidebar"]');
const tooltip = () => document.querySelector('[data-slot="tooltip-content"]');
const main = () => screen.getByRole('navigation', { name: 'Main' });

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
  it('shows the full sidebar from lg: icons and labels, marks only on the integrity views, count pills', async () => {
    stubViewport(1280);
    render(ShellHarness, { props: { user, viewCounts } });

    expect(sidebar()).toHaveAttribute('data-state', 'expanded');
    const links = within(main()).getAllByRole('link');
    expect(links.map((link) => link.textContent?.trim())).toEqual([
      'Jobs',
      'Candidates',
      'Templates',
      'Needs review 2',
      'Verification recommended 1',
      'New today 4',
      'Not screened yet 3',
      'Interviewing 0',
    ]);
    // Navigation pairs an icon with its label (I1); status shapes mark the two integrity views only (I2).
    const marks = links.map((link) => link.querySelector('[data-shape]')?.getAttribute('data-shape') ?? (link.querySelector('svg') ? 'icon' : null));
    expect(marks).toEqual(['icon', 'icon', 'icon', 'diamond', 'square', 'icon', 'icon', 'icon']);
    expect(within(links[3]!).getByText('2')).toHaveAttribute('data-slot', 'badge');
    expect(screen.getByRole('link', { name: /^Needs review/ })).toHaveAttribute('aria-current', 'page');
    expect(screen.queryByRole('button', { name: 'Open navigation' })).toBeNull();
    await expectNoAxeViolations();
  });

  it('is an icon rail at md, with each label in a tooltip', async () => {
    stubViewport(768);
    render(ShellHarness, { props: { user, viewCounts } });

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
    render(ShellHarness, { props: { user, viewCounts } });

    expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));

    const sheet = await screen.findByRole('dialog', { name: 'Navigation' });
    expect(within(sheet).getAllByRole('link', { name: /^Needs review/ })).toHaveLength(1);
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
  });
});
