import type { BillingAiUsage, BillingOverview, BillingPlan } from '@screenhire/api-client';
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

const aiUsage: BillingAiUsage = {
  month: '2026-11',
  builtInCalls: 41,
  tokens: 120000,
  providerCost: 3.12,
  charge: 3.744,
  marginPercent: 20,
  callsWithUnknownCost: 0,
  ownKeyCalls: 0,
  trialAllowance: 5,
  trialAllowanceUsed: 0,
  metered: true,
};

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
    expect(within(plans()).queryByText(/plus tax/)).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  describe('AI usage', () => {
    const panel = () => screen.getByRole('region', { name: 'AI usage' });

    it('states the margin plainly and shows this month: the calls, the provider\'s price and what the organization pays', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(within(panel()).getByText("AI checks on ScreenHire's key are billed at the provider's price plus 20%.")).toBeInTheDocument();
      expect(within(panel()).getByText('This month', { selector: 'dt' }).nextElementSibling).toHaveTextContent('41 AI calls');
      expect(within(panel()).getByText("Provider's price", { selector: 'dt' }).nextElementSibling).toHaveTextContent('$3.12');
      expect(within(panel()).getByText('Cost so far', { selector: 'dt' }).nextElementSibling).toHaveTextContent('$3.744');
      await expectNoAxeViolations();
    });

    it('takes the margin from the API, so a different setting reads as it is', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, marginPercent: 17.5 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText(/provider's price plus 17\.5%\./)).toBeInTheDocument();
    });

    it('links to the call-by-call detail on Verification and says that bringing your own key removes the charge', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(within(panel()).getByText(/Bring your own key and this charge goes away\./)).toBeInTheDocument();
      expect(within(panel()).getByRole('link', { name: 'See every call' })).toHaveAttribute('href', '/verification/activity');
      expect(within(panel()).getByRole('link', { name: 'Use your own key' })).toHaveAttribute('href', '/verification');
    });

    it('tells a trial what it includes and that the rest comes on its first invoice', async () => {
      renderPage(trial);

      await screen.findByText('Free trial');
      expect(within(panel()).getByText("Your trial includes $5.00 of AI use. Use above that is added to your first invoice.")).toBeInTheDocument();
    });

    it('says AI use during a trial is added to the first invoice when the trial includes none', async () => {
      renderPage({ ...trial, aiUsage: { ...aiUsage, trialAllowance: 0 } });

      await screen.findByText('Free trial');
      expect(within(panel()).getByText('AI use during your trial is added to your first invoice.')).toBeInTheDocument();
    });

    it('says what the trial covered once a plan is paying, and nothing about a trial when it covered none', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, trialAllowanceUsed: 2.5 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('Your trial covered $2.50 of AI use.')).toBeInTheDocument();
    });

    it('says nothing about the trial to a paying organization it covered nothing for', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(within(panel()).queryByText(/trial/i)).toBeNull();
    });

    it('says AI use is billed once a plan is chosen after the trial ended', async () => {
      renderPage({ ...trial, state: 'Ended', currentPlan: null, trialEndsAt: null });

      await screen.findByText('Ended');
      expect(within(panel()).getByText('AI use is billed once you choose a plan.')).toBeInTheDocument();
    });

    it('says billing for AI use is not set up where the payment provider has no metered price', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, metered: false } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('Billing for AI use is not set up here yet.')).toBeInTheDocument();
    });

    it('promises a trial no invoice line when the payment provider has no metered price', async () => {
      renderPage({ ...trial, aiUsage: { ...aiUsage, metered: false } });

      await screen.findByText('Free trial');
      expect(within(panel()).getByText('Billing for AI use is not set up here yet.')).toBeInTheDocument();
      expect(within(panel()).queryByText(/first invoice/)).toBeNull();
    });

    it('says calls on the organization\'s own key are not charged', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, ownKeyCalls: 3 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('3 calls ran on your own key, so ScreenHire charges nothing for them.')).toBeInTheDocument();
    });

    it('counts a call nothing could price and says it is not charged', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, callsWithUnknownCost: 2 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('2 calls have no known price, so they are not charged.')).toBeInTheDocument();
    });

    it('shows no calls and no charge for a month with no use', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, builtInCalls: 0, tokens: 0, providerCost: 0, charge: 0 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('This month', { selector: 'dt' }).nextElementSibling).toHaveTextContent('0 AI calls');
      expect(within(panel()).getByText('Cost so far', { selector: 'dt' }).nextElementSibling).toHaveTextContent('$0.00');
    });

    it('says "call" in the singular for one', async () => {
      renderPage({ ...active, aiUsage: { ...aiUsage, builtInCalls: 1, callsWithUnknownCost: 1 } });

      await screen.findByText('Active');
      expect(within(panel()).getByText('This month', { selector: 'dt' }).nextElementSibling).toHaveTextContent('1 AI call');
      expect(within(panel()).getByText('1 call has no known price, so it is not charged.')).toBeInTheDocument();
    });

    it('is absent when the API sends no usage', async () => {
      renderPage({ ...active, aiUsage: null });

      await screen.findByText('Active');
      expect(screen.queryByRole('region', { name: 'AI usage' })).toBeNull();
    });

    it('adds no primary button to the page (A1)', async () => {
      renderPage(active);

      await screen.findByText('Active');
      expect(primaryButtons().map((button) => button.textContent?.trim())).toEqual(['Manage billing']);
    });
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

  it('opens with the dialog focused, so the tooltip on the date does not open over the text by itself', async () => {
    renderPage(active);

    await fireEvent.click(await screen.findByRole('button', { name: 'Switch to Growth' }));

    const dialog = await screen.findByRole('dialog', { name: 'Change to Growth?' });
    await waitFor(() => expect(dialog).toHaveTextContent('a month from'));
    await waitFor(() => expect(document.activeElement).toBe(dialog));
    expect(within(dialog).getByText('Dec 5, 2026').closest('[data-slot="tooltip-trigger"]')).not.toBe(document.activeElement);
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

  it('says why the change was refused, leaves the dialog open and prices the change again', async () => {
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
    await waitFor(() => expect(posted.filter((call) => call.path === 'preview')).toHaveLength(2));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Confirm change' })).toBeEnabled());
  });

  it('says when a subscription that will not renew ends, instead of a next payment', async () => {
    renderPage({ ...active, cancelsAtPeriodEnd: true, nextPaymentAmount: null });

    expect(await screen.findByText('Ends on', { selector: 'dt' })).toBeInTheDocument();
    expect(screen.getByText('Ends on', { selector: 'dt' }).nextElementSibling).toHaveTextContent('Dec 5, 2026');
    expect(screen.queryByText('Next payment')).toBeNull();
    expect(within(plans()).queryByRole('button', { name: /^Switch/ })).toBeNull();
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

  it("takes the sandbox Checkout's tax markers out of the address with the outcome", async () => {
    location.url = new URL('http://dashboard.test/settings/billing?checkout=success&tax=requested&taxId=requested');
    renderPage();

    expect(await screen.findByText('Checkout finished. This page updates when the payment is confirmed.')).toBeInTheDocument();
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
