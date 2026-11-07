import { cleanup, render, screen, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import type { CurrentUser } from '@screenhire/api-client';
import { userAs } from '../../test-support/fixtures.js';
import WithShell from '../../test-support/with-shell.svelte';
import SettingsMenu from './settings-menu.svelte';

const state = vi.hoisted(() => ({ user: undefined as CurrentUser | undefined }));
vi.mock('$app/state', () => ({
  page: {
    url: new URL('http://dashboard.test/settings/team'),
    get data() {
      return { user: state.user };
    },
  },
}));

const content = createRawSnippet(() => ({ render: () => '<p>Page content</p>' }));

describe('settings menu', () => {
  beforeEach(() => {
    state.user = userAs('Owner');
  });

  const linksOf = (name: string) =>
    within(screen.getByRole('navigation', { name }))
      .getAllByRole('link')
      .map((link) => link.textContent?.trim());

  it('lists only the pages of its area, beside the page content, and marks the current one', async () => {
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true } } });
    const menu = screen.getByRole('navigation', { name: 'Organization' });

    expect(linksOf('Organization')).toEqual(['General', 'Team', 'Roles', 'Data requests']);
    expect(within(menu).getByRole('link', { name: 'Team' })).toHaveAttribute('aria-current', 'page');
    expect(within(menu).getByRole('link', { name: 'Roles' })).toHaveAttribute('href', '/settings/roles');
    expect(screen.queryByRole('navigation', { name: 'Settings' })).toBeNull();
    expect(screen.getByText('Page content')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('adds Billing for an Owner where billing is set up, and never for an Admin (A3)', () => {
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true, billing: true } } });
    expect(linksOf('Organization')).toEqual(['General', 'Team', 'Roles', 'Data requests', 'Billing']);
    expect(within(screen.getByRole('navigation', { name: 'Organization' })).getByRole('link', { name: 'Billing' })).toHaveAttribute('href', '/settings/billing');
    cleanup();

    state.user = userAs('Admin');
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true, billing: true } } });
    expect(linksOf('Organization')).toEqual(['General', 'Team', 'Roles', 'Data requests']);
  });

  it('lists the Hiring setup pages on one of them, and leaves Email domain out where there is no email provider (A3)', () => {
    render(WithShell, { props: { component: SettingsMenu, props: { children: content, current: '/settings/branding' }, features: {} } });
    expect(linksOf('Hiring setup')).toEqual(['Careers site', 'Branding', 'Verification', 'Email domain']);
    cleanup();

    render(WithShell, { props: { component: SettingsMenu, props: { children: content, current: '/settings/branding' }, features: { sendingDomain: false } } });
    expect(linksOf('Hiring setup')).toEqual(['Careers site', 'Branding', 'Verification']);
  });

  it('has no menu on an integration, whose pages are records of the directory (S9), and still shows the content', () => {
    for (const current of ['/settings/slack', '/settings/webhooks', '/settings/integrations']) {
      const { unmount } = render(WithShell, { props: { component: SettingsMenu, props: { children: content, current }, features: { slack: true } } });

      expect(screen.queryByRole('navigation')).toBeNull();
      expect(screen.getByText('Page content')).toBeInTheDocument();
      unmount();
    }
  });

  it('lists only the pages the role may use', () => {
    state.user = userAs('Admin');
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true } } });

    expect(linksOf('Organization')).toEqual(['General', 'Team', 'Roles', 'Data requests']);
    cleanup();

    state.user = { ...userAs('Admin'), organization: { ...userAs('Admin').organization!, permissions: ['EditSettings'] } };
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true } } });
    expect(linksOf('Organization')).toEqual(['General']);
  });

  it('shows only the content to someone whose role has no page in the area', () => {
    state.user = userAs('Recruiter');
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true } } });

    expect(screen.queryByRole('navigation')).toBeNull();
    expect(screen.getByText('Page content')).toBeInTheDocument();
  });

  it('scrolls the row, which may hide the current entry past its edge, to centre it', () => {
    // jsdom does no layout: entries 100px wide side by side in a 200px row.
    const offsetLeft = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetLeft')!;
    const offsetWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth')!;
    const clientWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth');
    Object.defineProperty(HTMLElement.prototype, 'offsetLeft', {
      configurable: true,
      get(this: HTMLElement) {
        return this.tagName === 'LI' ? [...this.closest('nav')!.querySelectorAll('li')].indexOf(this as HTMLLIElement) * 100 : 0;
      },
    });
    Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 100 });
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 200 });
    // jsdom keeps no scroll position, so record what the row is asked to scroll to.
    const scrollLeft = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollLeft')!;
    const scrolledTo: number[] = [];
    Object.defineProperty(Element.prototype, 'scrollLeft', { configurable: true, get: () => 0, set: (value: number) => scrolledTo.push(value) });
    try {
      render(WithShell, { props: { component: SettingsMenu, props: { children: content, current: '/settings/data-requests' }, features: { slack: true } } });

      // Data requests is the fourth entry, at 300px: the row scrolls to 250 so it sits in the middle.
      expect(scrolledTo).toEqual([250]);
    } finally {
      Object.defineProperty(HTMLElement.prototype, 'offsetLeft', offsetLeft);
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', offsetWidth);
      if (clientWidth) Object.defineProperty(HTMLElement.prototype, 'clientWidth', clientWidth);
      else delete (HTMLElement.prototype as { clientWidth?: number }).clientWidth;
      Object.defineProperty(Element.prototype, 'scrollLeft', scrollLeft);
    }
  });
});
