import type { AiUsageCallPage, AiUsageSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import AiUsagePanel from './ai-usage-panel.svelte';

// The AI usage panel (docs/architecture/ai-usage.md): the month against the budget and cap, use by day, model and key, every call
// with the provider's request id, the CSV, and the Owner's limits.
const limits: AiUsageSummary['limits'] = {
  unit: 'Tokens',
  budget: 1_000_000,
  cap: null,
  effectiveBudget: { amount: 1_000_000, unit: 'Tokens', isDefault: false, used: 850_000, percent: 85 },
  effectiveCap: { amount: 2_000_000, unit: 'Tokens', isDefault: true, used: 850_000, percent: 42 },
};
const summary: AiUsageSummary = {
  from: '2026-11-01T00:00:00Z',
  to: '2026-11-09T12:00:00Z',
  totals: { calls: 3, inputTokens: 700_000, outputTokens: 150_000, cost: 1.25, callsWithUnknownCost: 1, failedCalls: 0 },
  byDay: [{ date: '2026-11-08', calls: 3, inputTokens: 700_000, outputTokens: 150_000, cost: 1.25 }],
  byModel: [
    { provider: 'Anthropic', model: 'model-a', keySource: 'BuiltIn', calls: 2, inputTokens: 500_000, outputTokens: 100_000, cost: 1.25, callsWithUnknownCost: 0 },
    { provider: 'OpenRouter', model: 'vendor/model-b', keySource: 'OwnKey', calls: 1, inputTokens: 200_000, outputTokens: 50_000, cost: 0, callsWithUnknownCost: 1 },
  ],
  byKeySource: [],
  limits,
};
const firstPage: AiUsageCallPage = {
  calls: [
    {
      id: '01a157d4-0000-7000-8000-000000000003',
      occurredAt: '2026-11-08T10:00:00Z',
      check: 'consistency.comparison',
      applicationId: null,
      provider: 'Anthropic',
      model: 'model-a',
      keySource: 'BuiltIn',
      providerRequestId: 'msg_01ABC',
      inputTokens: 1200,
      outputTokens: 300,
      cost: 0.0042,
      costBasis: 'Priced',
      durationMs: 900,
      outcome: 'Ok',
      errorKind: null,
    },
  ],
  nextBefore: '01a157d4-0000-7000-8000-000000000003',
};
const secondPage: AiUsageCallPage = {
  calls: [{ ...firstPage.calls[0], id: '01a157d4-0000-7000-8000-000000000002', providerRequestId: 'msg_02DEF', outcome: 'TimedOut', cost: null, costBasis: 'Unknown' }],
  nextBefore: null,
};

vi.mock('$app/state', () => ({ page: { data: {}, url: new URL('http://dashboard.test/verification') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

type Handler = (init: RequestInit | undefined, url: string) => Response | Promise<Response>;

// The routes carry query strings (range, filters, cursor), so they are matched by method and path.
function renderPanel(canSetLimits: boolean, routes: Record<string, Handler> = {}) {
  const urls: string[] = [];
  const handlers: Record<string, Handler> = {
    'GET /v1/organization/ai-usage/summary': () => Response.json(summary),
    'GET /v1/organization/ai-usage/calls': (_init, url) => {
      urls.push(url);
      return Response.json(url.includes('before=') ? secondPage : firstPage);
    },
    ...routes,
  };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const key = `${(init?.method ?? 'GET').toUpperCase()} ${url.split('?')[0]}`;
      const handler = handlers[key];
      if (!handler) throw new Error(`No stub for ${key}`);
      return handler(init, url);
    }),
  );
  render(WithQueryClient, { props: { component: AiUsagePanel, props: { canSetLimits } } });
  return urls;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('AI usage panel', () => {
  it('shows the month, the budget as used of the amount with where it stands, and marks only the limit the Owner set', async () => {
    renderPanel(false);

    expect(await screen.findByText('850,000 tokens of 1,000,000 tokens')).toBeInTheDocument();
    expect(screen.getByText('Close')).toBeInTheDocument();
    expect(screen.getAllByText('Set by you')).toHaveLength(1); // the budget is the Owner's; the cap is ScreenHire's default and says nothing
    expect(screen.getByText(/1 call has no known cost/)).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('lists the calls with the provider request id and pages by the cursor of the page above', async () => {
    const urls = renderPanel(false);

    expect(await screen.findByText('msg_01ABC')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Show more' }));

    expect(await screen.findByText('msg_02DEF')).toBeInTheDocument();
    expect(screen.getAllByText('Timed out')).toHaveLength(2); // the filter's option and the call's badge
    expect(urls.some((url) => url.includes('before=01a157d4-0000-7000-8000-000000000003'))).toBe(true);
  });

  it('links the CSV export with the filters in force', async () => {
    renderPanel(false);
    await screen.findByText('msg_01ABC');

    await fireEvent.change(screen.getByLabelText('Filter by key'), { target: { value: 'OwnKey' } });

    await waitFor(() => expect(screen.getByRole('link', { name: /Export CSV/ }).getAttribute('href')).toContain('keySource=OwnKey'));
    expect(screen.getByRole('link', { name: /Export CSV/ }).getAttribute('href')).toContain('/v1/organization/ai-usage/calls/export');
  });

  it('offers the limits form to an Owner only', async () => {
    renderPanel(false);
    await screen.findByText('msg_01ABC');
    expect(screen.queryByRole('form', { name: 'Monthly AI limits' })).not.toBeInTheDocument();
  });

  it('saves the limits as numbers, and an empty cap as null', async () => {
    const bodies: Record<string, unknown>[] = [];
    renderPanel(true, {
      'PUT /v1/organization/ai-usage/limits': (init) => {
        bodies.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
        return Response.json(limits);
      },
    });
    const budget = (await screen.findByLabelText(/Monthly budget/)) as HTMLInputElement;
    await fireEvent.input(budget, { target: { value: '500000' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Save limits' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({ unit: 'Tokens', budget: 500000, cap: null });
  });
});
