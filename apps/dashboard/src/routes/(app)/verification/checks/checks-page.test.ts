import type { VerificationOverviewView } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user, userAs } from '../../../../test-support/fixtures.js';
import { ownKey, settingsWith, stubSettings } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';
// After the fixtures, which the `$app/state` mock below reads when this component's imports load it.
import ChecksSettings from '#lib/components/verification-area/checks-settings.svelte';

const location = vi.hoisted(() => ({ url: 'http://dashboard.test/verification/checks' }));
vi.mock('$app/state', () => ({
  page: {
    data: { user },
    get url() {
      return new URL(location.url);
    },
  },
  navigating: { to: null },
}));
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
// The button that opens a group's row, named by the group's name and its one-line summary.
const rowButton = (group: string) => within(section(group)).getByRole('button', { expanded: false });
const openRow = async (group: string) => fireEvent.click(rowButton(group));

afterEach(() => {
  vi.unstubAllGlobals();
  location.url = 'http://dashboard.test/verification/checks';
});

describe('the Checks tab', () => {
  it("gives each of the six groups one closed row: its switch labelled by its name, a summary and this month's count", async () => {
    renderPage(settingsWith({ checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: false } }));

    for (const name of labels.slice(0, 5)) {
      expect(await screen.findByRole('switch', { name })).toBeChecked();
    }
    expect(screen.getByRole('switch', { name: 'Form activity' })).not.toBeChecked();
    // Each switch sits in its own group's row, outside the button that opens the row.
    for (const [index, group] of groupOrder.entries()) {
      expect(within(section(group)).getByRole('switch', { name: labels[index] })).toBeInTheDocument();
      expect(rowButton(group)).toHaveAttribute('aria-expanded', 'false');
    }
    expect(rowButton('Contact')).toHaveTextContent('Whether the email address and phone number can be real');
    expect(section('Contact')).not.toHaveTextContent('For example:');
    // A count of applications, never a score or a share. A group that is off shows none: "Nothing found" would read as clean.
    await waitFor(() => expect(section('Network')).toHaveTextContent('Found in 4 of 14 applications this month'));
    expect(section('Documents')).toHaveTextContent('Nothing found this month');
    expect(section('FormUse')).not.toHaveTextContent(/found/i);
    expect(document.body).not.toHaveTextContent(/%|score/i);
    // What an Off group means is in the info button beside the panel title, not a line over the rows.
    expect(screen.getByRole('button', { name: 'About the checks' })).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent('records that it did not run');
    await expectNoAxeViolations();
  });

  it('opens a row in place to what the group looks at, its example and what off costs, and Expand all opens every row', async () => {
    renderPage(settingsWith());
    await screen.findByRole('switch', { name: 'Contact details' });

    await openRow('Contact');

    expect(within(section('Contact')).getByRole('button', { expanded: true })).toBeInTheDocument();
    expect(section('Contact')).toHaveTextContent('a throwaway email service');
    expect(section('Contact')).toHaveTextContent('For example:');
    expect(section('Contact')).toHaveTextContent('Off:');
    expect(section('Network')).not.toHaveTextContent('For example:');

    await fireEvent.click(screen.getByRole('button', { name: 'Expand all' }));
    for (const group of groupOrder) expect(section(group)).toHaveTextContent('For example:');
    expect(document.body).not.toHaveTextContent(/%|score/i);
    await expectNoAxeViolations();
    await fireEvent.click(screen.getByRole('button', { name: 'Collapse all' }));
    await waitFor(() => expect(document.body).not.toHaveTextContent('For example:'));
  });

  it('opens the Answers row for a link to it', async () => {
    location.url = 'http://dashboard.test/verification/checks#answers';
    renderPage(settingsWith());

    const policy = await screen.findByRole('combobox', { name: 'AI use while applying' });
    expect(section('Answers')).toContainElement(policy);
    expect(within(section('Network')).queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('puts each setting in the group it tunes', async () => {
    renderPage(settingsWith());

    await screen.findByRole('switch', { name: 'Answers' });
    for (const group of ['Answers', 'Network', 'Duplicates', 'Contact']) await openRow(group);
    // A group with nothing to tune opens to its words only.
    expect(within(section('Contact')).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(section('Contact')).queryByRole('combobox')).not.toBeInTheDocument();
    const answers = section('Answers');
    expect(within(answers).getByRole('combobox', { name: 'AI use while applying' })).toHaveDisplayValue('Allowed with disclosure (default)');
    expect(within(answers).getByRole('switch', { name: "Compare each application's sources" })).toBeInTheDocument();
    expect(within(section('Network')).getByRole('textbox', { name: 'Trusted networks' })).toBeInTheDocument();
    const duplicates = section('Duplicates');
    expect(within(duplicates).getByRole('textbox', { name: 'Trusted email domains' })).toBeInTheDocument();
    expect(within(duplicates).getByRole('combobox', { name: 'Device check' })).toBeInTheDocument();
    expect(duplicates).toHaveTextContent('It also affects Form activity');
    // The alerts are Settings > Notification defaults' and each person's own, never a feature's tab.
    expect(screen.queryByRole('table', { name: 'Verification alerts' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Notifications' })).not.toBeInTheDocument();
  });

  it('saves a group turned off, the policy and the consistency check in one Save, keeping the alerts and the key as saved', async () => {
    const bodies = renderPage(
      ownKey({ trustedNetworks: ['203.0.113.0/24'], alerts: { suggestedEmail: false, suggestedSlack: false, budgetEmail: false, budgetSlack: true } }),
    );
    await fireEvent.click(await screen.findByRole('switch', { name: 'Documents' }));
    await openRow('Answers');
    await fireEvent.change(screen.getByRole('combobox', { name: 'AI use while applying' }), { target: { value: 'NotAllowed' } });
    await fireEvent.click(screen.getByRole('switch', { name: "Compare each application's sources" }));

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      checks: { network: true, documents: false, formUse: true },
      defaultAiUse: 'NotAllowed',
      llmConsistencyCheck: true,
      trustedNetworks: ['203.0.113.0/24'],
      alerts: { suggestedEmail: false, suggestedSlack: false, budgetEmail: false, budgetSlack: true },
      modelSource: 'OwnKey',
      model: 'claude-test',
      apiKey: null,
    });
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
  });

  it('sends every setting on Save with every row closed, the same body as before', async () => {
    const bodies = renderPage(
      settingsWith({ trustedNetworks: ['203.0.113.7'], trustedDomains: ['partner.example.com'], deviceCheck: 'Consent', defaultAiUse: 'Allowed', llmConsistencyCheck: true }),
    );
    await screen.findByRole('switch', { name: 'Network' });

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: true },
      defaultAiUse: 'Allowed',
      llmConsistencyCheck: true,
      deviceCheck: 'Consent',
      trustedDomains: ['partner.example.com'],
      trustedNetworks: ['203.0.113.7'],
    });
  });

  it('opens the row of a setting the API refuses', async () => {
    stubSettings(settingsWith({ trustedNetworks: ['203.0.113.7'] }), {
      'GET /v1/verification/overview': () => Response.json(overview()),
      'PUT /v1/organization/verification-settings': () => Response.json({ errors: { trustedNetworks: ['203.0.113.7 is not allowed.'] } }, { status: 400 }),
    });
    render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });
    await screen.findByRole('switch', { name: 'Network' });

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await within(section('Network')).findByText('203.0.113.7 is not allowed.')).toBeInTheDocument();
  });

  it('opens the row of another refused setting and says why under Save', async () => {
    stubSettings(settingsWith(), {
      'GET /v1/verification/overview': () => Response.json(overview()),
      'PUT /v1/organization/verification-settings': () => Response.json({ errors: { defaultAiUse: ['Choose a policy this plan offers.'] } }, { status: 400 }),
    });
    render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });
    await screen.findByRole('switch', { name: 'Answers' });

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Choose a policy this plan offers.')).toBeInTheDocument();
    expect(within(section('Answers')).getByRole('combobox', { name: 'AI use while applying' })).toBeInTheDocument();
    expect(screen.queryByText('Saved. It applies from the next application.')).not.toBeInTheDocument();
  });

  it('opens the row a link names while the tab is already showing', async () => {
    stubSettings(settingsWith());
    const props = { detail: settingsWith(), canEdit: true, found: null };
    const view = render(WithQueryClient, { props: { component: ChecksSettings, props: { ...props, opened: '' } } });
    expect(within(section('Network')).queryByRole('textbox')).not.toBeInTheDocument();

    await view.rerender({ props: { component: ChecksSettings, props: { ...props, opened: 'network' } } });

    expect(await within(section('Network')).findByRole('textbox', { name: 'Trusted networks' })).toBeInTheDocument();
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
      await fireEvent.click(screen.getByRole('button', { name: 'Expand all' }));
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
