import type { BillingOverview, BillingPlan } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const location = vi.hoisted(() => ({ url: new URL('http://dashboard.test/settings/billing') }));
vi.mock('$app/state', () => ({
  page: {
    data: { user },
    get url() {
      return location.url;
    },
  },
  navigating: { to: null },
}));
const navigation = vi.hoisted(() => ({ goto: vi.fn(async (_url: string | URL, _options?: object) => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

const plan = (key: string, name: string, extra: Partial<BillingPlan> = {}): BillingPlan => ({
  key,
  name,
  monthlyPrice: null,
  currency: 'USD',
  activeJobs: null,
  unlimited: false,
  canSubscribe: true,
  ...extra,
});
const starter = plan('starter', 'Starter', { monthlyPrice: 49, activeJobs: 5 });
const growth = plan('growth', 'Growth', { monthlyPrice: 99, activeJobs: 25 });
const unlimited = plan('unlimited', 'Unlimited', { unlimited: true });

const trial: BillingOverview = {
  available: true,
  sandbox: false,
  state: 'Trialing',
  currentPlan: growth,
  trialEndsAt: '2026-11-20T12:00:00Z',
  nextPaymentAt: null,
  nextPaymentAmount: null,
  cancelsAtPeriodEnd: false,
  openJobs: 3,
  activeJobLimit: 25,
  canManage: false,
  plans: [starter, growth, unlimited],
};
const active: BillingOverview = {
  ...trial,
  state: 'Active',
  currentPlan: starter,
  trialEndsAt: null,
  nextPaymentAt: '2026-12-05T12:00:00Z',
  nextPaymentAmount: 49,
  activeJobLimit: 5,
  canManage: true,
};

// What the API answers for a switch from Starter: an upgrade owes the difference for the rest of the period, a downgrade owes nothing.
const prorationDate = '2026-11-20T09:00:00Z';
const quoteFor = (key: string) =>
  key === 'growth'
    ? { plan: 'growth', amountDueNow: 35.5, nextPaymentAmount: 99, nextPaymentAt: '2026-12-05T12:00:00Z', currency: 'USD', prorationDate }
    : { plan: key, amountDueNow: 0, nextPaymentAmount: 49, nextPaymentAt: '2026-12-05T12:00:00Z', currency: 'USD', prorationDate };

const assign = vi.fn();
const posted: { path: string; body: unknown }[] = [];

function renderPage(overview: BillingOverview | (() => Response) = trial, routes: Parameters<typeof apiStub>[0] = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/billing': () => (typeof overview === 'function' ? overview() : Response.json(overview)),
        'POST /v1/organization/billing/checkout': (init) => {
          posted.push({ path: 'checkout', body: JSON.parse(String(init?.body)) });
          return Response.json({ url: 'https://checkout.stripe.example.com/session' });
        },
        'POST /v1/organization/billing/plan/preview': (init) => {
          posted.push({ path: 'preview', body: JSON.parse(String(init?.body)) });
          return Response.json(quoteFor(JSON.parse(String(init?.body)).plan));
        },
        'POST /v1/organization/billing/plan': (init) => {
          posted.push({ path: 'change', body: JSON.parse(String(init?.body)) });
          return new Response(null, { status: 204 });
        },
        'POST /v1/organization/billing/portal': () => {
          posted.push({ path: 'portal', body: null });
          return Response.json({ url: '/v1/billing/sandbox/portal?state=abc' });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

beforeEach(() => {
  location.url = new URL('http://dashboard.test/settings/billing');
  vi.stubGlobal('location', { ...window.location, assign });
});

afterEach(() => {
  vi.unstubAllGlobals();
  assign.mockClear();
  navigation.goto.mockClear();
  posted.length = 0;
});

const plans = () => screen.getByRole('region', { name: 'Plans' });
const primaryButtons = () => Array.from(document.querySelectorAll('main button')).filter((button) => button.className.includes('bg-primary'));

describe('billing page', () => {
  it('shows the trial: its end, the open jobs against the limit, and every plan with its price and limit', async () => {
    renderPage();

    expect(await screen.findByText('Free trial')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Billing' })).toBeInTheDocument();
    expect(screen.getByText('Trial ends', { selector: 'dt' }).nextElementSibling).toHaveTextContent('Nov 20, 2026');
    expect(screen.getByText('Open jobs', { selector: 'dt' }).nextElementSibling).toHaveTextContent(/^3 of 25$/);
    expect(within(plans()).getByText('$49 a month')).toBeInTheDocument();
    expect(within(plans()).getByText('Up to 5 open jobs')).toBeInTheDocument();
    expect(within(plans()).getByText('$99 a month')).toBeInTheDocument();
    expect(within(plans()).getByText('Price not set')).toBeInTheDocument();
    expect(within(plans()).getByText('Unlimited open jobs')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('offers Choose on every plan Checkout can start, with no primary button before there is a subscription (A1)', async () => {
    renderPage({ ...trial, plans: [starter, growth, plan('later', 'Later', { canSubscribe: false })] });

    await screen.findByText('Free trial');
    expect(within(plans()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Choose Starter', 'Choose Growth']);
    expect(screen.queryByRole('button', { name: 'Manage billing' })).toBeNull();
    expect(primaryButtons()).toEqual([]);
  });

  it('shows an ended organization with a payment customer the plans to choose and no Manage billing, so there is still no primary (A1)', async () => {
    renderPage({ ...trial, state: 'Ended', trialEndsAt: null, currentPlan: null, canManage: true });

    await screen.findByText('Ended');
    expect(screen.queryByRole('button', { name: 'Manage billing' })).toBeNull();
    expect(within(plans()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Choose Starter', 'Choose Growth', 'Choose Unlimited']);
    expect(primaryButtons()).toEqual([]);
  });

  it('sends the chosen plan to Checkout and goes to the address it answers with', async () => {
    renderPage();

    await fireEvent.click(await screen.findByRole('button', { name: 'Choose Starter' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://checkout.stripe.example.com/session'));
    expect(posted).toEqual([{ path: 'checkout', body: { plan: 'starter' } }]);
  });

  it('says why Checkout could not start, and stays on the page', async () => {
    renderPage(trial, { 'POST /v1/organization/billing/checkout': () => Response.json({ detail: 'The payment provider did not answer. Try again in a moment.' }, { status: 502 }) });

    await fireEvent.click(await screen.findByRole('button', { name: 'Choose Growth' }));

    expect(await screen.findByText('The payment provider did not answer. Try again in a moment.')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Choose Growth' })).toBeEnabled();
  });

  it('shows a paying organization its plan and next payment, with Manage billing as the one primary action and Switch on the other plans', async () => {
    renderPage(active);

    expect(await screen.findByText('Active', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(screen.getByText('Next payment', { selector: 'dt' }).nextElementSibling).toHaveTextContent('$49 on Dec 5, 2026');
    expect(screen.getByText('Open jobs', { selector: 'dt' }).nextElementSibling).toHaveTextContent(/^3 of 5$/);
    expect(within(plans()).queryByText('Current plan')).toBeNull();
    expect(within(plans()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Switch to Growth', 'Switch to Unlimited']);
    expect(screen.getByText('To change your card or cancel, use Manage billing.')).toBeInTheDocument();
    expect(primaryButtons().map((button) => button.textContent?.trim())).toEqual(['Manage billing']);
    await expectNoAxeViolations();
  });

  it('opens the payment portal from Manage billing', async () => {
    renderPage(active);

    await fireEvent.click(await screen.findByRole('button', { name: 'Manage billing' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('/v1/billing/sandbox/portal?state=abc'));
    expect(posted).toEqual([{ path: 'portal', body: null }]);
  });

  it('shows what a switch costs before it is made: the amount now, then the monthly price from its date', async () => {
    renderPage(active);

    await fireEvent.click(await screen.findByRole('button', { name: 'Switch to Growth' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(dialog).toHaveTextContent('$35.50 now, then $99 a month from Dec 5, 2026'));
    expect(posted).toEqual([{ path: 'preview', body: { plan: 'growth' } }]);
    expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: 'Keep my plan' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says nothing is due now for a cheaper plan and that the unused time is credited', async () => {
    renderPage({ ...active, currentPlan: growth, activeJobLimit: 25, nextPaymentAmount: 99 });

    await fireEvent.click(await screen.findByRole('button', { name: 'Switch to Starter' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Starter?' });
    await waitFor(() => expect(dialog).toHaveTextContent('Nothing now, then $49 a month from Dec 5, 2026'));
    expect(dialog).toHaveTextContent('The unused time on your current plan is credited toward your next payment.');
  });

  it('changes the plan with the date the price was worked out at, and says the page updates once the payment is confirmed', async () => {
    renderPage(active);
    await fireEvent.click(await screen.findByRole('button', { name: 'Switch to Growth' }));
    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled());

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm change' }));

    await waitFor(() => expect(posted.at(-1)).toEqual({ path: 'change', body: { plan: 'growth', prorationDate } }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Your plan is changing to Growth. This page updates when the payment is confirmed.')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
  });

  it('keeps Confirm off and says why when the cost could not be worked out', async () => {
    renderPage(active, { 'POST /v1/organization/billing/plan/preview': () => Response.json({ detail: 'The payment provider did not answer. Try again in a moment.' }, { status: 502 }) });

    await fireEvent.click(await screen.findByRole('button', { name: 'Switch to Growth' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    expect(await within(dialog).findByText('The payment provider did not answer. Try again in a moment.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeDisabled();
  });

  it('says why the change was refused and leaves the dialog open', async () => {
    renderPage(active, {
      'POST /v1/organization/billing/plan': () => Response.json({ detail: 'The plan was not changed and you have not been charged.' }, { status: 502 }),
    });
    await fireEvent.click(await screen.findByRole('button', { name: 'Switch to Growth' }));
    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled());

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm change' }));

    expect(await within(dialog).findByText('The plan was not changed and you have not been charged.')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.queryByText(/Your plan is changing/)).toBeNull();
  });

  it('says when a subscription that will not renew ends, instead of a next payment', async () => {
    renderPage({ ...active, cancelsAtPeriodEnd: true, nextPaymentAmount: null });

    expect(await screen.findByText('Ends on', { selector: 'dt' })).toBeInTheDocument();
    expect(screen.getByText('Ends on', { selector: 'dt' }).nextElementSibling).toHaveTextContent('Dec 5, 2026');
    expect(screen.queryByText('Next payment')).toBeNull();
  });

  it('shows only the count when the plan has no limit', async () => {
    renderPage({ ...active, currentPlan: unlimited, activeJobLimit: null, openJobs: 3 });

    expect((await screen.findByText('Open jobs', { selector: 'dt' })).nextElementSibling).toHaveTextContent(/^3$/);
  });

  it('tells an owner a failed payment needs new details, with the payment status beside the title', async () => {
    renderPage({ ...active, state: 'PastDue' });

    expect(await screen.findByText('Your last payment failed. Update your payment details to keep publishing.')).toBeInTheDocument();
    expect(screen.getByText('Payment failed', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(within(plans()).queryByRole('button')).toBeNull();
    await expectNoAxeViolations();
  });

  it('tells an owner that open jobs stay open once it has ended, whatever ended, and offers the plans again', async () => {
    renderPage({ ...trial, state: 'Ended', currentPlan: null, trialEndsAt: null, activeJobLimit: null });

    expect(await screen.findByText('Open jobs stay open, but you cannot publish new jobs until you choose a plan.')).toBeInTheDocument();
    expect(screen.getByText('Ended', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(screen.getByText('Open jobs', { selector: 'dt' }).nextElementSibling).toHaveTextContent(/^3$/);
    expect(within(plans()).getAllByRole('button')).toHaveLength(3);
    await expectNoAxeViolations();
  });

  it('says billing is not set up where there is no payment provider', async () => {
    renderPage({ ...trial, available: false, state: null, currentPlan: null, plans: [] });

    expect(await screen.findByText('Billing is not set up here')).toBeInTheDocument();
    expect(within(screen.getByRole('main')).queryByRole('button')).toBeNull();
    expect(screen.queryByText('Free trial')).toBeNull();
    await expectNoAxeViolations();
  });

  it.each([
    ['success', 'Checkout finished. This page updates when the payment is confirmed.'],
    ['cancelled', 'Checkout was cancelled. You have not been charged.'],
  ])('says how a return from Checkout ended (%s), and takes it out of the address', async (outcome, sentence) => {
    location.url = new URL(`http://dashboard.test/settings/billing?checkout=${outcome}`);
    renderPage();

    expect(await screen.findByText(sentence)).toBeInTheDocument();
    await waitFor(() => expect(navigation.goto).toHaveBeenCalledOnce());
    expect(String(navigation.goto.mock.calls[0]?.[0])).toBe('http://dashboard.test/settings/billing');
  });

  it('shows a skeleton while loading, then says why billing could not load, with a retry', async () => {
    renderPage(() => Response.json({ detail: 'Try again later.' }, { status: 500 }));

    expect(screen.getByRole('status')).toHaveTextContent('Loading billing');
    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });
});
