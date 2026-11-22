import type { BillingAiUsage, BillingInvoice, BillingOverview, BillingPlan, BillingThisMonth } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import { creditRoutes } from '../../../../test-support/ai-credits.js';
import { aiUsageSummary } from '../../../../test-support/ai-usage.js';
import { settingsWith } from '../../../../test-support/verification-settings.js';
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

const aiUsage: BillingAiUsage = {
  month: '2026-11',
  builtInCalls: 41,
  charge: 3.744,
  callsWithUnknownCost: 0,
  ownKeyCalls: 0,
};

const trialMonth: BillingThisMonth = {
  month: '2026-11',
  currency: 'USD',
  planName: 'Growth',
  planAmount: 0,
  creditsBought: 25,
  total: 25,
  nextInvoiceAmount: null,
  nextInvoiceAt: null,
};
const activeMonth: BillingThisMonth = {
  ...trialMonth,
  planName: 'Starter',
  planAmount: 49,
  total: 74,
  nextInvoiceAmount: 49,
  nextInvoiceAt: '2026-12-05T12:00:00Z',
};

const invoices: BillingInvoice[] = [
  { id: 'in_2', date: '2026-11-05T12:00:00Z', amount: 49, currency: 'USD', status: 'Paid', pdfUrl: '/v1/billing/sandbox/invoices/in_2' },
  { id: 'in_1', date: '2026-10-05T12:00:00Z', amount: 52.5, currency: 'USD', status: 'Due', pdfUrl: null },
];

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
  aiUsage,
  thisMonth: trialMonth,
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
  thisMonth: activeMonth,
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
        ...Object.fromEntries(
          ['ChangeCard', 'Cancel'].map((intent) => [
            `POST /v1/organization/billing/portal?intent=${intent}`,
            () => {
              posted.push({ path: 'portal', body: intent });
              return Response.json({ url: '/v1/billing/sandbox/portal?state=abc' });
            },
          ]),
        ),
        'GET /v1/organization/billing/invoices': () => Response.json({ invoices }),
        'GET /v1/organization/billing/payment-method': () => Response.json({ method: { type: 'card', brand: 'visa', last4: '4242' } }),
        // An Owner sees AI usage, so the AI use section below the plans loads its own two queries.
        'GET /v1/organization/verification-settings': () => Response.json(settingsWith()),
        'GET /v1/organization/ai-usage/summary?timeZone=UTC': () => Response.json(aiUsageSummary),
        ...creditRoutes(),
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
const planCard = () => screen.getByRole('region', { name: 'Plan' });
const primaryButtons = () => Array.from(document.querySelectorAll('main button')).filter((button) => button.getAttribute('role') !== 'switch' && button.className.includes('bg-primary'));

describe('billing page', () => {
  it('shows the trial: its days left, the open jobs against the limit, and every plan with its price and limit', async () => {
    // Only the clock is faked, so TanStack Query's timers still run.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-11T09:00:00Z'));
    try {
      renderPage();

      expect(await screen.findByText('Free trial')).toBeInTheDocument();
      expect(screen.getByRole('heading', { level: 1, name: 'Billing' })).toBeInTheDocument();
      expect(planCard()).toHaveTextContent('Trial: 9 days left');
      expect(planCard()).toHaveTextContent('Growth until November 20');
      expect(planCard().querySelector('[data-headroom]')).toHaveTextContent(/^3 of 25 open jobs$/);
      expect(within(plans()).getByText('$49.00')).toBeInTheDocument();
      expect(within(plans()).getByText('Up to 5 open jobs')).toBeInTheDocument();
      expect(within(plans()).getByText('$99.00')).toBeInTheDocument();
      expect(within(plans()).getByText('Price not set')).toBeInTheDocument();
      expect(within(plans()).getByText('Unlimited open jobs')).toBeInTheDocument();
      expect(within(plans()).queryByText(/plus tax/)).not.toBeInTheDocument();
      await expectNoAxeViolations();
    } finally {
      vi.useRealTimers();
    }
  });

  const trialEndWords = 'The trial needs no card. When it ends, open jobs stay open and a new job needs a plan.';

  it('tells a trialing owner what the end of the trial does', async () => {
    renderPage();

    expect(await screen.findByText(trialEndWords)).toBeInTheDocument();
  });

  it('does not say it once there is a subscription', async () => {
    renderPage(active);

    await screen.findByText('Active');
    expect(screen.queryByText(trialEndWords)).not.toBeInTheDocument();
  });

  describe('AI use', () => {
    const panel = () => screen.getByRole('region', { name: 'AI use' });

    it('shows this month as the calls and the one amount for them, and never the provider\'s price or a margin', async () => {
      renderPage(active);

      await screen.findByText('Active');
      await within(panel()).findByText('Key', { selector: 'dt' });
      expect(within(panel()).getByText('This month', { selector: 'dt' }).nextElementSibling).toHaveTextContent(/^41 AI calls, \$3\.74$/);
      expect(within(panel()).queryByText(/provider's price|plus 20%|margin/i)).toBeNull();
      expect(screen.queryByText(/Provider's price|Cost so far/)).toBeNull();
      await expectNoAxeViolations();
    });

    it('links to the call-by-call detail on Verification and to the AI model, and says that bringing your own key means no credits are used', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(await within(panel()).findByText(/Bring your own key and the checks use no credits\./)).toBeInTheDocument();
      expect(within(panel()).getByText('Key', { selector: 'dt' }).nextElementSibling).toHaveTextContent("ScreenHire's key. The checks use your AI credits.");
      expect(within(panel()).getByRole('link', { name: 'See every call' })).toHaveAttribute('href', '/verification/activity');
      expect(within(panel()).getByRole('link', { name: 'Use your own key' })).toHaveAttribute('href', '/settings/ai-model');
    });

    it('says nothing about a trial allowance, an invoice line for AI use or a metered price, which prepaid credits replaced', async () => {
      renderPage(trial);

      await screen.findByText('Free trial');
      await within(panel()).findByText('Key', { selector: 'dt' });
      expect(within(panel()).queryByText(/trial|invoice|billed|not set up/i)).toBeNull();
    });

    it('says calls on the organization\'s own key are not charged', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, ownKeyCalls: 3 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('3 calls ran on your own key, so they used no credits.')).toBeInTheDocument();
    });

    it('counts a call nothing could price and says it used no credits', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, callsWithUnknownCost: 2 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('2 calls have no known price, so they used no credits.')).toBeInTheDocument();
    });

    it('shows no calls and no charge for a month with no use', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, builtInCalls: 0, charge: 0 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('This month', { selector: 'dt' }).nextElementSibling).toHaveTextContent('0 AI calls, $0.00');
    });

    it('says "call" in the singular for one', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, builtInCalls: 1, callsWithUnknownCost: 1 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('This month', { selector: 'dt' }).nextElementSibling).toHaveTextContent(/^1 AI call, /);
      expect(within(panel()).getByText('1 call has no known price, so it used no credits.')).toBeInTheDocument();
    });

    it('leaves out the month and its notes when the API sends no usage, keeping whose key runs the check', async () => {
      renderPage({ ...active, aiUsage: null });

      await screen.findByText('Active');
      expect(await within(panel()).findByText('Key', { selector: 'dt' })).toBeInTheDocument();
      expect(within(panel()).queryByText('This month', { selector: 'dt' })).toBeNull();
    });

    it('adds no primary button to the page (A1)', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(primaryButtons()).toEqual([]);
    });
  });

  describe('this month so far', () => {
    const panel = () => screen.getByRole('region', { name: 'This month so far' });
    const charges = () => within(panel()).getAllByRole('listitem').map((item) => item.textContent?.replace(/\s+/g, ' ').trim());

    it('adds up the plan and the AI credits bought, links the credits line to the credits, and gives the next invoice, the plan alone', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(charges()).toEqual(['Starter $49.00', 'AI credits $25.00', 'Total so far $74.00']);
      expect(within(panel()).getByRole('link', { name: 'AI credits' })).toHaveAttribute('href', '#ai-credits');
      expect(within(panel()).getByText(/Next invoice about/)).toHaveTextContent('Next invoice about $49.00 on Dec 5, 2026.');
      // The next payment is said once, here, not again under the plan (W7).
      expect(screen.queryByText('Next payment', { selector: 'dt' })).toBeNull();
      await expectNoAxeViolations();
    });

    it('charges a trial only for the credits it bought, no trial credit, and says nothing is charged before a plan', async () => {
      renderPage(trial);

      await screen.findByText('Free trial');
      expect(charges()).toEqual(['Growth, free trial $0.00', 'AI credits $25.00', 'Total so far $25.00']);
      expect(within(panel()).getByText('The plan costs nothing until you choose one.')).toBeInTheDocument();
      expect(within(panel()).queryByText(/Next invoice/)).toBeNull();
    });

    it('says the next invoice is before tax when Checkout adds tax', async () => {
      renderPage({ ...active, taxAtCheckout: true });

      await screen.findByText('Active');
      expect(within(panel()).getByText(/Next invoice about/)).toHaveTextContent('Next invoice about $49.00 plus tax where it applies on Dec 5, 2026.');
    });

    it('leaves out the credits line when none were bought this month, and a plan that ended', async () => {
      renderPage({ ...trial, state: 'Ended', currentPlan: null, trialEndsAt: null, thisMonth: { ...trialMonth, planName: null, planAmount: null, creditsBought: 0, total: 0 } });

      await screen.findByText('Ended');
      expect(charges()).toEqual(['Total so far $0.00']);
    });
  });

  describe('payment method and invoices', () => {
    it('shows the card with Change card and Cancel plan, each opening the payment page for what it says', async () => {
      renderPage(active);

      const panel = await screen.findByRole('region', { name: 'Payment method' });
      expect(await within(panel).findByText('Visa ending in 4242')).toBeInTheDocument();
      await fireEvent.click(within(panel).getByRole('button', { name: 'Change card' }));
      await waitFor(() => expect(assign).toHaveBeenCalledWith('/v1/billing/sandbox/portal?state=abc'));
      expect(posted).toEqual([{ path: 'portal', body: 'ChangeCard' }]);
      expect(screen.queryByText(/To change your card or cancel/)).toBeNull();
      expect(screen.queryByRole('button', { name: 'Manage billing' })).toBeNull();
      await expectNoAxeViolations();
    });

    it('opens the payment page to cancel from Cancel plan', async () => {
      renderPage(active);

      await fireEvent.click(await screen.findByRole('button', { name: 'Cancel plan' }));

      await waitFor(() => expect(posted).toEqual([{ path: 'portal', body: 'Cancel' }]));
    });

    it('lists the invoices newest first with their amount, status and PDF', async () => {
      renderPage(active);

      const panel = await screen.findByRole('region', { name: 'Invoices' });
      const rows = await within(panel).findAllByRole('row');
      expect(rows.slice(1).map((row) => row.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'Nov 5, 2026 $49.00 Paid PDF, invoice of Nov 5, 2026 (opens in a new tab)',
        'Oct 5, 2026 $52.50 Due No PDF',
      ]);
      expect(within(panel).getByRole('link', { name: /PDF, invoice of Nov 5, 2026/ })).toHaveAttribute('href', '/v1/billing/sandbox/invoices/in_2');
      await expectNoAxeViolations();
    });

    it('has neither before there is a payment customer', async () => {
      renderPage(trial);

      await screen.findByText('Free trial');
      expect(screen.queryByRole('region', { name: 'Payment method' })).toBeNull();
      expect(screen.queryByRole('region', { name: 'Invoices' })).toBeNull();
    });

    it('offers no Cancel plan for a plan already set to end', async () => {
      renderPage({ ...active, cancelsAtPeriodEnd: true, nextPaymentAmount: null });

      expect(await screen.findByRole('button', { name: 'Change card' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Cancel plan' })).toBeNull();
    });
  });

  it('says "plus tax where it applies" beside each price when Checkout adds tax, and not beside a price that is not set', async () => {
    renderPage({ ...trial, taxAtCheckout: true });

    await screen.findByText('Free trial');
    expect(within(plans()).getByText('Price not set')).toBeInTheDocument();
    for (const key of ['starter', 'growth']) expect(plans().querySelector(`[data-plan-card="${key}"]`)).toHaveTextContent('plus tax where it applies');
    expect(within(plans()).getAllByText('plus tax where it applies')).toHaveLength(2);
    await expectNoAxeViolations();
  });

  it('offers Choose on every plan Checkout can start, with no primary button before there is a subscription (A1)', async () => {
    renderPage({ ...trial, plans: [starter, growth, plan('later', 'Later', { canSubscribe: false })] });

    await screen.findByText('Free trial');
    expect(within(plans()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Choose Starter', 'Choose Growth']);
    expect(primaryButtons()).toEqual([]);
  });

  it('shows an ended organization with a payment customer the plans to choose and no Cancel plan, so there is still no primary (A1)', async () => {
    renderPage({ ...trial, state: 'Ended', trialEndsAt: null, currentPlan: null, canManage: true });

    await screen.findByText('Ended');
    expect(screen.queryByRole('button', { name: 'Cancel plan' })).toBeNull();
    expect(within(plans()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Choose Starter', 'Choose Growth', 'Choose Unlimited']);
    expect(primaryButtons()).toEqual([]);
  });

  it('brings the plans into view from the plan card, with focus there: Choose a plan on the trial, Change plan on a subscription', async () => {
    const scrolled = vi.fn();
    Element.prototype.scrollIntoView = scrolled;
    for (const [overview, label] of [
      [trial, 'Choose a plan'],
      [active, 'Change plan'],
    ] as const) {
      renderPage(overview);

      await fireEvent.click(await screen.findByRole('button', { name: label }));

      expect(document.activeElement).toBe(plans());
      expect(scrolled).toHaveBeenCalled();
      cleanup();
      scrolled.mockClear();
    }
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

  it('shows a paying organization its plan, marked among the plans, with Change to on the others and no primary action (A1)', async () => {
    renderPage(active);

    expect(await screen.findByText('Active', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(planCard().querySelector('[data-headroom]')).toHaveTextContent(/^3 of 5 open jobs$/);
    expect(within(plans()).getAllByText('Your plan')).toHaveLength(1);
    expect(plans().querySelector('[data-plan-card="starter"]')).toHaveTextContent('Your plan');
    expect(within(plans()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Change to Growth', 'Change to Unlimited']);
    expect(primaryButtons()).toEqual([]);
    await expectNoAxeViolations();
  });

  it('shows what a switch costs before it is made: the amount now, then the monthly price from its date', async () => {
    renderPage(active);

    await fireEvent.click(await screen.findByRole('button', { name: 'Change to Growth' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(dialog).toHaveTextContent('$35.50 now, then $99.00 a month from Dec 5, 2026'));
    expect(posted).toEqual([{ path: 'preview', body: { plan: 'growth' } }]);
    expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: 'Keep my plan' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('opens with the dialog focused, so the tooltip on the date does not open over the text by itself', async () => {
    renderPage(active);

    await fireEvent.click(await screen.findByRole('button', { name: 'Change to Growth' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(dialog).toHaveTextContent('a month from'));
    await waitFor(() => expect(document.activeElement).toBe(dialog));
    expect(within(dialog).getByText('Dec 5, 2026').closest('[data-slot="tooltip-trigger"]')).not.toBe(document.activeElement);
  });

  it('says nothing is due now for a cheaper plan and that the unused time is credited', async () => {
    renderPage({ ...active, currentPlan: growth, activeJobLimit: 25, nextPaymentAmount: 99 });

    await fireEvent.click(await screen.findByRole('button', { name: 'Change to Starter' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Starter?' });
    await waitFor(() => expect(dialog).toHaveTextContent('Nothing now, then $49.00 a month from Dec 5, 2026'));
    expect(dialog).toHaveTextContent('The unused time on your current plan is credited toward your next payment.');
  });

  it('changes the plan with the date the price was worked out at, and says the page updates once the payment is confirmed', async () => {
    renderPage(active);
    await fireEvent.click(await screen.findByRole('button', { name: 'Change to Growth' }));
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

    await fireEvent.click(await screen.findByRole('button', { name: 'Change to Growth' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    expect(await within(dialog).findByText('The payment provider did not answer. Try again in a moment.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeDisabled();
  });

  it('says why the change was refused, leaves the dialog open and prices the change again', async () => {
    renderPage(active, {
      'POST /v1/organization/billing/plan': () => Response.json({ detail: 'The plan was not changed and you have not been charged.' }, { status: 502 }),
    });
    await fireEvent.click(await screen.findByRole('button', { name: 'Change to Growth' }));
    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled());

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm change' }));

    expect(await within(dialog).findByText('The plan was not changed and you have not been charged.')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.queryByText(/Your plan is changing/)).toBeNull();
    await waitFor(() => expect(posted.filter((call) => call.path === 'preview')).toHaveLength(2));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled());
  });

  it('says when a subscription that will not renew ends, instead of a next payment', async () => {
    renderPage({ ...active, cancelsAtPeriodEnd: true, nextPaymentAmount: null });

    expect(await screen.findByText('Ends', { exact: false, selector: 'p' })).toHaveTextContent(/^Ends December 5$/);
    expect(within(planCard()).queryByText(/^Renews/)).toBeNull();
    // A plan set to end is renewed on the payment page first, so nothing here changes it.
    expect(within(plans()).queryByRole('button', { name: /^Change to/ })).toBeNull();
    expect(within(planCard()).queryByRole('button', { name: 'Change plan' })).toBeNull();
  });

  it('shows only the count when the plan has no limit', async () => {
    renderPage({ ...active, currentPlan: unlimited, activeJobLimit: null, openJobs: 3 });

    await screen.findByText('Active');
    expect(planCard().querySelector('[data-headroom]')).toHaveTextContent(/^3 open jobs$/);
    expect(planCard().querySelector('[data-headroom-meter]')).toBeNull();
  });

  it('tells an owner a failed payment needs new details, with the payment status beside the title', async () => {
    renderPage({ ...active, state: 'PastDue' });

    expect(await screen.findByText('Your last payment failed. Update your payment details to keep publishing.')).toBeInTheDocument();
    expect(screen.getByText('Payment failed', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    // No plan switches while a payment has failed; Change card is the way to new payment details.
    expect(within(plans()).queryAllByRole('button')).toEqual([]);
    expect(within(planCard()).queryByRole('button', { name: 'Change plan' })).toBeNull();
    expect(await screen.findByRole('button', { name: 'Change card' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('tells an owner that open jobs stay open once it has ended, whatever ended, and offers the plans again', async () => {
    renderPage({ ...trial, state: 'Ended', currentPlan: null, trialEndsAt: null, activeJobLimit: null });

    expect(await screen.findByText('Open jobs stay open, but you cannot publish new jobs until you choose a plan.')).toBeInTheDocument();
    expect(screen.getByText('Ended', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(planCard().querySelector('[data-headroom]')).toHaveTextContent(/^3 open jobs$/);
    expect(planCard()).toHaveTextContent('No plan');
    expect(within(planCard()).getByRole('button', { name: 'Choose a plan' })).toBeInTheDocument();
    expect(within(plans()).getAllByRole('button')).toHaveLength(3);
    await expectNoAxeViolations();
  });

  it('shows an owner only the AI use section where there is no payment provider, never a page of what is not set up (A3)', async () => {
    renderPage({ ...trial, available: false, state: null, currentPlan: null, plans: [] });

    expect(await screen.findByText('Model calls')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'AI use' })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Plan' })).toBeNull());
    expect(screen.queryByText('Billing is not set up here')).toBeNull();
    expect(screen.queryByText('Free trial')).toBeNull();
    expect(screen.queryByText('This month', { selector: 'dt' })).toBeNull();
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

  it("takes the sandbox Checkout's tax markers out of the address with the outcome", async () => {
    location.url = new URL('http://dashboard.test/settings/billing?checkout=success&tax=requested&taxId=requested');
    renderPage();

    expect(await screen.findByText('Checkout finished. This page updates when the payment is confirmed.')).toBeInTheDocument();
    await waitFor(() => expect(navigation.goto).toHaveBeenCalledOnce());
    expect(String(navigation.goto.mock.calls[0]?.[0])).toBe('http://dashboard.test/settings/billing');
  });

  it('shows a skeleton while loading, then says why billing could not load, with a retry', async () => {
    renderPage(() => Response.json({ detail: 'Try again later.' }, { status: 500 }));

    expect(screen.getByText('Loading billing…')).toHaveAttribute('role', 'status');
    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });
});
