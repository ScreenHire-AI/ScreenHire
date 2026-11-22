import type { CurrentUser, OrganizationRole } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { goto } from '$app/navigation';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, interviewer, userAs } from '../../../test-support/fixtures.js';
import { settingsWith } from '../../../test-support/verification-settings.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Layout from './+layout.svelte';

const location = vi.hoisted(() => ({ path: '/verification' }));
vi.mock('$app/state', () => ({
  page: {
    data: {},
    get url() {
      return new URL(`http://dashboard.test${location.path}`);
    },
  },
  navigating: { to: null },
}));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const content = createRawSnippet(() => ({ render: () => '<p>Tab content</p>' }));
const renderAs = (signedIn: CurrentUser) =>
  render(WithQueryClient, { props: { component: Layout, props: { data: { user: signedIn }, children: content } } });
const tabNames = () => within(screen.getByRole('tablist', { name: 'Verification pages' })).getAllByRole('tab').map((tab) => tab.textContent?.trim());

const everyTab = ['Overview', 'Activity log', 'Checks'];

const asDictionary = (user: CurrentUser, permissions: NonNullable<CurrentUser['organization']>['permissions'], seesAllJobs = true): CurrentUser => ({
  ...user,
  organization: { ...user.organization!, permissions, seesAllJobs },
});

beforeEach(() => {
  location.path = '/verification';
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/organization/verification-settings': () => Response.json(settingsWith()) })));
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.mocked(goto).mockClear();
});

// Everyone who can see verification has the area. A tab a role cannot use is left out of the strip, never shown disabled (A3).
describe('the Verification area frame', () => {
  it.each<Exclude<OrganizationRole, 'Custom'>>(['Owner', 'Admin'])('gives an %s the three tabs, in order', (role) => {
    renderAs(userAs(role));

    expect(tabNames()).toEqual(everyTab);
    expect(screen.getByRole('heading', { level: 1, name: 'Verification' })).toBeInTheDocument();
    expect(screen.getByText('Tab content')).toBeInTheDocument();
  });

  it.each<Exclude<OrganizationRole, 'Custom'>>(['Recruiter', 'Viewer'])('gives a %s the same three tabs and no disabled one', async (role) => {
    renderAs(userAs(role));

    expect(tabNames()).toEqual(everyTab);
    expect(screen.getByText('Tab content')).toBeInTheDocument();
    expect(document.querySelectorAll('[role="tab"][disabled], [role="tab"][aria-disabled="true"]')).toHaveLength(0);
    await expectNoAxeViolations();
  });

  it('gives a custom role without Edit settings or AI usage the same three tabs: AI use moved out of the area', () => {
    renderAs(interviewer);
    expect(tabNames()).toEqual(everyTab);

    renderAs(asDictionary(interviewer, ['ViewHiring', 'ViewAiUsage', 'EditSettings']));
    expect(screen.queryByRole('tab', { name: 'AI use' })).toBeNull();
  });

  it('selects the tab the address belongs to', () => {
    location.path = '/verification/checks';
    renderAs(userAs('Owner'));

    expect(screen.getByRole('tab', { name: 'Checks' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'false');
  });

  it('opens the Overview at the area address', () => {
    renderAs(userAs('Owner'));

    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
  });

  it('goes to the tab that is chosen', async () => {
    renderAs(userAs('Owner'));

    await fireEvent.click(screen.getByRole('tab', { name: 'Activity log' }));

    await waitFor(() => expect(goto).toHaveBeenCalledWith('/verification/activity'));
  });

  it('shows Last updated and History on the settings tabs only, so the band keeps one height', async () => {
    renderAs(userAs('Owner'));
    expect(document.querySelector('[data-last-updated]')).toBeNull();

    location.path = '/verification/checks';
    const { unmount } = renderAs(userAs('Owner'));
    await waitFor(() => expect(document.querySelector('[data-last-updated]')).not.toBeNull());
    unmount();
  });
});
