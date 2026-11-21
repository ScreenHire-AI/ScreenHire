import type { VerificationOverviewView } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user, userAs } from '../../../../test-support/fixtures.js';
import { ownKey, settingsWith, stubSettings } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/checks') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const groupOrder = ['Network', 'Contact', 'Documents', 'Answers', 'Duplicates', 'FormUse'] as const;
const labels = ['Network', 'Contact details', 'Documents', 'Answers', 'Duplicates', 'Form activity'];

const overview = (counts: Partial<Record<(typeof groupOrder)[number], number>> = {}, checked = 14): VerificationOverviewView => ({
  from: '2026-11-01T00:00:00Z',
  to: '2026-11-21T12:00:00Z',
  checked,
  pending: 0,
  levels: { clear: checked, review: 0, verify: 0 },
  groups: groupOrder.map((group, index) => ({ group, label: labels[index]!, applications: counts[group] ?? 0, on: true })),
  lastCheckedAt: '2026-11-21T11:00:00Z',
});

function renderPage(settings: Parameters<typeof stubSettings>[0], signedIn = user, month = overview({ Network: 4 })) {
  const bodies = stubSettings(settings, { 'GET /v1/verification/overview': () => Response.json(month) });
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
  return bodies;
}

const section = (group: string) => document.querySelector<HTMLElement>(`[data-check-group="${group}"]`)!;

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Checks tab', () => {
  it("gives each of the six groups its switch, what it looks at, an example, what off costs and this month's count", async () => {
    renderPage(settingsWith({ checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: false } }));

    for (const name of labels.slice(0, 5)) {
      expect(await screen.findByRole('switch', { name })).toBeChecked();
    }
    expect(screen.getByRole('switch', { name: 'Form activity' })).not.toBeChecked();
    expect(section('Contact')).toHaveTextContent('Whether the email address and phone number can be real');
    expect(section('Contact')).toHaveTextContent('For example:');
    expect(section('Contact')).toHaveTextContent('Off:');
    // A count of applications, never a score or a share.
    await waitFor(() => expect(section('Network')).toHaveTextContent('Found something in 4 of 14 applications this month'));
    expect(section('Documents')).toHaveTextContent('Found something in 0 of 14 applications this month');
    expect(document.body).not.toHaveTextContent(/%|score/i);
    await expectNoAxeViolations();
  });

  it('puts each setting in the group it tunes', async () => {
    renderPage(settingsWith());

    await screen.findByRole('switch', { name: 'Answers' });
    const answers = section('Answers');
    expect(within(answers).getByRole('combobox', { name: 'AI use while applying' })).toHaveDisplayValue('Allowed with disclosure (default)');
    expect(within(answers).getByRole('switch', { name: "Compare each application's sources" })).toBeInTheDocument();
    expect(within(section('Network')).getByRole('textbox', { name: 'Trusted networks' })).toBeInTheDocument();
    const duplicates = section('Duplicates');
    expect(within(duplicates).getByRole('textbox', { name: 'Trusted email domains' })).toBeInTheDocument();
    expect(within(duplicates).getByRole('combobox', { name: 'Device check' })).toBeInTheDocument();
    expect(duplicates).toHaveTextContent('It also affects Form activity');
    // The Verification suggested alert sits on this tab, by email and Slack, with the app always ticked.
    const grid = screen.getByRole('table', { name: 'Verification alerts' });
    expect(within(grid).getByRole('checkbox', { name: 'Verification suggested by email' })).toBeInTheDocument();
    expect(within(grid).getByText(/Always\./)).toBeInTheDocument();
  });

  it('saves a group turned off, the policy, the consistency check and the alert in one Save, keeping the AI usage alert and the key', async () => {
    const bodies = renderPage(
      ownKey({ trustedNetworks: ['203.0.113.0/24'], alerts: { suggestedEmail: false, suggestedSlack: false, budgetEmail: false, budgetSlack: true } }),
    );
    await fireEvent.click(await screen.findByRole('switch', { name: 'Documents' }));
    await fireEvent.change(screen.getByRole('combobox', { name: 'AI use while applying' }), { target: { value: 'NotAllowed' } });
    await fireEvent.click(screen.getByRole('switch', { name: "Compare each application's sources" }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Verification suggested by email' }));

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      checks: { network: true, documents: false, formUse: true },
      defaultAiUse: 'NotAllowed',
      llmConsistencyCheck: true,
      trustedNetworks: ['203.0.113.0/24'],
      alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: true },
      modelSource: 'OwnKey',
      model: 'claude-test',
      apiKey: null,
    });
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
  });

  it('shows Slack only once Slack is connected', async () => {
    renderPage(settingsWith({ slackConnected: false }));

    const grid = await screen.findByRole('table', { name: 'Verification alerts' });
    expect(within(grid).queryByRole('columnheader', { name: 'Slack' })).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/slack/i);
  });

  it('says why when the API refuses the save', async () => {
    stubSettings(settingsWith(), {
      'GET /v1/verification/overview': () => Response.json(overview()),
      'PUT /v1/organization/verification-settings': () => Response.json({ detail: 'The saved key can no longer be read.' }, { status: 400 }),
    });
    render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });

    await fireEvent.click(await screen.findByRole('button', { name: 'Save' }));

    expect(await screen.findByText(/Could not save|saved key/)).toBeInTheDocument();
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('shows each group on or off and its settings as text, with no switch, no Save, no alerts and nothing disabled', async () => {
      renderPage(settingsWith({ checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: false }, trustedDomains: ['partner.example.com'] }), userAs(role));

      await waitFor(() => expect(section('Network')).toBeInTheDocument());
      expect(within(section('Network')).getByText('On')).toBeInTheDocument();
      expect(within(section('FormUse')).getByText('Off')).toBeInTheDocument();
      expect(section('Answers')).toHaveTextContent('Allowed with disclosure');
      expect(section('Duplicates')).toHaveTextContent('partner.example.com');
      expect(screen.queryByRole('switch')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
      expect(screen.queryByRole('table', { name: 'Verification alerts' })).not.toBeInTheDocument();
      expect(document.querySelectorAll('[disabled]')).toHaveLength(0);
      await expectNoAxeViolations();
    });
  });

  it('shows an error with Try again when the settings do not load', async () => {
    stubSettings(settingsWith(), {
      'GET /v1/verification/overview': () => Response.json(overview()),
      'GET /v1/organization/verification-settings': () => Response.json({ detail: 'down' }, { status: 500 }),
    });
    render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
