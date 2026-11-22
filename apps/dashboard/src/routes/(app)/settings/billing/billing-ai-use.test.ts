import type { AiUsageSummary, BillingOverview, CurrentUser, OrganizationVerificationSettings } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { creditRoutes } from '../../../../test-support/ai-credits.js';
import { aiUsageSummary as summary } from '../../../../test-support/ai-usage.js';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import { ownKey, settingsWith } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Billing's AI use section (docs/architecture/billing.md, "AI credits"): what the checks used and whose key runs the AI check, beside
// the prepaid credits (their own tests are in billing-credits.test.ts). For whoever can see AI usage.
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
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

// No payment provider: an Owner gets the AI use section and nothing else, so these tests read only that section.
const noProvider = { available: false, sandbox: false, state: null, currentPlan: null, plans: [], aiUsage: null, thisMonth: null } as unknown as BillingOverview;

interface Options {
  usage?: AiUsageSummary | Response;
  routes?: Parameters<typeof apiStub>[0];
  signedIn?: CurrentUser;
}

/** Renders Billing with the settings, the usage summary and any other routes; returns every request. */
function renderPage(settings: OrganizationVerificationSettings, { usage = summary, routes = {}, signedIn = user }: Options = {}) {
  state.user = signedIn;
  const requests: string[] = [];
  const stub = apiStub({
    'GET /v1/organization/billing': () => Response.json(noProvider),
    ...creditRoutes(),
    'GET /v1/organization/verification-settings': () => Response.json(settings),
    'GET /v1/organization/ai-usage/summary?timeZone=UTC': () => (usage instanceof Response ? usage : Response.json(usage)),
    ...routes,
  });
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      requests.push(String(input));
      return stub(input, init);
    }),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return { requests };
}

// The panel once the settings have loaded: its skeleton is another element with the same name.
const aiUse = async () => (await screen.findByText('Key', { selector: 'dt' })).closest('section') as HTMLElement;

beforeEach(() => {
  state.url = new URL('http://dashboard.test/settings/billing');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Billing's AI use section", () => {
  it('holds the credits and auto-reload, whose key runs the check, what the checks used and the credits behind the balance, in that order, for an Owner', async () => {
    renderPage(settingsWith());

    await screen.findByText('Model calls');
    await screen.findByRole('heading', { name: 'Deductions' });
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim())).toEqual([
      'AI credits',
      'Auto-reload',
      'AI use',
      'Use by day, this month',
      'By model and key',
      expect.stringMatching(/^Purchases/),
      expect.stringMatching(/^Deductions/),
    ]);
    // Whose key is a setting of its own now, in Settings > Integrations > AI model; here it is a fact with a link.
    expect(screen.queryByRole('form', { name: 'Whose key runs the AI check' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Spending limits' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /^Save/ }).map((button) => button.textContent?.trim())).toEqual(['Save']);
    await expectNoAxeViolations();
  });

  it("shows an Admin, who sees AI usage but not the plan, the section and never asks for the Owner's billing", async () => {
    const { requests } = renderPage(settingsWith(), { signedIn: userAs('Admin') });

    await screen.findByText('Model calls');
    // The credits are under the same path, and an Admin reads them.
    expect(requests.some((url) => /^\/v1\/organization\/billing(\?|\/(?!credits)|$)/.test(url))).toBe(false);
    expect(screen.queryByRole('region', { name: 'Plan' })).toBeNull();
    expect(within(await aiUse()).getByRole('link', { name: 'Use your own key' })).toHaveAttribute('href', '/settings/ai-model');
  });

  it('names the own key, its provider and model, and offers to change it to who may', async () => {
    renderPage(ownKey());

    const panel = await aiUse();
    expect(within(panel).getByText(/^Your own key, /)).toHaveTextContent('Your own key, Anthropic, claude-test. Your AI provider bills you.');
    expect(within(panel).getByRole('link', { name: 'Change the key' })).toHaveAttribute('href', '/settings/ai-model');
    expect(within(panel).queryByText(/this charge goes away/)).toBeNull();
  });

  it('reads the key as a fact, with no link, for someone who sees usage but may not change settings', async () => {
    const reader: CurrentUser = { ...user, organization: { ...user.organization!, role: 'Custom', permissions: ['ViewHiring', 'ViewAiUsage'] } };
    renderPage(settingsWith(), { signedIn: reader });

    const panel = await aiUse();
    expect(within(panel).getByText("ScreenHire's key. The checks use your AI credits.")).toBeInTheDocument();
    expect(within(panel).queryByRole('link', { name: /key/ })).toBeNull();
    expect(within(panel).getByRole('link', { name: 'See every call' })).toHaveAttribute('href', '/verification/activity');
  });

  it('brings the section into view when the address names it, once what is above it has loaded', async () => {
    state.url = new URL('http://dashboard.test/settings/billing#ai-use');
    const scrolled = vi.fn();
    Element.prototype.scrollIntoView = scrolled;
    renderPage(settingsWith());

    await waitFor(() => expect(scrolled).toHaveBeenCalledOnce());
    expect(scrolled.mock.contexts[0]).toBe(document.getElementById('ai-use'));
  });
});

describe('what the checks used', () => {
  const stat = (title: string) => screen.getAllByText(title).map((node) => node.closest('[data-slot="report-stat"]')).find(Boolean) as HTMLElement;

  it("shows calls, tokens in and out, and as Cost what ScreenHire bills, with the own key's estimate apart and never added in", async () => {
    renderPage(settingsWith());

    await screen.findByText('Model calls');
    expect(within(stat('Model calls')).getByText('12')).toBeInTheDocument();
    expect(within(stat('Tokens')).getByText('10,500')).toBeInTheDocument();
    expect(within(stat('Tokens')).getByText('9,000 in, 1,500 out')).toBeInTheDocument();
    expect(within(stat('Cost')).getByText('$0.30')).toBeInTheDocument();
    expect(within(stat('Cost')).getByText('$0.12 more charged by your provider (estimate)')).toBeInTheDocument();
    expect(screen.queryByText('$0.42')).not.toBeInTheDocument();
  });

  it("names the tile for the own key's estimate when the range has only own-key use", async () => {
    renderPage(settingsWith(), { usage: { ...summary, byKeySource: [summary.byKeySource[1]!] } });

    await screen.findByText('Model calls');
    expect(within(stat('Charged by your provider (estimate)')).getByText('$0.12')).toBeInTheDocument();
    expect(screen.queryByText('Cost', { selector: '[data-slot="report-stat"] *' })).not.toBeInTheDocument();
  });

  it('says when the newest call happened under the Model calls number, as time ago with the exact time on hover (T7)', async () => {
    renderPage(settingsWith());

    const tile = (await screen.findByText('Model calls')).closest('[data-slot="report-stat"]') as HTMLElement;
    expect(tile).toHaveTextContent(/Last call 5 minutes ago/);
    expect(tile.querySelector('time')).toHaveAttribute('datetime', summary.lastCallAt!);
  });

  it("charts tokens by day on ScreenHire's key and on the own key, with the same numbers as a table", async () => {
    renderPage(settingsWith());

    const panel = (await screen.findByRole('heading', { name: 'Use by day, this month' })).closest('section') as HTMLElement;
    expect(within(panel).getByText("ScreenHire's key")).toBeInTheDocument();
    expect(within(panel).getByText('Your own key')).toBeInTheDocument();
    await fireEvent.click(within(panel).getByRole('button', { name: 'Table' }));
    const table = await within(panel).findByRole('table');
    expect(within(table).getByRole('columnheader', { name: /^Cost/ })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: /Charged by your provider \(estimate\)/ })).toBeInTheDocument();
    expect(within(table).getAllByRole('row')).toHaveLength(3);
  });

  it('lists use by model and key', async () => {
    renderPage(settingsWith());

    const table = within(await screen.findByRole('region', { name: 'Usage by model and key' })).getByRole('table');
    expect(within(table).getByText('claude-test')).toBeInTheDocument();
    expect(within(table).getByText('gpt-test')).toBeInTheDocument();
    const own = within(table).getByText('gpt-test').closest('tr') as HTMLElement;
    expect(own).toHaveTextContent('$0.12 estimate');
  });

  it('asks for the last 30 days when chosen', async () => {
    const { requests } = renderPage(settingsWith());
    await screen.findByText('Model calls');
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        requests.push(String(input));
        return Response.json(summary);
      }),
    );

    await fireEvent.change(screen.getByRole('combobox', { name: 'Date range' }), { target: { value: '30' } });

    await screen.findByText('Model calls');
    expect(requests.some((url) => url.includes('from=') && url.includes('to=') && url.includes('timeZone=UTC'))).toBe(true);
  });

  it('leaves out when the newest call happened while there has been none, and says so when there were no calls', async () => {
    renderPage(settingsWith(), { usage: { ...summary, totals: { ...summary.totals, calls: 0 }, byDay: [], byModel: [], lastCallAt: null } });

    expect(await screen.findByText('No model calls in this range.', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'No model calls in this range.' })).toBeInTheDocument();
    const tile = screen.getByText('Model calls').closest('[data-slot="report-stat"]') as HTMLElement;
    expect(tile).not.toHaveTextContent(/Last call/);
    expect(tile.querySelector('p')).toBeNull();
  });

  it('states a single day as a line, not a one-bar chart', async () => {
    renderPage(settingsWith(), { usage: { ...summary, byDay: [summary.byDay[0]!] } });

    expect(await screen.findByText(/All the use in this range was on .*, so the totals above are that day's\./)).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: /Nov/ })).not.toBeInTheDocument();
  });

  it('counts a call with no known cost as nothing, and says how many', async () => {
    renderPage(settingsWith(), { usage: { ...summary, totals: { ...summary.totals, callsWithUnknownCost: 2 } } });

    expect(await screen.findByText(/2 calls have no known cost, counted as nothing/)).toBeInTheDocument();
  });

  it('shows an error with Try again when the usage does not load', async () => {
    renderPage(settingsWith(), { usage: Response.json({ detail: 'down' }, { status: 500 }) });

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
