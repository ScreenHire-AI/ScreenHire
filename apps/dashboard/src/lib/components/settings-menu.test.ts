import { render, screen, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import type { CurrentUser } from '@screenhire/api-client';
import { userAs } from '../../test-support/fixtures.js';
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

  it('lists every settings page in order, in its named group, beside the page content, and marks the current one', async () => {
    render(SettingsMenu, { props: { children: content } });
    const menu = screen.getByRole('navigation', { name: 'Settings' });

    const groups = within(menu).getAllByRole('list');
    expect(groups.map((group) => [group.getAttribute('aria-labelledby') && document.getElementById(group.getAttribute('aria-labelledby')!)?.textContent, within(group).getAllByRole('link').map((link) => link.textContent?.trim())])).toEqual([
      ['Organization', ['General', 'Team', 'Roles']],
      ['Hiring', ['Careers site', 'Branding', 'Verification']],
      ['Integrations', ['Slack', 'MCP server', 'Webhooks', 'API keys']],
    ]);
    expect(within(menu).getByRole('link', { name: 'Team' })).toHaveAttribute('aria-current', 'page');
    expect(within(menu).getByRole('link', { name: 'Roles' })).toHaveAttribute('href', '/settings/roles');
    expect(screen.getByText('Page content')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('lists only the pages the role may use', () => {
    state.user = userAs('Recruiter');
    render(SettingsMenu, { props: { children: content } });
    const labels = within(screen.getByRole('navigation', { name: 'Settings' }))
      .queryAllByRole('link')
      .map((link) => link.textContent?.trim());

    expect(labels).not.toContain('Team');
    expect(labels).not.toContain('Roles');
    expect(labels).not.toContain('API keys');
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
      render(SettingsMenu, { props: { children: content, current: '/settings/webhooks' } });

      // Webhooks is the ninth entry, at 800px: the row scrolls to 750 so it sits in the middle.
      expect(scrolledTo).toEqual([750]);
    } finally {
      Object.defineProperty(HTMLElement.prototype, 'offsetLeft', offsetLeft);
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', offsetWidth);
      if (clientWidth) Object.defineProperty(HTMLElement.prototype, 'clientWidth', clientWidth);
      else delete (HTMLElement.prototype as { clientWidth?: number }).clientWidth;
      Object.defineProperty(Element.prototype, 'scrollLeft', scrollLeft);
    }
  });
});
