import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user } from '../../../../test-support/fixtures.js';
import { settingsWith, stubSettings } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Settings > Organization > Notification defaults: the organization's starting choices for the Verification suggested alert
// (once on Verification's Checks tab) and the AI usage alert (once on the AI use tab), in one place.
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/notification-defaults') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const renderPage = () => render(WithQueryClient, { props: { component: Page, props: {} } });
const form = () => screen.findByRole('form', { name: 'Notification defaults' });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the notification defaults', () => {
  it("lists both alerts by email and Slack, with the app always ticked, and points to each person's own choices", async () => {
    stubSettings(settingsWith());
    renderPage();

    const grid = within(await form()).getByRole('table', { name: 'Notification defaults' });
    expect(within(grid).getByRole('checkbox', { name: 'Verification suggested by email' })).toBeInTheDocument();
    expect(within(grid).getByRole('checkbox', { name: 'AI usage by email' })).toBeInTheDocument();
    expect(within(grid).getAllByText(/Always\./)).toHaveLength(2);
    expect(screen.getByRole('link', { name: 'Your account' })).toHaveAttribute('href', '/account/notifications');
    expect(screen.getByRole('heading', { level: 1, name: 'Notification defaults' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('saves both alerts, keeping every other setting as it was saved', async () => {
    const bodies = stubSettings(
      settingsWith({ llmConsistencyCheck: true, defaultAiUse: 'NotAllowed', alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: false } }),
      { 'GET /v1/account/notifications': () => Response.json({}) },
    );
    renderPage();

    const defaults = await form();
    await fireEvent.click(within(defaults).getByRole('checkbox', { name: 'AI usage on Slack, as a direct message' }));
    await fireEvent.click(within(defaults).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      llmConsistencyCheck: true,
      defaultAiUse: 'NotAllowed',
      alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: true },
    });
    expect(await screen.findByText('Saved. People who have not chosen for themselves start from these.')).toBeInTheDocument();
  });

  it('shows Slack only once Slack is connected', async () => {
    stubSettings(settingsWith({ slackConnected: false }));
    renderPage();

    const grid = within(await form()).getByRole('table', { name: 'Notification defaults' });
    expect(within(grid).queryByRole('columnheader', { name: 'Slack' })).not.toBeInTheDocument();
    expect(grid).not.toHaveTextContent(/slack/i);
  });

  it('hides email it cannot send and Slack until it is connected, and then has nothing to save', async () => {
    stubSettings(settingsWith({ emailAvailable: false, slackConnected: false }));
    renderPage();

    const defaults = await form();
    expect(within(defaults).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(defaults).queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  });
});
