import type { CurrentUser, OrganizationRole } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { interviewer, user, userAs } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Layout from './+layout.svelte';

const state = vi.hoisted(() => ({ url: new URL('http://dashboard.test/settings/team') }));
vi.mock('$app/state', () => ({
  page: {
    data: {},
    get url() {
      return state.url;
    },
  },
  navigating: { to: null },
}));

const content = createRawSnippet(() => ({ render: () => '<p>Settings page content</p>' }));

const renderAs = (signedIn: CurrentUser, path = '/settings/team') => {
  state.url = new URL(`http://dashboard.test${path}`);
  return render(WithQueryClient, { props: { component: Layout, props: { data: { user: signedIn }, children: content } } });
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('settings layout', () => {
  it.each<Exclude<OrganizationRole, 'Custom'>>(['Owner', 'Admin'])('shows the page to an %s', (role) => {
    renderAs(userAs(role));

    expect(screen.getByText('Settings page content')).toBeInTheDocument();
    expect(screen.queryByText('Not in your role')).not.toBeInTheDocument();
  });

  it.each<Exclude<OrganizationRole, 'Custom'>>(['Recruiter', 'Viewer'])('tells a %s their role cannot change it, and shows no page content', async (role) => {
    renderAs(userAs(role));

    expect(screen.getByText('Not in your role')).toBeInTheDocument();
    expect(screen.getByText('Your role cannot change team. Ask an owner or admin of your organization.')).toBeInTheDocument();
    expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
    await expectNoAxeViolations();
  });

  it('opens a page by its permission, whatever the role is called', () => {
    const keeper: CurrentUser = { ...interviewer, organization: { ...interviewer.organization!, permissions: ['ViewHiring', 'ManageApiKeys'] } };

    renderAs(keeper, '/settings/api-keys');

    expect(screen.getByText('Settings page content')).toBeInTheDocument();
  });

  it('refuses a custom role the page its permissions do not open', () => {
    renderAs(interviewer, '/settings/roles/compare');

    expect(screen.getByText('Not in your role')).toBeInTheDocument();
    expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
  });

  it('shows no page content when the session has no organization', () => {
    renderAs({ ...user, organization: null });

    expect(screen.getByText('Not in your role')).toBeInTheDocument();
    expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
  });
});
