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

  it('hides email it cannot send and Slack until it is connected, and links to Slack', async () => {
    renderPage(settingsWith({ emailAvailable: false, slackConnected: false }));

    const suggested = within(await screen.findByRole('group', { name: 'Verification suggested' }));
    expect(suggested.queryByRole('checkbox', { name: 'By email' })).not.toBeInTheDocument();
    expect(suggested.queryByRole('checkbox', { name: 'On Slack, as a direct message' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Connect it in Settings/ })).toHaveAttribute('href', '/settings/slack');
    await expectNoAxeViolations();
  });

  it('shows no limits when the server sent none, which it does for who cannot see AI usage', async () => {
    renderPage(settingsWith());
    await save();

    expect(screen.queryByRole('heading', { name: 'Monthly limits' })).not.toBeInTheDocument();
  });

  it('gives an Owner the form for the budget and cap, with the thresholds as text', async () => {
    renderPage(settingsWith({ limits }));

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

  it('starts an Owner with no limits of their own in dollars when the default is shown in dollars, and names the default', async () => {
    renderPage(settingsWith({ limits: dollarLimits }));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    // A number typed after reading "$3.20" on the Overview is dollars, not tokens.
    expect(screen.getByLabelText('Count in')).toHaveValue('Money');
    expect(screen.getByText(/default budget, \$3\.20 a month, for its own key/)).toBeInTheDocument();
    expect(screen.getByText(/default cap, \$3\.20 a month, for its own key/)).toBeInTheDocument();
  });

  it('keeps the unit an Owner chose once they have set a limit, though the default beside it shows in dollars', async () => {
    // Their own cap in tokens; the budget they left alone is the default, in dollars, and comes first.
    renderPage(settingsWith({ limits: { ...dollarLimits, cap: 4_000_000, effectiveCap: { amount: 4_000_000, unit: 'Tokens', isDefault: false, used: 420, percent: 0 } } }));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getByLabelText('Count in')).toHaveValue('Tokens');
    expect(screen.getByLabelText(/Monthly cap/)).toHaveValue('4000000');
  });

  it('shows an Admin the default budget and cap in dollars where the deployment prices its model', async () => {
    renderPage(settingsWith({ limits: dollarLimits }), {}, userAs('Admin'));

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getAllByText('$0.0569 of $3.20')).toHaveLength(2);
    expect(screen.getByText(/In dollars it is an estimate/)).toBeInTheDocument();
    expect(screen.queryByText(/tokens of/)).not.toBeInTheDocument();
  });
});
