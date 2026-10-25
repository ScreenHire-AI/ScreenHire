import { render, screen, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import SettingsMenu from './settings-menu.svelte';

vi.mock('$app/state', () => ({ page: { url: new URL('http://dashboard.test/settings/team') } }));

const content = createRawSnippet(() => ({ render: () => '<p>Page content</p>' }));

describe('settings menu', () => {
  it('lists every settings page in order, beside the page content, and marks the current one', async () => {
    render(SettingsMenu, { props: { children: content } });
    const menu = screen.getByRole('navigation', { name: 'Settings' });

    expect(within(menu).getAllByRole('link').map((link) => link.textContent?.trim())).toEqual([
      'Organization',
      'Team',
      'Roles',
      'Branding',
      'Integrity',
      'API keys',
      'Webhooks',
      'Email templates',
    ]);
    expect(within(menu).getByRole('link', { name: 'Team' })).toHaveAttribute('aria-current', 'page');
    expect(within(menu).getByRole('link', { name: 'Roles' })).toHaveAttribute('href', '/settings/roles');
    expect(screen.getByText('Page content')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("marks a record's page under its entry: a webhook endpoint is under Webhooks", () => {
    render(SettingsMenu, { props: { children: content, current: '/settings/webhooks/e1/edit' } });

    const current = screen.getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page');
    expect(current.map((link) => link.textContent?.trim())).toEqual(['Webhooks']);
  });
});
