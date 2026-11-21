import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobs, user, userAs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import { preferences } from '../preferences.svelte.js';
import { setupPanel } from '../setup-panel.svelte.js';
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

// A new organization: the signed-in Owner alone, nothing set, no job published. Three steps are open (no email provider here).
const newOrganization = {
  'GET /v1/organization/setup': () => Response.json({ hidden: false, skippedSteps: [] }),
  'GET /v1/organization/members': () => Response.json([{ id: 'avery' }]),
  'GET /v1/organization/invitations': () => Response.json([]),
  'GET /v1/organization/branding': () => Response.json({ brandColor: null, secondColor: null, headingFont: null, logo: null, favicon: null, sharingImage: null }),
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
const asked = (path: string) => vi.mocked(fetch).mock.calls.some(([url]) => String(url) === path);

afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
});

describe('notification bell', () => {
  it('counts the setup steps still to do and the alerts not yet read, and says so to a screen reader', async () => {
    stub([unreadItem, readItem], 1);
    renderBell();

    // Three steps open, one alert unread. The read one is still listed but does not count.
    const button = await screen.findByRole('button', { name: 'Notifications, 4 need attention' });
    expect(button.querySelector('[data-bell-count]')).toHaveTextContent('4');
  });

  it('opens at its setup steps when a page asks, and not for an ask made before it mounted', async () => {
    setupPanel.open();
    stub([unreadItem], 1);
    renderBell();
    // Once the steps are in, so the panel has its setup section to put focus on.
    await screen.findByRole('button', { name: 'Notifications, 4 need attention' });
    expect(screen.queryByRole('dialog', { name: 'Needs attention' })).toBeNull();

    setupPanel.open();
    const panel = await screen.findByRole('dialog', { name: 'Needs attention' });
    const setup = await within(panel).findByRole('region', { name: 'Finish setting up' });
    await waitFor(() => expect(setup).toHaveFocus());
  });

  it('opens by itself once right after onboarding, when the steps are known, and never again', async () => {
    preferences.queueSetupTour(user.id);
    stub([], 0);
    const { unmount } = renderBell();

    const panel = await screen.findByRole('dialog', { name: 'Needs attention' });
    const setup = await within(panel).findByRole('region', { name: 'Finish setting up' });
    await waitFor(() => expect(setup).toHaveFocus());
    unmount();

    renderBell();
    // Once the steps are in, the moment it would open.
    await screen.findByRole('button', { name: 'Notifications, 3 need attention' });
    expect(screen.queryByRole('dialog', { name: 'Needs attention' })).toBeNull();
  });

  it('keeps the open after onboarding for a bell that has steps to show, so one with none does not spend it', async () => {
    preferences.queueSetupTour(user.id);
    stub([], 0);
    // The same person, seen first by a bell that holds no setup steps for them.
    const { unmount } = renderBell(userAs('Recruiter'));
    await screen.findByRole('button', { name: 'Notifications' });
    expect(screen.queryByRole('dialog', { name: 'Needs attention' })).toBeNull();
    unmount();

    renderBell();
    expect(await screen.findByRole('dialog', { name: 'Needs attention' })).toBeInTheDocument();
  });

  it('starts focus on the panel again when it is opened by hand after a page opened it at the steps', async () => {
    stub([], 0);
    renderBell();
    await screen.findByRole('button', { name: 'Notifications, 3 need attention' });

    setupPanel.open();
    const panel = await screen.findByRole('dialog', { name: 'Needs attention' });
    await waitFor(() => expect(within(panel).getByRole('region', { name: 'Finish setting up' })).toHaveFocus());
    await fireEvent.keyDown(panel, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Needs attention' })).toBeNull());

    const reopened = await openBell();
    await waitFor(() => expect(reopened).toHaveFocus());
  });

  it('does not open by itself for anyone who did not just finish onboarding', async () => {
    stub([], 0);
    renderBell();

    await bell();
    await screen.findByRole('button', { name: 'Notifications, 3 need attention' });
    expect(screen.queryByRole('dialog', { name: 'Needs attention' })).toBeNull();
  });

  it('says in its tooltip that it holds setup while steps are left, so a new Owner knows why to open it', async () => {
    stub([], 0);
    renderBell();

    await fireEvent.focus(await screen.findByRole('button', { name: 'Notifications, 3 need attention' }));

    expect(await screen.findAllByText('Setup and notifications')).not.toHaveLength(0);
  });

  it("lists the optional email-domain step as Optional and never counts it, so a team without a domain has no badge (the owner's walk-through, 2026-11-21)", async () => {
    // Everything else is done: a teammate invited, branding set, a job published.
    stub([], 0, {
      'GET /v1/organization/members': () => Response.json([{ id: 'avery' }, { id: 'jordan' }]),
      'GET /v1/organization/branding': () => Response.json({ brandColor: '#1f4e5a', secondColor: null, headingFont: null, logo: null, favicon: null, sharingImage: null }),
      'GET /v1/organization/sending-domain': () => Response.json({ available: true, sandbox: false, domain: null }),
      'GET /v1/jobs': () => Response.json(jobs),
    });
    renderBell();

    const button = await screen.findByRole('button', { name: /^Notifications/ });
    await waitFor(() => expect(button).toHaveAccessibleName('Notifications'));
    expect(button.querySelector('[data-bell-count]')).toBeNull();
    const panel = await openBell();
    const step = await within(panel).findByRole('link', { name: 'Send email from your own domain' });
    expect(step.parentElement).toHaveTextContent('Optional');
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
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Set your branding', 'Publish your first job', 'Invite your team']);
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
    expect(within(panel).getAllByRole('link')).toHaveLength(2);
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

  it('takes a step the organization does not need off the list and the count, saved on the server, and keeps focus in the steps', async () => {
    let skipped: string[] = [];
    stub([], 0, {
      'GET /v1/organization/setup': () => Response.json({ hidden: false, skippedSteps: skipped }),
      'POST /v1/organization/setup/skip': (init) => {
        skipped = [...skipped, (JSON.parse(String(init?.body)) as { step: string }).step];
        return Response.json({ hidden: false, skippedSteps: skipped });
      },
    });
    renderBell();
    const panel = await openBell();
    const setup = await within(panel).findByRole('region', { name: 'Finish setting up' });

    await fireEvent.click(within(setup).getByRole('button', { name: 'Not needed: Set your branding' }));

    await waitFor(() => expect(within(setup).queryByRole('link', { name: 'Set your branding' })).toBeNull());
    expect(skipped).toEqual(['Branding']);
    await waitFor(() => expect(setup).toHaveFocus());
    // The steps after it keep their numbers, and a skipped step is not done.
    expect(within(setup).getAllByRole('listitem').map((item) => item.textContent?.match(/^\s*(\d)/)?.[1])).toEqual(['2', '3']);
    expect(within(setup).queryByText(/done$/)).toBeNull();
    expect(screen.getByRole('button', { name: 'Notifications, 2 need attention' })).toBeInTheDocument();
  });

  it('hides the whole setup for the organization from the section, which takes its steps out of the count', async () => {
    let hidden = false;
    stub([], 0, {
      'GET /v1/organization/setup': () => Response.json({ hidden, skippedSteps: [] }),
      'POST /v1/organization/setup/hide': () => {
        hidden = true;
        return Response.json({ hidden, skippedSteps: [] });
      },
    });
    renderBell();
    const panel = await openBell();

    await fireEvent.click(await within(panel).findByRole('button', { name: 'Hide setup for everyone' }));

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Finish setting up' })).toBeNull());
    expect(posted('/v1/organization/setup/hide')).toBe(true);
    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
  });

  it('asks none of the setup questions for an organization that hid its setup', async () => {
    stub([], 0, { 'GET /v1/organization/setup': () => Response.json({ hidden: true, skippedSteps: [] }) });
    renderBell();
    const panel = await openBell();

    expect(await within(panel).findByText('Nothing needs attention.')).toBeInTheDocument();
    expect(asked('/v1/organization/setup')).toBe(true);
    expect(asked('/v1/organization/members')).toBe(false);
    expect(asked('/v1/jobs')).toBe(false);
  });

  it('says so in the section when a skip cannot be saved, and keeps the step', async () => {
    stub([], 0, { 'POST /v1/organization/setup/skip': () => Response.json({ errors: { step: ['Choose one of the setup steps.'] } }, { status: 400 }) });
    renderBell();
    const panel = await openBell();
    const setup = await within(panel).findByRole('region', { name: 'Finish setting up' });

    await fireEvent.click(within(setup).getByRole('button', { name: 'Not needed: Invite your team' }));

    expect(await within(setup).findByText('Could not save that. Try again.')).toHaveAttribute('role', 'alert');
    // By text: jsdom's floating-ui leaves the panel's wrapper hidden after the re-render, so roles read as inaccessible.
    expect(within(setup).getByText('Invite your team').closest('a')).toHaveAttribute('href', '/settings/team');
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
