import type { OrganizationRole } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { interviewer, userAs } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Layout from './+layout.svelte';

vi.mock('$app/state', () => ({ page: { data: {}, url: new URL('http://dashboard.test/verification') }, navigating: { to: null } }));

const content = createRawSnippet(() => ({ render: () => '<p>Verification page content</p>' }));
const renderAs = (signedIn: Parameters<typeof userAs>[0] | typeof interviewer) =>
  render(WithQueryClient, {
    props: { component: Layout, props: { data: { user: typeof signedIn === 'string' ? userAs(signedIn) : signedIn }, children: content } },
  });

// Verification kept the permission its page had as a Settings page: Edit settings, as the API asks (ADR-0014).
describe('the Verification area gate', () => {
  it.each<Exclude<OrganizationRole, 'Custom'>>(['Owner', 'Admin'])('opens the page to an %s', (role) => {
    renderAs(role);

    expect(screen.getByText('Verification page content')).toBeInTheDocument();
    expect(screen.queryByText('Not in your role')).not.toBeInTheDocument();
  });

  it.each<Exclude<OrganizationRole, 'Custom'>>(['Recruiter', 'Viewer'])('tells a %s their role cannot change it, and shows no page content', async (role) => {
    renderAs(role);

    expect(screen.getByText('Not in your role')).toBeInTheDocument();
    expect(screen.getByText('Your role cannot change verification. Ask an owner or admin of your organization.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Verification' })).toBeInTheDocument();
    expect(screen.queryByText('Verification page content')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
    await expectNoAxeViolations();
  });

  it('refuses a custom role without Edit settings, whatever else it holds', () => {
    renderAs(interviewer);

    expect(screen.getByText('Not in your role')).toBeInTheDocument();
  });
});
