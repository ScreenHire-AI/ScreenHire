import type { OrganizationVerificationSettings } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Nobody has changed the defaults yet (T6).
const lastUpdated = { at: null, by: 'ScreenHire' as const };

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/verification') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const defaults: OrganizationVerificationSettings = {
  llmConsistencyCheck: false,
  monthlyTokenBudget: null,
  tokensUsedThisMonth: 0,
  lastUpdated,
  modelSource: 'Included',
  provider: null,
  baseUrl: null,
  model: null,
  apiKeySaved: false,
  budgetApplies: true,
  effectiveMonthlyTokenBudget: 2_000_000,
  checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: true },
  defaultAiUse: 'AllowedWithDisclosure',
  deviceCheck: null,
  trustedDomains: [],
  trustedNetworks: [],
  alerts: { suggestedEmail: false, suggestedSlack: false, budgetEmail: false, budgetSlack: false },
  emailAvailable: true,
  slackConnected: true,
};
const settingsWith = (overrides: Partial<OrganizationVerificationSettings> = {}): OrganizationVerificationSettings => ({ ...defaults, ...overrides });
const ownKey = (overrides: Partial<OrganizationVerificationSettings> = {}) =>
  settingsWith({ modelSource: 'OwnKey', provider: 'Anthropic', model: 'claude-test', apiKeySaved: true, budgetApplies: false, ...overrides });

type Body = Record<string, unknown>;

function renderPage(settings: OrganizationVerificationSettings, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: Body[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/verification-settings': () => Response.json(settings),
        'PUT /v1/organization/verification-settings': (init) => {
          bodies.push(JSON.parse(String(init?.body)) as Body);
          return Response.json(settings);
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

const check = () => screen.findByRole('switch', { name: /Compare each application's sources/ });
const budget = () => screen.findByLabelText(/Monthly token budget/) as Promise<HTMLInputElement>;
const save = () => screen.findByRole('button', { name: 'Save' });
const apiKey = () => screen.findByLabelText('API key') as Promise<HTMLInputElement>;
const model = () => screen.findByLabelText('Model') as Promise<HTMLInputElement>;
const type = async (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value } });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('verification settings page', () => {
  it('shows the four panels in order', async () => {
    renderPage(settingsWith());

    await check();
    const headings = screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim());
    expect(headings.filter((name) => ['AI model', 'Checks', 'Policy', 'Alerts'].includes(name ?? ''))).toEqual(['AI model', 'Checks', 'Policy', 'Alerts']);
    await expectNoAxeViolations();
  });

  it('loads the switch and the budget', async () => {
    renderPage(settingsWith({ llmConsistencyCheck: true, monthlyTokenBudget: 250000, tokensUsedThisMonth: 420 }));

    expect(await check()).toBeChecked();
    expect((await budget()).value).toBe('250000');
  });

  it('loads a switch that is off and an empty budget, and names the default', async () => {
    renderPage(settingsWith());

    expect(await check()).not.toBeChecked();
    expect((await budget()).value).toBe('');
    expect(screen.getByText(/ScreenHire's default of 2,000,000 tokens/)).toBeInTheDocument();
  });

  it('shows the tokens used this month', async () => {
    renderPage(settingsWith({ tokensUsedThisMonth: 420 }));

    expect(await screen.findByText('Tokens used this month')).toBeInTheDocument();
    expect(screen.getByText('420')).toBeInTheDocument();
  });

  it('saves the switch and the budget as a number, with the key empty', async () => {
    const bodies = renderPage(settingsWith());
    await fireEvent.click(await check());
    await type(await budget(), '120000');

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ llmConsistencyCheck: true, monthlyTokenBudget: 120000, modelSource: 'Included', provider: null, model: null, apiKey: null });
    expect(await screen.findByText('Saved. It applies from the next application.')).toBeInTheDocument();
  });

  it('saves an empty budget as null', async () => {
    const bodies = renderPage(settingsWith({ monthlyTokenBudget: 250000 }));
    await type(await budget(), '');

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ monthlyTokenBudget: null });
  });

  it('refuses a budget that is not a whole number, and sends nothing', async () => {
    const bodies = renderPage(settingsWith());
    await type(await budget(), '12.5k');

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter a whole number of tokens, or leave it empty.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('says why when the API refuses the settings', async () => {
    renderPage(settingsWith(), { 'PUT /v1/organization/verification-settings': () => Response.json({ detail: 'Only owners and admins can change this.' }, { status: 403 }) });

    await fireEvent.click(await save());

    expect(await screen.findByText('Only owners and admins can change this.')).toBeInTheDocument();
  });

  describe('AI model', () => {
    it('offers the own-key fields only when the own key is chosen, and then drops the budget', async () => {
      renderPage(settingsWith());
      expect(screen.queryByLabelText('API key')).not.toBeInTheDocument();
      expect(await budget()).toBeInTheDocument();

      await fireEvent.click(await screen.findByRole('radio', { name: 'Your own key' }));

      expect(await apiKey()).toHaveAttribute('type', 'password');
      expect(screen.queryByLabelText(/Monthly token budget/)).not.toBeInTheDocument();
      expect(screen.getByText(/provider handles this data under your own terms/)).toBeInTheDocument();
      expect(screen.getByText(/token budget does not apply/)).toBeInTheDocument();
      expect(screen.getByText('Tokens used this month')).toBeInTheDocument();
    });

    it('never shows a saved key: the box is empty and the hint says one is saved', async () => {
      renderPage(ownKey({ tokensUsedThisMonth: 9000 }));

      const box = await apiKey();
      expect(box.value).toBe('');
      expect(box).toHaveAttribute('autocomplete', 'off');
      expect(box).toHaveAttribute('placeholder', 'Enter a new key to replace it');
      expect(screen.getByText('A key is saved. Leave this empty to keep it.')).toBeInTheDocument();
      expect((await model()).value).toBe('claude-test');
      expect(screen.getByText('9,000')).toBeInTheDocument();
      await expectNoAxeViolations();
    });

    it('keeps a saved key by sending apiKey null', async () => {
      const bodies = renderPage(ownKey());
      await apiKey();

      await fireEvent.click(await save());

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ modelSource: 'OwnKey', provider: 'Anthropic', baseUrl: null, model: 'claude-test', apiKey: null });
    });

    it('sends the typed key once, then empties the box', async () => {
      const bodies = renderPage(ownKey());
      const box = await apiKey();
      await type(box, '  sk-new-key  ');

      await fireEvent.click(await save());

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ apiKey: 'sk-new-key' });
      await waitFor(() => expect(box.value).toBe(''));
    });

    it('needs a key before the first save of an own key, and sends nothing', async () => {
      const bodies = renderPage(settingsWith());
      await fireEvent.click(await screen.findByRole('radio', { name: 'Your own key' }));
      await type(await model(), 'claude-test');

      await fireEvent.click(await save());

      expect(await screen.findByText('Enter the API key.')).toBeInTheDocument();
      expect(bodies).toEqual([]);
    });

    it('needs a new key when the provider changes, so an old key is never sent elsewhere', async () => {
      const bodies = renderPage(ownKey());
      const select = (await screen.findByLabelText('Provider')) as HTMLSelectElement;
      await fireEvent.change(select, { target: { value: 'OpenAiCompatible' } });
      await type(await screen.findByLabelText('Endpoint address'), 'https://example.test/v1');

      await fireEvent.click(await save());

      expect(await screen.findByText('Enter the API key for this provider.')).toBeInTheDocument();
      expect(bodies).toEqual([]);
    });

    it('asks for an https endpoint for an OpenAI-compatible provider, and sends it with the key', async () => {
      const bodies = renderPage(settingsWith());
      await fireEvent.click(await screen.findByRole('radio', { name: 'Your own key' }));
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
        'PUT /v1/organization/verification-settings': () => Response.json({ errors: { apiKey: ['The provider did not accept this key.'] } }, { status: 400 }),
      });
      await type(await apiKey(), 'sk-bad');

      await fireEvent.click(await save());

      expect(await screen.findByText('The provider did not accept this key.')).toBeInTheDocument();
    });

    it('tests the connection and says it works, sending the model and no key when the box is empty', async () => {
      const tests: Body[] = [];
      renderPage(ownKey(), {
        'POST /v1/organization/verification-settings/llm/test': (init) => {
          tests.push(JSON.parse(String(init?.body)) as Body);
          return Response.json({ ok: true, problem: null });
        },
      });
      await apiKey();

      await fireEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

      expect(await screen.findByText('Connected. The key and model work.')).toBeInTheDocument();
      expect(tests).toEqual([{ provider: 'Anthropic', baseUrl: null, apiKey: null, model: 'claude-test' }]);
    });

    it('tests the connection and says what was wrong, in a sentence', async () => {
      renderPage(ownKey(), {
        'POST /v1/organization/verification-settings/llm/test': () => Response.json({ ok: false, problem: 'InvalidKey' }),
      });
      await apiKey();

      await fireEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

      expect(await screen.findByText('The provider did not accept this key.')).toBeInTheDocument();
      expect(screen.queryByText('Connected. The key and model work.')).not.toBeInTheDocument();
    });

    it('loads the provider\'s models into the model field, which still takes typing', async () => {
      renderPage(ownKey(), {
        'POST /v1/organization/verification-settings/llm/models': () => Response.json({ listed: true, models: ['model-a', 'model-b'] }),
      });
      await apiKey();

      await fireEvent.click(await screen.findByRole('button', { name: 'Load models' }));

      await waitFor(() => expect(document.querySelectorAll('#llm-model-options option')).toHaveLength(2));
      expect(screen.getByText(/2 models from your provider/)).toBeInTheDocument();
      await type(await model(), 'something-typed');
      expect((await model()).value).toBe('something-typed');
    });

    it('says to type the id when the provider offers no list', async () => {
      renderPage(ownKey(), {
        'POST /v1/organization/verification-settings/llm/models': () => Response.json({ listed: false, models: [] }),
      });
      await apiKey();

      await fireEvent.click(await screen.findByRole('button', { name: 'Load models' }));

      expect(await screen.findByText('This provider does not list its models. Type the model id.')).toBeInTheDocument();
    });
  });

  describe('Checks', () => {
    it('loads the six groups and saves one turned off', async () => {
      const bodies = renderPage(settingsWith({ checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: false } }));

      for (const name of ['Network', 'Contact details', 'Documents', 'Answers', 'Duplicates']) {
        expect(await screen.findByRole('switch', { name })).toBeChecked();
      }
      expect(screen.getByRole('switch', { name: 'Form use' })).not.toBeChecked();
      await fireEvent.click(screen.getByRole('switch', { name: 'Documents' }));

      await fireEvent.click(await save());

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ checks: { network: true, contact: true, documents: false, answers: true, duplicates: true, formUse: false } });
    });
  });

  describe('Policy', () => {
    it('saves the default AI use and the device check, with by region as null', async () => {
      const bodies = renderPage(settingsWith());
      await fireEvent.change(await screen.findByLabelText('Default AI use for new jobs'), { target: { value: 'NotAllowed' } });
      const device = (await screen.findByLabelText('Device check')) as HTMLSelectElement;
      expect(device.value).toBe('ByRegion');
      expect(within(device).getByRole('option', { name: 'By region (default)' })).toBeInTheDocument();
      expect(screen.getByText(/Region rules still apply/)).toBeInTheDocument();
      await fireEvent.change(device, { target: { value: 'Consent' } });

      await fireEvent.click(await save());
      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ defaultAiUse: 'NotAllowed', deviceCheck: 'Consent' });

      await fireEvent.change(device, { target: { value: 'ByRegion' } });
      await fireEvent.click(await save());
      await waitFor(() => expect(bodies).toHaveLength(2));
      expect(bodies[1]).toMatchObject({ deviceCheck: null });
    });

    it('adds a trusted domain lower-cased, refuses one that is not a domain, and removes one', async () => {
      const bodies = renderPage(settingsWith({ trustedDomains: ['old.example.com'] }));
      const input = (await screen.findByLabelText('Trusted email domains', { selector: 'input' })) as HTMLInputElement;
      expect(screen.getByRole('button', { name: 'Remove old.example.com' })).toBeInTheDocument();

      await type(input, 'localhost');
      await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));
      expect(await screen.findByText('Enter a domain such as partner.example.com.')).toBeInTheDocument();

      await type(input, ' Partner.Example.COM ');
      await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));
      expect(await screen.findByRole('button', { name: 'Remove partner.example.com' })).toBeInTheDocument();
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
      expect(await screen.findByText('Enter a network such as 203.0.113.0/24.')).toBeInTheDocument();

      await type(input, '203.0.113.0/24');
      await fireEvent.click(screen.getByRole('button', { name: 'Add network' }));
      expect(await screen.findByRole('button', { name: 'Remove 203.0.113.0/24' })).toBeInTheDocument();

      await fireEvent.click(await save());

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ trustedNetworks: ['203.0.113.0/24'] });
    });

    it("shows the server's complaint about a trusted entry under its list", async () => {
      renderPage(settingsWith({ trustedNetworks: ['203.0.113.0/24'] }), {
        'PUT /v1/organization/verification-settings': () => Response.json({ errors: { trustedNetworks: ['That network is too wide. Use /16 or narrower.'] } }, { status: 400 }),
      });

      await fireEvent.click(await save());

      expect(await screen.findByText('That network is too wide. Use /16 or narrower.')).toBeInTheDocument();
    });
  });

  describe('Alerts', () => {
    it('saves the four options', async () => {
      const bodies = renderPage(settingsWith());
      const group = async (name: string) => within(await screen.findByRole('group', { name }));
      await fireEvent.click((await group('Verification suggested')).getByRole('checkbox', { name: 'By email' }));
      await fireEvent.click((await group('Budget at 80%')).getByRole('checkbox', { name: 'On Slack, as a direct message' }));

      await fireEvent.click(await save());

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ alerts: { suggestedEmail: true, suggestedSlack: false, budgetEmail: false, budgetSlack: true } });
    });

    it('hides email it cannot send, disables Slack until it is connected, and links to Slack', async () => {
      renderPage(settingsWith({ emailAvailable: false, slackConnected: false }));

      const suggested = within(await screen.findByRole('group', { name: 'Verification suggested' }));
      expect(suggested.queryByRole('checkbox', { name: 'By email' })).not.toBeInTheDocument();
      expect(suggested.getByRole('checkbox', { name: 'On Slack, as a direct message' })).toBeDisabled();
      expect(screen.getByRole('link', { name: /Connect it in Settings/ })).toHaveAttribute('href', '/settings/slack');
      await expectNoAxeViolations();
    });
  });
});
