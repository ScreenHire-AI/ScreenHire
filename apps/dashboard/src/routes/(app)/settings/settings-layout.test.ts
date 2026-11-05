import type { CurrentUser, OrganizationRole } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { interviewer, user, userAs } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WithShell from '../../../test-support/with-shell.svelte';
import Layout from './+layout.svelte';

const state = vi.hoisted(() => ({ url: new URL('http://dashboard.test/settings/team') }));
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock('$app/navigation', () => navigation);
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
  navigation.goto.mockClear();
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

  // Data requests need Erase candidates and every job, as the API does (ADR-0015).
  describe('Data requests', () => {
    it.each<Exclude<OrganizationRole, 'Custom'>>(['Owner', 'Admin'])('shows the page to an %s', (role) => {
      renderAs(userAs(role), '/settings/data-requests');

      expect(screen.getByText('Settings page content')).toBeInTheDocument();
    });

    it.each<Exclude<OrganizationRole, 'Custom'>>(['Recruiter', 'Viewer'])('tells a %s their role cannot change it', async (role) => {
      renderAs(userAs(role), '/settings/data-requests');

      expect(screen.getByText('Not in your role')).toBeInTheDocument();
      expect(screen.getByText('Your role cannot change data requests. Ask an owner or admin of your organization.')).toBeInTheDocument();
      expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
      await expectNoAxeViolations();
    });

    it('says an admin who sees only their own jobs needs all jobs, and shows no page content', () => {
      const admin = userAs('Admin');
      renderAs({ ...admin, organization: { ...admin.organization!, seesAllJobs: false } }, '/settings/data-requests');

      expect(screen.getByText('Needs all jobs')).toBeInTheDocument();
      expect(screen.getByText(/Data requests reach every job, and you see only the jobs you're on/)).toBeInTheDocument();
      expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
    });
  });

  it('shows no page content when the session has no organization', () => {
    renderAs({ ...user, organization: null });

    expect(screen.getByText('Not in your role')).toBeInTheDocument();
    expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
  });

  // Slack's page exists only where a Slack app is set up (A3).
  describe('Slack', () => {
    const renderSlack = (features: { slack?: boolean }, signedIn: CurrentUser = userAs('Owner')) => {
      state.url = new URL('http://dashboard.test/settings/slack');
      return render(WithQueryClient, { props: { component: WithShell, props: { component: Layout, props: { data: { user: signedIn }, children: content }, features } } });
    };

    it('shows the page where Slack is set up', () => {
      renderSlack({ slack: true });

      expect(screen.getByText('Settings page content')).toBeInTheDocument();
      expect(navigation.goto).not.toHaveBeenCalled();
    });

    it('sends an address to it to the first page the person may use where it is not, drawing nothing on the way', () => {
      renderSlack({ slack: false });

      expect(navigation.goto).toHaveBeenCalledWith('/settings/organization', { replaceState: true });
      expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
      expect(screen.queryByText('Not in your role')).not.toBeInTheDocument();
    });

    it('waits, drawing nothing and going nowhere, until the shell has heard from Slack', () => {
      renderSlack({});

      expect(navigation.goto).not.toHaveBeenCalled();
      expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
      expect(screen.queryByText('Not in your role')).not.toBeInTheDocument();
    });

    it.each([
      ['a role that cannot use it', userAs('Viewer')],
      ['no organization, where the shell never asks Slack', { ...user, organization: null }],
    ])('answers someone with %s without waiting for Slack', (_, signedIn) => {
      renderSlack({}, signedIn);

      expect(screen.getByText('Not in your role')).toBeInTheDocument();
      expect(screen.queryByText('Settings page content')).not.toBeInTheDocument();
      expect(navigation.goto).not.toHaveBeenCalled();
    });
  });
});
