import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { user } from '../../../../test-support/fixtures.js';
import { ownKey, settingsWith, stubSettings, type Body } from '../../../../test-support/verification-settings.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/ai-model') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

function renderPage(settings: Parameters<typeof stubSettings>[0], routes: Parameters<typeof stubSettings>[1] = {}) {
  const bodies = stubSettings(settings, routes);
  render(WithQueryClient, { props: { component: Page, props: { data: { user } } } });
  return bodies;
}

const check = () => screen.findByRole('switch', { name: /Compare each application's sources/ });
const save = () => screen.findByRole('button', { name: 'Save' });
const apiKey = () => screen.findByLabelText('API key') as Promise<HTMLInputElement>;
const model = () => screen.findByLabelText('Model') as Promise<HTMLInputElement>;
const type = async (element: HTMLElement, value: string) => fireEvent.input(element, { target: { value } });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the AI model tab', () => {
  it('shows one panel, the model, and none of the other settings', async () => {
    renderPage(settingsWith());

    await check();
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim())).toEqual(['AI model']);
    expect(screen.queryByRole('switch', { name: 'Network' })).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('loads the switch on and off', async () => {
    renderPage(settingsWith({ llmConsistencyCheck: true }));
    expect(await check()).toBeChecked();
  });

  it('loads a switch that is off', async () => {
    renderPage(settingsWith());
    expect(await check()).not.toBeChecked();
  });

  it('saves the switch with the key empty, and keeps every other setting as it was saved', async () => {
    const bodies = renderPage(settingsWith({ defaultAiUse: 'NotAllowed', trustedDomains: ['partner.example.com'], checks: { network: true, contact: true, documents: false, answers: true, duplicates: true, formUse: true } }));
    await fireEvent.click(await check());

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
    renderPage(settingsWith(), { 'PUT /v1/organization/verification-settings': () => Response.json({ detail: 'Only owners and admins can change this.' }, { status: 403 }) });

    await fireEvent.click(await save());

    expect(await screen.findByText('Only owners and admins can change this.')).toBeInTheDocument();
  });

  it('offers the own-key fields only when the own key is chosen', async () => {
    renderPage(settingsWith());
    expect(screen.queryByLabelText('API key')).not.toBeInTheDocument();
    expect(await screen.findByText(/bills you for what the checks use/)).toBeInTheDocument();

    await fireEvent.click(await screen.findByRole('radio', { name: /^Your own key/ }));

    expect(await apiKey()).toHaveAttribute('type', 'password');
    expect(screen.getByText(/provider handles this data under your own terms/)).toBeInTheDocument();
    expect(screen.getByText(/ScreenHire charges nothing extra/)).toBeInTheDocument();
    expect(screen.getByText(/the Activity log lists every call/)).toBeInTheDocument();
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
    expect(screen.queryByLabelText('Endpoint address')).not.toBeInTheDocument();

    await fireEvent.change(provider, { target: { value: 'AzureOpenAi' } });
    expect(await screen.findByLabelText('Endpoint address')).toBeInTheDocument();
  });

  it('sends the own key prices, and the address only for a preset that takes one', async () => {
    const bodies = renderPage(ownKey({ provider: 'OpenRouter', apiKeySaved: false, model: 'a/b' }));
    await type(await apiKey(), 'sk-test');
    await type((await screen.findByLabelText(/Input price/)) as HTMLInputElement, '3');
    await type((await screen.findByLabelText(/Output price/)) as HTMLInputElement, '15.5');

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ provider: 'OpenRouter', baseUrl: null, ownKeyInputPrice: 3, ownKeyOutputPrice: 15.5 });
  });

  it('never shows a saved key: the box is empty and the hint says one is saved', async () => {
    renderPage(ownKey());

    const box = await apiKey();
    expect(box.value).toBe('');
    expect(box).toHaveAttribute('autocomplete', 'off');
    expect(box).toHaveAttribute('placeholder', 'Enter a new key to replace it');
    expect(screen.getByText('A key is saved. Leave this empty to keep it.')).toBeInTheDocument();
    expect((await model()).value).toBe('claude-test');
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
    await fireEvent.click(await screen.findByRole('radio', { name: /^Your own key/ }));
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

  it("loads the provider's models into the model field, which still takes typing", async () => {
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
