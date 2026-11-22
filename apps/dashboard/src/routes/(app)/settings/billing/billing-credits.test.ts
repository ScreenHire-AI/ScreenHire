import type { AiCreditsView, BillingOverview, CurrentUser } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { aiCredits, applicationId, creditDeductions, creditPurchases, creditsWith } from '../../../../test-support/ai-credits.js';
import { aiUsageSummary } from '../../../../test-support/ai-usage.js';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import { settingsWith } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// The prepaid AI credits on Billing's AI use section (ADR-0032, docs/architecture/billing.md, "AI credits"): the balance, buying,
// auto-reload, and the purchases and deductions behind the balance.
const state = vi.hoisted(() => ({ user: null as CurrentUser | null, url: new URL('http://dashboard.test/settings/billing') }));
vi.mock('$app/state', () => ({
  page: {
    get data() {
      return { user: state.user };
    },
    get url() {
      return state.url;
    },
  },
  navigating: { to: null },
}));
const navigation = vi.hoisted(() => ({ goto: vi.fn(async (_url: string | URL, _options?: object) => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

// No payment provider for plans: these tests read the AI use section, where the credits live.
const noProvider = { available: false, sandbox: false, state: null, currentPlan: null, plans: [], aiUsage: null, thisMonth: null } as unknown as BillingOverview;

const assign = vi.fn();
const sent: { path: string; body: unknown }[] = [];

function renderPage(credits: AiCreditsView = aiCredits, { signedIn = user, routes = {} }: { signedIn?: CurrentUser; routes?: Parameters<typeof apiStub>[0] } = {}) {
  state.user = signedIn;
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/billing': () => Response.json(noProvider),
        'GET /v1/organization/verification-settings': () => Response.json(settingsWith()),
        'GET /v1/organization/ai-usage/summary?timeZone=UTC': () => Response.json(aiUsageSummary),
        'GET /v1/organization/billing/credits': () => Response.json(credits),
        'GET /v1/organization/billing/credits/purchases': () => Response.json({ purchases: creditPurchases }),
        'GET /v1/organization/billing/credits/deductions': () => Response.json({ deductions: creditDeductions, next: null }),
        'POST /v1/organization/billing/credits/checkout': (init) => {
          sent.push({ path: 'checkout', body: JSON.parse(String(init?.body)) });
          return Response.json({ url: 'https://checkout.stripe.example.com/credits' });
        },
        'PUT /v1/organization/billing/credits/settings': (init) => {
          sent.push({ path: 'settings', body: JSON.parse(String(init?.body)) });
          return new Response(null, { status: 204 });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
}

const creditsPanel = async () => (await screen.findByRole('heading', { name: 'AI credits' })).closest('section') as HTMLElement;
const tableOf = async (title: RegExp) => within((await screen.findByRole('heading', { name: title })).closest('section') as HTMLElement).findByRole('table');
const primaryButtons = () => Array.from(document.querySelectorAll('main button')).filter((button) => button.getAttribute('role') !== 'switch' && button.className.includes('bg-primary'));
const refusal = (field: string, message: string) =>
  Response.json({ title: 'One or more validation errors occurred.', status: 400, errors: { [field]: [message] } }, { status: 400 });

beforeEach(() => {
  state.url = new URL('http://dashboard.test/settings/billing');
  vi.stubGlobal('location', { ...window.location, assign });
});

afterEach(() => {
  vi.unstubAllGlobals();
  assign.mockClear();
  navigation.goto.mockClear();
  sent.length = 0;
});

describe('the AI credits panel', () => {
  it('shows the balance in dollars and how long credits last, with no pause badge while there is a balance', async () => {
    renderPage();

    const panel = await creditsPanel();
    expect(within(panel).getByText('Balance', { selector: 'dt' }).nextElementSibling).toHaveTextContent('$18.40');
    expect(within(panel).queryByText('AI checks paused')).not.toBeInTheDocument();
    expect(within(panel).getByText('Credits last 12 months from when they are added.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("says the AI checks are paused at zero on ScreenHire's key, with the badge icon and words, and that a pause says nothing about a candidate", async () => {
    renderPage(creditsWith({ balance: 0, paused: true }));

    const panel = await creditsPanel();
    const badge = within(panel).getByText('AI checks paused').closest('[data-slot="badge"]') as HTMLElement;
    expect(badge.querySelector('[data-status-icon="circle-minus"]')).not.toBeNull();
    expect(badge).toHaveAttribute('data-status', 'neutral');
    expect(panel).toHaveTextContent('The other checks keep running, and a paused check says nothing about a candidate.');
    await expectNoAxeViolations();
  });

  it('says the own key uses no credits, with no pause badge even at a zero balance', async () => {
    renderPage(creditsWith({ ownKey: true, balance: 0, paused: false }));

    const panel = await creditsPanel();
    expect(within(panel).getByText('Your own key runs the AI check, so it uses no credits.')).toBeInTheDocument();
    expect(within(panel).queryByText('AI checks paused')).not.toBeInTheDocument();
    // Auto-reload tops up ScreenHire's key, so it is not offered on an own key.
    expect(screen.queryByRole('form', { name: 'Auto-reload' })).not.toBeInTheDocument();
  });

  it('lets an Owner buy each fixed amount: it posts the amount and goes to the payment page the API returned', async () => {
    renderPage();

    const panel = await creditsPanel();
    const group = within(panel).getByRole('group', { name: 'Add credits' });
    expect(within(group).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['$10.00 of AI credits', '$25.00 of AI credits', '$50.00 of AI credits']);
    await fireEvent.click(within(group).getByRole('button', { name: '$25.00 of AI credits' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://checkout.stripe.example.com/credits'));
    expect(sent).toEqual([{ path: 'checkout', body: { amount: 25 } }]);
  });

  it('shows why buying failed and keeps the buttons usable', async () => {
    renderPage(aiCredits, { routes: { 'POST /v1/organization/billing/credits/checkout': () => Response.json({ detail: 'Checkout is down.' }, { status: 502 }) } });

    const panel = await creditsPanel();
    await fireEvent.click(within(panel).getByRole('button', { name: '$10.00 of AI credits' }));

    expect(await within(panel).findByText('Checkout is down.')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
    expect(within(panel).getByRole('button', { name: '$10.00 of AI credits' })).toBeEnabled();
  });

  it('offers an Admin no buy buttons, and says who adds credits', async () => {
    renderPage(aiCredits, { signedIn: userAs('Admin') });

    const panel = await creditsPanel();
    expect(within(panel).getByText('An owner adds credits and turns on auto-reload.')).toBeInTheDocument();
    expect(within(panel).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Auto-reload' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /^Purchases/ })).not.toBeInTheDocument();
    await screen.findByRole('heading', { name: /^Deductions/ });
    await expectNoAxeViolations();
  });

  it('shows no credits panel where no payment provider is set up', async () => {
    renderPage(creditsWith({ available: false }));

    await screen.findByText('Model calls');
    expect(screen.queryByRole('heading', { name: 'AI credits' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /^Deductions/ })).not.toBeInTheDocument();
  });

  it('shows an error with Try again when the credits do not load', async () => {
    renderPage(aiCredits, { routes: { 'GET /v1/organization/billing/credits': () => Response.json({ detail: 'down' }, { status: 500 }) } });

    const panel = await creditsPanel();
    expect(await within(panel).findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it.each([
    ['success', 'Payment sent. The credits appear here when the payment is confirmed.'],
    ['cancelled', 'Checkout was cancelled. You have not been charged.'],
  ])('says how a return from Checkout ended (%s) and takes it out of the address', async (outcome, sentence) => {
    state.url = new URL(`http://dashboard.test/settings/billing?credits=${outcome}`);
    renderPage();

    const panel = await creditsPanel();
    expect(within(panel).getByRole('status')).toHaveTextContent(sentence);
    await waitFor(() => expect(navigation.goto).toHaveBeenCalledOnce());
    expect(String(navigation.goto.mock.calls[0]?.[0])).toBe('http://dashboard.test/settings/billing');
  });

  it('grants nothing when the browser comes back from Checkout: the balance is what the API says, and nothing is sent', async () => {
    state.url = new URL('http://dashboard.test/settings/billing?credits=success');
    renderPage();

    const panel = await creditsPanel();
    expect(within(panel).getByText('Balance', { selector: 'dt' }).nextElementSibling).toHaveTextContent('$18.40');
    expect(sent).toEqual([]);
  });

  it('adds no primary button to the page (A1)', async () => {
    renderPage();

    await screen.findByRole('heading', { name: /^Deductions/ });
    expect(primaryButtons()).toEqual([]);
  });
});

describe('auto-reload', () => {
  const form = async () => within(await screen.findByRole('form', { name: 'Auto-reload' }));

  it('cannot be turned on before a card is saved, and the switch says why beside it', async () => {
    renderPage(creditsWith({ hasSavedCard: false }));

    const auto = await form();
    const toggle = auto.getByRole('switch', { name: 'Auto-reload' });
    expect(toggle).toBeDisabled();
    expect(toggle).toHaveAccessibleDescription('Buy credits once to save a card. Auto-reload charges that card.');
    expect(auto.queryByLabelText('When the balance falls below')).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('saves auto-reload in dollars: the threshold, the amount, the monthly maximum and the low-balance alert', async () => {
    renderPage(creditsWith({ hasSavedCard: true }));

    const auto = await form();
    await fireEvent.click(auto.getByRole('switch', { name: 'Auto-reload' }));
    await fireEvent.input(auto.getByLabelText('Most in a month'), { target: { value: '100' } });
    await fireEvent.click(auto.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(sent).toEqual([{ path: 'settings', body: { lowBalanceAlert: 5, autoReload: { enabled: true, below: 5, amount: 10, monthlyMax: 100 } } }]),
    );
    expect(await auto.findByText('Saved. Auto-reload is on.')).toBeInTheDocument();
  });

  it('turns auto-reload off from a saved state and says only Saved', async () => {
    renderPage(creditsWith({ hasSavedCard: true, autoReload: { enabled: true, below: 8, amount: 25, monthlyMax: 75, reloadedThisMonth: 25 } }));

    const auto = await form();
    expect(auto.getByLabelText('When the balance falls below')).toHaveValue('8.00');
    expect(auto.getByText(/\$25\.00 so far this month\./)).toBeInTheDocument();
    await fireEvent.click(auto.getByRole('switch', { name: 'Auto-reload' }));
    await fireEvent.click(auto.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(sent).toHaveLength(1));
    expect((sent[0]!.body as { autoReload: { enabled: boolean } }).autoReload.enabled).toBe(false);
    expect(await auto.findByText('Saved.')).toBeInTheDocument();
  });

  it('shows an API refusal of auto-reload on the field it names, and says it saved nothing', async () => {
    renderPage(creditsWith({ hasSavedCard: true }), {
      routes: { 'PUT /v1/organization/billing/credits/settings': () => refusal('autoReload.enabled', 'Buy credits once to save a card first.') },
    });

    const auto = await form();
    await fireEvent.click(auto.getByRole('switch', { name: 'Auto-reload' }));
    await fireEvent.click(auto.getByRole('button', { name: 'Save' }));

    expect(await auto.findByText('Buy credits once to save a card first.')).toBeInTheDocument();
    expect(auto.queryByText(/^Saved/)).not.toBeInTheDocument();
  });

  it("shows an API refusal of the amount on the Add field, not under the API's own name for it", async () => {
    renderPage(creditsWith({ hasSavedCard: true }), {
      routes: { 'PUT /v1/organization/billing/credits/settings': () => refusal('autoReload.amount', 'Choose an amount that can be bought.') },
    });

    const auto = await form();
    await fireEvent.click(auto.getByRole('switch', { name: 'Auto-reload' }));
    await fireEvent.click(auto.getByRole('button', { name: 'Save' }));

    expect(await auto.findByText('Choose an amount that can be bought.')).toBeInTheDocument();
    expect(auto.getByLabelText('Add')).toHaveAttribute('aria-invalid', 'true');
  });

  it('refuses an amount that is not dollars and cents before it sends anything', async () => {
    renderPage(creditsWith({ hasSavedCard: true }));

    const auto = await form();
    await fireEvent.input(auto.getByLabelText('Low-balance alert'), { target: { value: 'ten' } });
    await fireEvent.click(auto.getByRole('button', { name: 'Save' }));

    expect(await auto.findByText('Enter an amount in dollars.')).toBeInTheDocument();
    expect(sent).toEqual([]);
  });
});

describe('the purchases', () => {
  const rows = (table: HTMLElement) =>
    within(table)
      .getAllByRole('row')
      .slice(1)
      .map((row) => row.textContent?.replace(/\s+/g, ' ').trim());

  it('lists the starting credit, each purchase and a refused reload with its status, newest first, for an Owner', async () => {
    renderPage();

    const table = await tableOf(/^Purchases/);
    expect(rows(table)).toEqual([
      'Nov 8, 2026 Auto-reload $10.00 Failed Nothing added',
      'Nov 5, 2026 Bought $25.00 Paid Nov 5, 2027',
      'Oct 1, 2026 Starting credit $5.00 Included Never',
    ]);
    await expectNoAxeViolations();
  });

  it('says so when nothing has been bought', async () => {
    renderPage(aiCredits, { routes: { 'GET /v1/organization/billing/credits/purchases': () => Response.json({ purchases: [] }) } });

    expect(await screen.findByText('No credits bought yet.')).toBeInTheDocument();
  });

  it('shows an error with Try again when the purchases do not load', async () => {
    renderPage(aiCredits, { routes: { 'GET /v1/organization/billing/credits/purchases': () => Response.json({ detail: 'down' }, { status: 500 }) } });

    const panel = (await screen.findByRole('heading', { name: /^Purchases/ })).closest('section') as HTMLElement;
    expect(await within(panel).findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});

describe('the deductions', () => {
  const text = (row: HTMLElement) => row.textContent?.replace(/\s+/g, ' ').trim();

  it("links a check to the application's verification tab by the candidate's name and shows an expiry as credits that expired", async () => {
    renderPage();

    const table = await tableOf(/^Deductions/);
    const check = within(table).getByRole('link', { name: 'AI check, Casey Rowan' });
    expect(check).toHaveAttribute('href', `/applications/${applicationId}?tab=verification`);
    expect(text(check.closest('tr') as HTMLElement)).toBe('Nov 8, 2026 AI check, Casey Rowan 2 $0.31');
    const expiry = within(table).getByText('Credits expired').closest('tr') as HTMLElement;
    expect(text(expiry)).toBe('Oct 31, 2026 Credits expired $1.25');
    expect(within(expiry).queryByRole('link')).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('shows a check whose application was erased as text, never a link', async () => {
    renderPage(aiCredits, {
      routes: {
        'GET /v1/organization/billing/credits/deductions': () =>
          Response.json({ deductions: [{ ...creditDeductions[0]!, applicationId: null, candidateName: null }], next: null }),
      },
    });

    const table = await tableOf(/^Deductions/);
    expect(within(table).getByText('AI check, application erased')).toBeInTheDocument();
    expect(within(table).queryByRole('link')).not.toBeInTheDocument();
  });

  it('says it lists the newest and links to every call when there are more', async () => {
    renderPage(aiCredits, {
      routes: { 'GET /v1/organization/billing/credits/deductions': () => Response.json({ deductions: creditDeductions, next: 'cursor-2' }) },
    });

    expect(await screen.findByText('The newest 2.')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'See every call' }).some((link) => link.getAttribute('href') === '/verification/activity')).toBe(true);
  });

  it('says so when no credits were used', async () => {
    renderPage(aiCredits, { routes: { 'GET /v1/organization/billing/credits/deductions': () => Response.json({ deductions: [], next: null }) } });

    expect(await screen.findByText('No credits used yet.')).toBeInTheDocument();
  });
});
