import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user, userAs } from '../../../../test-support/fixtures.js';
import { settingsWith, stubSettings } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/policy') }, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

function renderPage(settings: Parameters<typeof stubSettings>[0], routes: Parameters<typeof stubSettings>[1] = {}, signedIn = user) {
  const bodies = stubSettings(settings, routes);
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
  return bodies;
}

const save = () => screen.findByRole('button', { name: 'Save' });
const type = async (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value } });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Policy tab', () => {
  it('saves the default AI use and the device check, with by region as null', async () => {
    const bodies = renderPage(settingsWith());
    await fireEvent.change(await screen.findByLabelText('AI use while applying'), { target: { value: 'NotAllowed' } });
    const device = (await screen.findByLabelText('Device check')) as HTMLSelectElement;
    expect(device.value).toBe('ByRegion');
    expect(within(device).getByRole('option', { name: 'By region (default)' })).toBeInTheDocument();
    expect(screen.getByText(/Where the law needs consent/)).toBeInTheDocument();
    await fireEvent.change(device, { target: { value: 'Consent' } });

    await fireEvent.click(await save());
    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ defaultAiUse: 'NotAllowed', deviceCheck: 'Consent' });

    await fireEvent.change(device, { target: { value: 'ByRegion' } });
    await fireEvent.click(await save());
    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[1]).toMatchObject({ deviceCheck: null });
  });

  it('has no Signal Network control, which stays off until legal review', async () => {
    renderPage(settingsWith());
    await save();

    expect(screen.queryByText(/signal network/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('saves only the policy, keeping the checks and the model as they were saved', async () => {
    const bodies = renderPage(settingsWith({ llmConsistencyCheck: true, checks: { network: false, contact: true, documents: true, answers: true, duplicates: true, formUse: true } }));
    await fireEvent.change(await screen.findByLabelText('AI use while applying'), { target: { value: 'Allowed' } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ llmConsistencyCheck: true, modelSource: 'Included', apiKey: null, checks: { network: false }, defaultAiUse: 'Allowed' });
  });

  it('adds a trusted domain lower-cased, refuses one that is not a domain, and removes one', async () => {
    const bodies = renderPage(settingsWith({ trustedDomains: ['old.example.com'] }));
    const input = (await screen.findByLabelText('Trusted email domains', { selector: 'input' })) as HTMLInputElement;
    expect(screen.getByRole('button', { name: 'Remove old.example.com' })).toBeInTheDocument();
    expect(screen.getByText('1 of 50 domains')).toBeInTheDocument();

    await type(input, 'localhost');
    await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));
    expect(await screen.findByText('Enter a domain such as partner.example.com.')).toBeInTheDocument();

    await type(input, ' Partner.Example.COM ');
    await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));
    expect(await screen.findByRole('button', { name: 'Remove partner.example.com' })).toBeInTheDocument();
    expect(screen.getByText('2 of 50 domains')).toBeInTheDocument();
    expect(screen.queryByText('Enter a domain such as partner.example.com.')).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove old.example.com' }));

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ trustedDomains: ['partner.example.com'] });
  });

  it('adds a trusted network and refuses one that is too wide or not a network', async () => {
    const bodies = renderPage(settingsWith());
    const input = (await screen.findByLabelText('Trusted networks', { selector: 'input' })) as HTMLInputElement;

    await type(input, '10.0.0.0/8');
    await fireEvent.click(screen.getByRole('button', { name: 'Add network' }));
    expect(await screen.findByText('That network is too wide. Use /16 or narrower.')).toBeInTheDocument();

    await type(input, 'office');
    await fireEvent.click(screen.getByRole('button', { name: 'Add network' }));
    expect(await screen.findByText('Enter an address such as 203.0.113.7, or a range such as 203.0.113.0/24.')).toBeInTheDocument();

    // An office usually has one address, so a plain address is enough: no range to work out (W8).
    await type(input, '203.0.113.7');
    await fireEvent.click(screen.getByRole('button', { name: 'Add network' }));
    expect(await screen.findByRole('button', { name: 'Remove 203.0.113.7' })).toBeInTheDocument();

    await type(input, '203.0.113.0/24');
    await fireEvent.click(screen.getByRole('button', { name: 'Add network' }));
    expect(await screen.findByRole('button', { name: 'Remove 203.0.113.0/24' })).toBeInTheDocument();

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ trustedNetworks: ['203.0.113.7', '203.0.113.0/24'] });
  });

  it("shows the server's complaint about a trusted entry under its list", async () => {
    renderPage(settingsWith({ trustedNetworks: ['203.0.113.0/24'] }), {
      'PUT /v1/organization/verification-settings': () => Response.json({ errors: { trustedNetworks: ['That network is too wide. Use /16 or narrower.'] } }, { status: 400 }),
    });

    await fireEvent.click(await save());

    expect(await screen.findByText('That network is too wide. Use /16 or narrower.')).toBeInTheDocument();
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('reads the policy as facts, with no field, no Save and nothing disabled', async () => {
      renderPage(settingsWith({ defaultAiUse: 'NotAllowed', deviceCheck: 'Consent', trustedDomains: ['partner.example.com'] }), {}, userAs(role));

      expect(await screen.findByText(/Not allowed/)).toBeInTheDocument();
      expect(screen.getByText('Ask for consent')).toBeInTheDocument();
      expect(screen.getByRole('list', { name: 'Trusted email domains' })).toHaveTextContent('partner.example.com');
      expect(screen.getByText('None')).toBeInTheDocument();
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
      expect(document.querySelectorAll('[disabled]')).toHaveLength(0);
      await expectNoAxeViolations();
    });
  });
});
