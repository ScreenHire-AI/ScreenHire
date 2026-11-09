<script lang="ts">
  import { untrack } from 'svelte';
  import ListIcon from '~icons/lucide/list';
  import PlugZapIcon from '~icons/lucide/plug-zap';
  import SaveIcon from '~icons/lucide/save';
  import type { HttpValidationProblemDetails, ModelProvider as ModelProviderOrNull, OrganizationVerificationSettings } from '@screenhire/api-client';
  import { ApiError, createListVerificationModels, createTestVerificationModel } from '@screenhire/api-client/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { modelProblemMessages, modelProviderLabels } from '#lib/verification-settings.js';
  import { createVerificationSettingsSave, savedMessage } from '#lib/verification-settings-save.js';

  // The AI model tab (verification-engine.md 10.11): which model answers the consistency check, and the provider, key and
  // model when the organization brings its own. It saves only these fields. The API key is write-only: the box is never
  // filled from the server, and an empty box keeps the saved key.
  let { detail }: { detail: OrganizationVerificationSettings } = $props();

  type ModelProvider = NonNullable<ModelProviderOrNull>;
  const save = createVerificationSettingsSave();
  const listModels = createListVerificationModels();
  const testModel = createTestVerificationModel();

  const trimSlashes = (url: string) => url.trim().replace(/\/+$/, '');
  const providers = Object.keys(modelProviderLabels) as [ModelProvider, ...ModelProvider[]];
  const price = z
    .string()
    .trim()
    .regex(/^\d*(\.\d{1,4})?$/, 'Enter a price in dollars, or leave it empty.');
  // What the server says each provider preset fills in (the address, and whether the person types one).
  const presetOf = (provider: ModelProvider) => detail.presets.find((p) => p.provider === provider);

  const schema = z
    .object({
      llmConsistencyCheck: z.boolean(),
      modelSource: z.enum(['Included', 'OwnKey']),
      provider: z.enum(providers),
      baseUrl: z.string().trim(),
      model: z.string().trim(),
      apiKey: z.string(),
      ownKeyInputPrice: price,
      ownKeyOutputPrice: price,
    })
    .superRefine((value, context) => {
      if (value.modelSource !== 'OwnKey') return;
      if (!value.model) context.addIssue({ code: 'custom', path: ['model'], message: 'Enter the model to use.' });
      if (presetOf(value.provider)?.baseUrlEditable && !/^https:\/\/\S+$/.test(value.baseUrl)) {
        context.addIssue({ code: 'custom', path: ['baseUrl'], message: 'Enter the full address of the endpoint, starting with https://.' });
      }
      // A saved key stays only for the same provider and address, so an old key is never sent to a new host.
      const keptKey =
        detail.apiKeySaved === true &&
        detail.modelSource === 'OwnKey' &&
        detail.provider === value.provider &&
        trimSlashes(detail.baseUrl ?? '') === (presetOf(value.provider)?.baseUrlEditable ? trimSlashes(value.baseUrl) : '');
      if (!value.apiKey.trim() && !keptKey) {
        context.addIssue({ code: 'custom', path: ['apiKey'], message: detail.apiKeySaved ? 'Enter the API key for this provider.' : 'Enter the API key.' });
      }
    });

  const initial = untrack(() => ({
    llmConsistencyCheck: detail.llmConsistencyCheck,
    modelSource: detail.modelSource as 'Included' | 'OwnKey',
    provider: (detail.provider ?? 'Anthropic') as ModelProvider,
    baseUrl: detail.baseUrl ?? '',
    model: detail.model ?? '',
    apiKey: '',
    ownKeyInputPrice: detail.ownKeyInputPrice?.toString() ?? '',
    ownKeyOutputPrice: detail.ownKeyOutputPrice?.toString() ?? '',
  }));

  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(defaults(initial, zod4(schema)), {
    SPA: true,
    dataType: 'json',
    validationMethod: 'submit-only',
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      const data = submitted.data;
      const own = data.modelSource === 'OwnKey';
      try {
        await save(detail, {
          llmConsistencyCheck: data.llmConsistencyCheck,
          modelSource: data.modelSource,
          provider: own ? data.provider : null,
          baseUrl: own && presetOf(data.provider)?.baseUrlEditable ? data.baseUrl : null,
          model: own ? data.model : null,
          apiKey: own && data.apiKey.trim() ? data.apiKey.trim() : null,
          ownKeyInputPrice: own && data.ownKeyInputPrice.trim() ? Number(data.ownKeyInputPrice) : null,
          ownKeyOutputPrice: own && data.ownKeyOutputPrice.trim() ? Number(data.ownKeyOutputPrice) : null,
        });
        submitted.data.apiKey = ''; // the key was sent once; the box never holds a saved one
        saved = savedMessage;
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the settings')];
        }
      }
    },
  });

  const own = $derived($form.modelSource === 'OwnKey');
  const preset = $derived(presetOf($form.provider));

  // Load models and Test connection use what is typed, and the saved key when the box is empty (the server decides).
  let models = $state<string[]>([]);
  let modelsNote = $state('');
  let loadingModels = $state(false);
  let testing = $state(false);
  let connected = $state(false);
  let testFailure = $state('');
  const endpoint = () => ({
    provider: $form.provider,
    baseUrl: preset?.baseUrlEditable ? $form.baseUrl : null,
    apiKey: $form.apiKey.trim() || null,
  });
  const endpointChanged = () => {
    connected = false;
    testFailure = '';
  };
  const firstError = (error: unknown, fallback: string) => {
    if (error instanceof ApiError && error.status === 400) {
      const first = Object.values((error.body as HttpValidationProblemDetails)?.errors ?? {})[0]?.[0];
      if (first) return first;
    }
    return describeError(error, fallback);
  };

  async function loadModels() {
    loadingModels = true;
    modelsNote = '';
    try {
      const response = await listModels.mutateAsync({ data: endpoint() });
      models = response.data.models;
      modelsNote = response.data.listed ? '' : 'This provider does not list its models. Type the model id.';
    } catch (error) {
      models = [];
      modelsNote = firstError(error, 'Could not load the models. Type the model id.');
    } finally {
      loadingModels = false;
    }
  }

  async function test() {
    testing = true;
    connected = false;
    testFailure = '';
    try {
      const response = await testModel.mutateAsync({ data: { ...endpoint(), model: $form.model } });
      if (response.data.ok) connected = true;
      else testFailure = modelProblemMessages[response.data.problem ?? 'Other'];
    } catch (error) {
      testFailure = firstError(error, 'Could not test the connection');
    } finally {
      testing = false;
    }
  }

  const message = (list: string[] | undefined) => list?.map((text) => ({ message: text }));
  const describedBy = (...ids: (string | false | undefined)[]) => ids.filter(Boolean).join(' ') || undefined;
</script>

<form method="POST" use:enhance novalidate aria-label="AI model settings" class="flex flex-col gap-5">
  <Panel title="AI model">
            <div class="flex flex-col gap-5">
              <div class="flex flex-col gap-2">
                <p id="model-source-label" class="text-body font-medium">Which model checks consistency</p>
                <div role="radiogroup" aria-labelledby="model-source-label" class="grid gap-2 sm:grid-cols-2">
                  <ChoiceCard name="modelSource" value="Included" bind:group={$form.modelSource} onchange={endpointChanged} title="ScreenHire's included model" description="Billed at the provider's price plus a margin." />
                  <ChoiceCard name="modelSource" value="OwnKey" bind:group={$form.modelSource} onchange={endpointChanged} title="Your own key" description="A key from your own AI provider." />
                </div>
              </div>

              <div class="flex items-start gap-4">
                <div class="flex min-w-0 flex-1 flex-col gap-1">
                  <Field.Label for="consistency-check">Compare each application's sources</Field.Label>
                  <Field.Description id="consistency-check-hint">
                    Sends the resume, answers and any uploaded LinkedIn export to {own ? 'your AI provider' : "ScreenHire's AI provider"}.
                  </Field.Description>
                </div>
                <Switch id="consistency-check" bind:checked={$form.llmConsistencyCheck} aria-describedby="consistency-check-hint" class="mt-1" />
              </div>

              {#if own}
                <p class="text-muted-foreground">
                  Your provider handles this data under your own terms and bills you for it. ScreenHire charges nothing extra, and the Activity log lists every call so you can compare it with your provider's dashboard.
                </p>

                <Field.Field>
                  <Field.Label for="llm-provider">Provider</Field.Label>
                  <select id="llm-provider" name="provider" class={selectClass} bind:value={$form.provider} onchange={endpointChanged}>
                    {#each providers as provider (provider)}
                      <option value={provider}>{modelProviderLabels[provider]}</option>
                    {/each}
                  </select>
                  {#if preset?.baseUrl}
                    <Field.Description id="llm-provider-hint">Calls go to {preset.baseUrl}.</Field.Description>
                  {/if}
                </Field.Field>

                {#if preset?.baseUrlEditable}
                  <Field.Field data-invalid={$errors.baseUrl ? true : undefined}>
                    <Field.Label for="llm-base-url">Endpoint address</Field.Label>
                    <Input
                      id="llm-base-url"
                      name="baseUrl"
                      type="url"
                      autocomplete="off"
                      placeholder={$form.provider === 'AzureOpenAi' ? 'https://your-resource.openai.azure.com' : 'https://api.example.com/v1'}
                      bind:value={$form.baseUrl}
                      oninput={endpointChanged}
                      aria-invalid={$errors.baseUrl ? 'true' : undefined}
                      aria-describedby={describedBy('llm-base-url-hint', $errors.baseUrl && 'llm-base-url-error')}
                    />
                    <Field.Description id="llm-base-url-hint">
                      {#if $form.provider === 'AzureOpenAi'}Your Azure OpenAI resource's address. The model is the name of your deployment.{:else}The address of an OpenAI-compatible API. It must be https.{/if}
                    </Field.Description>
                    <Field.Error id="llm-base-url-error" errors={message($errors.baseUrl)} />
                  </Field.Field>
                {/if}

                <Field.Field data-invalid={$errors.apiKey ? true : undefined}>
                  <Field.Label for="llm-api-key">API key</Field.Label>
                  <Input
                    id="llm-api-key"
                    name="apiKey"
                    type="password"
                    autocomplete="off"
                    spellcheck="false"
                    placeholder={detail.apiKeySaved ? 'Enter a new key to replace it' : undefined}
                    bind:value={$form.apiKey}
                    oninput={endpointChanged}
                    aria-invalid={$errors.apiKey ? 'true' : undefined}
                    aria-describedby={describedBy('llm-api-key-hint', $errors.apiKey && 'llm-api-key-error')}
                  />
                  <Field.Description id="llm-api-key-hint">
                    {#if detail.apiKeySaved && detail.modelSource === 'OwnKey'}A key is saved. Leave this empty to keep it.{:else}It is stored encrypted and never shown again.{/if}
                  </Field.Description>
                  <Field.Error id="llm-api-key-error" errors={message($errors.apiKey)} />
                </Field.Field>

                <Field.Field data-invalid={$errors.model ? true : undefined}>
                  <Field.Label for="llm-model">Model</Field.Label>
                  <div class="flex flex-wrap items-start gap-2">
                    <div class="min-w-48 flex-1">
                      <Input
                        id="llm-model"
                        name="model"
                        list="llm-model-options"
                        autocomplete="off"
                        spellcheck="false"
                        bind:value={$form.model}
                        oninput={endpointChanged}
                        aria-invalid={$errors.model ? 'true' : undefined}
                        aria-describedby={describedBy('llm-model-hint', $errors.model && 'llm-model-error')}
                      />
                      <datalist id="llm-model-options">
                        {#each models as id (id)}<option value={id}></option>{/each}
                      </datalist>
                    </div>
                    <Button type="button" variant="outline" size="lg" disabled={loadingModels} onclick={loadModels}><ListIcon aria-hidden="true" />Load models</Button>
                  </div>
                  <Field.Description id="llm-model-hint">
                    {modelsNote || (models.length > 0 ? `${models.length} models from your provider. Pick one or type an id.` : "Type the model's id, or load your provider's list.")}
                  </Field.Description>
                  <Field.Error id="llm-model-error" errors={message($errors.model)} />
                </Field.Field>

                <div class="flex flex-wrap items-center gap-3">
                  <Button type="button" variant="outline" size="lg" disabled={testing} onclick={test}><PlugZapIcon aria-hidden="true" />Test connection</Button>
                  <p role="status" class="flex items-center gap-2 text-body empty:hidden">
                    {#if testing}Testing…{:else if connected}<StatusIcon status="done" />Connected. The key and model work.{/if}
                  </p>
                  <FailureText message={testFailure} />
                </div>
                <p class="text-muted-foreground">Saving a changed key, provider, address or model tests it first.</p>

                <div class="grid grid-cols-[repeat(auto-fit,minmax(12rem,1fr))] gap-4">
                  <Field.Field data-invalid={$errors.ownKeyInputPrice ? true : undefined}>
                    <Field.Label for="own-input-price">Input price <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
                    <Input id="own-input-price" name="ownKeyInputPrice" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.ownKeyInputPrice} aria-invalid={$errors.ownKeyInputPrice ? 'true' : undefined} />
                    <Field.Description>US dollars per million tokens.</Field.Description>
                    <Field.Error errors={message($errors.ownKeyInputPrice)} />
                  </Field.Field>
                  <Field.Field data-invalid={$errors.ownKeyOutputPrice ? true : undefined}>
                    <Field.Label for="own-output-price">Output price <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
                    <Input id="own-output-price" name="ownKeyOutputPrice" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.ownKeyOutputPrice} aria-invalid={$errors.ownKeyOutputPrice ? 'true' : undefined} />
                    <Field.Description>US dollars per million tokens.</Field.Description>
                    <Field.Error errors={message($errors.ownKeyOutputPrice)} />
                  </Field.Field>
                </div>
              {:else}
                <p class="text-muted-foreground">ScreenHire pays the provider and bills you for what the checks use, at the provider's price plus a margin.</p>
              {/if}
            </div>

      {#snippet footer()}
        <div class="flex flex-wrap items-center justify-end gap-2">
          <FailureText message={$errors._errors?.[0]} class="mr-auto" />
          <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
          <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
        </div>
      {/snippet}
  </Panel>
</form>
