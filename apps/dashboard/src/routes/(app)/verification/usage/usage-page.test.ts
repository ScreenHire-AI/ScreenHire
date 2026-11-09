import type { AiUsageSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import { limits } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/usage') }, navigating: { to: null } }));

const summary: AiUsageSummary = {
  from: '2026-11-01T00:00:00Z',
  to: '2026-11-09T12:00:00Z',
  totals: { calls: 12, inputTokens: 9000, outputTokens: 1500, cost: 0.42, callsWithUnknownCost: 0, failedCalls: 1 },
  byDay: [
    { date: '2026-11-07', calls: 5, inputTokens: 4000, outputTokens: 500, cost: 0.2, builtInTokens: 4500, ownKeyTokens: 0, builtInCost: 0.2, ownKeyCost: 0 },
    { date: '2026-11-08', calls: 7, inputTokens: 5000, outputTokens: 1000, cost: 0.22, builtInTokens: 2000, ownKeyTokens: 4000, builtInCost: 0.1, ownKeyCost: 0.12 },
  ],
  byModel: [
    { provider: 'Anthropic', model: 'claude-test', keySource: 'BuiltIn', calls: 8, inputTokens: 6000, outputTokens: 500, cost: 0.3, callsWithUnknownCost: 0 },
    { provider: 'OpenAi', model: 'gpt-test', keySource: 'OwnKey', calls: 4, inputTokens: 3000, outputTokens: 1000, cost: 0.12, callsWithUnknownCost: 0 },
  ],
  byKeySource: [],
  limits,
};

function renderPage(result: AiUsageSummary | Response, routes: Parameters<typeof apiStub>[0] = {}) {
  const requests: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      requests.push(String(input));
      return apiStub({
        'GET /v1/organization/ai-usage/summary?timeZone=UTC': () => (result instanceof Response ? result : Response.json(result)),
        ...routes,
      })(input, init);
    }),
  );
  render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });
  return requests;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Usage tab', () => {
  it('shows calls, tokens in and out and cost for the month', async () => {
    renderPage(summary);

    await screen.findByText('Model calls');
    const stat = async (title: string) =>
      screen.getAllByText(title).map((node) => node.closest('[data-slot="report-stat"]')).find(Boolean) as HTMLElement;
    expect(within(await stat('Model calls')).getByText('12')).toBeInTheDocument();
    expect(within(await stat('Tokens')).getByText('10,500')).toBeInTheDocument();
    expect(within(await stat('Tokens')).getByText('9,000 in, 1,500 out')).toBeInTheDocument();
    expect(within(await stat('Cost')).getByText('$0.42')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('shows the month against the budget and the cap', async () => {
    renderPage(summary);

    const panel = (await screen.findByRole('heading', { name: 'This month against the limits' })).closest('section') as HTMLElement;
    expect(within(panel).getByText('Monthly budget')).toBeInTheDocument();
    expect(within(panel).getByText('Monthly cap')).toBeInTheDocument();
    expect(within(panel).getAllByText('420 tokens of 2,000,000 tokens')).toHaveLength(2);
  });

  it('charts tokens by day on ScreenHire\'s key and on the own key, with the same numbers as a table', async () => {
    renderPage(summary);

    const panel = (await screen.findByRole('heading', { name: 'Use by day, this month' })).closest('section') as HTMLElement;
    expect(within(panel).getByText("ScreenHire's key")).toBeInTheDocument();
    expect(within(panel).getByText('Your own key')).toBeInTheDocument();
    await fireEvent.click(within(panel).getByRole('button', { name: 'Table' }));
    const table = await within(panel).findByRole('table');
    expect(within(table).getByRole('columnheader', { name: /Cost/ })).toBeInTheDocument();
    expect(within(table).getAllByRole('row')).toHaveLength(3);
  });

  it('lists use by model and key', async () => {
    renderPage(summary);

    const table = within(await screen.findByRole('region', { name: 'Usage by model and key' })).getByRole('table');
    expect(within(table).getByText('claude-test')).toBeInTheDocument();
    expect(within(table).getByText('gpt-test')).toBeInTheDocument();
    expect(within(table).getByText('Your own key')).toBeInTheDocument();
  });

  it('asks for the last 30 days when chosen', async () => {
    const requests = renderPage(summary, {});
    await screen.findByText('Model calls');
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      requests.push(String(input));
      return Response.json(summary);
    }));

    await fireEvent.change(screen.getByRole('combobox', { name: 'Date range' }), { target: { value: '30' } });

    await screen.findByText('Model calls');
    expect(requests.some((url) => url.includes('from=') && url.includes('to=') && url.includes('timeZone=UTC'))).toBe(true);
  });

  it('says so when there were no calls', async () => {
    renderPage({ ...summary, totals: { ...summary.totals, calls: 0 }, byDay: [], byModel: [] });

    expect(await screen.findByText('No model calls in this range.', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'No model calls in this range.' })).toBeInTheDocument();
  });

  it('states a single day as a line, not a one-bar chart', async () => {
    renderPage({ ...summary, byDay: [summary.byDay[0]] });

    expect(await screen.findByText(/All the use in this range was on .*, so the totals above are that day's\./)).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: /Nov/ })).not.toBeInTheDocument();
  });

  it('counts a call with no known cost as nothing, and says how many', async () => {
    renderPage({ ...summary, totals: { ...summary.totals, callsWithUnknownCost: 2 } });

    expect(await screen.findByText(/2 calls have no known cost, counted as nothing/)).toBeInTheDocument();
  });

  it('shows an error with Try again when the usage does not load', async () => {
    renderPage(Response.json({ detail: 'down' }, { status: 500 }));

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
