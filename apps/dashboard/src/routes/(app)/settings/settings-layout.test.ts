import type { CurrentUser, OrganizationRole } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Layout from './+layout.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/team') }, navigating: { to: null } }));

const content = createRawSnippet(() => ({ render: () => '<p>Settings page content</p>' }));

const renderAs = (role: OrganizationRole | null) => {
  const signedIn: CurrentUser = { ...user, organization: role ? { ...user.organization!, role } : null };
  return render(WithQueryClient, { props: { component: Layout, props: { data: { user: signedIn }, children: content } } });
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('settings layout', () => {
  it.each<OrganizationRole>(['Owner', 'Admin'])('shows the page to an %s', (role) => {
    renderAs(role);

    expect(screen.getByText('Settings page content')).toBeInTheDocument();
    expect(screen.queryByText('Owners and admins only')).not.toBeInTheDocument();
  });

  it.each<OrganizationRole>(['Recruiter', 'Viewer'])('tells a %s who can change settings, and shows no page content', async (role) => {
    renderAs(role);

    expect(screen.getByText('Owners and admins only')).toBeInTheDocument();
    expect(screen.getByText('Ask an owner or admin of your organization to change its settings.')).toBeInTheDocument();
    expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
    await expectNoAxeViolations();
  });

  it('shows no page content when the session has no organization', () => {
    renderAs(null);

    expect(screen.getByText('Owners and admins only')).toBeInTheDocument();
    expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
  });
});
