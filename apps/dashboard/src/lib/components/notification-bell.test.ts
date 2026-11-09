import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobs, user, userAs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import { preferences } from '../preferences.svelte.js';
import NotificationBell from './notification-bell.svelte';

const id = (n: number) => `01a157d4-0000-7000-8000-0000000004${n.toString().padStart(2, '0')}`;
const unreadItem = {
  id: id(1),
  kind: 'sending-domain-failing',
  payload: { domain: 'example.com' },
  link: '/settings/sending-domain',
  createdAt: '2026-10-20T10:00:00Z',
  readAt: null,
};
const readItem = { ...unreadItem, id: id(2), payload: { domain: 'example.org' }, readAt: '2026-10-20T11:00:00Z' };

// A new organization: the signed-in Owner alone, nothing set, no job published. Four steps are open (no email provider here).
const newOrganization = {
  'GET /v1/organization/members': () => Response.json([{ id: 'avery' }]),
  'GET /v1/organization/invitations': () => Response.json([]),
  'GET /v1/organization/branding': () => Response.json({ brandColor: null, secondColor: null, headingFont: null, logo: null, favicon: null, sharingImage: null }),
  'GET /v1/organization': () => Response.json({ embedOrigins: [], questionsPerPage: DEFAULT_QUESTIONS_PER_PAGE, lastUpdated: { at: null, by: null, created: true } }),
  'GET /v1/organization/sending-domain': () => Response.json({ available: false, sandbox: false, domain: null }),
  'GET /v1/jobs': () => Response.json(jobs.map((job) => ({ ...job, publishedAt: null }))),
};

function stub(items: unknown[], unreadCount: number, extra: Record<string, () => Response> = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(apiStub({ ...newOrganization, 'GET /v1/notifications': () => Response.json({ items, unreadCount }), ...extra })),
  );
}

function renderBell(who: typeof user = user) {
  return render(WithQueryClient, { props: { component: NotificationBell, props: { user: who } } });
}

const bell = () => screen.findByRole('button', { name: /^Notifications/ });
async function openBell() {
  await fireEvent.click(await bell());
  return screen.findByRole('dialog', { name: 'Needs attention' });
}
const posted = (path: string) => vi.mocked(fetch).mock.calls.some(([url, init]) => String(url) === path && init?.method === 'POST');

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
});

describe('notification bell', () => {
  it('counts the setup steps still to do and the alerts not yet read, and says so to a screen reader', async () => {
    stub([unreadItem, readItem], 1);
    renderBell();

    // Four steps open, one alert unread. The read one is still listed but does not count.
    const button = await screen.findByRole('button', { name: 'Notifications, 5 need attention' });
    expect(button.querySelector('[data-bell-count]')).toHaveTextContent('5');
  });

  it('shows no count when nothing needs attention, and says so when opened', async () => {
    stub([], 0);
    renderBell(userAs('Recruiter'));
    const button = await bell();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument());
    expect(button.querySelector('[data-bell-count]')).toBeNull();

    const panel = await openBell();
    expect(await within(panel).findByText('Nothing needs attention.')).toBeInTheDocument();
    expect(within(panel).queryByText('Finish setting up')).toBeNull();
  });

  it('lists the setup steps first, numbered by their place, each a link to the page that does it', async () => {
    stub([unreadItem], 1);
    renderBell();
    const panel = await openBell();

    const setup = await within(panel).findByRole('region', { name: 'Finish setting up' });
    const links = within(setup).getAllByRole('link');
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Set your branding', 'Publish your first job', 'Invite your team', 'Set up your careers page']);
    expect(links[0]).toHaveAttribute('href', '/settings/branding');
    // The setup comes before the alerts.
    const alerts = within(panel).getByRole('list', { name: 'Alerts' });
    expect(setup.compareDocumentPosition(alerts) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('folds the steps that are done into one line', async () => {
    stub([], 0, { 'GET /v1/organization/invitations': () => Response.json([{ id: 'pending' }]) });
    renderBell();
    const panel = await openBell();

    expect(await within(panel).findByText('1 step done')).toBeInTheDocument();
    expect(within(panel).queryByRole('link', { name: 'Invite your team' })).toBeNull();
    expect(within(panel).getAllByRole('link')).toHaveLength(3);
  });

  it('shows an alert as one plain line that links to the fix, bold and announced while unread', async () => {
    stub([unreadItem, readItem], 1);
    renderBell();
    const panel = await openBell();

    const alerts = await within(panel).findByRole('list', { name: 'Alerts' });
    const [first, second] = within(alerts).getAllByRole('link');
    expect(first).toHaveTextContent('Unread: example.com stopped verifying');
    expect(first).toHaveAttribute('href', '/settings/sending-domain');
    expect(first).toHaveClass('font-semibold');
    expect(second).toHaveTextContent('example.org stopped verifying');
    expect(second).not.toHaveClass('font-semibold');
    expect(within(alerts).queryByRole('img')).toBeNull();
  });

  it('marks an alert read when its link is followed, and leaves a read one alone', async () => {
    stub([unreadItem, readItem], 1, {
      [`POST /v1/notifications/${unreadItem.id}/read`]: () => new Response(null, { status: 204 }),
      [`POST /v1/notifications/${readItem.id}/read`]: () => new Response(null, { status: 204 }),
    });
    const stopNavigation = (event: MouseEvent) => event.preventDefault();
    document.addEventListener('click', stopNavigation);
    renderBell();
    const panel = await openBell();
    const alerts = await within(panel).findByRole('list', { name: 'Alerts' });

    await fireEvent.click(within(alerts).getByRole('link', { name: /example.org/ }));
    expect(posted(`/v1/notifications/${readItem.id}/read`)).toBe(false);
    // Following a link closes the panel; opening it again lists the same alerts.
    const reopened = await openBell();
    await fireEvent.click(await within(reopened).findByRole('link', { name: /example.com/ }));
    await waitFor(() => expect(posted(`/v1/notifications/${unreadItem.id}/read`)).toBe(true));
    document.removeEventListener('click', stopNavigation);
  });

  it('marks everything read from the header, offered only while something is unread', async () => {
    stub([unreadItem], 1, { 'POST /v1/notifications/read-all': () => new Response(null, { status: 204 }) });
    const { unmount } = renderBell();
    const panel = await openBell();

    await fireEvent.click(await within(panel).findByRole('button', { name: 'Mark all as read' }));
    await waitFor(() => expect(posted('/v1/notifications/read-all')).toBe(true));
    unmount();

    stub([readItem], 0);
    renderBell();
    const again = await openBell();
    await within(again).findByRole('list', { name: 'Alerts' });
    expect(within(again).queryByRole('button', { name: 'Mark all as read' })).toBeNull();
  });

  it('dismisses an alert for this person from its own button', async () => {
    stub([unreadItem], 1, { [`POST /v1/notifications/${unreadItem.id}/dismiss`]: () => new Response(null, { status: 204 }) });
    renderBell();
    const panel = await openBell();

    await fireEvent.click(await within(panel).findByRole('button', { name: 'Dismiss: example.com stopped verifying' }));
    await waitFor(() => expect(posted(`/v1/notifications/${unreadItem.id}/dismiss`)).toBe(true));
  });

  it('gives a recruiter the alerts and no setup, and asks nothing about the organization', async () => {
    stub([unreadItem], 1);
    renderBell(userAs('Recruiter'));
    const panel = await openBell();

    await within(panel).findByRole('list', { name: 'Alerts' });
    expect(within(panel).queryByText('Finish setting up')).toBeNull();
    expect(screen.getByRole('button', { name: 'Notifications, 1 need attention' })).toBeInTheDocument();
    expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url) === '/v1/organization/members')).toBe(false);
  });

  it('dismisses the setup in this browser, which takes its steps out of the count', async () => {
    stub([], 0);
    renderBell();
    const panel = await openBell();

    await fireEvent.click(await within(panel).findByRole('button', { name: 'Hide setup in this browser' }));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Finish setting up' })).toBeNull());
    expect(preferences.isSetupDismissed(user.organization!.id)).toBe(true);
    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
  });

  it('shows a kind it does not know as a generic line with its link', async () => {
    stub([{ ...unreadItem, kind: 'something-new', payload: {}, link: '/settings/billing' }], 1);
    renderBell(userAs('Recruiter'));
    const panel = await openBell();

    const link = await within(panel).findByRole('link', { name: /Something needs a look/ });
    expect(link).toHaveAttribute('href', '/settings/billing');
  });

  it('tells an Owner the days left in the trial and links to Billing, one notification that counts once', async () => {
    // Noon three days from today, so it is three calendar days away whatever the hour the test runs (Vitest pins TZ to UTC).
    const today = new Date();
    const endsAt = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() + 3, 12)).toISOString();
    stub([{ ...unreadItem, kind: 'trial-ending', payload: { endsAt, plan: 'Growth' }, link: '/settings/billing' }], 1);
    renderBell(); // the Owner
    const panel = await openBell();

    const alerts = await within(panel).findByRole('list', { name: 'Alerts' });
    const link = within(alerts).getByRole('link');
    expect(link).toHaveTextContent('Unread: Your free trial ends in 3 days');
    expect(link).toHaveAttribute('href', '/settings/billing');
    expect(within(alerts).getAllByRole('link')).toHaveLength(1);
  });

  it('says the trial has ended, with the link to Billing, once the server has swapped the notification', async () => {
    const endsAt = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    stub([{ ...unreadItem, kind: 'trial-ended', payload: { endsAt, plan: 'Growth' }, link: '/settings/billing' }], 1);
    renderBell();
    const panel = await openBell();

    const link = await within(panel).findByRole('link', { name: /free trial has ended/ });
    expect(link).toHaveAttribute('href', '/settings/billing');
  });

  it('shows a member who is not an Owner no trial notification, because the server sends them none', async () => {
    // The server picks the audience (Manage billing, Owner only), so a Recruiter's list holds no trial notification.
    stub([], 0);
    renderBell(userAs('Recruiter'));
    const panel = await openBell();

    expect(await within(panel).findByText('Nothing needs attention.')).toBeInTheDocument();
    expect(within(panel).queryByText(/free trial/)).toBeNull();
    expect(screen.queryByRole('link', { name: /free trial/ })).toBeNull();
  });

  it('says when the notifications cannot load and offers another try', async () => {
    stub([], 0, { 'GET /v1/notifications': () => new Response(null, { status: 500 }) });
    renderBell(userAs('Recruiter'));
    const panel = await openBell();

    expect(await within(panel).findByRole('alert')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('has no accessibility violations, closed or open', async () => {
    stub([unreadItem, readItem], 1);
    renderBell();
    await bell();
    await expectNoAxeViolations();
    const panel = await openBell();
    await within(panel).findByRole('list', { name: 'Alerts' });
    await expectNoAxeViolations();
  });
});
