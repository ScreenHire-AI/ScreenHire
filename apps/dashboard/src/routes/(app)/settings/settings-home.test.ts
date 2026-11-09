import type { CurrentUser } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { interviewer, userAs } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WithShell from '../../../test-support/with-shell.svelte';
import type { SendingDomainMark } from '../../../lib/sending-domain.js';
import type { SettingsFeatures } from '../../../lib/settings.js';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: {}, url: new URL('http://dashboard.test/settings') }, navigating: { to: null } }));

function renderHome(signedIn: CurrentUser = userAs('Owner'), features: SettingsFeatures = { slack: true, billing: true }, sendingDomainMark?: SendingDomainMark) {
  return render(WithQueryClient, {
    props: { component: WithShell, props: { component: Page, props: { data: { user: signedIn } }, features, sendingDomainMark } },
  });
}

const panel = (name: string) => screen.getByRole('region', { name });
const linksIn = (element: HTMLElement) => within(element).getAllByRole('link').map((link) => link.textContent?.trim());

describe('the Settings home', () => {
  it('lists every setting an Owner may use in three groups, each name a link, under the Settings title', async () => {
    renderHome();

    expect(screen.getByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toHaveTextContent('Settings');
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim())).toEqual(['Organization', 'Careers site', 'Integrations']);
    expect(linksIn(panel('Organization'))).toEqual(['General', 'Team', 'Roles', 'Email domain', 'Data requests', 'Billing']);
    expect(linksIn(panel('Careers site'))).toEqual(['Careers page', 'Branding']);
    expect(linksIn(panel('Integrations'))).toEqual(['Slack', 'MCP server', 'Webhooks', 'API keys']);
    expect(within(panel('Organization')).getByRole('link', { name: 'Email domain' })).toHaveAttribute('href', '/settings/sending-domain');
    expect(within(panel('Careers site')).getByRole('link', { name: 'Careers page' })).toHaveAttribute('href', '/settings/careers-site');
    expect(screen.queryByRole('link', { name: 'Verification' })).toBeNull();
    await expectNoAxeViolations();
  });

  it('has no icons and no counts on its rows or its panel titles (W7, W9)', () => {
    renderHome();

    for (const name of ['Organization', 'Careers site', 'Integrations']) {
      expect(panel(name).querySelector('li svg')).toBeNull();
      expect(panel(name).querySelector('[data-slot="badge"]')).toBeNull();
      expect(within(panel(name)).getByRole('heading', { level: 2 })).toHaveTextContent(new RegExp(`^${name}$`));
    }
  });

  it('makes the whole row the link, so a tap on its line opens the setting too (F4)', () => {
    renderHome();
    const link = within(panel('Organization')).getByRole('link', { name: 'General' });

    expect(link.closest('li')).toHaveClass('relative');
    expect(link).toHaveClass('after:absolute', 'after:inset-0');
  });

  it('adds a line under a name only where the name does not explain itself', () => {
    renderHome();
    const rows = (name: string) => within(panel(name)).getAllByRole('listitem');
    const row = (group: string, label: string) => rows(group).find((item) => within(item).queryByRole('link', { name: label }))!;

    expect(row('Organization', 'General')).toHaveTextContent('Your name and where your data lives');
    expect(row('Organization', 'Data requests')).toHaveTextContent('Candidates asking to see or erase their data');
    expect(row('Organization', 'Billing').querySelector('p')).toBeNull();
    expect(row('Careers site', 'Branding').querySelector('p')).toBeNull();
    expect(row('Integrations', 'Slack').querySelector('p')).toBeNull();
    expect(row('Integrations', 'MCP server')).toHaveTextContent('Let an AI assistant work with your jobs');
    expect(row('Organization', 'Team').querySelector('p')).toBeNull();
    expect(row('Organization', 'Roles').querySelector('p')).toBeNull();
    // A plumber or a cafe owner has never met the word, so Webhooks says what it is for, as API keys does.
    expect(row('Integrations', 'Webhooks')).toHaveTextContent('Tell another tool when something happens');
  });

  it('leaves out a setting the role cannot use, and a group with none left (A3)', () => {
    const keeper: CurrentUser = { ...interviewer, organization: { ...interviewer.organization!, permissions: ['ViewHiring', 'ManageApiKeys'], seesAllJobs: true } };
    renderHome(keeper);

    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim())).toEqual(['Integrations']);
    expect(linksIn(panel('Integrations'))).toEqual(['MCP server', 'API keys']);
    expect(screen.queryByRole('link', { name: 'Team' })).toBeNull();
  });

  it('leaves out Slack and Billing where the installation has none of them', () => {
    renderHome(userAs('Owner'), { slack: false, billing: false });

    expect(screen.queryByRole('link', { name: 'Slack' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Billing' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Webhooks' })).toBeInTheDocument();
  });

  describe('search', () => {
    const search = () => screen.getByRole('searchbox', { name: 'Search settings' });
    const type = (value: string) => fireEvent.input(search(), { target: { value } });
    // The top bar's Live indicator is a status too, so the count is found by its words.
    const count = () => screen.queryByText(/^[0-9]+ settings?$/);

    it('is named Search settings, in a search landmark, and shows the groups until something is typed', () => {
      renderHome();

      expect(screen.getByRole('search')).toContainElement(search());
      expect(search()).toHaveAttribute('placeholder', 'Search settings');
      expect(count()).toBeNull();
    });

    it('finds a setting by the everyday words kept beside it, with its group after the name', async () => {
      renderHome();

      await type('logo');

      expect(count()).toHaveTextContent('1 setting');
      expect(count()).toHaveAttribute('role', 'status');
      expect(count()).not.toHaveClass('sr-only');
      const results = screen.getByRole('region', { name: 'Settings that match' });
      expect(linksIn(results)).toEqual(['Branding']);
      expect(results).toHaveTextContent('Careers site');
      expect(within(results).getByRole('link', { name: 'Branding' })).toHaveAttribute('href', '/settings/branding');
      // The groups give way to the one list of matches.
      expect(screen.queryByRole('region', { name: 'Organization' })).toBeNull();
      await expectNoAxeViolations();
    });

    it('finds Billing by "invoice" and Team and Roles by "invite", counting what it found', async () => {
      renderHome();

      await type('invoice');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Billing']);

      await type('invite');
      expect(count()).toHaveTextContent('2 settings');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Team', 'Roles']);
    });

    it('finds what lives in its own area by everyday words, and names the area where a setting has its group', async () => {
      renderHome();

      await type('ai key');
      const results = screen.getByRole('region', { name: 'Settings that match' });
      expect(linksIn(results)).toEqual(['AI model']);
      expect(within(results).getByRole('link', { name: 'AI model' })).toHaveAttribute('href', '/verification/ai-model');
      expect(results).toHaveTextContent('Verification');

      // Slack asks for scorecards too, so it is a setting that matches, and it comes first.
      await type('scorecard');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Slack', 'Scorecards']);
      expect(screen.getByRole('link', { name: 'Scorecards' })).toHaveAttribute('href', '/templates?tab=scorecards');
      expect(screen.getByRole('region', { name: 'Settings that match' })).toHaveTextContent('Templates');

      await type('notifications');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Slack', 'Notifications']);
    });

    it('offers an area page only to a role that may open it, never a dead end (A3)', async () => {
      renderHome(interviewer);

      await type('ai key');

      // An Interviewer cannot edit the AI model, so it is not offered.
      expect(screen.getByText('No setting matches "ai key".')).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'AI model' })).toBeNull();
    });

    it('finds by name, by description and by group, ignoring case and the spaces at the ends', async () => {
      renderHome();

      await type('  WEBHOOKS ');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Webhooks']);

      await type('erase');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Data requests']);

      await type('integrations');
      expect(linksIn(screen.getByRole('region', { name: 'Settings that match' }))).toEqual(['Slack', 'MCP server', 'Webhooks', 'API keys']);
    });

    it('says when nothing matches, and Clear search brings the groups back', async () => {
      renderHome();

      await type('zzz');

      expect(count()).toHaveTextContent('0 settings');
      // The empty panel says it on screen, so the count is there for screen readers only (W7).
      expect(count()).toHaveClass('sr-only');
      expect(screen.getByText('No setting matches "zzz".')).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Team' })).toBeNull();
      await expectNoAxeViolations();

      await fireEvent.click(screen.getByRole('button', { name: 'Clear search' }));

      expect(search()).toHaveValue('');
      expect(screen.getByRole('region', { name: 'Organization' })).toBeInTheDocument();
      expect(count()).toBeNull();
    });

    it('only searches what the role may use', async () => {
      renderHome({ ...userAs('Admin') });

      await type('invoice');

      // An Admin has no Billing, so the search has nothing to show, not a disabled row (A3).
      expect(screen.getByText('No setting matches "invoice".')).toBeInTheDocument();
    });
  });

  describe('the email domain', () => {
    const row = () => within(panel('Organization')).getByRole('link', { name: 'Email domain' }).closest('li')!;

    const features = { slack: true, billing: true };

    it.each([
      [{ status: 'waiting', label: 'Not set up' }],
      [{ status: 'needsFixes', label: 'Records to add' }],
    ] as const)('says %j on its row, as a status with an icon, a colour and the word (T1)', (mark) => {
      renderHome(userAs('Owner'), features, mark);

      const badge = within(row()).getByText(mark.label).closest('[data-status]')!;
      expect(badge.querySelector('svg')).not.toBeNull();
    });

    it('says nothing once the domain is verified, which is no mark, or before the shell has heard (T3)', () => {
      renderHome();

      expect(row().querySelector('[data-status]')).toBeNull();
      expect(row()).toHaveTextContent(/^Email domain\s*Send email from your own address$/);
    });

    it('shows it in a search result too', async () => {
      renderHome(userAs('Owner'), features, { status: 'needsFixes', label: 'Records to add' });

      await fireEvent.input(screen.getByRole('searchbox', { name: 'Search settings' }), { target: { value: 'dns' } });

      const results = screen.getByRole('region', { name: 'Settings that match' });
      expect(within(results).getByText('Records to add')).toBeInTheDocument();
    });
  });
});
