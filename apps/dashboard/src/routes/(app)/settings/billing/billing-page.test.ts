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
  taxAtCheckout: false,
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
    expect(within(plans()).queryByText(/plus tax/)).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says "plus tax where it applies" beside each price when Checkout adds tax, and not beside a price that is not set', async () => {
    renderPage({ ...trial, taxAtCheckout: true });

    await screen.findByText('Free trial');
    expect(within(plans()).getByText('$49 a month plus tax where it applies')).toBeInTheDocument();
    expect(within(plans()).getByText('$99 a month plus tax where it applies')).toBeInTheDocument();
    expect(within(plans()).getByText('Price not set')).toBeInTheDocument();
    expect(within(plans()).getAllByText(/plus tax where it applies/)).toHaveLength(2);
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

  it('shows a paying organization its plan and next payment, with Manage billing as the one primary action and no Choose', async () => {
    renderPage(active);

    expect(await screen.findByText('Active', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(screen.getByText('Next payment', { selector: 'dt' }).nextElementSibling).toHaveTextContent('$49 on Dec 5, 2026');
    expect(screen.getByText('Open jobs', { selector: 'dt' }).nextElementSibling).toHaveTextContent(/^3 of 5$/);
    expect(within(plans()).queryByText('Current plan')).toBeNull();
    expect(within(plans()).queryByRole('button')).toBeNull();
    expect(screen.getByText('To change your plan, use Manage billing.')).toBeInTheDocument();
    expect(primaryButtons().map((button) => button.textContent?.trim())).toEqual(['Manage billing']);
    await expectNoAxeViolations();
  });

  it('opens the payment portal from Manage billing', async () => {
    renderPage(active);

    await fireEvent.click(await screen.findByRole('button', { name: 'Manage billing' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('/v1/billing/sandbox/portal?state=abc'));
    expect(posted).toEqual([{ path: 'portal', body: null }]);
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
