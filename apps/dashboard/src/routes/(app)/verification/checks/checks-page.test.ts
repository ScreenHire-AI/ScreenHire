import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user, userAs } from '../../../../test-support/fixtures.js';
import { ownKey, settingsWith, stubSettings } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/checks') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

function renderPage(settings: Parameters<typeof stubSettings>[0], signedIn = user) {
  const bodies = stubSettings(settings);
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
  return bodies;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Checks tab', () => {
  it('loads the six groups, each with a line on what it looks at, and saves one turned off', async () => {
    const bodies = renderPage(settingsWith({ checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: false } }));

    for (const name of ['Network', 'Contact details', 'Documents', 'Answers', 'Duplicates']) {
      expect(await screen.findByRole('switch', { name })).toBeChecked();
    }
    expect(screen.getByRole('switch', { name: 'Form use' })).not.toBeChecked();
    expect(screen.getByText('Whether the email address and phone number can be real.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('switch', { name: 'Documents' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ checks: { network: true, contact: true, documents: false, answers: true, duplicates: true, formUse: false } });
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('saves only the groups, keeping an own key, the policy and the alerts as they were saved', async () => {
    const bodies = renderPage(
      ownKey({ defaultAiUse: 'NotAllowed', deviceCheck: 'Consent', trustedNetworks: ['203.0.113.0/24'], alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: true } }),
    );
    await fireEvent.click(await screen.findByRole('switch', { name: 'Network' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      modelSource: 'OwnKey',
      provider: 'Anthropic',
      model: 'claude-test',
      apiKey: null,
      defaultAiUse: 'NotAllowed',
      deviceCheck: 'Consent',
      trustedNetworks: ['203.0.113.0/24'],
      alerts: { suggestedEmail: true, budgetSlack: true },
      checks: { network: false },
    });
  });

  it('says why when the API refuses the save', async () => {
    stubSettings(settingsWith(), { 'PUT /v1/organization/verification-settings': () => Response.json({ detail: 'The saved key can no longer be read.' }, { status: 400 }) });
    render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });

    await fireEvent.click(await screen.findByRole('button', { name: 'Save' }));

    expect(await screen.findByText(/Could not save|saved key/)).toBeInTheDocument();
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('shows which groups are on and off as text, with no switch, no Save and nothing disabled', async () => {
      renderPage(settingsWith({ checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: false } }), userAs(role));

      const list = await screen.findByRole('list', { name: 'Check groups' });
      const rows = within(list).getAllByRole('listitem');
      expect(rows).toHaveLength(6);
      expect(within(rows[0]).getByText('Network')).toBeInTheDocument();
      expect(within(rows[0]).getByText('On')).toBeInTheDocument();
      expect(within(rows[5]).getByText('Form use')).toBeInTheDocument();
      expect(within(rows[5]).getByText('Off')).toBeInTheDocument();
      expect(screen.queryByRole('switch')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
      expect(document.querySelectorAll('[disabled]')).toHaveLength(0);
      await expectNoAxeViolations();
    });
  });

  it('shows an error with Try again when the settings do not load', async () => {
    stubSettings(settingsWith(), { 'GET /v1/organization/verification-settings': () => Response.json({ detail: 'down' }, { status: 500 }) });
    render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
