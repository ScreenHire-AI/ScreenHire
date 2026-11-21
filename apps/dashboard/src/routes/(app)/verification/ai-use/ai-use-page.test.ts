import type { AiUsageSummary, CurrentUser, OrganizationVerificationSettings } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import { dollarLimits, limits, ownKey, settingsWith, type Body } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/ai-use') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

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
  byKeySource: [
    { provider: '', model: '', keySource: 'BuiltIn', calls: 8, inputTokens: 6000, outputTokens: 500, cost: 0.3, callsWithUnknownCost: 0 },
    { provider: '', model: '', keySource: 'OwnKey', calls: 4, inputTokens: 3000, outputTokens: 1000, cost: 0.12, callsWithUnknownCost: 0 },
  ],
  limits,
  // Five minutes before the test runs, so it always reads as time ago.
  lastCallAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
};

interface Options {
  usage?: AiUsageSummary | Response;
  routes?: Parameters<typeof apiStub>[0];
  signedIn?: CurrentUser;
}

/** Renders the tab with the settings, the usage summary and any other routes; returns the settings saves and every request. */
function renderPage(settings: OrganizationVerificationSettings, { usage = summary, routes = {}, signedIn = user }: Options = {}) {
  const bodies: Body[] = [];
  const requests: string[] = [];
  const stub = apiStub({
    'GET /v1/organization/verification-settings': () => Response.json(settings),
    'PUT /v1/organization/verification-settings': (init) => {
      bodies.push(JSON.parse(String(init?.body)) as Body);
      return Response.json(settings);
    },
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
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
  return { bodies, requests };
}

const keyForm = () => screen.findByRole('form', { name: 'Whose key runs the AI check' });
const save = async () => within(await keyForm()).getByRole('button', { name: 'Save' });
const apiKey = () => screen.findByLabelText('API key') as Promise<HTMLInputElement>;
const model = () => screen.findByLabelText('Model') as Promise<HTMLInputElement>;
const type = async (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value } });
const limitsPanel = async () => (await screen.findByRole('heading', { name: 'Monthly limits' })).closest('section') as HTMLElement;

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the AI use tab', () => {
  it('holds what the checks used, the budget and cap, the AI usage alert and whose key runs the AI check, for an Owner', async () => {
    renderPage(settingsWith({ limits: dollarLimits }));

    await screen.findByText('Model calls');
    await keyForm();
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim())).toEqual([
      'Use by day, this month',
      'By model and key',
      'Monthly limits',
      'Notifications',
      'Whose key runs the AI check',
    ]);
    // The consistency check is a Checks setting now; nothing here turns a group of checks on or off.
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    // Three forms save on their own, so each Save names what it saves and only the key's is filled (A1).
    const saves = screen.getAllByRole('button', { name: /^Save/ });
    expect(saves.map((button) => button.textContent?.trim())).toEqual(['Save limits', 'Save alert', 'Save']);
    expect(saves.filter((button) => /\bbg-primary(?!\/)/.test(button.className))).toHaveLength(1);
    await expectNoAxeViolations();
  });

  it('shows an Admin limited to their own jobs the key and the alert, but none of the usage, which covers every job', async () => {
    const admin = userAs('Admin');
    const { requests } = renderPage(settingsWith(), { signedIn: { ...admin, organization: { ...admin.organization!, seesAllJobs: false } } });

    await keyForm();
    expect(screen.queryByText('Model calls')).not.toBeInTheDocument();
    expect(requests.some((url) => url.includes('/ai-usage/summary'))).toBe(false);
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

describe('the monthly budget and cap', () => {
  it('shows no limits when the server sent none, which it does for who cannot see AI usage', async () => {
    renderPage(settingsWith());
    await keyForm();

    expect(screen.queryByRole('heading', { name: 'Monthly limits' })).not.toBeInTheDocument();
  });

  it('gives an Owner the form in dollars, saying the limits apply whichever key runs the checks, and naming the default', async () => {
    renderPage(settingsWith({ limits: dollarLimits }));

    const panel = await limitsPanel();
    expect(within(panel).getByRole('form', { name: 'Monthly AI limits' })).toBeInTheDocument();
    expect(panel).toHaveTextContent('In US dollars. They apply whichever key runs the checks.');
    expect(panel).not.toHaveTextContent(/own key has none/);
    expect(within(panel).getByLabelText(/Monthly budget/)).toHaveValue('');
    expect(within(panel).getAllByText("Empty keeps ScreenHire's default of $3.84 a month.")).toHaveLength(2);
    // The thresholds are one press away.
    await fireEvent.click(within(panel).getByRole('button', { name: 'About the budget and cap' }));
    expect(await screen.findByText(/warns you at 50%, 80% and 100%/)).toBeInTheDocument();
  });

  it('shows the limits in tokens with one short note where the model has no price', async () => {
    renderPage(settingsWith({ limits }), { signedIn: userAs('Admin') });

    const panel = await limitsPanel();
    expect(within(panel).getAllByText('420 tokens of 2,000,000 tokens')).toHaveLength(2);
    expect(within(panel).getAllByText('In tokens until the AI model has a price.')).toHaveLength(1);
  });

  it("starts from an Owner's own limit in dollars, one stored in tokens shown as the API converted it, so saving keeps it", async () => {
    renderPage(settingsWith({ limits: { ...dollarLimits, unit: 'Tokens', cap: 4_000_000, effectiveCap: { amount: 7.68, unit: 'Money', isDefault: false, used: 0.0683, percent: 0 } } }));

    const panel = await limitsPanel();
    expect(within(panel).getByLabelText(/Monthly cap/)).toHaveValue('7.68');
    expect(within(panel).getByLabelText(/Monthly budget/)).toHaveValue('');
  });

  it('sends the limits in dollars with no unit', async () => {
    const sent: unknown[] = [];
    renderPage(settingsWith({ limits: dollarLimits }), {
      routes: {
        'PUT /v1/organization/ai-usage/limits': (init) => {
          sent.push(JSON.parse(String(init?.body)));
          return Response.json(dollarLimits);
        },
      },
    });

    await fireEvent.input(within(await limitsPanel()).getByLabelText(/Monthly budget/), { target: { value: '25.50' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save limits' }));

    await waitFor(() => expect(sent).toEqual([{ budget: 25.5, cap: null }]));
  });

  it('lets an Owner remove a limit set before the model lost its price, after asking', async () => {
    const sent: unknown[] = [];
    renderPage(settingsWith({ limits: { ...limits, cap: 5, effectiveCap: { amount: 5, unit: 'Money', isDefault: false, used: 0, percent: 0 } } }), {
      routes: {
        'PUT /v1/organization/ai-usage/limits': (init) => {
          sent.push(JSON.parse(String(init?.body)));
          return Response.json(limits);
        },
      },
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Remove limits' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove your budget and cap?' });
    expect(dialog).not.toHaveTextContent(/own key has none/);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove limits' }));

    await waitFor(() => expect(sent).toEqual([{ budget: null, cap: null }]));
  });

  it('offers no form where the AI model has no price, and says why once, with the limits in tokens', async () => {
    renderPage(settingsWith({ limits }));

    const panel = await limitsPanel();
    expect(within(panel).queryByRole('form', { name: 'Monthly AI limits' })).not.toBeInTheDocument();
    expect(panel).toHaveTextContent(/Limits count dollars, and the AI model in use has no price yet\./);
  });

  it('shows an Admin the budget and cap as facts, since only an Owner sets them', async () => {
    renderPage(settingsWith({ limits: dollarLimits }), { signedIn: userAs('Admin') });

    const panel = await limitsPanel();
    expect(panel).toHaveTextContent(/An owner sets the budget and cap/);
    expect(within(panel).getAllByText('$0.07 of $3.84')).toHaveLength(2);
    expect(within(panel).queryByRole('form', { name: 'Monthly AI limits' })).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });
});

describe('the AI usage alert', () => {
  it('saves email and Slack for the alert, keeping the Verification suggested alert as saved', async () => {
    const { bodies } = renderPage(settingsWith({ alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: false } }));
    const form = await screen.findByRole('form', { name: 'AI usage alert' });
    expect(within(form).getByText(/Always\./)).toBeInTheDocument();

    await fireEvent.click(within(form).getByRole('checkbox', { name: 'AI usage on Slack, as a direct message' }));
    await fireEvent.click(within(form).getByRole('button', { name: 'Save alert' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: true } });
  });

  it('hides email it cannot send and Slack until it is connected, and then has nothing to save', async () => {
    renderPage(settingsWith({ emailAvailable: false, slackConnected: false }));

    const form = await screen.findByRole('form', { name: 'AI usage alert' });
    expect(within(form).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(form).queryByRole('button', { name: 'Save alert' })).not.toBeInTheDocument();
    expect(form).not.toHaveTextContent(/slack/i);
  });
});

describe('whose key runs the AI check', () => {
  it('saves the key choice, keeping every other setting as it was saved', async () => {
    const { bodies } = renderPage(
      settingsWith({ llmConsistencyCheck: true, defaultAiUse: 'NotAllowed', trustedDomains: ['partner.example.com'], checks: { network: true, contact: true, documents: false, answers: true, duplicates: true, formUse: true } }),
    );

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      llmConsistencyCheck: true,
      modelSource: 'Included',
      provider: null,
      model: null,
      apiKey: null,
      defaultAiUse: 'NotAllowed',
      trustedDomains: ['partner.example.com'],
      checks: { documents: false },
    });
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
  });

  it('says why when the API refuses the settings', async () => {
    renderPage(settingsWith(), { routes: { 'PUT /v1/organization/verification-settings': () => Response.json({ detail: 'Only owners and admins can change this.' }, { status: 403 }) } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Only owners and admins can change this.')).toBeInTheDocument();
  });

  it('describes each choice in a line, with who bills what one press away, and offers the own-key fields only when it is chosen', async () => {
    renderPage(settingsWith());
    expect(await screen.findByText("You're billed for what the checks use.")).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /ScreenHire's key/ })).toBeChecked();
    expect(screen.queryByLabelText('API key')).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'About the two keys' }));
    expect(await screen.findByText(/provider handles this data under your own terms/)).toBeInTheDocument();
    expect(screen.getByText(/ScreenHire charges nothing for these calls/)).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('radio', { name: /^Your own key/ }));
    expect(await apiKey()).toHaveAttribute('type', 'password');
  });

  it('reads as a fact for who cannot edit settings', async () => {
    const viewer = userAs('Viewer');
    renderPage(ownKey({ limits: dollarLimits }), { signedIn: { ...viewer, organization: { ...viewer.organization!, permissions: ['ViewAiUsage'] } } });

    const panel = (await screen.findByRole('heading', { name: 'Whose key runs the AI check' })).closest('section') as HTMLElement;
    expect(panel).toHaveTextContent('Your own key, Anthropic, claude-test. Your AI provider bills you.');
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'AI usage alert' })).not.toBeInTheDocument();
  });

  it('lists every provider preset, fills in a fixed address and asks for the address only where the person types one', async () => {
    renderPage(ownKey({ model: '', apiKeySaved: false }));
    const provider = (await screen.findByLabelText('Provider')) as HTMLSelectElement;

    expect(within(provider).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Anthropic',
      'OpenAI',
      'OpenRouter',
      'Azure OpenAI',
      'Google Gemini',
      'Other (OpenAI-compatible)',
    ]);
    expect(screen.queryByLabelText('Endpoint address')).not.toBeInTheDocument();

    await fireEvent.change(provider, { target: { value: 'OpenRouter' } });
    expect(await screen.findByText('Calls go to https://openrouter.ai/api/v1.')).toBeInTheDocument();

    await fireEvent.change(provider, { target: { value: 'AzureOpenAi' } });
    expect(await screen.findByLabelText('Endpoint address')).toBeInTheDocument();
  });

  it('sends the own key prices, and the address only for a preset that takes one', async () => {
    const { bodies } = renderPage(ownKey({ provider: 'OpenRouter', apiKeySaved: false, model: 'a/b' }));
    await type(await apiKey(), 'sk-test');
    await type((await screen.findByLabelText(/Input price/)) as HTMLInputElement, '3');
    await type((await screen.findByLabelText(/Output price/)) as HTMLInputElement, '15.5');

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ provider: 'OpenRouter', baseUrl: null, ownKeyInputPrice: 3, ownKeyOutputPrice: 15.5 });
  });

  it('never shows a saved key, and keeps it by sending apiKey null', async () => {
    const { bodies } = renderPage(ownKey());

    const box = await apiKey();
    expect(box.value).toBe('');
    expect(box).toHaveAttribute('autocomplete', 'off');
    expect(box).toHaveAttribute('placeholder', 'Enter a new key to replace it');
    expect(screen.getByText('A key is saved. Leave this empty to keep it.')).toBeInTheDocument();
    expect((await model()).value).toBe('claude-test');

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ modelSource: 'OwnKey', provider: 'Anthropic', baseUrl: null, model: 'claude-test', apiKey: null });
  });

  it('sends the typed key once, then empties the box', async () => {
    const { bodies } = renderPage(ownKey());
    const box = await apiKey();
    await type(box, '  sk-new-key  ');

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ apiKey: 'sk-new-key' });
    await waitFor(() => expect(box.value).toBe(''));
  });

  it('needs a key before the first save of an own key, and sends nothing', async () => {
    const { bodies } = renderPage(settingsWith());
    await fireEvent.click(await screen.findByRole('radio', { name: /^Your own key/ }));
    await type(await model(), 'claude-test');

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter the API key.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('needs a new key when the provider changes, so an old key is never sent elsewhere', async () => {
    const { bodies } = renderPage(ownKey());
    const select = (await screen.findByLabelText('Provider')) as HTMLSelectElement;
    await fireEvent.change(select, { target: { value: 'OpenAiCompatible' } });
    await type(await screen.findByLabelText('Endpoint address'), 'https://example.test/v1');

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter the API key for this provider.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('asks for an https endpoint for an OpenAI-compatible provider, and sends it with the key', async () => {
    const { bodies } = renderPage(settingsWith());
    await fireEvent.click(await screen.findByRole('radio', { name: /^Your own key/ }));
    await fireEvent.change(await screen.findByLabelText('Provider'), { target: { value: 'OpenAiCompatible' } });
    await type(await model(), 'gpt-test');
    await type(await apiKey(), 'sk-test');
    await type(await screen.findByLabelText('Endpoint address'), 'http://example.test/v1');

    await fireEvent.click(await save());
    expect(await screen.findByText(/starting with https/)).toBeInTheDocument();
    expect(bodies).toEqual([]);

    await type(await screen.findByLabelText('Endpoint address'), 'https://example.test/v1');
    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ modelSource: 'OwnKey', provider: 'OpenAiCompatible', baseUrl: 'https://example.test/v1', model: 'gpt-test', apiKey: 'sk-test' });
  });

  it('shows the server saying the key was refused, on the key field', async () => {
    renderPage(ownKey(), {
      routes: { 'PUT /v1/organization/verification-settings': () => Response.json({ errors: { apiKey: ['The provider did not accept this key.'] } }, { status: 400 }) },
    });
    await type(await apiKey(), 'sk-bad');

    await fireEvent.click(await save());

    expect(await screen.findByText('The provider did not accept this key.')).toBeInTheDocument();
  });

  it('tests the connection and says it works, sending the model and no key when the box is empty', async () => {
    const tests: Body[] = [];
    renderPage(ownKey(), {
      routes: {
        'POST /v1/organization/verification-settings/llm/test': (init) => {
          tests.push(JSON.parse(String(init?.body)) as Body);
          return Response.json({ ok: true, problem: null });
        },
      },
    });
    await apiKey();

    await fireEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText('Connected. The key and model work.')).toBeInTheDocument();
    expect(tests).toEqual([{ provider: 'Anthropic', baseUrl: null, apiKey: null, model: 'claude-test' }]);
  });

  it('tests the connection and says what was wrong, in a sentence', async () => {
    renderPage(ownKey(), { routes: { 'POST /v1/organization/verification-settings/llm/test': () => Response.json({ ok: false, problem: 'InvalidKey' }) } });
    await apiKey();

    await fireEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText('The provider did not accept this key.')).toBeInTheDocument();
    expect(screen.queryByText('Connected. The key and model work.')).not.toBeInTheDocument();
  });

  it('says to type the id when the provider offers no list', async () => {
    renderPage(ownKey(), { routes: { 'POST /v1/organization/verification-settings/llm/models': () => Response.json({ listed: false, models: [] }) } });
    await apiKey();

    await fireEvent.click(await screen.findByRole('button', { name: 'Load models' }));

    expect(await screen.findByText('This provider does not list its models. Type the model id.')).toBeInTheDocument();
  });

  it("loads the provider's models into the model field, which still takes typing", async () => {
    renderPage(ownKey(), {
      routes: { 'POST /v1/organization/verification-settings/llm/models': () => Response.json({ listed: true, models: ['model-a', 'model-b'] }) },
    });
    await apiKey();

    await fireEvent.click(await screen.findByRole('button', { name: 'Load models' }));

    await waitFor(() => expect(document.querySelectorAll('#llm-model-options option')).toHaveLength(2));
    expect(screen.getByText(/2 models from your provider/)).toBeInTheDocument();
    await type(await model(), 'something-typed');
    expect((await model()).value).toBe('something-typed');
  });
});
