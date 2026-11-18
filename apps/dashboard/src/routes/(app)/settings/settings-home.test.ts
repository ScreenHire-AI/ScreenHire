import type { CurrentUser } from '@screenhire/api-client';
import { render, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { goto } from '$app/navigation';
import { userAs } from '../../../test-support/fixtures.js';
import { nothingHappensFor } from '../../../test-support/quiet.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WithShell from '../../../test-support/with-shell.svelte';
import type { SettingsFeatures } from '../../../lib/settings.js';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: {}, url: new URL('http://dashboard.test/settings') }, navigating: { to: null } }));
vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));

function renderHome(signedIn: CurrentUser, features: SettingsFeatures = { slack: true, billing: true }) {
  return render(WithQueryClient, { props: { component: WithShell, props: { component: Page, props: { data: { user: signedIn } }, features } } });
}

afterEach(() => vi.mocked(goto).mockClear());

// Settings has no page of its own: its address opens the first settings page the role may use, whose menu lists the rest.
describe('the Settings address', () => {
  it('opens General for an Owner, in place of the address', async () => {
    renderHome(userAs('Owner'));

    await waitFor(() => expect(goto).toHaveBeenCalledWith('/settings/organization', { replaceState: true }));
  });

  it('opens the first page a narrower role may use', async () => {
    const teamOnly = { ...userAs('Admin'), organization: { ...userAs('Admin').organization!, permissions: ['ManageTeam'] } } as CurrentUser;
    renderHome(teamOnly);

    await waitFor(() => expect(goto).toHaveBeenCalledWith('/settings/team', { replaceState: true }));
  });

  it('goes nowhere for a role with no settings page, which the layout answers', async () => {
    renderHome(userAs('Recruiter'));

    await nothingHappensFor(20, 'a navigation for a role with no settings page');
    expect(goto).not.toHaveBeenCalled();
  });
});
