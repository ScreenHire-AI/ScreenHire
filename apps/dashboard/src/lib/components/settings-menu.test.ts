import { cleanup, fireEvent, render, screen, within } from '@testing-library/svelte';
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

  const menu = () => screen.getByRole('navigation', { name: 'Settings' });
  // A group's pages: its list is named by the group's heading.
  const linksOf = (group: string) =>
    within(within(menu()).getByRole('list', { name: group }))
      .getAllByRole('link')
      .map((link) => link.textContent?.trim());
  const groupNames = () => within(menu()).getAllByRole('list').map((list) => list.getAttribute('aria-labelledby') && document.getElementById(list.getAttribute('aria-labelledby')!)?.textContent);

  it('lists every settings page in its group on every settings page, beside the content, and marks the current one', async () => {
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true, billing: true } } });

    expect(groupNames()).toEqual(['Organization', 'Careers site', 'Integrations']);
    expect(linksOf('Organization')).toEqual(['General', 'Team', 'Roles', 'Email domain', 'Data requests', 'Billing']);
    expect(linksOf('Careers site')).toEqual(['Careers page', 'Branding']);
    expect(linksOf('Integrations')).toEqual(['Slack', 'MCP server', 'Webhooks', 'API keys']);
    expect(within(menu()).getByRole('link', { name: 'Team' })).toHaveAttribute('aria-current', 'page');
    expect(within(menu()).getByRole('link', { name: 'Roles' })).toHaveAttribute('href', '/settings/roles');
    expect(screen.getByText('Page content')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('is the same whole menu on a Careers site page and on an integration, marking that page', () => {
    for (const current of ['/settings/branding', '/settings/webhooks', '/settings/webhooks/new']) {
      const { unmount } = render(WithShell, { props: { component: SettingsMenu, props: { children: content, current }, features: { slack: true } } });

      expect(groupNames()).toEqual(['Organization', 'Careers site', 'Integrations']);
      expect(within(menu()).getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page').length).toBeLessThanOrEqual(1);
      unmount();
    }
  });

  it('leaves out Billing for an Admin, Email domain without an email provider, and Slack where none is set up (A3)', () => {
    state.user = userAs('Admin');
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: false, billing: true, sendingDomain: false } } });

    expect(linksOf('Organization')).toEqual(['General', 'Team', 'Roles', 'Data requests']);
    expect(linksOf('Integrations')).toEqual(['MCP server', 'Webhooks', 'API keys']);
  });

  it('lists only the pages the role may use, and leaves out a group with none left', () => {
    state.user = { ...userAs('Admin'), organization: { ...userAs('Admin').organization!, permissions: ['EditSettings'] } };
    render(WithShell, { props: { component: SettingsMenu, props: { children: content, current: '/settings/organization' }, features: { slack: true } } });

    // Edit settings opens General, the email domain and the careers site; the rest needs more.
    expect(linksOf('Organization')).toEqual(['General', 'Email domain']);
    expect(groupNames()).not.toContain('Integrations');
  });

  it('has no menu on a page that is not a setting, and still shows the content', () => {
    for (const current of ['/settings', '/settings/integrations', '/verification']) {
      const { unmount } = render(WithShell, { props: { component: SettingsMenu, props: { children: content, current }, features: { slack: true } } });

      expect(screen.queryByRole('navigation')).toBeNull();
      expect(screen.getByText('Page content')).toBeInTheDocument();
      unmount();
    }
  });

  it('shows only the content to someone whose role has no settings page', () => {
    state.user = userAs('Recruiter');
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true } } });

    expect(screen.queryByRole('navigation')).toBeNull();
    expect(screen.getByText('Page content')).toBeInTheDocument();
  });

  it('narrows the menu by the words kept beside each page, and says so when nothing matches', async () => {
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: { slack: true, billing: true } } });
    const search = within(menu()).getByRole('searchbox', { name: 'Search settings' });

    await fireEvent.input(search, { target: { value: 'logo' } });
    expect(groupNames()).toEqual(['Careers site']);
    expect(linksOf('Careers site')).toEqual(['Branding']);

    await fireEvent.input(search, { target: { value: 'invoice' } });
    expect(linksOf('Organization')).toEqual(['Billing']);

    await fireEvent.input(search, { target: { value: 'nothing like this' } });
    expect(within(menu()).queryAllByRole('link')).toHaveLength(0);
    expect(within(menu()).getByRole('status')).toHaveTextContent('No setting matches.');
  });

  it('marks Email domain with a check once it works and a cross while it needs fixing, on the same line as its name', () => {
    const emailDomain = () => within(menu()).getByRole('link', { name: /^Email domain/ });
    const marks = [
      { status: 'done', label: 'Verified' },
      { status: 'broken', label: 'Records to add' },
    ] as const;
    for (const sendingDomainMark of marks) {
      const { unmount } = render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: {}, sendingDomainMark } });

      // The words are for screen readers and the hover; the row stays one line.
      const mark = emailDomain().querySelector(`[data-setting-mark="${sendingDomainMark.status}"]`)!;
      expect(mark).toHaveAttribute('title', sendingDomainMark.label);
      expect(mark.querySelector('.sr-only')).toHaveTextContent(sendingDomainMark.label);
      expect(mark.querySelector('svg')).not.toBeNull();
      unmount();
    }

    // Not set up: the setting is optional, so no mark at all.
    render(WithShell, { props: { component: SettingsMenu, props: { children: content }, features: {} } });
    expect(emailDomain()).toHaveTextContent(/^Email domain$/);
    expect(emailDomain().querySelector('[data-setting-mark]')).toBeNull();
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

      // Data requests is the fifth entry, at 400px: the row scrolls to 350 so it sits in the middle.
      expect(scrolledTo).toEqual([350]);
    } finally {
      Object.defineProperty(HTMLElement.prototype, 'offsetLeft', offsetLeft);
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', offsetWidth);
      if (clientWidth) Object.defineProperty(HTMLElement.prototype, 'clientWidth', clientWidth);
      else delete (HTMLElement.prototype as { clientWidth?: number }).clientWidth;
      Object.defineProperty(Element.prototype, 'scrollLeft', scrollLeft);
      cleanup();
    }
  });
});
