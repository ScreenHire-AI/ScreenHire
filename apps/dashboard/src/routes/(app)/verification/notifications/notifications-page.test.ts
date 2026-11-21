import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user, userAs } from '../../../../test-support/fixtures.js';
import { dollarLimits, limits, settingsWith, stubSettings } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/notifications') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

function renderPage(settings: Parameters<typeof stubSettings>[0], routes: Parameters<typeof stubSettings>[1] = {}, signedIn = user) {
  const bodies = stubSettings(settings, routes);
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
  return bodies;
}

const save = () => screen.findByRole('button', { name: 'Save' });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Notifications tab', () => {
  it('saves the four options and nothing else', async () => {
    const bodies = renderPage(settingsWith({ defaultAiUse: 'Allowed' }));
    const group = async (name: string) => within(await screen.findByRole('group', { name }));
    await fireEvent.click((await group('Verification suggested')).getByRole('checkbox', { name: 'By email' }));
    await fireEvent.click((await group('AI usage')).getByRole('checkbox', { name: 'On Slack, as a direct message' }));

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ defaultAiUse: 'Allowed', alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: true } });
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
  });

  it('hides email it cannot send and Slack until it is connected, with no nudge to connect it', async () => {
    renderPage(settingsWith({ emailAvailable: false, slackConnected: false }));

    const suggested = within(await screen.findByRole('group', { name: 'Verification suggested' }));
    expect(suggested.queryByRole('checkbox', { name: 'By email' })).not.toBeInTheDocument();
    expect(suggested.queryByRole('checkbox', { name: 'On Slack, as a direct message' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Slack is not connected/)).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Connect it in Settings/ })).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('shows no limits when the server sent none, which it does for who cannot see AI usage', async () => {
    renderPage(settingsWith());
    await save();

    expect(screen.queryByRole('heading', { name: 'Monthly limits' })).not.toBeInTheDocument();
  });

  it('gives an Owner the form for the budget and cap, with the thresholds as text', async () => {
    renderPage(settingsWith({ limits: dollarLimits }));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getByRole('form', { name: 'Monthly AI limits' })).toBeInTheDocument();
    expect(screen.getByText(/warns you at 50%, 80% and 100%/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Monthly budget/)).toBeInTheDocument();
  });

  it('shows an Admin the budget and cap as facts, since only an Owner sets them', async () => {
    renderPage(settingsWith({ limits }), {}, userAs('Admin'));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getByText(/An owner sets the budget and cap/)).toBeInTheDocument();
    expect(screen.getAllByText('420 tokens of 2,000,000 tokens')).toHaveLength(2);
    expect(screen.queryByRole('form', { name: 'Monthly AI limits' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Monthly budget/)).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('asks for limits in dollars only, starts empty on the default and names it', async () => {
    renderPage(settingsWith({ limits: dollarLimits }));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Count in')).not.toBeInTheDocument();
    expect(screen.getByText(/In US dollars\./)).toBeInTheDocument();
    expect(screen.getByLabelText(/Monthly budget/)).toHaveValue('');
    expect(screen.getByText(/default budget, \$3\.84 a month, for its own key/)).toBeInTheDocument();
    expect(screen.getByText(/default cap, \$3\.84 a month, for its own key/)).toBeInTheDocument();
  });

  it('starts from an Owner\'s own limit in dollars, one stored in tokens shown as the API converted it, so saving keeps it', async () => {
    // The cap was set in tokens before limits were in dollars; the API converts it at the key's price.
    renderPage(settingsWith({ limits: { ...dollarLimits, unit: 'Tokens', cap: 4_000_000, effectiveCap: { amount: 7.68, unit: 'Money', isDefault: false, used: 0.0683, percent: 0 } } }));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Monthly cap/)).toHaveValue('7.68');
    expect(screen.getByLabelText(/Monthly budget/)).toHaveValue('');
  });

  it('sends the limits in dollars with no unit', async () => {
    const sent: unknown[] = [];
    renderPage(settingsWith({ limits: dollarLimits }), {
      'PUT /v1/organization/ai-usage/limits': (init) => {
        sent.push(JSON.parse(String(init?.body)));
        return Response.json(dollarLimits);
      },
    });

    await fireEvent.input(await screen.findByLabelText(/Monthly budget/), { target: { value: '25.50' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save limits' }));

    await waitFor(() => expect(sent).toEqual([{ budget: 25.5, cap: null }]));
  });

  it('lets an Owner remove a limit set before the model lost its price, after asking', async () => {
    const sent: unknown[] = [];
    renderPage(settingsWith({ limits: { ...limits, cap: 5, effectiveCap: { amount: 5, unit: 'Money', isDefault: false, used: 0, percent: 0 } } }), {
      'PUT /v1/organization/ai-usage/limits': (init) => {
        sent.push(JSON.parse(String(init?.body)));
        return Response.json(limits);
      },
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Remove limits' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove your budget and cap?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove limits' }));

    await waitFor(() => expect(sent).toEqual([{ budget: null, cap: null }]));
  });

  it('offers no form where the AI model has no price, and says why', async () => {
    renderPage(settingsWith({ limits }));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Monthly AI limits' })).not.toBeInTheDocument();
    expect(screen.getByText(/Limits count dollars, and the AI model in use has no price yet\./)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove limits' })).not.toBeInTheDocument();
  });

  it('shows an Admin the default budget and cap in dollars where the deployment prices its model', async () => {
    renderPage(settingsWith({ limits: dollarLimits }), {}, userAs('Admin'));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getAllByText('$0.07 of $3.84')).toHaveLength(2);
    expect(screen.queryByText(/estimate/)).not.toBeInTheDocument();
    expect(screen.queryByText(/tokens of/)).not.toBeInTheDocument();
  });
});
